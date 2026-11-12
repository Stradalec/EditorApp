using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EditorApp.source;

namespace EditorApp.services.document
{
    internal class DocumentWriterService
    {
        private readonly DocumentListDiffService _diffService;
        public DocumentWriterService(DocumentListDiffService diffService)
        {
            _diffService = diffService;
        }
        public void AppendBibliographyToDocument(
            string docxPath,
            IReadOnlyList<BibliographyItemResult> bibliography)
        {
            using var document = WordprocessingDocument.Open(docxPath, true);
            var mainPart = document.MainDocumentPart!;
            var body = mainPart.Document.Body!;
            var sample = GetFirstBibliographyParagraph(document);
            var finalSection = body.Elements<SectionProperties>().LastOrDefault();

            body.InsertBefore(new Paragraph(), finalSection);

            foreach (var item in bibliography)
            {
                var paragraph = sample != null
                    ? _diffService.CreateDiffParagraph(
                        item.OutputText, item.OriginalText, sample)
                    : new Paragraph(new Run(new Text(item.OutputText)));

                if (item.HighlightAmber)
                {
                    foreach (var run in paragraph.Descendants<Run>())
                    {
                        run.RunProperties ??= new RunProperties();
                        run.RunProperties.Color = new Color { Val = "B45309" };
                    }
                }

                body.InsertBefore(paragraph, finalSection);
            }

            mainPart.Document.Save();
        }

        private Paragraph GetFirstBibliographyParagraph(WordprocessingDocument inputDocument)
        {
            var body = inputDocument.MainDocumentPart.Document.Body;
            bool foundStart = false;

            foreach (var element in body.Elements())
            {
                if (element is Paragraph paragraph)
                {
                    string text = paragraph.InnerText;
                    string[] keywords = { "Список литературы", "Библиографический список", "Список источников", "Список использованных источников", "список использованной литературы" };
                    bool containsList = keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                    if (!foundStart && containsList)
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
        
    }
}
