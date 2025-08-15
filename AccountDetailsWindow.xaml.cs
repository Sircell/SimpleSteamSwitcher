using System.Windows;
using SimpleSteamSwitcher.ViewModels;

namespace SimpleSteamSwitcher
{
    public partial class AccountDetailsWindow : Window
    {
        public AccountDetailsWindow()
        {
            InitializeComponent();
            DataContext = new AccountDetailsViewModel();
        }
    }
} 