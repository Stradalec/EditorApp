using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Break = DocumentFormat.OpenXml.Wordprocessing.Break;
using Run = DocumentFormat.OpenXml.Wordprocessing.Run;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace EditorApp.services.document
{
    internal class DocumentListExtractService
    {
        public async Task<BibliographyExtractResult> ExtractBibliography(string filePath)
        {

            if (!File.Exists(filePath))
            {
                return new BibliographyExtractResult {
                    Items = new List<string>(),
                    Text = "Файл не найден."
                }; 
            }

            if (Path.GetExtension(filePath).ToLower() == ".doc")
            {
                return new BibliographyExtractResult { Items = new List<string>(), Text = "Формат .doc не поддерживается. Пожалуйста, сохраните файл как .docx." };
            }
            return await Task.Run(() => {
                StringBuilder result = new StringBuilder();
                bool foundBibliography = false;

                try
                {
                    using var document = WordprocessingDocument.Open(filePath, false);
                    var body = document.MainDocumentPart?.Document.Body;
                    if (body == null)
                    {
                        return new BibliographyExtractResult { Items = new List<string>(), Text = "Документ пустой или повреждён."};
                    }

                    foreach (var element in body.Elements())
                    {
                        if (element is SectionProperties)
                        {
                            if (foundBibliography)
                            {
                                break;
                            }
                        }

                        if (element is Paragraph paragraph)
                        {
                            string text = ExtractTextPreservingIndices(paragraph);
                            var pageBreak = paragraph.Descendants<Break>().FirstOrDefault(b => b.Type?.Value == BreakValues.Page);

                            if (pageBreak != null && foundBibliography)
                            {
                                break;
                            }

                            if (foundBibliography)
                            {
                                if (Regex.IsMatch(text, @"^(Приложение|Appendix)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                                {
                                    Debug.WriteLine($"Остановка: найдено ключевое слово: {text}");
                                    break;
                                }
                            }
                            string[] keywords = { "Список литературы", "Библиографический список", "Список источников", "Список использованных источников", "список использованной литературы" };
                            bool containsList = keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

                            if (!foundBibliography && containsList)
                            {
                                foundBibliography = true;
                                continue;
                            }

                            if (foundBibliography)
                            {
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    result.AppendLine(text.Trim());
                                }

                            }
                        }
                        else if (foundBibliography)
                        {
                            result.AppendLine($"[Элемент: {element.LocalName}]");
                        }
                    }
                }
                catch
                {
                    return new BibliographyExtractResult
                    {
                        Items = new List<string>(),
                        Text = "Ошибка при чтении документа."
                    }; 
                }
                var items = result.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

                return new BibliographyExtractResult {
                    Items = items,
                    Text = items.Count > 0
                        ? string.Join("\n", items)
                        : "Раздел 'Список литературы' не найден."
                };
            });
        }
        private static string ExtractTextPreservingIndices(Paragraph paragraph)
        {
            var sb = new StringBuilder();

            foreach (var run in paragraph.Descendants<Run>())
            {
                string text = string.Concat(run.Elements<Text>().Select(t => t.Text));

                var verticalAlign = run.RunProperties?
                    .VerticalTextAlignment?
                    .Val?
                    .Value;

                if (verticalAlign == VerticalPositionValues.Subscript)
                {
                    text = ToSubscript(text);
                }
                else if (verticalAlign == VerticalPositionValues.Superscript)
                {
                    text = ToSuperscript(text);
                }

                sb.Append(text);
            }

            return sb.ToString();
        }

        private static string ToSubscript(string text)
        {
            return text
                .Replace('0', '₀')
                .Replace('1', '₁')
                .Replace('2', '₂')
                .Replace('3', '₃')
                .Replace('4', '₄')
                .Replace('5', '₅')
                .Replace('6', '₆')
                .Replace('7', '₇')
                .Replace('8', '₈')
                .Replace('9', '₉');
        }

        private static string ToSuperscript(string text)
        {
            return text
                .Replace('0', '⁰')
                .Replace('1', '¹')
                .Replace('2', '²')
                .Replace('3', '³')
                .Replace('4', '⁴')
                .Replace('5', '⁵')
                .Replace('6', '⁶')
                .Replace('7', '⁷')
                .Replace('8', '⁸')
                .Replace('9', '⁹');
        }
    }
}
