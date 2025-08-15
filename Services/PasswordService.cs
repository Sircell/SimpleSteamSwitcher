using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SimpleSteamSwitcher.Services
{
    public class PasswordService
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;

        public PasswordService()
        {
            // Generate a machine-specific key based on hardware
            var machineKey = Environment.MachineName + Environment.UserName + Environment.ProcessorCount.ToString();
            var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(machineKey));
            
            _key = new byte[32]; // 256-bit key
            _iv = new byte[16];  // 128-bit IV
            
            Array.Copy(keyBytes, 0, _key, 0, 32);
            Array.Copy(keyBytes, 0, _iv, 0, 16);
        }

        public string EncryptPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return string.Empty;

            try
            {
                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = _iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var encryptor = aes.CreateEncryptor();
                using var msEncrypt = new MemoryStream();
                using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
                using var swEncrypt = new StreamWriter(csEncrypt);
                
                swEncrypt.Write(password);
                swEncrypt.Close();
                
                return Convert.ToBase64String(msEncrypt.ToArray());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error encrypting password: {ex.Message}");
                return string.Empty;
            }
        }

        public string DecryptPassword(string encryptedPassword)
        {
            if (string.IsNullOrEmpty(encryptedPassword))
                return string.Empty;

            try
            {
                var cipherBytes = Convert.FromBase64String(encryptedPassword);

                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = _iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                using var msDecrypt = new MemoryStream(cipherBytes);
                using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using var srDecrypt = new StreamReader(csDecrypt);
                
                return srDecrypt.ReadToEnd();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error decrypting password: {ex.Message}");
                return string.Empty;
            }
        }

        public bool IsPasswordValid(string password)
        {
            return !string.IsNullOrWhiteSpace(password) && password.Length >= 1;
        }
    }
} 