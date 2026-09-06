using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.services
{
    public interface IApiKeyStore
    {
        void Save(string apiKey);
        string? Load();
        void Clear();
    }
    internal class ApiKeyStore : IApiKeyStore
    {
        private readonly string _path;

        public ApiKeyStore()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EditorApp"
            );
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "api_key.bin");
        }
        public void Save(string apiKey)
        {
            var plaintext = Encoding.UTF8.GetBytes(apiKey);
            var protectedBytes = ProtectedData.Protect(
                plaintext,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );
            File.WriteAllBytes(_path, protectedBytes);
        }

        public string? Load()
        {
            if (!File.Exists(_path)) return null;

            var protectedBytes = File.ReadAllBytes(_path);
            var plaintext = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );
            var s = Encoding.UTF8.GetString(plaintext);
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        public void Clear()
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }
}
