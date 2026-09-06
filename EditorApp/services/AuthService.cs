using EditorApp.source;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.services
{
    internal class AuthService : IAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly ApiKeyStore _keyStore;

        public AuthService(HttpClient httpClient, ApiKeyStore keyStore)
        {
            _httpClient = httpClient;
            _keyStore = keyStore;
        }
        public async Task<string> Register(string userName, string inviteCode)
        {
            string reason = "";
            try
            {
                var request = new RegisterRequest {
                    user_name = userName,
                    invite_code = inviteCode
                };

                var response = await _httpClient.PostAsJsonAsync("register", request);

                var bodyText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    reason = $"Регистрация не удалась: {(int)response.StatusCode} {response.ReasonPhrase}\n{bodyText}";

                    return reason;
                }

                var data = await response.Content.ReadFromJsonAsync<RegisterResponse>();
                if (data == null || string.IsNullOrWhiteSpace(data.api_key))
                {
                    reason = "Сервер вернул пустой ответ или не прислал api_key.";
                    return reason;
                }


                reason = $"Пользователь: {data.user_name}\nAPI ключ: {data.api_key}";
                _keyStore.Save(data.api_key);
                _httpClient.DefaultRequestHeaders.Remove("X-API-Key");
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", data.api_key);
                reason = "Успех";
            }
            catch (Exception ex)
            {
                reason = $"Ошибка регистрации: {ex.Message}";
            }
            return reason;
        }
    }
}
