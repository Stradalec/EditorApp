using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.services
{
    public  interface IFormatService
    {
        Task<string> FormatBibliographyAsync(string rawText);
    }
}
