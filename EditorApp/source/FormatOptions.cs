using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.source
{
    public partial class FormatOptions : ObservableObject
    {
        [ObservableProperty]
        private bool isFormatActive;


        [ObservableProperty]
        private bool isReferencesActive;

    }
}
