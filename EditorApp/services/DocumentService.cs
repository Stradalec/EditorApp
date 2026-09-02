using DocumentFormat.OpenXml;
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
namespace EditorApp.services
{
    internal class DocumentService : IDocumentService
    {
        private List<string> _oldLiteratureList = new List<string>();
        public async Task<string> ExtractBibliographyAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return "Файл не найден.";
            }

            if (Path.GetExtension(filePath).ToLower() == ".doc")
            {
                return "Формат .doc не поддерживается. Пожалуйста, сохраните файл как .docx.";
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
                        return "Документ пустой или повреждён.";
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
                                    System.Diagnostics.Debug.WriteLine($"Остановка: найдено ключевое слово: {text}");
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
                    return "Ошибка при чтении документа.";
                }
                _oldLiteratureList = result.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                return _oldLiteratureList.Count > 0 ? string.Join("\n", _oldLiteratureList) : "Раздел 'Список литературы' не найден.";
            });
        }

        public async Task<string> SaveAsProcessedAsync(string originalPath, string bibliographyText, string flag, string suffix = "_Обработано")
        {
            if (!File.Exists(originalPath)) 
            {
                throw new FileNotFoundException("Файл не найден", originalPath);
            }
                

            string directory = Path.GetDirectoryName(originalPath)!;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);
            string timestamp = DateTime.Now.ToString("HHmmss");
            string outputPath = originalPath;
            if (!originalPath.Contains("_Обработано"))
            {
                outputPath = Path.Combine(directory, $"{fileNameWithoutExt}{suffix}_{timestamp}{extension}");
            }

            if (!string.Equals(originalPath, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => File.Copy(originalPath, outputPath, overwrite: true));
            }

            if (flag == "list")
            {
                await Task.Run(() => AppendBibliographyToDocument(outputPath, bibliographyText));
            }
                

            return outputPath;
        }
        public Task OpenAsProcessed(string originalPath)
        {
            OpenWithDefaultApp(originalPath);
            return Task.CompletedTask;
        }
        private void AppendBibliographyToDocument(string docxPath, string bibliographyText)
        {
            using var document = WordprocessingDocument.Open(docxPath, true); 
            var body = document.MainDocumentPart!.Document.Body;
            Paragraph samplePara = GetFirstBibliographyParagraph(document);
            var items = bibliographyText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            body.AppendChild(new Paragraph());
            var newItems = bibliographyText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            for(int differenceIndex = 0; differenceIndex < newItems.Count; differenceIndex++)
            {
                string newItem = newItems[differenceIndex];
                string oldItem = differenceIndex < _oldLiteratureList.Count ? _oldLiteratureList[differenceIndex] : "";

                if (samplePara != null)
                {

                    body.AppendChild(CreateDiffParagraph(newItem, oldItem, samplePara));
                }
                else
                {
                    body.AppendChild(new Paragraph(new Run(new Text(newItem))));
                }
            }

            document.MainDocumentPart.Document.Save();
        }
        private Paragraph CreateDiffParagraph(string newItem, string oldItem, Paragraph samplePara)
        {
            var paragraph = new Paragraph();

            if (samplePara.ParagraphProperties != null)
            {
                paragraph.ParagraphProperties = (ParagraphProperties)samplePara.ParagraphProperties.CloneNode(true);
            }

            RunProperties baseRunProperties = GetBaseRunProperties(samplePara);

            HashSet<string> oldWordSet = BuildNormalizedWordSet(oldItem);

            
            MatchCollection matches = Regex.Matches(
                newItem ?? "",
                @"([A-Za-zА-Яа-яЁё0-9]+(?:\.[A-Za-zА-Яа-яЁё0-9]+)*)|(\s+)|(.{1})",
                RegexOptions.CultureInvariant
            );

            foreach (Match match in matches)
            {
                string tokenText = match.Value;
                if (string.IsNullOrEmpty(tokenText))
                {
                    continue;
                }

                bool isWordToken = match.Groups[1].Success;
                bool shouldHighlight = false;

                if (isWordToken)
                {
                    string normalized = NormalizeWord(tokenText);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        shouldHighlight = !oldWordSet.Contains(normalized);
                    }
                }

                paragraph.AppendChild(MakeRun(tokenText, baseRunProperties, shouldHighlight));
            }

            return paragraph;
        }

        private static HashSet<string> BuildNormalizedWordSet(string text)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(text))
            {
                return set;
            }

            MatchCollection matches = Regex.Matches(
                text,
                @"[A-Za-zА-Яа-яЁё0-9]+(?:\.[A-Za-zА-Яа-яЁё0-9]+)*",
                RegexOptions.CultureInvariant
            );

            foreach (Match match in matches)
            {
                string normalized = NormalizeWord(match.Value);
                if (!string.IsNullOrEmpty(normalized))
                {
                    set.Add(normalized);
                }
            }

            return set;
        }

        private static string NormalizeWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return "";
            }

            string normalized = word
                .Normalize(NormalizationForm.FormKC)
                .Replace('\u00A0', ' ')
                .Trim()
                .ToLowerInvariant();

            
            normalized = normalized.Trim('.', ',', ';', ':', '!', '?', '"', '\'', '«', '»', '“', '”', '(', ')', '[', ']', '{', '}', '—', '–', '-');

            return normalized;
        }

        private static Run MakeRun(string text, RunProperties baseRunProperties, bool isInserted)
        {
            var run = new Run {
                RunProperties = (RunProperties)baseRunProperties.CloneNode(true)
            };

            if (isInserted)
            {
                run.RunProperties.Append(new Color() { Val = "00CC00" });
            }

            var textNode = new Text(text) {
                Space = SpaceProcessingModeValues.Preserve
            };

            run.AppendChild(textNode);
            return run;
        }
        private RunProperties GetBaseRunProperties(Paragraph samplePara)
        {
            var sampleRun = samplePara.Descendants<Run>().FirstOrDefault();
            return sampleRun?.RunProperties != null
                ? (RunProperties)sampleRun.RunProperties.CloneNode(true)
                : new RunProperties(new FontSize() { Val = "28" }); 
        }
        private bool AreWordsEqual(string firstString, string secondString)
        {
            var cleanFirstString = RemovePunctuation(firstString).ToLowerInvariant();
            var cleanSecondString = RemovePunctuation(secondString).ToLowerInvariant();
            return cleanFirstString == cleanSecondString;
        }

        private string RemovePunctuation(string word)
        {
            return new string(word.Where(c => !char.IsPunctuation(c)).ToArray());
        }
        private void OpenWithDefaultApp(string path)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(path) {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Не удалось открыть файл: {ex.Message}", ex);
            }
        }
        private Paragraph GetFirstBibliographyParagraph(WordprocessingDocument inputDocument, string startKeyword = "Список литературы")
        {
            var body = inputDocument.MainDocumentPart.Document.Body;
            bool foundStart = false;

            foreach (var element in body.Elements())
            {
                if (element is Paragraph paragraph)
                {
                    string text = paragraph.InnerText;

                    if (!foundStart && text.Contains(startKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        foundStart = true;
                        continue;
                    }

                    if (foundStart && !string.IsNullOrWhiteSpace(text))
                    {
                        
                        return paragraph;
                    }
                }
            }

            return null; 
        }
        private Paragraph CloneFormattedParagraph(string text, Paragraph sampleParagraph)
        {
            
            var newParagraph = new Paragraph();

            
            if (sampleParagraph.ParagraphProperties != null)
            {
                newParagraph.ParagraphProperties = (ParagraphProperties)sampleParagraph.ParagraphProperties.CloneNode(true);
            }

            
            var run = new Run(new Text(text));

            
            var sampleRun = sampleParagraph.Descendants<Run>().FirstOrDefault();
            if (sampleRun?.RunProperties != null)
            {
                run.RunProperties = (RunProperties)sampleRun.RunProperties.CloneNode(true);
            }

            newParagraph.AppendChild(run);
            return newParagraph;
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
