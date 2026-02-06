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
            string serverAddress = configuration["ServerData:Address"];
            var httpClient = new HttpClient {
                BaseAddress = new Uri(serverAddress)
            };
            var keyStore = new ApiKeyStore();           
            var dialogService = new DialogService();
            var authorizationService = new AuthService(httpClient, keyStore);
            var savedKey = keyStore.Load();
            if (string.IsNullOrEmpty(savedKey))
            {
                AuthViewModel authorizationModel = new AuthViewModel(authorizationService, dialogService);
                var authorizationWindow = new AuthirizationWindow(authorizationModel);
                bool? result = authorizationWindow.ShowDialog();
                if (result != true) 
                {
                    Shutdown();
                    return;
                }
                savedKey = keyStore.Load();
                if (string.IsNullOrWhiteSpace(savedKey))
                {
                    dialogService.ShowMessage("Ключ не найден после регистрации.", "Ошибка", MessageBoxButton.OK);
                    Shutdown();
                    return;
                }
            }
            httpClient.DefaultRequestHeaders.Remove("X-API-Key");
            httpClient.DefaultRequestHeaders.Add("X-API-Key", savedKey);
            var formatService = new FormatService(httpClient);
            
            var documentService = new DocumentService();
            
            var createdViewModel = new ApplicationViewModel(documentService, dialogService, formatService);
            
            var mainWindow = new MainWindow();
            mainWindow.DataContext = createdViewModel;
            mainWindow.Show();
        }
            
    }
}
