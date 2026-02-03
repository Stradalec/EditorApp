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
        
        public MainWindow()
        {
            
            InitializeComponent();
           
        }

        private void HandleDragEnter(object sender, DragEventArgs dragEvent)
        {
            if (dragEvent.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] dragFiles = (string[])dragEvent.Data.GetData(DataFormats.FileDrop);
                if (dragFiles.Length == 1 && System.IO.Path.GetExtension(dragFiles[0]).ToLower() == ".docx")
                {
                    this.Background = Brushes.White;
                    dragEvent.Effects = DragDropEffects.Copy;
                }
                else
                {
                    dragEvent.Effects = DragDropEffects.None;
                }
                dragEvent.Handled = true;
            }
        }
        private void HandleDragLeave(object sender, DragEventArgs dragEvent)
        {
            if (dragEvent.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var converter = new BrushConverter();
                this.Background = (Brush)converter.ConvertFromString("#FFFFF0");
                dragEvent.Handled = true;
            }
        }
        private async void HandleDrop(object sender, DragEventArgs dragEvent)
        {
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
                    MessageBox.Show("Поддерживаются только файлы .docx", "Неподдерживаемый формат", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}
