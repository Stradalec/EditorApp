using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EditorApp.services.document
{
    internal class DocumentListDiffService
    {
        public Paragraph CreateDiffParagraph(string newItem, string oldItem, Paragraph samplePara)
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
    }
}
