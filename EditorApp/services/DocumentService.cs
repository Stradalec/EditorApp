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
                return "Файл не найден.";

            return await Task.Run(() => {
                StringBuilder result = new StringBuilder();
                bool foundBibliography = false;

                try
                {
                    using var document = WordprocessingDocument.Open(filePath, false);
                    var body = document.MainDocumentPart?.Document.Body;
                    if (body == null) return "Документ пустой или повреждён.";

                    foreach (var element in body.Elements())
                    {
                        if (element is Paragraph paragraph)
                        {
                            string text = paragraph.InnerText;

                            if (!foundBibliography &&
                                text.Contains("Список литературы", StringComparison.OrdinalIgnoreCase))
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
                throw new FileNotFoundException("Файл не найден", originalPath);

            string directory = Path.GetDirectoryName(originalPath)!;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);
            string outputPath = Path.Combine(directory, $"{fileNameWithoutExt}{suffix}{extension}");


            await Task.Run(() => File.Copy(originalPath, outputPath, overwrite: true));

            await Task.Run(() => AppendBibliographyToDocument(outputPath, bibliographyText));

            OpenWithDefaultApp(outputPath);
        }

        private void AppendBibliographyToDocument(string docxPath, string bibliographyText)
        {
            using var document = WordprocessingDocument.Open(docxPath, true); 
            var body = document.MainDocumentPart!.Document.Body;

            body.AppendChild(new Paragraph());

            body.AppendChild(new Paragraph(
                 new Run(
            new RunProperties(
                new Bold(), 
                new FontSize() { Val = "28" } 
            ),
            new Text("Список литературы")
        )
            ));
            body.AppendChild(new Paragraph()); 


            foreach (var line in bibliographyText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) ||
                    trimmedLine.StartsWith("[Элемент:") ||
                    trimmedLine.Contains("не найден", StringComparison.OrdinalIgnoreCase)) continue;

                var paragraph = new Paragraph(
                    new Run(new Text(trimmedLine))
                );

                body.AppendChild(paragraph);
            }


            document.MainDocumentPart!.Document.Save();
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
    }
}
