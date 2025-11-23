using EditorApp.source;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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
                return "Исходный текст пуст.";

            var request = new ListRequest { text = rawText };

            var response = await _httpClient.PostAsJsonAsync("", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ListResponse>();
                return result?.result ?? "Форматированный текст отсутствует.";
            }

            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Ошибка сервера: {response.StatusCode}\n{error}");
        }
    }
}

