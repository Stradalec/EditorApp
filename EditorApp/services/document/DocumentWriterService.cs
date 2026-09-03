using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.services.document
{
    internal class DocumentWriterService
    {
        private readonly DocumentListDiffService _diffService = new DocumentListDiffService();
        public DocumentWriterService(DocumentListDiffService diffService)
        {
            _diffService = diffService;
        }
        public void AppendBibliographyToDocument(string docxPath, string bibliographyText, List<string> _oldLiteratureList)
        {
            using var document = WordprocessingDocument.Open(docxPath, true);
            var body = document.MainDocumentPart!.Document.Body;
            Paragraph samplePara = GetFirstBibliographyParagraph(document);
            body.AppendChild(new Paragraph());
            var newItems = bibliographyText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            for (int differenceIndex = 0; differenceIndex < newItems.Count; differenceIndex++)
            {
                string newItem = newItems[differenceIndex];
                string oldItem = differenceIndex < _oldLiteratureList.Count ? _oldLiteratureList[differenceIndex] : "";

                if (samplePara != null)
                {

                    body.AppendChild(_diffService.CreateDiffParagraph(newItem, oldItem, samplePara));
                }
                else
                {
                    body.AppendChild(new Paragraph(new Run(new Text(newItem))));
                }
            }

            document.MainDocumentPart.Document.Save();
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
