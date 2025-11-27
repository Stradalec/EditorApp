using DocumentFormat.OpenXml.Packaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Wordprocessing;
namespace EditorApp.services
{
    public class DocumentService : IDocumentService
    {
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

                return foundBibliography ? result.ToString().Trim() : "Раздел 'Список литературы' не найден.";
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

            foreach (var item in items)
            {
                if (samplePara != null)
                {

                    body.AppendChild(CloneFormattedParagraph(item, samplePara));
                }
                else
                {

                    body.AppendChild(new Paragraph(new Run(new Text(item))));
                }
            }

            document.MainDocumentPart.Document.Save();
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
