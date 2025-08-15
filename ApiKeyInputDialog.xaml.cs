using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using SimpleSteamSwitcher.Services;

namespace SimpleSteamSwitcher
{
    public partial class ApiKeyInputDialog : Window
    {
        public string ApiKey { get; private set; } = "";
        public bool SaveKey { get; private set; } = true;
        private readonly ApiKeyService? _apiKeyService;

        public ApiKeyInputDialog(ApiKeyService? apiKeyService = null)
        {
            InitializeComponent();
            _apiKeyService = apiKeyService;
            
            // Pre-populate if there's a saved key
            if (_apiKeyService?.HasSavedApiKey() == true)
            {
                var maskedKey = _apiKeyService.GetMaskedApiKey();
                CurrentKeyText.Text = $"Current API key: {maskedKey}";
                
                // Pre-fill the text box with the actual key for easy editing
                var savedKey = _apiKeyService.LoadApiKey();
                if (!string.IsNullOrEmpty(savedKey))
                {
                    ApiKeyTextBox.Text = savedKey;
                }
            }
            else
            {
                CurrentKeyText.Text = "No API key currently saved";
            }
            
            ApiKeyTextBox.Focus();
            ApiKeyTextBox.SelectAll();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ApiKey = ApiKeyTextBox.Text.Trim();
            SaveKey = SaveApiKeyCheckBox.IsChecked == true;
            
            if (string.IsNullOrEmpty(ApiKey))
            {
                MessageBox.Show("Please enter your Steam Web API key.", "API Key Required", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ApiKey.Length != 32)
            {
                MessageBox.Show("Steam Web API keys are typically 32 characters long. Please verify your key.", 
                    "Invalid API Key Format", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open browser: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            e.Handled = true;
        }
    }
} 