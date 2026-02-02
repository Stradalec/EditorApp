using EditorApp.services;
using EditorApp.source;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.Http;
namespace EditorApp
{

    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        
        public MainWindow()
        {
            var builder = new ConfigurationBuilder().SetBasePath(AppDomain.CurrentDomain.BaseDirectory).AddJsonFile("appSettings.json", optional: false, reloadOnChange: true);
            IConfigurationRoot configuration = builder.Build();
            string apiKey = configuration["ServerData:ApiKey"];
            string serverAddress = configuration["ServerData:Address"];
            var httpClient = new HttpClient {
                BaseAddress = new Uri(serverAddress) 
            };
            httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            var formatService = new FormatService(httpClient);
            var dialogService = new DialogService();
            var documentService = new DocumentService();
            InitializeComponent();
            DataContext = new ApplicationViewModel(documentService, dialogService, formatService);
        }

        private void HandleDrag(object sender, DragEventArgs dragEvent)
        {
            if (dragEvent.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] dragFiles = (string[])dragEvent.Data.GetData(DataFormats.FileDrop);
                if (dragFiles.Length == 1 && System.IO.Path.GetExtension(dragFiles[0]).ToLower() == ".docx")
                {
                    dragEvent.Effects = DragDropEffects.Copy;
                }
                else
                {
                    dragEvent.Effects = DragDropEffects.None;
                }
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
