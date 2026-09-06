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
        Task<string> SaveAsProcessedAsync(string originalPath, string bibliographyText, string flag, string suffix = "_Обработано");
        Task  OpenAsProcessed(string originalPath);
    }
}
