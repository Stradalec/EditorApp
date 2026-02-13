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
using System.Threading;
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
        private FormatOptions _selectedOptions = new FormatOptions();

        [ObservableProperty]
        private string _progressText = "";
        private CancellationTokenSource _cancellationTokenSource;
        [ObservableProperty]
        private int _progressValue;

        [ObservableProperty]
        private int _progressMaximum = 100;
        [RelayCommand]
        private async Task LoadDocument(string filePath = null)
        {
            string path = filePath;
            if (path == null && _dialogService.ShowOpenFileDialog(out path) != true)
            {
                return;
            }
                
            SelectedDocument = new Document {
                DocumentName = Path.GetFileName(path),
                FilePath = path
            };
            try
            {
                string bibliography = await _documentService.ExtractBibliographyAsync(path);
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
            _cancellationTokenSource = new CancellationTokenSource();
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
                

                var progressSteps = new List<(string name, int weight)>();
                if (SelectedOptions.IsFormatActive)
                {
                    progressSteps.Add(("Форматирование", 70));
                }
                if (SelectedOptions.IsReferencesActive)
                {
                    progressSteps.Add(("Проверка ссылок", 30));
                }
                var progressService = new ProgressService((value, text) => { 
                    ProgressValue = value; 
                    ProgressText = text; 
                }, progressSteps);
                int stepIndex = 0;
                if (SelectedOptions.IsFormatActive)
                {
                    progressService.SetText("Извлечение списка литературы...");
                    var stepProgress = progressService.CreateStepProgress(stepIndex);
                    FormattedBibliography = await _formatService.FormatBibliographyAsync(SelectedDocument.BibliographyContent, stepProgress, _cancellationTokenSource.Token);
                    progressService.SetText("Сохранение файла...");
                    await _documentService.SaveAsProcessedAsync(SelectedDocument.FilePath, FormattedBibliography);
                    progressService.CompleteStep(stepIndex);
                    ++stepIndex;
                }
                List<(string text, string url, bool isAlive)> linkResult = new();
                if (SelectedOptions.IsReferencesActive)
                {
                    progressService.SetText("Проверка ссылок");
                    var linkProgress = progressService.CreateStepProgress(stepIndex);


                    linkResult = await _formatService.CheckLinksAsync(SelectedDocument.FilePath,linkProgress,_cancellationTokenSource.Token);

                    progressService.CompleteStep(stepIndex);
                    ++stepIndex;
                    var lines = linkResult.Select(result => $"{(result.isAlive ? "ХОР" : "ПЛХ")} | {result.url} | {result.text}");
                    _dialogService.ShowMessage(string.Join(Environment.NewLine, lines), "Оповещение", MessageBoxButton.OK);
                }
                progressService.Finish();
            }
            catch (OperationCanceledException)
            {
                _dialogService.ShowMessage("Операция отменена пользователем", "Оповещение", MessageBoxButton.OK);
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
        [RelayCommand]
        private  void CancelAnyProcess()
        {
            _cancellationTokenSource.Cancel();
        }
        [RelayCommand]
        private void OpenHelp()
        {
            _dialogService.ShowMessage("В модель идёт обрабатываться весь текст после слов \"список литературы\". Записи отправляются по принципу \"новый абзац\" - \"новая запись\". \r\n В среднем обработка списка литературы занимает 3-7 минут (при списке приблизительно в 20 элементов). Для отслеживания есть шкала прогресса. \r\n Прогресс работы со списком литературы отображается в пунктах этого списка, для ссылок - в количестве обработанных ссылок ", "Справка", MessageBoxButton.OK);
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
