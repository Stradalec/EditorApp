using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.source
{
    public partial class Document : ObservableObject
    {
        [ObservableProperty]
        private string documentName;


        [ObservableProperty]
        private string filePath;

        [ObservableProperty]
        private string bibliographyContent;
    }
}
