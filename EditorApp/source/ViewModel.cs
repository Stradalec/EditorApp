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

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool> _canExecute;

    public RelayCommand(Action execute, Func<bool> canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object parameter) => _execute();
    public event EventHandler CanExecuteChanged {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }
}
namespace EditorApp.source
{
    class ApplicationViewModel : INotifyPropertyChanged
    {
        private Document selectedDocument;

        public event PropertyChangedEventHandler PropertyChanged;
        public ICommand LoadDocumentCommand { get; private set; }   
        public Document SelectedDocument {
            get { return selectedDocument; }
            set {
                selectedDocument = value;
                OnPropertyChanged("SelectedDocument");
            }
        }
        private void LoadDocument()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "Documents (*.docx)|*.docx";
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
            LoadDocumentCommand = new RelayCommand(LoadDocument);
        }
        public void OnPropertyChanged([CallerMemberName] string prop = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(prop));
        }
    }
}
