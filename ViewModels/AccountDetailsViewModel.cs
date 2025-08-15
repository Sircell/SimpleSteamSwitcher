using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SimpleSteamSwitcher.ViewModels
{
    public class AccountDetailsViewModel : INotifyPropertyChanged
    {
        private string _detailsText;
        private ICommand _copyAllCommand;
        private ICommand _closeCommand;

        public string DetailsText
        {
            get => _detailsText;
            set
            {
                _detailsText = value;
                OnPropertyChanged();
            }
        }

        public ICommand CopyAllCommand
        {
            get => _copyAllCommand;
            set
            {
                _copyAllCommand = value;
                OnPropertyChanged();
            }
        }

        public ICommand CloseCommand
        {
            get => _closeCommand;
            set
            {
                _closeCommand = value;
                OnPropertyChanged();
            }
        }

        public AccountDetailsViewModel()
        {
            CopyAllCommand = new RelayCommand(CopyAllText);
            CloseCommand = new RelayCommand(CloseWindow);
        }

        private void CopyAllText()
        {
            try
            {
                if (!string.IsNullOrEmpty(DetailsText))
                {
                    Clipboard.SetText(DetailsText);
                    MessageBox.Show("Account details copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy text: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseWindow()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var window = Application.Current.Windows.OfType<AccountDetailsWindow>().FirstOrDefault();
                window?.Close();
            });
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 