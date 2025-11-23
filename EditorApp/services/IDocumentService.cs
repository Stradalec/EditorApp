using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.services
{
    public interface IDocumentService
    {
        Task<string> ExtractBibliographyAsync(string filePath);
        Task SaveAsProcessedAsync(string originalPath, string bibliographyText, string suffix = "_Обработано");
    }
}
