using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.services.document
{
        internal class BibliographyExtractResult
        {
            public string Text { get; set; } = "";
            public List<string> Items { get; set; } = new();
        }
}
