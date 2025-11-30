using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace EditorApp.services
{
    public class DocumentService : IDocumentService
    {
        private List<string> _oldLiteratureList = new List<string>();
        public async Task<string> ExtractBibliographyAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return "Файл не найден.";
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
                        if (element is Paragraph paragraph)
                        {
                            string text = paragraph.InnerText;
                            string[] keywords = { "Список литературы", "Библиографический список", "Список источников", "Список использованных источников", "список использованной литературы" };
                            bool containsList = keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

                            if (!foundBibliography && containsList)
                            {
                                foundBibliography = true;
                                continue;
                            }

                            if (foundBibliography)
                            {
                                result.AppendLine(text.Trim());
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

        public async Task SaveAsProcessedAsync(string originalPath, string bibliographyText, string suffix = "_Обработано")
        {
            if (!File.Exists(originalPath)) 
            {
                throw new FileNotFoundException("Файл не найден", originalPath);
            }
                

            string directory = Path.GetDirectoryName(originalPath)!;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);
            string timestamp = DateTime.Now.ToString("HHmmss");
            string outputPath = Path.Combine(directory, $"{fileNameWithoutExt}{suffix}_{timestamp}{extension}");


            await Task.Run(() => File.Copy(originalPath, outputPath, overwrite: true));

            await Task.Run(() => AppendBibliographyToDocument(outputPath, bibliographyText));

            OpenWithDefaultApp(outputPath);
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

            var baseRunProps = GetBaseRunProperties(samplePara);
            var oldWords = oldItem.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var newWords = newItem.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int oldIndex = 0;

            for (int differenceIndex = 0; differenceIndex < newWords.Length; differenceIndex++)
            {
                string word = newWords[differenceIndex];

                bool isMatch = oldIndex < oldWords.Length && AreWordsEqual(word, oldWords[oldIndex]);


                var run = new Run();
                run.RunProperties = (RunProperties)baseRunProps.CloneNode(true); 


                if (isMatch)
                {
                    oldIndex++;
                }
                else
                {
                    run.RunProperties.Append(new Color() { Val = "00CC00" }); 
                }

                run.AppendChild(new Text(word + (differenceIndex   < newWords.Length - 1 ? " " : ""))); 
                paragraph.AppendChild(run);
                if (differenceIndex < newWords.Length - 1)
                {
                    var space = new Text(" ");
                    space.Space = SpaceProcessingModeValues.Preserve;
                    run.AppendChild(space);
                }
 
            }
            return paragraph;
        }
        private RunProperties GetBaseRunProperties(Paragraph samplePara)
        {
            var sampleRun = samplePara.Descendants<Run>().FirstOrDefault();
            return sampleRun?.RunProperties != null
                ? (RunProperties)sampleRun.RunProperties.CloneNode(true)
                : new RunProperties(new FontSize() { Val = "24" }); 
        }
        private bool AreWordsEqual(string a, string b)
        {
            var cleanA = RemovePunctuation(a).ToLowerInvariant();
            var cleanB = RemovePunctuation(b).ToLowerInvariant();
            return cleanA == cleanB;
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
    }
}
