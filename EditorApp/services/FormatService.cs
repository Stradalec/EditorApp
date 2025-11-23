using EditorApp.source;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
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

        public async Task<string> FormatBibliographyAsync(string rawText)
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
            foreach (var item in items)
            {
                try
                {
                    var formatted = await FormatSingleItemAsync(item);
                    if (formatted.EndsWith("[Элемент: sectPr]", StringComparison.OrdinalIgnoreCase))
                    {
                        formatted = formatted.Substring(0, formatted.Length - "[Элемент: sectPr]".Length);
                    }
                    formattedItems.Add(formatted.Trim());
                }
                catch
                {
                    formattedItems.Add(item.Trim());
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
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var regex = new Regex(@"(?:^|\n)\s*(?:\[\d+\]|\d+)\s*[\.\)\-\–\—]\s+[А-ЯA-ZЁ]",RegexOptions.Multiline | RegexOptions.Compiled);


            var matches = regex.Matches(text);
            var items = new List<string>();
            int startIndex = 0;
            for (int itemIndex = 0; itemIndex < matches.Count; ++itemIndex)
            {
                int matchIndex = matches[itemIndex].Index;

                if (matchIndex == 0) continue;

                string item = text.Substring(startIndex, matchIndex - startIndex).Trim();
                if (!string.IsNullOrEmpty(item))
                    items.Add(item);

                startIndex = matchIndex;
            }

            string lastItem = text.Substring(startIndex).Trim();
            if (!string.IsNullOrEmpty(lastItem))
                items.Add(lastItem);


            return items;
        }

        private async Task<string> FormatSingleItemAsync(string item)
        {
            var request = new ListRequest { text = item };

            var response = await _httpClient.PostAsJsonAsync("", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ListResponse>();
                return result?.result?.Trim() ?? item;
            }

            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Ошибка при обработке пункта: {response.StatusCode}\n{error}");
        }
    }
}

