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
        private readonly HttpClient _linkHttpClient;
        public FormatService(HttpClient httpClient, HttpClient linkHttpClient)
        {
            _httpClient = httpClient;
            _linkHttpClient = linkHttpClient;
        }

        public async Task<IReadOnlyList<BibliographyItemResult>> FormatBibliographyAsync(
            string rawText,
            TemplateItem selectedTemplate,
            IProgress<(int current, int total)> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return Array.Empty<BibliographyItemResult>();

            var items = SplitIntoBibliographyItems(rawText);
            var results = new List<BibliographyItemResult>(items.Count);

            foreach (string item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                results.Add(await FormatSingleItemAsync(
                    item, selectedTemplate, cancellationToken));

                progress?.Report((results.Count, items.Count));
            }

            return results;
        }

        private async Task<BibliographyItemResult> FormatSingleItemAsync(
            string item,
            TemplateItem selectedTemplate,
            CancellationToken cancellationToken)
        {
            var fallback = new BibliographyItemResult(item, item, HighlightAmber: true);

            try
            {
                var request = new ListRequest {
                    text = item,
                    language = DetectLanguage(item),
                    templateId = selectedTemplate?.Id ?? "default"
                };

                using var response = await _httpClient.PostAsJsonAsync(
                    "format", request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return fallback;

                var data = await response.Content.ReadFromJsonAsync<ListResponse>(
                    cancellationToken: cancellationToken);

                if (data == null || string.IsNullOrWhiteSpace(data.result))
                    return fallback;

                const string sectionMarker = "[Элемент: sectPr]";
                string output = data.result.Trim();

                if (output.EndsWith(sectionMarker, StringComparison.OrdinalIgnoreCase))
                    output = output.Substring(0, output.Length - sectionMarker.Length);

                output = RemoveExtraSpaces(output);

                return string.IsNullOrWhiteSpace(output)
                    ? fallback
                    : new BibliographyItemResult(item, output, data.missing_data);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return fallback;
            }
            catch (HttpRequestException)
            {
                return fallback;
            }
            catch (System.Text.Json.JsonException)
            {
                return fallback;
            }
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

                if (!HasVisibleText(trimmed))
                    continue;

                if (trimmed.StartsWith("[Элемент:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Regex.IsMatch(trimmed, @"^\d{1,3}[\.\)\-\–\—]\s*$"))
                    continue;

                items.Add(trimmed);
            }

            return items;
        }

        private static bool HasVisibleText(string text)
        {
            return text.Any(ch =>
                !char.IsWhiteSpace(ch) &&
                !char.IsControl(ch) &&
                char.GetUnicodeCategory(ch) !=
                    System.Globalization.UnicodeCategory.Format);
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
            var collected = new List<(string text, string url, OpenXmlElement element)>();
            var result = new List<(string text, string url, bool isAlive)>();
            WordprocessingDocument document = WordprocessingDocument.Open(filePath, true);
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
                collected.Add((text, url, link));
            }
            foreach (var fieldSimple in mainDocumentPart.Document.Body.Descendants<OpenXmlElement>().Where(element => element.LocalName == "fldSimple"))
            {
                var instruct = fieldSimple.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "instr").Value;
                var url = ExtractUrlFromHyperlinkInstruction(instruct);
                if (url == null) continue;

                var text = string.Concat(fieldSimple.Descendants<Text>().Select(text => text.Text));
                if (string.IsNullOrWhiteSpace(text)) text = url;
                collected.Add((text, url, fieldSimple));
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
                            if (url != null && !collected.Any(result => result.Item2.Equals(url, StringComparison.OrdinalIgnoreCase)))
                            {
                                break;
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
            result = new List<(string text, string url, bool isAlive)>(collected.Count);

            int total = collected.Count;
            for (int tryUrlIndex = 0; tryUrlIndex < total; ++tryUrlIndex)
            {
                token.ThrowIfCancellationRequested();

                var (text, url, element) = collected[tryUrlIndex];
                bool ok = await TryUrlAsync(url, token);

                if (!ok)
                {
                    PaintLinkRed(element);
                }

                result.Add((text, url, ok));

                progress?.Report((tryUrlIndex + 1, total));
            }
            mainDocumentPart.Document.Save();
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
        public async Task<bool> TryUrlAsync(string url, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            try
            {
                using var requestHead = new HttpRequestMessage(HttpMethod.Head, uri);
                using var response = await _linkHttpClient.SendAsync(
                    requestHead,
                    HttpCompletionOption.ResponseHeadersRead,
                    token);

                if ((int)response.StatusCode == 405 ||
                    (int)response.StatusCode == 403)
                {
                    using var requestGet = new HttpRequestMessage(HttpMethod.Get, uri);
                    using var responseGet = await _linkHttpClient.SendAsync(
                        requestGet,
                        HttpCompletionOption.ResponseHeadersRead,
                        token);

                    return responseGet.IsSuccessStatusCode;
                }

                return response.IsSuccessStatusCode;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        private static void PaintLinkRed(OpenXmlElement element)
        {
            var runs = element.Descendants<Run>();

            foreach (var run in runs)
            {
                run.RunProperties ??= new RunProperties();
                run.RunProperties.Color = new Color() { Val = "FF0000" };
            }
        }


        internal static string DetectLanguage(string inputString)
        {
            if (string.IsNullOrWhiteSpace(inputString))
            {
                return "RU";
            }


            bool hasHttpsLink = inputString.Contains("https://", StringComparison.OrdinalIgnoreCase);
            bool hasRussianLetter = Regex.IsMatch(inputString, @"[А-Яа-яЁё]");

            if (hasHttpsLink && hasRussianLetter)
            {
                return "RU";
            }
                

            int ruCount = Regex.Matches(inputString, @"[А-Яа-яЁё]").Count;
            int enCount = Regex.Matches(inputString, @"[A-Za-z]").Count;

            return ruCount > enCount ? "RU" : "EN";
        }
    }
}

