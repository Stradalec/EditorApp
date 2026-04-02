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
        private readonly HttpClient _httpClient;
        [ObservableProperty]
        private Document selectedDocument;
        [ObservableProperty]
        private string _formattedBibliography = "";
        [ObservableProperty]
        private bool _isProcessing;
        [ObservableProperty]
        private FormatOptions _selectedOptions = new FormatOptions();
        public ObservableCollection<TemplateItem> Templates { get; } = new ObservableCollection<TemplateItem>
        {
            new TemplateItem { Id = "default", Title = "ГОСТ Р 7.0.100-2018" },
            new TemplateItem { Id = "test", Title = "Тест" }
        };

        [ObservableProperty]
        private TemplateItem? selectedTemplate;
        [ObservableProperty]
        private string _progressText = "";
        private CancellationTokenSource _cancellationTokenSource;
        [ObservableProperty]
        private int _progressValue;

        [ObservableProperty]
        private int _progressMaximum = 100;

        [ObservableProperty]
        private string estimatedTimeText = "";

        partial void OnSelectedTemplateChanged(TemplateItem? value)
        {
            if (value?.Id is null) return;
            _ = SetTemplateOnServerAsync(value.Id);
        }

        private async Task SetTemplateOnServerAsync(string templateId)
        {
            try
            {
                var resp = await _httpClient.PostAsJsonAsync("set_template", new { templateId });
                var body = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"set_template exception: {ex}");
            }
        }

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
                var startTime = DateTime.Now;
                EstimatedTimeText = "Осталось: оценка недоступна";
                ProgressMaximum = 100;
                ProgressValue = 0;


                string newDocumentPath = "original";
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

                    if (value > 0)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var totalEstimatedSeconds = elapsed.TotalSeconds * ProgressMaximum / value;
                        var remainingSeconds = Math.Max(0, totalEstimatedSeconds - elapsed.TotalSeconds);
                        EstimatedTimeText = $"Осталось: ~ {TimeSpan.FromSeconds(remainingSeconds):mm\\:ss}";
                    }
                    else
                    {
                        EstimatedTimeText = "Осталось: оценка недоступна";
                    }
                }, progressSteps);
                int stepIndex = 0;
                if (SelectedOptions.IsFormatActive)
                {
                    progressService.SetText("Извлечение списка литературы...");
                    var stepProgress = progressService.CreateStepProgress(stepIndex);
                    FormattedBibliography = await _formatService.FormatBibliographyAsync(SelectedDocument.BibliographyContent, SelectedTemplate, stepProgress, _cancellationTokenSource.Token);
                    progressService.SetText("Сохранение файла...");
                    newDocumentPath = await _documentService.SaveAsProcessedAsync(SelectedDocument.FilePath, FormattedBibliography, "list");
                    progressService.CompleteStep(stepIndex);
                    ++stepIndex;
                }
                List<(string text, string url, bool isAlive)> linkResult = new();
                if (SelectedOptions.IsReferencesActive)
                {
                    progressService.SetText("Проверка ссылок");
                    var linkProgress = progressService.CreateStepProgress(stepIndex);

                    if (newDocumentPath == "original")
                    {
                        newDocumentPath = await _documentService.SaveAsProcessedAsync(SelectedDocument.FilePath, FormattedBibliography, "links");
                    }

                    linkResult = await _formatService.CheckLinksAsync(newDocumentPath, linkProgress, _cancellationTokenSource.Token);

                    progressService.CompleteStep(stepIndex);
                    ++stepIndex;

                    int badCount = linkResult.Count(result => !result.isAlive);
                    _dialogService.ShowMessage($"Проверка завершена. Нерабочих ссылок: {badCount}. Они выделены красным в документе.", "Оповещение", MessageBoxButton.OK);
                }
                await _documentService.OpenAsProcessed(newDocumentPath);
                    
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
                EstimatedTimeText = "";
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
        public ApplicationViewModel(IDocumentService documentService, IDialogService dialogService, IFormatService formatService, HttpClient httpClient)
        {
            _documentService = documentService;
            _dialogService = dialogService;
            _formatService = formatService;
            SelectedDocument = new Document();
            _httpClient = httpClient;
        }

    }

}
