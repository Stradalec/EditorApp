using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Packaging;

using DocumentFormat.OpenXml.Vml;
using DocumentFormat.OpenXml.Wordprocessing;
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
using System.Windows;

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
            var lines = text.Split(new[] { "\result\n", "\result", "\n" }, StringSplitOptions.None);

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
                var response = await _httpClient.PostAsJsonAsync("format", request, cancellationToken);
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
        public async Task<List<(string text, string url, bool isAlive)>> CheckLinksAsync(string filePath, IProgress<(int current, int total)>? progress, CancellationToken token)
        {
            var result = new List<(string, string, bool)>();
            WordprocessingDocument document = WordprocessingDocument.Open(filePath, false);
            var mainDocumentPart = document.MainDocumentPart;
            if (mainDocumentPart?.Document?.Body == null)
            {
                return result;
            }
            var links = mainDocumentPart.Document.Body.Descendants<Hyperlink>();
            foreach (var link in links) 
            {
                if (link.Id == null)
                {
                    continue;
                }
                var relationship = mainDocumentPart.HyperlinkRelationships.FirstOrDefault(relation => relation.Id == link.Id);
                if (relationship == null)
                {
                    continue;
                }
                string url = relationship.Uri.ToString();
                var text = string.Concat(link.Descendants<Text>().Select(linkText => linkText.Text));
               
                result.Add((text, url, await TryUrlAsync(url)));
            }
            foreach (var fieldSimple in mainDocumentPart.Document.Body.Descendants<OpenXmlElement>().Where(element => element.LocalName == "fldSimple"))
            {
                var instruct = fieldSimple.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "instr").Value;
                var url = ExtractUrlFromHyperlinkInstruction(instruct);
                if (url == null) continue;

                var text = string.Concat(fieldSimple.Descendants<Text>().Select(text => text.Text));
                if (string.IsNullOrWhiteSpace(text)) text = url;

                result.Add((text, url, await TryUrlAsync(url)));
            }
            string? currentInstr = null;
            bool inField = false;

            foreach (var element in mainDocumentPart.Document.Body.Descendants<OpenXmlElement>())
            {
                if (element.LocalName == "fldChar")
                {
                    var fldType = element.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "fldCharType").Value;

                    if (string.Equals(fldType, "begin", StringComparison.OrdinalIgnoreCase))
                    {
                        inField = true;
                        currentInstr = "";
                    }
                    else if (string.Equals(fldType, "end", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(currentInstr))
                        {
                            var url = ExtractUrlFromHyperlinkInstruction(currentInstr);
                            if (url != null && !result.Any(result => result.Item2.Equals(url, StringComparison.OrdinalIgnoreCase)))
                            {
                                result.Add((url, url, await TryUrlAsync(url)));
                            }
                                
                        }

                        inField = false;
                        currentInstr = null;
                    }

                    continue;
                }

                if (inField && element.LocalName == "instrText")
                {
                    currentInstr += element.InnerText;
                }
            }
            document.Dispose();
            return result;
        }
        private static string? ExtractUrlFromHyperlinkInstruction(string? instr)
        {
            if (string.IsNullOrWhiteSpace(instr)) 
            {
                return null;
            } 
            var match = Regex.Match(instr, @"HYPERLINK\s+""(?<url>[^""]+)""", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["url"].Value : null;
        }
        public async Task<bool> TryUrlAsync(string url)
        {
            try
            {
                using var requestHead = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(requestHead);
                if ((int)response.StatusCode == 405 || (int)response.StatusCode == 403)
                {
                    using var requestGet = new HttpRequestMessage(HttpMethod.Get, url);
                    using var responseGet = await _httpClient.SendAsync(requestGet);
                    return responseGet.IsSuccessStatusCode;
                }
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}

