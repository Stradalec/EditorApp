using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace EditorApp.source
{
    partial class ApplicationViewModel : ObservableObject
    {
        [ObservableProperty]
        private Document selectedDocument;
        [RelayCommand]
        private void LoadDocument()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "Word Documents (*.docx)|*.docx|All files (*.*)|*.*";
            if (dialog.ShowDialog() == true)
            {
                SelectedDocument = new Document {
                    DocumentName = System.IO.Path.GetFileName(dialog.FileName)
                };
            }
        }

        public ApplicationViewModel()
        {
            SelectedDocument = new Document();
        }
    }
}
