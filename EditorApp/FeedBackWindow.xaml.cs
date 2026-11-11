using System;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using EditorApp.services;
namespace EditorApp
{
    public partial class FeedbackWindow : Window
    {
        private readonly HttpClient _apiClient;
        private bool _isSending;
        private readonly string _templateId;
        public FeedbackWindow(HttpClient apiClient, string templateId)
        {
            InitializeComponent();
            _apiClient = apiClient;
            _templateId = templateId;
            Loaded += (_, _) => SourceTypeBox.Focus();
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string sourceType = SourceTypeBox.Text.Trim();
            string originalText = OriginalTextBox.Text;
            string modelOutput = ModelOutputBox.Text;
            string expectedOutput = ExpectedOutputBox.Text;

            if (string.IsNullOrWhiteSpace(sourceType) ||
                string.IsNullOrWhiteSpace(originalText) ||
                string.IsNullOrWhiteSpace(modelOutput) ||
                string.IsNullOrWhiteSpace(expectedOutput))
            {
                StatusText.Text = "Заполните все четыре поля.";
                return;
            }

            var feedback = new {
                templateId = _templateId,
                source_type = sourceType,
                language = FormatService.DetectLanguage(originalText),
                original_text = originalText,
                model_output = modelOutput,
                expected_output = expectedOutput
            };

            bool sent = false;
            SetSending(true);
            StatusText.Text = "Отправляем…";

            try
            {
                using var response = await _apiClient.PostAsJsonAsync(
                    "feedback", feedback);

                sent = response.IsSuccessStatusCode;
                if (!sent)
                {
                    StatusText.Text = response.StatusCode switch {
                        HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed =>
                            "Сервер пока не принимает отзывы.",
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                            "Сервер отклонил доступ. Проверьте регистрацию в приложении.",
                        _ => "Не удалось отправить отзыв. Попробуйте ещё раз."
                    };
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Сервер не ответил вовремя. Попробуйте ещё раз.";
            }
            catch (HttpRequestException)
            {
                StatusText.Text = "Не удалось связаться с сервером. Проверьте подключение и попробуйте ещё раз.";
            }
            finally
            {
                SetSending(false);
            }

            if (sent)
            {
                DialogResult = true;
            }
        }

        private void SetSending(bool isSending)
        {
            _isSending = isSending;
            Fields.IsEnabled = !isSending;
            SendButton.IsEnabled = !isSending;
            CloseButton.IsEnabled = !isSending;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (_isSending)
            {
                e.Cancel = true;
            }
        }
    }
}