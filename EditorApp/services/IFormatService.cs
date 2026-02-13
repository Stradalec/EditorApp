using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EditorApp.services
{
    public  interface IFormatService
    {
        Task<string> FormatBibliographyAsync(string rawText, IProgress<(int current, int total)> progress, CancellationToken cancellationToken);
        Task<List<(string text, string url, bool isAlive)>> CheckLinksAsync(string filePath);
    }
}
