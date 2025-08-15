using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using SimpleSteamSwitcher.ViewModels;
using SimpleSteamSwitcher.Models;

namespace SimpleSteamSwitcher
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            
            // Set up password box binding
            Loaded += MainWindow_Loaded;
            
            // Set up responsive design
            SizeChanged += MainWindow_SizeChanged;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Bind password box to view model
            LoginPasswordBox.PasswordChanged += (s, args) =>
            {
                _viewModel.LoginPassword = LoginPasswordBox.Password;
            };

            // Listen for property changes to trigger filtering
            _viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(_viewModel.GameSearchText))
                {
                    ApplyFiltersAndPagination();
                }
            };

            // Listen for collection changes to update owner filter
            _viewModel.AllGames.CollectionChanged += (s, args) =>
            {
                PopulateOwnerFilter();
                ApplyFiltersAndPagination();
            };
            
            // Apply initial responsive layout
            ApplyResponsiveLayout();
        }
        
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayout();
        }
        
        private void ApplyResponsiveLayout()
        {
            if (HeaderButtonsPanel == null || FiltersGrid == null) return;
            
            double windowWidth = ActualWidth;
            
            // Responsive header buttons layout
            if (windowWidth < 900)
            {
                // Stack buttons vertically for narrow windows
                HeaderButtonsPanel.Orientation = Orientation.Vertical;
                HeaderButtonsPanel.HorizontalAlignment = HorizontalAlignment.Right;
                
                // Adjust button margins for vertical layout
                foreach (var child in HeaderButtonsPanel.Children)
                {
                    if (child is Button button)
                    {
                        button.Margin = new Thickness(0, 2, 0, 2);
                        button.HorizontalAlignment = HorizontalAlignment.Right;
                    }
                }
            }
            else
            {
                // Horizontal layout for wider windows
                HeaderButtonsPanel.Orientation = Orientation.Horizontal;
                HeaderButtonsPanel.HorizontalAlignment = HorizontalAlignment.Right;
                
                // Restore horizontal margins
                foreach (var child in HeaderButtonsPanel.Children)
                {
                    if (child is Button button)
                    {
                        button.Margin = new Thickness(0, 0, 10, 0);
                        button.HorizontalAlignment = HorizontalAlignment.Stretch;
                    }
                }
            }
            
            // Responsive filters layout
            if (windowWidth < 1200)
            {
                // For medium windows, stack some filters
                if (windowWidth < 1000)
                {
                    // For narrow windows, stack all filters vertically
                    FiltersGrid.RowDefinitions.Clear();
                    FiltersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    FiltersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    FiltersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    
                    // Move filters to appropriate rows
                    Grid.SetRow(GameTypeFilter.Parent as StackPanel, 0);
                    Grid.SetRow(OwnerFilter.Parent as StackPanel, 0);
                    Grid.SetRow(SortFilter.Parent as StackPanel, 0);
                    Grid.SetRow(InstalledFilter.Parent as StackPanel, 1);
                    Grid.SetRow(PageSizeFilter.Parent as StackPanel, 1);
                    Grid.SetRow(PrevPageButton.Parent as StackPanel, 2);
                }
                else
                {
                    // For medium windows, use 2 rows
                    FiltersGrid.RowDefinitions.Clear();
                    FiltersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    FiltersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    
                    // First row: Type, Owner, Sort
                    Grid.SetRow(GameTypeFilter.Parent as StackPanel, 0);
                    Grid.SetRow(OwnerFilter.Parent as StackPanel, 0);
                    Grid.SetRow(SortFilter.Parent as StackPanel, 0);
                    
                    // Second row: Installed, PageSize, Pagination
                    Grid.SetRow(InstalledFilter.Parent as StackPanel, 1);
                    Grid.SetRow(PageSizeFilter.Parent as StackPanel, 1);
                    Grid.SetRow(PrevPageButton.Parent as StackPanel, 1);
                }
            }
            else
            {
                // For wide windows, use single row layout
                FiltersGrid.RowDefinitions.Clear();
                FiltersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                
                // Reset all filters to first row
                Grid.SetRow(GameTypeFilter.Parent as StackPanel, 0);
                Grid.SetRow(OwnerFilter.Parent as StackPanel, 0);
                Grid.SetRow(SortFilter.Parent as StackPanel, 0);
                Grid.SetRow(InstalledFilter.Parent as StackPanel, 0);
                Grid.SetRow(PageSizeFilter.Parent as StackPanel, 0);
                Grid.SetRow(PrevPageButton.Parent as StackPanel, 0);
            }
            
            // Handle very small windows with compact mode
            if (windowWidth < 800)
            {
                ApplyCompactMode();
            }
            else
            {
                RemoveCompactMode();
            }
        }
        
        private void ApplyCompactMode()
        {
            // Reduce padding and margins for compact mode
            if (FiltersGrid != null)
            {
                FiltersGrid.Margin = new Thickness(0, 5, 0, 5);
            }
            
            // Adjust search box width
            var searchBox = FindName("GameSearchBox") as TextBox;
            if (searchBox != null)
            {
                searchBox.MaxWidth = 250;
            }
            
            // Make filter dropdowns smaller
            if (GameTypeFilter != null) GameTypeFilter.Width = 100;
            if (OwnerFilter != null) OwnerFilter.Width = 120;
            if (SortFilter != null) SortFilter.Width = 100;
            if (InstalledFilter != null) InstalledFilter.Width = 100;
            if (PageSizeFilter != null) PageSizeFilter.Width = 70;
        }
        
        private void RemoveCompactMode()
        {
            // Restore normal padding and margins
            if (FiltersGrid != null)
            {
                FiltersGrid.Margin = new Thickness(0, 10, 0, 0);
            }
            
            // Restore normal sizes
            if (GameTypeFilter != null) GameTypeFilter.Width = 120;
            if (OwnerFilter != null) OwnerFilter.Width = 150;
            if (SortFilter != null) SortFilter.Width = 120;
            if (InstalledFilter != null) InstalledFilter.Width = 120;
            if (PageSizeFilter != null) PageSizeFilter.Width = 80;
        }
        
        private void GameItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border && border.DataContext is SimpleSteamSwitcher.Models.Game game)
            {
                // Find the account that owns this game
                var ownerAccount = _viewModel?.Accounts?.FirstOrDefault(a => a.SteamId == game.OwnerSteamId);
                
                if (ownerAccount != null)
                {
                    // Switch to the Accounts tab first
                    _viewModel?.ShowAccountsTabCommand?.Execute(null);
                    
                    // Execute the switch command
                    if (_viewModel?.SwitchToAccountCommand?.CanExecute(ownerAccount) == true)
                    {
                        // Set the pending game launch so it auto-starts after the switch
                        _viewModel.SetPendingGameToLaunch(game.AppId);
                        _viewModel.SwitchToAccountCommand.Execute(ownerAccount);
                    }
                }
            }
        }

        private void LaunchF2PGame_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var game = button?.DataContext as Game;
                
                if (game != null)
                {
                    // Find the ComboBox in the same StackPanel
                    var stackPanel = button?.Parent as StackPanel;
                    var comboBox = stackPanel?.Children.OfType<ComboBox>().FirstOrDefault();
                    var selectedAccount = comboBox?.SelectedItem as SteamAccount;
                    
                    if (selectedAccount != null)
                    {
                        // Execute the command with game and selected account
                        var args = new object[] { game, selectedAccount };
                        (_viewModel.LaunchF2PGameCommand as RelayCommand<object>)?.Execute(args);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LaunchF2PGame_Click: {ex.Message}");
            }
        }

        public void ClearPasswordBox()
        {
            LoginPasswordBox.Password = string.Empty;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        // Pagination and filtering state
        private int _currentPage = 1;
        private int _pageSize = 25;
        private string _gameTypeFilter = "All Games";
        private string _ownerFilter = "All Owners";
        private string _installedFilter = "All";
        private string _sortOption = "Name A-Z";

        private void GameTypeFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (GameTypeFilter?.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                _gameTypeFilter = selectedItem.Content.ToString() ?? "All Games";
                _currentPage = 1;
                ApplyFiltersAndPagination();
            }
        }

        		private void OwnerFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
		{
			if (OwnerFilter?.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
			{
				// Use Tag as the filter key when present to preserve existing filtering behavior
				var key = selectedItem.Tag?.ToString() ?? selectedItem.Content?.ToString() ?? "All Owners";
				_ownerFilter = key;
				_currentPage = 1;
				ApplyFiltersAndPagination();
			}
		}

        private void SortFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (SortFilter?.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                _sortOption = selectedItem.Content.ToString() ?? "Name A-Z";
                _currentPage = 1;
                ApplyFiltersAndPagination();
            }
        }

        private void PageSizeFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selected = (PageSizeFilter.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
            _pageSize = selected == "All" ? int.MaxValue : int.Parse(selected ?? "25");
            _currentPage = 1;
            ApplyFiltersAndPagination();
        }

        private void InstalledFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _installedFilter = (InstalledFilter.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "All";
            _currentPage = 1;
            ApplyFiltersAndPagination();
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                ApplyFiltersAndPagination();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = GetTotalPages();
            if (_currentPage < totalPages)
            {
                _currentPage++;
                ApplyFiltersAndPagination();
            }
        }

        private void ApplyFiltersAndPagination()
        {
            if (_viewModel?.AllGames == null) return;

            var filteredGames = _viewModel.AllGames.AsEnumerable();

            // Apply search filter
            var searchTerm = _viewModel.GameSearchText?.ToLower() ?? "";
            if (!string.IsNullOrEmpty(searchTerm))
            {
                filteredGames = filteredGames.Where(g => 
                    g.Name.ToLower().Contains(searchTerm) ||
                    g.OwnerDisplay.ToLower().Contains(searchTerm) ||
                    g.GameType.ToLower().Contains(searchTerm));
            }

            // Apply type filter
            if (_gameTypeFilter == "Paid Only")
                filteredGames = filteredGames.Where(g => g.IsPaid);
            else if (_gameTypeFilter == "Free-to-Play")
                filteredGames = filteredGames.Where(g => !g.IsPaid);

            // Installed filter
            if (_installedFilter == "Installed Only")
                filteredGames = filteredGames.Where(g => g.IsInstalled);
            else if (_installedFilter == "Not Installed")
                filteredGames = filteredGames.Where(g => !g.IsInstalled);

            			// Apply owner filter (owner-aware across deduped games)
			if (_ownerFilter != "All Owners")
			{
				// Apply owner filter
				var selectedKey = _ownerFilter;
				var account = _viewModel.Accounts.FirstOrDefault(a =>
					string.Equals(a.AccountName, selectedKey, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(a.PersonaName, selectedKey, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(a.SteamId, selectedKey, StringComparison.OrdinalIgnoreCase));
				var steamId = account?.SteamId;
				filteredGames = filteredGames.Where(g =>
					string.Equals(g.OwnerDisplay, selectedKey, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(g.OwnerAccountName, selectedKey, StringComparison.OrdinalIgnoreCase) ||
					(!string.IsNullOrEmpty(steamId) && g.OwnerSteamIds != null && g.OwnerSteamIds.Contains(steamId)) ||
					(g.OwnerAccountNames != null && g.OwnerAccountNames.Contains(selectedKey)) ||
					(!g.IsPaid && !string.IsNullOrEmpty(steamId) && g.AvailableAccounts != null && g.AvailableAccounts.Any(a => a.SteamId == steamId))
				);
			}

            // Apply sorting
            filteredGames = _sortOption switch
            {
                "Name Z-A" => filteredGames.OrderByDescending(g => g.Name),
                "Playtime ↓" => filteredGames.OrderByDescending(g => g.PlaytimeForever),
                "Playtime ↑" => filteredGames.OrderBy(g => g.PlaytimeForever),
                _ => filteredGames.OrderBy(g => g.Name) // Default: Name A-Z
            };

            var gamesList = filteredGames.ToList();

            // Update pagination info
            var totalGames = gamesList.Count;
            var totalPages = GetTotalPages(totalGames);
            
            // Ensure current page is valid
            if (_currentPage > totalPages && totalPages > 0)
                _currentPage = totalPages;

            // Apply pagination
            var pagedGames = _pageSize == int.MaxValue ? 
                gamesList : 
                gamesList.Skip((_currentPage - 1) * _pageSize).Take(_pageSize);

            // Update UI
            _viewModel.FilteredGames.Clear();
            foreach (var game in pagedGames)
            {
                _viewModel.FilteredGames.Add(game);
            }

            // Update pagination controls
            UpdatePaginationControls(totalGames, totalPages);
        }

        private int GetTotalPages(int? totalGames = null)
        {
            if (_viewModel?.AllGames == null) return 1;
            
            var count = totalGames ?? _viewModel.AllGames.Count;
            return _pageSize == int.MaxValue ? 1 : (int)Math.Ceiling((double)count / _pageSize);
        }

        private void UpdatePaginationControls(int totalGames, int totalPages)
        {
            if (PageInfoText != null)
            {
                if (_pageSize == int.MaxValue)
                    PageInfoText.Text = $"{totalGames} games";
                else
                    PageInfoText.Text = $"Page {_currentPage} of {totalPages}";
            }

            if (PrevPageButton != null)
                PrevPageButton.IsEnabled = _currentPage > 1;

            if (NextPageButton != null)
                NextPageButton.IsEnabled = _currentPage < totalPages && _pageSize != int.MaxValue;
        }

        		private void PopulateOwnerFilter()
		{
			if (_viewModel == null || OwnerFilter == null) return;

			var currentSelectionKey = _ownerFilter;
			OwnerFilter.Items.Clear();
			OwnerFilter.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "All Owners", Tag = "All Owners" });

			// Build a fast lookup of existing owner keys from the games list (these are the exact values used by filtering)
			var ownerKeys = _viewModel.AllGames
				.Select(g => g.OwnerDisplay)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			// Populate the dropdown from saved accounts, but always show AccountName as the label (never persona)
			// For Tag (filter key), try to find a matching key from games for this account; fallback to AccountName
			foreach (var acc in _viewModel.Accounts.OrderBy(a => a.AccountName, StringComparer.OrdinalIgnoreCase))
			{
				if (string.IsNullOrWhiteSpace(acc.AccountName) && string.IsNullOrWhiteSpace(acc.SteamId))
					continue;

				var label = acc.AccountName; // display only account name
				// Try to find an OwnerDisplay used by any game belonging to this account
				var tagKey = ownerKeys.FirstOrDefault(k =>
					string.Equals(k, acc.AccountName, StringComparison.OrdinalIgnoreCase) ||
					(!string.IsNullOrEmpty(acc.SteamId) && _viewModel.AllGames.Any(g => g.OwnerSteamId == acc.SteamId && g.OwnerDisplay == k))
				);
				if (string.IsNullOrEmpty(tagKey))
					tagKey = acc.AccountName; // fallback; selecting this may yield zero games if none exist

				OwnerFilter.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = label, Tag = tagKey });
			}

			// Restore selection by key (Tag)
			for (int i = 0; i < OwnerFilter.Items.Count; i++)
			{
				if (OwnerFilter.Items[i] is System.Windows.Controls.ComboBoxItem item && 
					(item.Tag?.ToString() ?? item.Content?.ToString()) == currentSelectionKey)
				{
					OwnerFilter.SelectedIndex = i;
					break;
				}
			}
		}
    }
} 