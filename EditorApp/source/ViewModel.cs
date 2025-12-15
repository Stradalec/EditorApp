using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EditorApp.services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace EditorApp.source
{
    partial class ApplicationViewModel : ObservableObject
    {
        private readonly IDocumentService _documentService;
        private readonly IDialogService _dialogService;
        private readonly IFormatService _formatService;
        [ObservableProperty]
        private Document selectedDocument;
        [ObservableProperty]
        private string _formattedBibliography = "";
        [ObservableProperty]
        private bool _isProcessing;

        [ObservableProperty]
        private string _progressText = "";

        [ObservableProperty]
        private int _progressValue;

        [ObservableProperty]
        private int _progressMaximum = 100;
        [RelayCommand]
        private async Task LoadDocument()
        {
            if (_dialogService.ShowOpenFileDialog(out string filePath) != true)
            {
                return;
            }
         
            SelectedDocument = new Document {
                DocumentName = Path.GetFileName(filePath),
                FilePath = filePath
            };
            try
            {
                string bibliography = await _documentService.ExtractBibliographyAsync(filePath);
                if (bibliography.Contains("не поддерживается") || bibliography.Contains("не найден") ||  bibliography.Contains("повреждён") || bibliography.Contains("ошибка"))
                {
                    _dialogService.ShowMessage(bibliography, "Ошибка", MessageBoxButton.OK);
                    SelectedDocument.BibliographyContent = "";
                    return; 
                }
                SelectedDocument.BibliographyContent = bibliography;
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage($"Ошибка при обработке документа: {ex.Message}","Ошибка",MessageBoxButton.OK);
            }
        }
        [RelayCommand]
        private void OpenDocument()
        {
            if (SelectedDocument?.FilePath != null && File.Exists(SelectedDocument.FilePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(SelectedDocument.FilePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessage($"Не удалось открыть файл: {ex.Message}", "Ошибка", MessageBoxButton.OK);
                }
            }
        }

        [RelayCommand]
        private async Task ProcessAndSaveDocument()
        {
            if (string.IsNullOrEmpty(SelectedDocument?.FilePath))
            {
                _dialogService.ShowMessage("Файл не выбран.", "Ошибка", MessageBoxButton.OK);
                return;
            }

            try
            {
                IsProcessing = true;
                ProgressMaximum = 100;
                ProgressValue = 0;
                ProgressText = "Извлечение списка литературы...";

                ProgressValue = 10;
                ProgressText = "Отправка на форматирование...";
                var progress = new Progress<(int current, int total)>(p =>
                {
                    ProgressValue = p.current;
                    ProgressMaximum = p.total;
                    ProgressText = $"Форматирование: {p.current}/{p.total}";
                });
                FormattedBibliography = await _formatService.FormatBibliographyAsync(SelectedDocument.BibliographyContent, progress);

                ProgressValue = 60;
                ProgressText = "Сохранение файла...";

                await _documentService.SaveAsProcessedAsync(SelectedDocument.FilePath, FormattedBibliography);

                ProgressValue = 100;
                ProgressText = "Готово!";

            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK);
            }
            finally
            {
                IsProcessing = false;
                ProgressValue = 0;
                ProgressText = "";
            }
        }

        public ApplicationViewModel(IDocumentService documentService, IDialogService dialogService, IFormatService formatService)
        {
            _documentService = documentService;
            _dialogService = dialogService;
            _formatService = formatService;
            SelectedDocument = new Document();
        }

    }

}
