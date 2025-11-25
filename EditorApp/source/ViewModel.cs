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
                FormattedBibliography = await _formatService.FormatBibliographyAsync(SelectedDocument.BibliographyContent);
                if (string.IsNullOrWhiteSpace(FormattedBibliography))
                {
                    var result = _dialogService.ShowMessage("Список литературы не был отформатирован. Всё равно сохранить?", "Предупреждение",MessageBoxButton.YesNo);
                    if (result == MessageBoxResult.No)
                    {
                        return;
                    }                       
                }
                await _documentService.SaveAsProcessedAsync(SelectedDocument.FilePath, FormattedBibliography);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK);
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
