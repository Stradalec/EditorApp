using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EditorApp.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace EditorApp.source
{
    public partial class AuthViewModel : ObservableObject
    {
        private readonly IAuthService _authService;
        private readonly IDialogService _dialogService;

        [ObservableProperty]
        private string userName = "";

        [ObservableProperty]
        private string inviteCode = "";

        public AuthViewModel(IAuthService authService, IDialogService dialogService)
        {
            _authService = authService;
            _dialogService = dialogService;
        }

        [RelayCommand]
        private async Task Register()
        {
            var result = await _authService.Register(userName, inviteCode);
            if (result != "Успех")
            {
                _dialogService.ShowMessage(result, "Ошибка регистрации", MessageBoxButton.OK);
                return;
            }

            OnRegisterSuccess?.Invoke();
        }

        public event Action? OnRegisterSuccess;
    }
}
