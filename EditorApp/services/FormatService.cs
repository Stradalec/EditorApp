using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Vml;
using EditorApp.source;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EditorApp.services
{
    internal class FormatService : IFormatService
    {
        private readonly HttpClient _httpClient;
        public FormatService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> FormatBibliographyAsync(string rawText, IProgress<(int current, int total)> progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return "Исходный текст пуст.";
            }
                
            var items = SplitIntoBibliographyItems(rawText);
            if (!items.Any())
            {
                return rawText;
            }
            var formattedItems = new List<string>();
            int total = items.Count;
            for (int formatIndex = 0; formatIndex < items.Count; ++formatIndex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var formatted = await FormatSingleItemAsync(items[formatIndex], cancellationToken);
                    if (formatted.EndsWith("[Элемент: sectPr]", StringComparison.OrdinalIgnoreCase))
                    {
                        formatted = formatted.Substring(0, formatted.Length - "[Элемент: sectPr]".Length);
                    }
                    formattedItems.Add(formatted.Trim());
                    progress?.Report((formatIndex + 1, total));
                }
                catch
                {
                    formattedItems.Add(items[formatIndex].Trim());
                }
            }
            return string.Join("\n", formattedItems);
        }
        private List<string> SplitIntoBibliographyItems(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var items = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (trimmed.StartsWith("[Элемент:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Regex.IsMatch(trimmed, @"^\d{1,3}[\.\)\-\–\—]\s*$"))
                    continue;

                items.Add(trimmed);
            }

            return items;
        }

        private async Task<string> FormatSingleItemAsync(string item, CancellationToken cancellationToken)
        {
            if (Regex.IsMatch(item.Trim(), @"^\d{1,3}[\.\)\-\–\—]\s*$"))
            {
                return item;
            }

            string reason = ""; 
            try
            {
                var request = new ListRequest { text = item };
                var response = await _httpClient.PostAsJsonAsync("", request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ListResponse>();
                    var formatted = result?.result?.Trim();

                    if (!string.IsNullOrWhiteSpace(formatted))
                    {
                        formatted = RemoveExtraSpaces(formatted); 
                        return formatted;
                    }
                }

                var error = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Ошибка при обработке пункта: {response.StatusCode}\n{error}");
            }           
            catch (HttpRequestException ex)
            {
                reason = $" Сетевая ошибка: {ex.Message}";
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                reason = "Таймаут подключения к серверу";
            }
            catch (OperationCanceledException ex)
            {
                reason = $" Операция отменена пользователем: {ex.Message}";
                throw;
            }
            catch (Exception ex)
            {
                reason = $"Неизвестная ошибка: {ex.Message}";
            }
            return $"Не удалось отформатировать{reason}";
        }
        private string RemoveExtraSpaces(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            text = Regex.Replace(text, @"\s{2,}", " ");

            return text.Trim();
        }
    }
}

