using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SimpleSteamSwitcher.Services
{
    public class ApiKeyService
    {
        private readonly LogService _logger;
        private readonly string _configPath;
        private const string API_KEY_FILE = "api_key.dat";

        public ApiKeyService()
        {
            _logger = new LogService();
            _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleSteamSwitcher");
            
            // Ensure the config directory exists
            if (!Directory.Exists(_configPath))
            {
                Directory.CreateDirectory(_configPath);
            }
        }

        public void SaveApiKey(string apiKey)
        {
            try
            {
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("Attempted to save empty API key");
                    return;
                }

                var apiKeyFilePath = Path.Combine(_configPath, API_KEY_FILE);
                
                // Convert the API key to bytes
                var data = Encoding.UTF8.GetBytes(apiKey);
                
                // Encrypt the data using Windows DPAPI (Data Protection API)
                // This ensures only the current user on this machine can decrypt it
                var encryptedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                
                // Save the encrypted data to file
                File.WriteAllBytes(apiKeyFilePath, encryptedData);
                
                _logger.LogSuccess("Steam Web API key saved securely");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save API key: {ex.Message}");
                throw;
            }
        }

        public string? LoadApiKey()
        {
            try
            {
                var apiKeyFilePath = Path.Combine(_configPath, API_KEY_FILE);
                
                if (!File.Exists(apiKeyFilePath))
                {
                    _logger.LogInfo("No saved API key found");
                    return null;
                }

                // Read the encrypted data
                var encryptedData = File.ReadAllBytes(apiKeyFilePath);
                
                // Decrypt the data using Windows DPAPI
                var decryptedData = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
                
                // Convert back to string
                var apiKey = Encoding.UTF8.GetString(decryptedData);
                
                _logger.LogSuccess("Steam Web API key loaded successfully");
                return apiKey;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load API key: {ex.Message}");
                return null;
            }
        }

        public bool HasSavedApiKey()
        {
            var apiKeyFilePath = Path.Combine(_configPath, API_KEY_FILE);
            return File.Exists(apiKeyFilePath);
        }

        public void DeleteApiKey()
        {
            try
            {
                var apiKeyFilePath = Path.Combine(_configPath, API_KEY_FILE);
                
                if (File.Exists(apiKeyFilePath))
                {
                    File.Delete(apiKeyFilePath);
                    _logger.LogSuccess("API key deleted successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to delete API key: {ex.Message}");
                throw;
            }
        }

        public string GetMaskedApiKey()
        {
            var apiKey = LoadApiKey();
            if (string.IsNullOrEmpty(apiKey))
                return "Not set";
            
            if (apiKey.Length <= 8)
                return new string('*', apiKey.Length);
            
            // Show first 4 and last 4 characters, mask the middle
            return $"{apiKey.Substring(0, 4)}{"".PadLeft(apiKey.Length - 8, '*')}{apiKey.Substring(apiKey.Length - 4)}";
        }
    }
} 