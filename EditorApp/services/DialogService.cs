using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace EditorApp.services
{
    public class DialogService : IDialogService
    {
        public MessageBoxResult ShowMessage(string message, string caption, MessageBoxButton buttons)
        {
            return MessageBox.Show(message, caption, buttons);
        }

        public bool? ShowOpenFileDialog(out string filePath)
        {
            filePath = "";
            var dialog = new OpenFileDialog {
                Filter = "Word Documents (*.docx)|*.docx|All files (*.*)|*.*"
            };

            var result = dialog.ShowDialog();
            if (result == true)
            {
                filePath = dialog.FileName;
            }
            return result;
        }
    }
}
