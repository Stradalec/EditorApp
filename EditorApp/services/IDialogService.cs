using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace EditorApp.services
{
    public interface IDialogService
    {
        MessageBoxResult ShowMessage(string message, string caption, MessageBoxButton buttons);
        bool? ShowOpenFileDialog(out string filePath);
    }
}
