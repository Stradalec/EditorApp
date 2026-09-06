using EditorApp.source;
using System.Windows;
using System.Windows.Media;
namespace EditorApp
{

    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public bool IsDragOver {
            get { return (bool)GetValue(IsDragOverProperty); }
            set { SetValue(IsDragOverProperty, value); }
        }

        public static readonly DependencyProperty IsDragOverProperty = DependencyProperty.Register(nameof(IsDragOver),typeof(bool),typeof(MainWindow),new PropertyMetadata(false));
        public MainWindow()
        {
            
            InitializeComponent();
           
        }

        private void HandleDragEnter(object sender, DragEventArgs dragEvent)
        {
            SetDragState(dragEvent);
        }

        private void HandleDragOver(object sender, DragEventArgs dragEvent)
        {
            SetDragState(dragEvent);
        }

        private void HandleDragLeave(object sender, DragEventArgs dragEvent)
        {
            IsDragOver = false;
            dragEvent.Handled = true;
        }
        private async void HandleDrop(object sender, DragEventArgs dragEvent)
        {
            IsDragOver = false;

            if (dragEvent.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])dragEvent.Data.GetData(DataFormats.FileDrop);

                if (files.Length == 1 && System.IO.Path.GetExtension(files[0]).ToLower() == ".docx")
                {
                    var viewModel = DataContext as ApplicationViewModel;
                    if (viewModel != null)
                    {
                        await viewModel.LoadDocumentCommand.ExecuteAsync(files[0]);
                    }
                }
                else
                {
                    MessageBox.Show(
                        "Поддерживаются только файлы .docx",
                        "Неподдерживаемый формат",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            dragEvent.Handled = true;
        }
        private void SetDragState(DragEventArgs dragEvent)
        {
            bool canAcceptDocx = false;

            if (dragEvent.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] dragFiles = (string[])dragEvent.Data.GetData(DataFormats.FileDrop);

                if (dragFiles.Length == 1)
                {
                    string extension = System.IO.Path.GetExtension(dragFiles[0]).ToLower();
                    if (extension == ".docx")
                    {
                        canAcceptDocx = true;
                    }
                }
            }

            IsDragOver = canAcceptDocx;
            dragEvent.Effects = canAcceptDocx ? DragDropEffects.Copy : DragDropEffects.None;
            dragEvent.Handled = true;
        }
    }
}
