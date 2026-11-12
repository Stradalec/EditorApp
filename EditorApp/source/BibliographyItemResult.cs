using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.source
{
    public sealed record BibliographyItemResult(
            string OriginalText,
            string OutputText,
            bool HighlightAmber);
}
