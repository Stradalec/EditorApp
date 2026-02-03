using EditorApp.services;
using EditorApp.source;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace EditorApp
{
    /// <summary>
    /// Логика взаимодействия для App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs startupEvent)
        {
            base.OnStartup(startupEvent);
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
            var createdViewModel = new ApplicationViewModel(documentService, dialogService, formatService);
            var mainWindow = new MainWindow();
            mainWindow.DataContext = createdViewModel;
            mainWindow.Show();
        }
            
    }
}
