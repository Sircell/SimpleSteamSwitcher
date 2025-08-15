using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using SimpleSteamSwitcher.Models;
using SimpleSteamSwitcher.Services;
using SimpleSteamSwitcher.ViewModels;
using SimpleSteamSwitcher;
using Newtonsoft.Json;
using System.Text;
using System.Diagnostics;
using System.Threading;

namespace SimpleSteamSwitcher.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private SteamService _steamService;
        private LogService _logger;
        private ApiKeyService _apiKeyService;
        private GameCacheService _gameCacheService;
        private SteamWebApiService? _steamWebApiService;
        private static bool _isSwitchingInProgress = false;
        private SteamAccount? _currentAccount;
        private bool _isLoading;
        private string _statusMessage = "Ready";
        private bool _isAccountsTabVisible = true;
        private bool _isLoginTabVisible = false;
        private bool _isGamesTabVisible = false;
        private string _loginUsername = "";
        private bool _rememberPassword = true;
        private string _loginStatusMessage = "";
        private string _loginPassword = "";
        private bool _isSteamRunning = false;
        private string _searchText = "";
        private bool _showCompactView = true;
        private bool _enableAlternatingRows = true;
        private int? _pendingAppIdToLaunch = null;
        
        // Cache auto-refresh functionality
        private System.Windows.Threading.DispatcherTimer? _cacheRefreshTimer;
        private bool _isAutoRefreshingCache = false;
        
        // Steam login file monitoring
        private System.IO.FileSystemWatcher? _steamLoginFileWatcher;
        private System.Windows.Threading.DispatcherTimer? _steamLoginCheckTimer;
        private Dictionary<string, DateTime> _lastKnownSteamIds = new();
        private bool _isProcessingSteamLoginChanges = false;

        public ObservableCollection<SteamAccount> Accounts { get; } = new();
        public ObservableCollection<SteamAccount> FilteredAccounts { get; } = new();
        public ObservableCollection<Game> AllGames { get; } = new();
        public ObservableCollection<Game> FilteredGames { get; } = new();

        private string _gameSearchText = "";
        private bool _isLoadingGames = false;
        private string _cacheStatus = "No cache loaded";

        public ICommand SwitchToAccountCommand { get; private set; }
        public ICommand RefreshAccountsCommand { get; private set; }
        public ICommand DiscoverAccountsCommand { get; private set; }
        public ICommand SelectSteamDirectoryCommand { get; private set; }
        public ICommand RemoveAccountCommand { get; private set; }
        public ICommand ExportPasswordsCommand { get; private set; }

        public ICommand BrowseSteamFolderCommand { get; private set; }
        public ICommand TestSteamAccessCommand { get; private set; }
        public ICommand ShowAccountsTabCommand { get; private set; }
        public ICommand ShowLoginTabCommand { get; private set; }
        public ICommand ShowGamesTabCommand { get; private set; }
        public ICommand SaveAccountCommand { get; private set; }

        public ICommand ExportAccountsCommand { get; private set; }
        public ICommand ImportAccountsCommand { get; private set; }
        public ICommand ExportAccountsWithPasswordsCommand { get; private set; }

        public ICommand RefreshOnlineStatusCommand { get; private set; }
        public ICommand RefreshGameCountCommand { get; private set; }
        public ICommand ConfigureApiKeyCommand { get; private set; }
        public ICommand LaunchF2PGameCommand { get; private set; }

        public ICommand LoadAllGamesCommand { get; private set; }
        public ICommand ClearGameCacheCommand { get; private set; }
        public ICommand RefreshGameCacheCommand { get; private set; }
        public ICommand ScanDemoBetaGamesCommand { get; private set; }

        public ICommand ToggleViewCommand { get; private set; }
        public ICommand BulkDeleteCommand { get; private set; }
        // Removed: PowerShell auto-fill commands for simplified account switching
        public ICommand OpenLogFileCommand { get; private set; }
        public ICommand ClearLogFileCommand { get; private set; }
        public ICommand OpenAppDataFolderCommand { get; private set; }
        public ICommand DiagnoseGameLoadingCommand { get; private set; }

        public ICommand AddPasswordCommand { get; private set; }
        public ICommand ShowAccountDetailsCommand { get; private set; }
        // Removed: Debug and fix commands for simplified account switching
        public ICommand CleanupDuplicatesCommand { get; private set; }

        // API Key Status Properties
        private bool _hasApiKey;
        public bool HasApiKey
        {
            get => _hasApiKey;
            set
            {
                _hasApiKey = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ApiKeyButtonText));
                OnPropertyChanged(nameof(ApiKeyButtonColor));
            }
        }

        public string ApiKeyButtonText => HasApiKey ? "✅ API Key OK" : "❌ API Key Missing";
        public string ApiKeyButtonColor => HasApiKey ? "#4CAF50" : "#F44336";

        public MainViewModel()
        {
            // Initialize commands
            InitializeCommands();
            
            // Initialize services
            InitializeServices();
            
            // Start Steam login file monitoring
            StartSteamLoginFileMonitoring();
            
            // Load accounts and start background processes
            _ = LoadAccountsAndStartBackgroundProcessesAsync();
            
            // Clean up duplicates on startup
            _ = CleanupDuplicateAccountsOnStartupAsync();
        }
        
        /// <summary>
        /// Clean up duplicate accounts on startup
        /// </summary>
        private async Task CleanupDuplicateAccountsOnStartupAsync()
        {
            try
            {
                // Wait a bit for accounts to load
                await Task.Delay(2000);
                
                _logger.LogInfo("=== STARTUP DUPLICATE CLEANUP ===");
                await CleanupDuplicateAccountsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during startup duplicate cleanup: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Initialize all commands
        /// </summary>
        private void InitializeCommands()
        {
            SwitchToAccountCommand = new RelayCommand<SteamAccount>(async account => await SwitchToAccountAsync(account));
            RefreshAccountsCommand = new RelayCommand(async () => await RefreshAccountsAsync());
            DiscoverAccountsCommand = new RelayCommand(async () => await DiscoverAccountsAsync());
            SelectSteamDirectoryCommand = new RelayCommand(async () => await SelectSteamDirectoryAsync());
            RemoveAccountCommand = new RelayCommand<SteamAccount>(async account => await RemoveAccountAsync(account));
            ExportPasswordsCommand = new RelayCommand(async () => await ExportPasswordsToFileAsync());

            BrowseSteamFolderCommand = new RelayCommand(async () => await BrowseSteamFolderAsync());
            ShowAccountsTabCommand = new RelayCommand(async () => { ShowAccountsTab(); await Task.CompletedTask; });
            ShowLoginTabCommand = new RelayCommand(async () => { ShowLoginTab(); await Task.CompletedTask; });
            ShowGamesTabCommand = new RelayCommand(async () => { ShowGamesTab(); await Task.CompletedTask; });
            SaveAccountCommand = new RelayCommand(async () => await SaveAccountAsync());

            ExportAccountsCommand = new RelayCommand(async () => await ExportAccountsAsync());
            ImportAccountsCommand = new RelayCommand(async () => await ImportAccountsAsync());
            ExportAccountsWithPasswordsCommand = new RelayCommand(async () => await ExportAccountsWithPasswordsAsync());

            RefreshGameCountCommand = new RelayCommand(async () => await UpdateAccountsGameCountAsync(Accounts.ToList()));
            ConfigureApiKeyCommand = new RelayCommand(() => ConfigureApiKey());
            LaunchF2PGameCommand = new RelayCommand<object>(LaunchF2PGame);
            LoadAllGamesCommand = new RelayCommand(async () => await LoadAllGamesAsync());
            ClearGameCacheCommand = new RelayCommand(() => ExecuteAsync(ClearGameCacheAsync));
            RefreshGameCacheCommand = new RelayCommand(async () => await RefreshGameCacheAsync());
            ScanDemoBetaGamesCommand = new RelayCommand(() => ExecuteAsync(ScanDemoBetaGamesAsync));

            ToggleViewCommand = new RelayCommand(() => ToggleView());
            BulkDeleteCommand = new RelayCommand<List<SteamAccount>>(async accounts => await BulkDeleteAccountsAsync(accounts));
            OpenLogFileCommand = new RelayCommand(() => OpenLogFile());
            ClearLogFileCommand = new RelayCommand(async () => await ClearLogFileAsync());
            OpenAppDataFolderCommand = new RelayCommand(() => OpenAppDataFolder());
            DiagnoseGameLoadingCommand = new RelayCommand(async () => await DiagnoseGameLoadingAsync());

            AddPasswordCommand = new RelayCommand<SteamAccount>(async account => await AddPasswordAsync(account));
            ShowAccountDetailsCommand = new RelayCommand<SteamAccount>(account => ShowAccountDetails(account));
            CleanupDuplicatesCommand = new RelayCommand(async () => await CleanupDuplicateAccountsAsync());
        }
        
        /// <summary>
        /// Initialize all services
        /// </summary>
        private void InitializeServices()
        {
            _steamService = new SteamService();
            _logger = new LogService();
            _apiKeyService = new ApiKeyService();
            _gameCacheService = new GameCacheService(_logger);
            
            // Check API key status
            RefreshApiKeyStatus();
            
            // Start periodic Steam status updates
            StartSteamStatusTimer();
            
            // Start cache auto-refresh timer
            StartCacheAutoRefreshTimer();
        }
        
        /// <summary>
        /// Load accounts and start background processes
        /// </summary>
        private async Task LoadAccountsAndStartBackgroundProcessesAsync()
        {
            await LoadAccountsAsync();
            
            // Check for cached games after a brief delay to ensure UI is loaded
            _ = Task.Delay(500).ContinueWith(async _ => await CheckForCachedGamesOnStartupAsync());
            
            // Auto-load saved API key and then auto-load cached games
            _ = InitializeApiKeyAsync().ContinueWith(async _ => await AutoLoadCachedGamesAsync());
        }

        /// <summary>
        /// Check for cached games immediately on startup and show Games tab if available
        /// </summary>
        private async Task CheckForCachedGamesOnStartupAsync()
        {
            try
            {
                _logger.LogInfo("=== CHECKING FOR CACHED GAMES ON STARTUP ===");
                
                // Show brief loading message
                StatusMessage = "Checking for cached games...";
                
                // Check if we have cached data without waiting for API key
                var cache = await _gameCacheService.LoadCacheAsync();
                
                if (cache != null && cache.Games.Count > 0)
                {
                    _logger.LogInfo($"Found {cache.Games.Count} cached games on startup - showing Games tab immediately");
                    StatusMessage = "Loading games from cache...";
                    
                    // Load games from cache immediately
                    var games = cache.Games.Select(cg => cg.ToGame()).ToList();
                    
                    // Update installed status (quick operation)
                    var installedAppIds = await _steamService.GetInstalledAppIdsAsync();
                    games = _gameCacheService.UpdateInstalledStatus(games, installedAppIds);

                    // Add games to UI collection immediately
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _logger.LogInfo($"Adding {games.Count} games to AllGames collection");
                        
                        // Clear and populate AllGames
                        AllGames.Clear();
                        foreach (var game in games.OrderBy(g => g.Name))
                        {
                            AllGames.Add(game);
                        }
                        
                        // Clear and populate FilteredGames
                        _logger.LogInfo($"Adding {games.Count} games to FilteredGames collection");
                        FilteredGames.Clear();
                        foreach (var game in AllGames)
                        {
                            FilteredGames.Add(game);
                        }
                        
                        // Force UI refresh notifications
                        OnPropertyChanged(nameof(AllGames));
                        OnPropertyChanged(nameof(FilteredGames));
                        
                        _logger.LogInfo($"UI collections updated - AllGames: {AllGames.Count}, FilteredGames: {FilteredGames.Count}");
                        
                        // Additional verification
                        _logger.LogInfo($"Verification - AllGames.Count: {AllGames.Count}, FilteredGames.Count: {FilteredGames.Count}");
                        if (AllGames.Count > 0)
                        {
                            _logger.LogInfo($"First game in AllGames: {AllGames[0].Name}");
                        }
                        if (FilteredGames.Count > 0)
                        {
                            _logger.LogInfo($"First game in FilteredGames: {FilteredGames[0].Name}");
                        }
                    });
                    
                    // Set up F2P account options
                    try
                    {
                        PopulateF2PAccountOptions();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to populate F2P account options during startup: {ex.Message}");
                    }
                    
                    // Show Accounts tab by default instead of Games tab
                    ShowAccountsTab();
                    
                    // Give UI time to update
                    await Task.Delay(200);
                    
                    // Double-check that games are visible and force refresh if needed
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _logger.LogInfo($"Final check - AllGames: {AllGames.Count}, FilteredGames: {FilteredGames.Count}");
                        
                        // Always ensure FilteredGames has the same content as AllGames
                        if (AllGames.Count > 0)
                        {
                            _logger.LogInfo("Ensuring FilteredGames matches AllGames");
                            FilteredGames.Clear();
                            foreach (var game in AllGames)
                            {
                                FilteredGames.Add(game);
                            }
                            
                            // Force UI refresh notifications
                            OnPropertyChanged(nameof(FilteredGames));
                            OnPropertyChanged(nameof(AllGames));
                            
                            _logger.LogSuccess($"Final UI update - AllGames: {AllGames.Count}, FilteredGames: {FilteredGames.Count}");
                        }
                    });
                    
                    // Update status with attention-grabbing message
                    var accountsWithGames = games.GroupBy(g => g.OwnerSteamId).Count();
                    StatusMessage = $"🎮 Games loaded from cache! {games.Count} games from {accountsWithGames} accounts are ready to use!";
                    CacheStatus = $"Cache loaded: {cache.LastUpdated:MM/dd HH:mm} (expires in {Math.Max(0, (int)(cache.LastUpdated.Add(cache.CacheValidDuration) - DateTime.Now).TotalHours)}h)";
                    
                    _logger.LogSuccess($"=== STARTUP CACHE LOAD COMPLETED: {games.Count} games displayed immediately ===");
                    
                    // Flash the status message briefly to draw attention
                    await Task.Delay(2000);
                    StatusMessage = $"Ready - {games.Count} games loaded from cache";
                }
                else
                {
                    _logger.LogInfo("No cached games found on startup - staying on Accounts tab");
                    StatusMessage = "Ready - no cached games found";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking for cached games on startup: {ex.Message}");
                StatusMessage = "Ready - error checking cached games";
            }
        }

        /// <summary>
        /// Fetch all games from Steam API (used when cache is expired or missing)
        /// </summary>
        private async Task<List<Game>> FetchAllGamesFromApiAsync()
        {
            try
            {
                var allGames = new List<Game>();
                var accountsList = Accounts.ToList(); // Snapshot to avoid collection modification
                var totalGamesLoaded = 0;
                var accountsWithGames = 0;

                // Detect installed games first to mark IsInstalled quickly
                StatusMessage = "Detecting installed games...";
                var installedAppIds = await _steamService.GetInstalledAppIdsAsync();

                // OPTIMIZATION: Process accounts in parallel for faster loading
                var semaphore = new SemaphoreSlim(2, 2); // Reduced to 2 concurrent requests to be more conservative
                var completedAccounts = 0;
                var tasks = accountsList.Select(async account =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        _logger.LogInfo($"=== STARTING ACCOUNT PROCESSING: {account.DisplayName} ({account.SteamId}) ===");
                        
                        // Update status in a thread-safe way with progress
                        var current = Interlocked.Increment(ref completedAccounts);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            StatusMessage = $"Loading games... ({current}/{accountsList.Count}) - {account.DisplayName}";
                        });
                        
                        _logger.LogInfo($"Loading games for account: {account.DisplayName} ({account.SteamId})");

                        var steamGames = await _steamWebApiService.GetAccountGamesAsync(account.SteamId);
                        
                        if (steamGames.Any())
                        {
                            Interlocked.Increment(ref accountsWithGames);
                            
                            // Prepare games for batch addition to avoid UI thread issues
                            var gamesToAdd = new List<Game>();
                            
                            foreach (var steamGame in steamGames)
                            {
                                var game = new Game
                                {
                                    AppId = steamGame.AppId,
                                    Name = steamGame.Name,
                                    PlaytimeForever = steamGame.PlaytimeForever,
                                    ImgIconUrl = steamGame.ImgIconUrl,
                                    OwnerSteamId = account.SteamId,
                                    OwnerAccountName = account.AccountName,
                                    OwnerPersonaName = account.PersonaName,
                                    LastUpdated = DateTime.Now,
                                    // Quick check for known F2P games - immediate optimization
                                    IsPaid = !_steamWebApiService.IsKnownFreeToPlayGame(steamGame.AppId),
                                    IsInstalled = installedAppIds.Contains(steamGame.AppId),
                                    AvailableAccounts = new List<SteamAccount>() // Initialize the list
                                };
                                
                                gamesToAdd.Add(game);
                                Interlocked.Increment(ref totalGamesLoaded);
                            }
                            
                            // Thread-safe addition to shared collection
                            lock (allGames)
                            {
                            allGames.AddRange(gamesToAdd);
                            }

                            _logger.LogInfo($"Added {gamesToAdd.Count} games for {account.DisplayName}");
                            _logger.LogSuccess($"Loaded {steamGames.Count} games for {account.DisplayName}");
                        }
                        else
                        {
                            _logger.LogInfo($"No games found for {account.DisplayName} (may be private or no games)");
                        }

                        // Reduced delay for parallel processing
                        await Task.Delay(200);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error loading games for {account.DisplayName}: {ex.Message}");
                        _logger.LogError($"Stack trace: {ex.StackTrace}");
                    }
                    finally
                    {
                        semaphore.Release();
                }
                });

                await Task.WhenAll(tasks);
                
                _logger.LogInfo($"Finished loading games from {accountsList.Count} accounts. Total games so far: {allGames.Count}");

                // Aggregate owners per AppId for owner-aware filtering
                var ownersByApp = allGames
                    .GroupBy(g => g.AppId)
                    .ToDictionary(
                        grp => grp.Key,
                        grp => new
                        {
                            SteamIds = grp.Select(x => x.OwnerSteamId).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList(),
                            AccountNames = grp.Select(x => x.OwnerAccountName).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                        }
                    );

                // Remove duplicates (same game owned by multiple accounts)
                _logger.LogInfo($"Starting duplicate removal process from {allGames.Count} total games...");
                var uniqueGames = allGames
                    .GroupBy(g => g.AppId)
                    .Select(group => group.OrderByDescending(g => g.PlaytimeForever).First())
                    .ToList();

                // Attach aggregated owners to the representative game
                foreach (var ug in uniqueGames)
                {
                    if (ownersByApp.TryGetValue(ug.AppId, out var owners))
                    {
                        ug.OwnerSteamIds = new HashSet<string>(owners.SteamIds);
                        ug.OwnerAccountNames = new HashSet<string>(owners.AccountNames, StringComparer.OrdinalIgnoreCase);
                    }
                }

                // Use a safer approach to update the collection
                var sortedGames = uniqueGames.OrderBy(g => g.Name).ToList();
                
                try
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        AllGames.Clear();
                        foreach (var game in sortedGames)
                        {
                            AllGames.Add(game);
                        }
                    });
                    _logger.LogInfo("Successfully updated games collection with unique games");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error updating games collection: {ex.Message}");
                    _logger.LogError($"Stack trace: {ex.StackTrace}");
                }

                // OPTIMIZATION: Smart paid/free detection with parallel processing and caching
                StatusMessage = "Determining game types (paid vs free-to-play)...";
                _logger.LogInfo("Determining paid status for unknown games...");
                
                var unknownGames = uniqueGames.Where(g => g.IsPaid).ToList(); // All games marked as paid need verification
                
                if (unknownGames.Count > 0)
                {
                    _logger.LogInfo($"Checking paid status for {unknownGames.Count} games using optimized parallel processing...");
                    
                    // OPTIMIZATION: Process games sequentially with proper delays to avoid API rate limiting
                    _logger.LogInfo("Processing games sequentially to avoid Steam API rate limits...");
                    var processedCount = 0;
                
                    foreach (var game in unknownGames)
                    {
                        try
                        {
                            var isReallyPaid = await _steamWebApiService.IsGamePaidAsync(game.AppId);
                            game.IsPaid = isReallyPaid;
                            
                            processedCount++;
                            if (processedCount % 10 == 0 || processedCount == unknownGames.Count)
                            {
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    StatusMessage = $"Checking game types... ({processedCount}/{unknownGames.Count})";
                                });
                                _logger.LogInfo($"Paid status progress: {processedCount}/{unknownGames.Count}");
                            }
                            
                            // Longer delay to be respectful to Steam API and avoid rate limiting
                            await Task.Delay(800);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error checking paid status for {game.Name}: {ex.Message}");
                            // Keep as paid if check fails (conservative approach)
                        }
                    }
                    _logger.LogInfo($"Determined paid status for {processedCount} games using sequential processing");
                }
                
                _logger.LogSuccess($"Fetched {uniqueGames.Count} unique games from {accountsWithGames} accounts");
                return uniqueGames.OrderBy(g => g.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in FetchAllGamesFromApiAsync: {ex.Message}");
                throw; // Re-throw to be caught by LoadAllGamesAsync
            }
        }

        /// <summary>
        /// Auto-load cached games on app startup
        /// </summary>
        private async Task AutoLoadCachedGamesAsync()
        {
            try
            {
                // Wait a bit for the app to fully initialize
                await Task.Delay(1000);
                
                if (_steamWebApiService == null)
                {
                    _logger.LogInfo("Cannot auto-load cached games - Steam Web API service not initialized");
                    return;
                }

                // Skip if games were already loaded on startup
                if (AllGames.Count > 0)
                {
                    _logger.LogInfo("Games already loaded on startup - skipping auto-load");
                    return;
                }

                _logger.LogInfo("=== AUTO-LOADING CACHED GAMES ON STARTUP ===");
                
                // Check if we have cached data
                var cache = await _gameCacheService.LoadCacheAsync();
                
                if (cache != null)
                {
                    _logger.LogInfo($"Auto-loading {cache.Games.Count} games from cache");
                    StatusMessage = "Loading games from cache...";
                    
                    var games = cache.Games.Select(cg => cg.ToGame()).ToList();
                    
                    // Update installed status (quick operation)
                    StatusMessage = "Updating installed game status...";
                    var installedAppIds = await _steamService.GetInstalledAppIdsAsync();
                    games = _gameCacheService.UpdateInstalledStatus(games, installedAppIds);

                    // Add games to UI collection
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        AllGames.Clear();
                        foreach (var game in games.OrderBy(g => g.Name))
                        {
                            AllGames.Add(game);
                        }
                    });

                    // Set up F2P account options
                    try
                    {
                        PopulateF2PAccountOptions();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to populate F2P account options during auto-load: {ex.Message}");
                    }
                    
                    // CRITICAL: Immediately populate FilteredGames so games show in UI right away
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        FilteredGames.Clear();
                        foreach (var game in AllGames)
                        {
                            FilteredGames.Add(game);
                        }
                        
                        // Force UI refresh notifications
                        OnPropertyChanged(nameof(FilteredGames));
                        OnPropertyChanged(nameof(AllGames));
                    });
                    
                    var accountsWithGames = games.GroupBy(g => g.OwnerSteamId).Count();
                    StatusMessage = $"🎮 Games loaded from cache! {AllGames.Count} games from {accountsWithGames} accounts are ready to use!";
                    CacheStatus = $"Cache loaded: {cache.LastUpdated:MM/dd HH:mm} (expires in {Math.Max(0, (int)(cache.LastUpdated.Add(cache.CacheValidDuration) - DateTime.Now).TotalHours)}h)";
                    _logger.LogSuccess($"=== AUTO-LOAD COMPLETED: {AllGames.Count} games from cache ===");
                    
                    // Show the Games tab since we have data - this makes cached games immediately visible
                    ShowGamesTab();
                    
                    // Flash the status message briefly to draw attention
                    await Task.Delay(2000);
                    StatusMessage = $"Ready - {AllGames.Count} games loaded from cache";
                }
                else
                {
                    _logger.LogInfo("No cached games found - games will need to be loaded manually");
                    StatusMessage = "No cached games found. Click 'Load All Games' to fetch from Steam.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during auto-load of cached games: {ex.Message}");
                StatusMessage = "Failed to auto-load cached games";
            }
        }

        private async Task InitializeApiKeyAsync()
        {
            try
            {
                var savedApiKey = _apiKeyService.LoadApiKey();
                if (!string.IsNullOrEmpty(savedApiKey))
                {
                    _steamWebApiService = new SteamWebApiService(savedApiKey);
                    _logger.LogSuccess("Steam Web API key loaded from secure storage");
                    _logger.LogInfo("Auto game count refresh will be enabled after loading accounts");
                    StatusMessage = $"API key loaded ({_apiKeyService.GetMaskedApiKey()}). Game counts will auto-refresh.";
                }
                else
                {
                    _logger.LogInfo("No saved API key found - game count auto-refresh disabled");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading saved API key: {ex.Message}");
            }
        }

        public void SetSteamWebApiKey(string apiKey, bool saveKey = true)
        {
            try
            {
                _steamWebApiService = new SteamWebApiService(apiKey);
                
                if (saveKey)
                {
                    _apiKeyService.SaveApiKey(apiKey);
                    _logger.LogSuccess("Steam Web API key set and saved securely");
                    StatusMessage = $"API key configured and saved ({_apiKeyService.GetMaskedApiKey()}). Game count data now available.";
                }
                else
                {
                    _logger.LogSuccess("Steam Web API key set successfully (not saved)");
                    StatusMessage = "Steam Web API key configured. Game count data now available.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error setting Steam Web API key: {ex.Message}");
                StatusMessage = $"Error setting API key: {ex.Message}";
            }
        }

        public void ConfigureApiKey()
        {
            if (HasApiKey)
            {
                // Show current API key status and option to change
                var maskedKey = _apiKeyService.GetMaskedApiKey();
                var result = MessageBox.Show(
                    $"Current API Key: {maskedKey}\n\nDo you want to:\n• Yes: Enter a new API key\n• No: Keep current key\n• Cancel: Remove current key",
                    "API Key Configuration",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ShowApiKeyInputDialog();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    // Remove current key
                _apiKeyService.DeleteApiKey();
                    RefreshApiKeyStatus();
                    StatusMessage = "API key removed. Some features will be unavailable.";
                    _logger.LogInfo("API key removed by user");
                }
            }
            else
            {
                // No API key exists, show input dialog
                ShowApiKeyInputDialog();
            }
        }

        private void RefreshApiKeyStatus()
        {
            HasApiKey = _apiKeyService.HasSavedApiKey();
        }

        private void ShowApiKeyInputDialog()
        {
            var dialog = new ApiKeyInputDialog();
            if (dialog.ShowDialog() == true)
            {
                var apiKey = dialog.ApiKey;
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    try
                    {
                        // Use SetSteamWebApiKey to properly initialize the service
                        SetSteamWebApiKey(apiKey, dialog.SaveKey);
                        RefreshApiKeyStatus();
                        // StatusMessage is already set in SetSteamWebApiKey
                        _logger.LogSuccess("New API key configured by user and service initialized");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to save API key: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        _logger.LogError($"Failed to save API key: {ex.Message}");
                    }
                }
            }
        }

        public SteamAccount? CurrentAccount
        {
            get => _currentAccount;
            set => SetProperty(ref _currentAccount, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsAccountsTabVisible
        {
            get => _isAccountsTabVisible;
            set => SetProperty(ref _isAccountsTabVisible, value);
        }

        public bool IsLoginTabVisible
        {
            get => _isLoginTabVisible;
            set => SetProperty(ref _isLoginTabVisible, value);
        }

        public bool IsGamesTabVisible
        {
            get => _isGamesTabVisible;
            set => SetProperty(ref _isGamesTabVisible, value);
        }

        public string LoginUsername
        {
            get => _loginUsername;
            set => SetProperty(ref _loginUsername, value);
        }

        public bool RememberPassword
        {
            get => _rememberPassword;
            set => SetProperty(ref _rememberPassword, value);
        }

        public string LoginStatusMessage
        {
            get => _loginStatusMessage;
            set => SetProperty(ref _loginStatusMessage, value);
        }

        public string LoginPassword
        {
            get => _loginPassword;
            set => SetProperty(ref _loginPassword, value);
        }

        public bool IsSteamRunning
        {
            get => _isSteamRunning;
            set => SetProperty(ref _isSteamRunning, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterAccounts();
                }
            }
        }

        public string GameSearchText
        {
            get => _gameSearchText;
            set
            {
                if (SetProperty(ref _gameSearchText, value))
                {
                    // Notify UI to apply filters - the filtering is now handled in MainWindow
                    OnPropertyChanged(nameof(GameSearchText));
                }
            }
        }

        public bool IsLoadingGames
        {
            get => _isLoadingGames;
            set => SetProperty(ref _isLoadingGames, value);
        }

        public string CacheStatus
        {
            get => _cacheStatus;
            set => SetProperty(ref _cacheStatus, value);
        }

        public bool ShowCompactView
        {
            get => _showCompactView;
            set
            {
                if (SetProperty(ref _showCompactView, value))
                {
                    FilterAccounts();
                }
            }
        }
        
        public bool EnableAlternatingRows
        {
            get => _enableAlternatingRows;
            set
            {
                if (SetProperty(ref _enableAlternatingRows, value))
                {
                    OnPropertyChanged(nameof(EnableAlternatingRows));
                }
            }
        }

        /// <summary>
        /// Update the last known Steam IDs when accounts are loaded or updated
        /// </summary>
        private void UpdateLastKnownSteamIds()
        {
            try
            {
                _lastKnownSteamIds.Clear();
                foreach (var account in Accounts)
                {
                    if (!string.IsNullOrEmpty(account.SteamId))
                    {
                        _lastKnownSteamIds[account.SteamId] = account.LastLogin;
                    }
                }
                _logger.LogInfo($"Updated last known Steam IDs: {_lastKnownSteamIds.Count} IDs tracked");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating last known Steam IDs: {ex.Message}");
            }
        }

        /// <summary>
        /// Enhanced LoadAccountsAsync that also updates Steam ID monitoring
        /// </summary>
        private async Task LoadAccountsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading accounts...";

                var accounts = await _steamService.LoadSavedAccountsAsync();
                System.Diagnostics.Debug.WriteLine($"Loaded {accounts.Count} accounts from file");
                
                // Load ban status for accounts (in background, non-blocking)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _steamService.LoadBanStatusForAccountsAsync(accounts);
                        
                        // Update UI on main thread after ban status is loaded
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            FilterAccounts(); // Refresh the UI
                            StatusMessage = $"Loaded {accounts.Count} accounts with ban status";
                        });
                        
                        // Auto-refresh game counts if API key is available
                        if (_steamWebApiService != null)
                        {
                            _logger.LogInfo("API key available - auto-refreshing game counts after ban status update");
                            
                            // Run game count update in background
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = "Auto-refreshing game counts...";
                            });
                            
                            await UpdateAccountsGameCountAsync(accounts);
                            
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = $"Game counts auto-refreshed for {accounts.Count} accounts";
                            });
                        }
                        else
                        {
                            _logger.LogInfo("No API key available - skipping auto game count refresh");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading ban status: {ex.Message}");
                        _logger.LogError($"Error in ban status/game count loading: {ex.Message}");
                    }
                });
                
                // Update online status for all accounts
                await UpdateAccountsOnlineStatusAsync(accounts);
                
                // Clear existing collections
                Accounts.Clear();
                FilteredAccounts.Clear();
                
                // Add loaded accounts
                foreach (var account in accounts)
                {
                    Accounts.Add(account);
                    System.Diagnostics.Debug.WriteLine($"Added account: {account.DisplayName} ({account.SteamId}) - Online: {account.IsCurrentAccount}");
                }

                // Get current account
                CurrentAccount = await _steamService.GetCurrentAccountAsync();
                
                // Update filtered accounts
                FilterAccounts();
                
                System.Diagnostics.Debug.WriteLine($"Filtered accounts count: {FilteredAccounts.Count}");
                StatusMessage = $"Loaded {Accounts.Count} accounts";
                
                // Update Steam ID monitoring after accounts are loaded
                UpdateLastKnownSteamIds();
                
                // Update account status after loading
                await UpdateAccountStatusAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading accounts: {ex.Message}");
                StatusMessage = $"Error loading accounts: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Update account status after loading accounts
        /// </summary>
        private async Task UpdateAccountStatusAsync()
        {
            try
            {
                // This method will be called after accounts are loaded
                // It can be expanded to include additional status updates
                _logger.LogInfo("Account status update completed");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating account status: {ex.Message}");
            }
        }

        private void StartSteamStatusTimer()
        {
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(2); // Check every 2 seconds
            timer.Tick += (sender, e) =>
            {
                var currentSteamStatus = _steamService.IsSteamRunning();
                if (IsSteamRunning != currentSteamStatus)
                {
                    IsSteamRunning = currentSteamStatus;
                    _logger.LogInfo($"🔄 Steam status changed: {(IsSteamRunning ? "Running" : "Not Running")}");
                }
            };
            timer.Start();
        }

        private void StartCacheAutoRefreshTimer()
        {
            _cacheRefreshTimer = new System.Windows.Threading.DispatcherTimer();
            _cacheRefreshTimer.Interval = TimeSpan.FromMinutes(30); // Check every 30 minutes
            _cacheRefreshTimer.Tick += async (sender, e) => await CheckAndRefreshCacheAsync();
            _cacheRefreshTimer.Start();
            
            _logger.LogInfo("Cache auto-refresh timer started - will check every 30 minutes");
        }

        /// <summary>
        /// Check if cache needs refreshing and automatically update it
        /// This runs every 30 minutes to keep games data fresh
        /// </summary>
        private async Task CheckAndRefreshCacheAsync()
        {
            try
            {
                // Prevent multiple simultaneous refresh operations
                if (_isAutoRefreshingCache)
                {
                    _logger.LogInfo("Cache auto-refresh already in progress, skipping this cycle");
                    return;
                }

                // Check if we have an API key (required for refreshing)
                if (_steamWebApiService == null)
                {
                    _logger.LogInfo("Cannot auto-refresh cache - Steam Web API service not initialized");
                    return;
                }

                // Check if we have any games loaded
                if (AllGames.Count == 0)
                {
                    _logger.LogInfo("No games loaded, skipping cache refresh check");
                    return;
                }

                // Check if cache is expired
                var cache = await _gameCacheService.LoadCacheAsync();
                if (cache == null || !cache.IsExpired)
                {
                    var timeUntilExpiry = cache?.LastUpdated.Add(cache.CacheValidDuration) - DateTime.Now ?? TimeSpan.Zero;
                    _logger.LogInfo($"Cache is still valid. Expires in {timeUntilExpiry.TotalMinutes:F0} minutes");
                    return;
                }

                _logger.LogInfo("=== AUTO-REFRESHING EXPIRED CACHE ===");
                _isAutoRefreshingCache = true;

                try
                {
                    // Show status to user
                    StatusMessage = "Auto-refreshing expired games cache...";
                    CacheStatus = "Auto-refreshing expired cache...";

                    // Fetch fresh data from Steam API
                    _logger.LogInfo("Fetching fresh games data from Steam API...");
                    var freshGames = await FetchAllGamesFromApiAsync();

                    // Save fresh data to cache
                    await _gameCacheService.SaveCacheAsync(freshGames);
                    _logger.LogInfo("Fresh game data saved to cache");

                    // Update UI with fresh data
                    await DisplayGamesImmediatelyAsync(freshGames);

                    // Set up F2P account options
                    try
                    {
                        PopulateF2PAccountOptions();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to populate F2P account options during auto-refresh: {ex.Message}");
                    }

                    var accountsWithGames = freshGames.GroupBy(g => g.OwnerSteamId).Count();
                    StatusMessage = $"Auto-refreshed {AllGames.Count} games from {accountsWithGames} accounts";
                    CacheStatus = $"Fresh data: {DateTime.Now:MM/dd HH:mm} (expires in 7h)";

                    _logger.LogSuccess($"=== AUTO-REFRESH COMPLETED: {freshGames.Count} games updated ===");
                }
                finally
                {
                    _isAutoRefreshingCache = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during automatic cache refresh: {ex.Message}");
                _isAutoRefreshingCache = false;
            }
        }

        /// <summary>
        /// Start monitoring Steam login files for changes
        /// </summary>
        private void StartSteamLoginFileMonitoring()
        {
            try
            {
                // Initialize last known Steam IDs from current accounts
                InitializeLastKnownSteamIds();
                
                // Start file system watcher for immediate detection
                StartFileSystemWatcher();
                
                // Start backup timer for periodic checking (every 30 seconds)
                StartSteamLoginCheckTimer();
                
                _logger.LogInfo("Steam login file monitoring started - will check every 30 seconds");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start Steam login file monitoring: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Initialize the dictionary of last known Steam IDs from current accounts
        /// </summary>
        private void InitializeLastKnownSteamIds()
        {
            _lastKnownSteamIds.Clear();
            foreach (var account in Accounts)
            {
                if (!string.IsNullOrEmpty(account.SteamId))
                {
                    _lastKnownSteamIds[account.SteamId] = account.LastLogin;
                }
            }
            _logger.LogInfo($"Initialized {_lastKnownSteamIds.Count} known Steam IDs for monitoring");
        }
        
        /// <summary>
        /// Start file system watcher for Steam login files
        /// </summary>
        private void StartFileSystemWatcher()
        {
            try
            {
                var steamPath = _steamService.GetSteamPath();
                var configPath = Path.Combine(steamPath, "config");
                
                if (!Directory.Exists(configPath))
                {
                    _logger.LogWarning($"Steam config directory not found: {configPath}");
                    return;
                }
                
                _steamLoginFileWatcher = new System.IO.FileSystemWatcher(configPath)
                {
                    Filter = "*.vdf",
                    NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };
                
                _steamLoginFileWatcher.Changed += OnSteamLoginFileChanged;
                _steamLoginFileWatcher.Created += OnSteamLoginFileChanged;
                
                _logger.LogInfo($"File system watcher started for Steam config directory: {configPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start file system watcher: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Start backup timer for periodic Steam login file checking
        /// </summary>
        private void StartSteamLoginCheckTimer()
        {
            _steamLoginCheckTimer = new System.Windows.Threading.DispatcherTimer();
            _steamLoginCheckTimer.Interval = TimeSpan.FromSeconds(30); // Check every 30 seconds
            _steamLoginCheckTimer.Tick += async (sender, e) => await CheckForSteamLoginChangesAsync();
            _steamLoginCheckTimer.Start();
        }
        
        /// <summary>
        /// Handle Steam login file changes via file system watcher
        /// </summary>
        private async void OnSteamLoginFileChanged(object sender, System.IO.FileSystemEventArgs e)
        {
            try
            {
                // Small delay to ensure file write is complete
                await Task.Delay(1000);
                
                _logger.LogInfo($"Steam login file changed: {e.Name}");
                await CheckForSteamLoginChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling Steam login file change: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check for changes in Steam login files and process new Steam IDs
        /// </summary>
        private async Task CheckForSteamLoginChangesAsync()
        {
            if (_isProcessingSteamLoginChanges)
            {
                _logger.LogInfo("Steam login change processing already in progress, skipping this check");
                return;
            }
            
            try
            {
                _isProcessingSteamLoginChanges = true;
                
                // Discover current accounts from Steam
                var discoveredAccounts = await _steamService.DiscoverAccountsAsync();
                
                // Find new Steam IDs that weren't known before
                var newSteamIds = new List<string>();
                foreach (var account in discoveredAccounts)
                {
                    if (!string.IsNullOrEmpty(account.SteamId) && 
                        !_lastKnownSteamIds.ContainsKey(account.SteamId))
                    {
                        newSteamIds.Add(account.SteamId);
                        _logger.LogInfo($"New Steam ID discovered: {account.SteamId} for account: {account.AccountName}");
                    }
                }
                
                // Process new Steam IDs if any found
                if (newSteamIds.Any())
                {
                    _logger.LogInfo($"Found {newSteamIds.Count} new Steam IDs, processing...");
                    await ProcessNewSteamIdsAsync(newSteamIds);
                    
                    // Update last known Steam IDs
                    foreach (var account in discoveredAccounts)
                    {
                        if (!string.IsNullOrEmpty(account.SteamId))
                        {
                            _lastKnownSteamIds[account.SteamId] = account.LastLogin;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking for Steam login changes: {ex.Message}");
            }
            finally
            {
                _isProcessingSteamLoginChanges = false;
            }
        }
        
        /// <summary>
        /// Process newly discovered Steam IDs by fetching their data
        /// </summary>
        private async Task ProcessNewSteamIdsAsync(List<string> newSteamIds)
        {
            try
            {
                _logger.LogInfo($"=== PROCESSING {newSteamIds.Count} NEW STEAM IDs ===");
                
                // First, clean up any existing duplicates
                await CleanupDuplicateAccountsAsync();
                
                StatusMessage = $"🔄 Processing {newSteamIds.Count} new Steam accounts...";
                
                foreach (var steamId in newSteamIds)
                {
                    try
                    {
                        _logger.LogInfo($"Processing new Steam ID: {steamId}");
                        
                        // Check if we already have an account with this Steam ID
                        if (AccountWithSteamIdExists(steamId))
                        {
                            _logger.LogInfo($"Account with Steam ID {steamId} already exists, skipping");
                            continue;
                        }
                        
                        // Find the account that this Steam ID belongs to
                        var account = await FindAccountForSteamIdAsync(steamId);
                        if (account != null)
                        {
                            _logger.LogInfo($"Found account {account.AccountName} for Steam ID {steamId}");
                            
                            // Update the account with the discovered Steam ID
                            account.SteamId = steamId;
                            account.LastLogin = DateTime.Now;
                            
                            // Fetch account data from Steam API
                            await FetchAndProcessNewSteamAccountAsync(steamId, account);
                            
                            // Update the account in the accounts collection
                            await UpdateAccountInCollectionAsync(account);
                        }
                        else
                        {
                            _logger.LogWarning($"No account found for Steam ID {steamId}, processing as orphaned Steam ID");
                            // Fetch account data from Steam API (without account association)
                            await FetchAndProcessNewSteamAccountAsync(steamId);
                        }
                        
                        // Small delay between API calls to avoid overwhelming Steam
                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error processing Steam ID {steamId}: {ex.Message}");
                    }
                }
                
                StatusMessage = $"✅ Processed {newSteamIds.Count} new Steam accounts";
                _logger.LogSuccess($"=== COMPLETED PROCESSING {newSteamIds.Count} NEW STEAM IDs ===");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing new Steam IDs: {ex.Message}");
                StatusMessage = $"❌ Error processing new Steam accounts";
            }
        }
        
        /// <summary>
        /// Find the account that a Steam ID belongs to by checking Steam's login files
        /// </summary>
        private async Task<SteamAccount?> FindAccountForSteamIdAsync(string steamId)
        {
            try
            {
                _logger.LogInfo($"Finding account for Steam ID: {steamId}");
                
                // First, check if we already have an account with this Steam ID
                var existingAccountWithSteamId = Accounts.FirstOrDefault(a => a.SteamId == steamId);
                if (existingAccountWithSteamId != null)
                {
                    _logger.LogInfo($"Found existing account with Steam ID {steamId}: {existingAccountWithSteamId.AccountName}");
                    return existingAccountWithSteamId;
                }
                
                // Discover accounts from Steam to find which account this Steam ID belongs to
                var discoveredAccounts = await _steamService.DiscoverAccountsAsync();
                
                // Find the account with this Steam ID
                var discoveredAccount = discoveredAccounts.FirstOrDefault(a => a.SteamId == steamId);
                
                if (discoveredAccount != null)
                {
                    _logger.LogInfo($"Found discovered account: {discoveredAccount.AccountName} with Steam ID {steamId}");
                    
                    // Now find the corresponding account in our saved accounts
                    // Try exact match first, then case-insensitive match
                    var savedAccount = Accounts.FirstOrDefault(a => 
                        a.AccountName.Equals(discoveredAccount.AccountName, StringComparison.OrdinalIgnoreCase));
                    
                    if (savedAccount != null)
                    {
                        _logger.LogInfo($"Found matching saved account: {savedAccount.AccountName}");
                        return savedAccount;
                    }
                    else
                    {
                        _logger.LogWarning($"No saved account found for discovered account: {discoveredAccount.AccountName}");
                        
                        // Check if we have an account with a similar name (typo tolerance)
                        var similarAccount = Accounts.FirstOrDefault(a => 
                            a.AccountName.Equals(discoveredAccount.AccountName, StringComparison.OrdinalIgnoreCase) ||
                            a.AccountName.Replace("_", "").Replace("-", "").Equals(discoveredAccount.AccountName.Replace("_", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase));
                        
                        if (similarAccount != null)
                        {
                            _logger.LogInfo($"Found account with similar name: {similarAccount.AccountName} (discovered: {discoveredAccount.AccountName})");
                            return similarAccount;
                        }
                    }
                }
                else
                {
                    _logger.LogWarning($"No discovered account found for Steam ID: {steamId}");
                }
                
                // If we still can't find a match, check if any account is missing a Steam ID
                // This could happen if the account was added before Steam was logged in
                var accountsWithoutSteamId = Accounts.Where(a => string.IsNullOrEmpty(a.SteamId)).ToList();
                if (accountsWithoutSteamId.Any())
                {
                    _logger.LogInfo($"Found {accountsWithoutSteamId.Count} accounts without Steam IDs - may need manual association");
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error finding account for Steam ID {steamId}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Update an account in the accounts collection and trigger UI refresh
        /// </summary>
        private async Task UpdateAccountInCollectionAsync(SteamAccount updatedAccount)
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Find the account in the collection and update it
                    var existingAccount = Accounts.FirstOrDefault(a => a.AccountName == updatedAccount.AccountName);
                    if (existingAccount != null)
                    {
                        // Update the existing account object
                        existingAccount.SteamId = updatedAccount.SteamId;
                        existingAccount.LastLogin = updatedAccount.LastLogin;
                        
                        // Trigger UI refresh
                        OnPropertyChanged(nameof(Accounts));
                        OnPropertyChanged(nameof(FilteredAccounts));
                        
                        _logger.LogInfo($"Updated account {updatedAccount.AccountName} with Steam ID {updatedAccount.SteamId}");
                    }
                });
                
                // Save the updated accounts to file
                await _steamService.SaveAccountsAsync(Accounts.ToList());
                _logger.LogInfo($"Saved updated accounts to file");
                
                // Update the last known Steam IDs tracking
                UpdateLastKnownSteamIds();
                
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating account in collection: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Fetch and process data for a newly discovered Steam account
        /// </summary>
        private async Task FetchAndProcessNewSteamAccountAsync(string steamId, SteamAccount? account = null)
        {
            try
            {
                _logger.LogInfo($"=== FETCHING DATA FOR NEW STEAM ID: {steamId} ===");
                
                // Check if we have an API key
                if (_steamWebApiService == null)
                {
                    _logger.LogWarning("Steam Web API service not available, cannot fetch data for new account");
                    return;
                }
                
                // Fetch owned games for this Steam ID
                var games = await _steamWebApiService.GetAccountGamesAsync(steamId);
                if (games != null && games.Any())
                {
                    _logger.LogInfo($"Retrieved {games.Count} games for Steam ID: {steamId}");
                    
                    // Convert to Game objects
                    var newGames = games.Select(g => new Game
                    {
                        AppId = g.AppId,
                        Name = g.Name,
                        PlaytimeForever = g.PlaytimeForever,
                        ImgIconUrl = g.ImgIconUrl,
                        OwnerSteamId = steamId,
                        OwnerAccountName = account?.AccountName ?? "", // Use account name if available
                        OwnerPersonaName = account?.PersonaName ?? "", // Use persona name if available
                        IsInstalled = false, // Will be updated when checking installed games
                        IsPaid = true // Default to paid, will be updated when checking store info
                    }).ToList();
                    
                    // Add new games to collections
                    await AddNewGamesToCollectionsAsync(newGames);
                    
                    // Update game cache
                    await UpdateGameCacheWithNewGamesAsync(newGames);
                    
                    _logger.LogSuccess($"Successfully processed {newGames.Count} games for Steam ID: {steamId}");
                }
                else
                {
                    _logger.LogInfo($"No games found for Steam ID: {steamId}");
                }
                
                // Fetch ban status for this Steam ID
                try
                {
                    var banStatus = await _steamWebApiService.GetBanStatusAsync(steamId);
                    if (banStatus != null)
                    {
                        _logger.LogInfo($"Ban status for {steamId}: {banStatus.Status}");
                        
                        // Update account ban status if account is available
                        if (account != null)
                        {
                            account.BanInfo = banStatus;
                            account.BanStatusLastChecked = DateTime.Now;
                            _logger.LogInfo($"Updated ban status for account {account.AccountName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not fetch ban status for {steamId}: {ex.Message}");
                }
                
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching data for Steam ID {steamId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Add new games to the UI collections
        /// </summary>
        private async Task AddNewGamesToCollectionsAsync(List<Game> newGames)
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var game in newGames)
                    {
                        // Check if game already exists
                        var existingGame = AllGames.FirstOrDefault(g => 
                            g.AppId == game.AppId && g.OwnerSteamId == game.OwnerSteamId);
                        
                        if (existingGame == null)
                        {
                            AllGames.Add(game);
                            FilteredGames.Add(game);
                        }
                    }
                    
                    // Trigger UI updates
                    OnPropertyChanged(nameof(AllGames));
                    OnPropertyChanged(nameof(FilteredGames));
                });
                
                _logger.LogInfo($"Added {newGames.Count} new games to UI collections");
                
                // Switch to Games tab if we have new games
                if (newGames.Any())
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ShowGamesTab();
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error adding new games to collections: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update game cache with new games
        /// </summary>
        private async Task UpdateGameCacheWithNewGamesAsync(List<Game> newGames)
        {
            try
            {
                // Load current cache
                var currentCache = await _gameCacheService.LoadCacheAsync();
                
                // Convert current cache to List<Game>
                var allGames = new List<Game>();
                
                if (currentCache != null && currentCache.Games != null)
                {
                    allGames.AddRange(currentCache.Games.Select(cg => cg.ToGame()));
                }
                
                // Add new games
                allGames.AddRange(newGames);
                
                // Save updated cache
                await _gameCacheService.SaveCacheAsync(allGames);
                
                _logger.LogInfo($"Updated game cache with {newGames.Count} new games");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating game cache: {ex.Message}");
            }
        }

        private async Task UpdateAccountStatusInstantly(SteamAccount switchedAccount)
        {
            try
            {
                _logger.LogInfo($"⚡ INSTANT UI UPDATE: Setting {switchedAccount.DisplayName} as current");
                
                // Immediately set all accounts to offline
                foreach (var account in Accounts)
                {
                    account.IsCurrentAccount = false;
                }
                
                // Set the switched account as current immediately
                switchedAccount.IsCurrentAccount = true;
                
                // Update the CurrentAccount property for the UI header display
                CurrentAccount = switchedAccount;
                _logger.LogInfo($"⚡ Updated CurrentAccount to: {switchedAccount.DisplayName}");
                
                // Update Steam running status
                IsSteamRunning = _steamService.IsSteamRunning();
                _logger.LogInfo($"⚡ Updated Steam running status: {IsSteamRunning}");
                
                // Force immediate UI refresh
                OnPropertyChanged(nameof(Accounts));
                OnPropertyChanged(nameof(FilteredAccounts));
                OnPropertyChanged(nameof(CurrentAccount));
                OnPropertyChanged(nameof(IsSteamRunning));
                FilterAccounts();
                
                _logger.LogSuccess($"✅ INSTANT UI UPDATE COMPLETED for {switchedAccount.DisplayName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in instant status update: {ex.Message}");
            }
        }

        private async Task UpdateAccountsOnlineStatusAsync(List<SteamAccount> accounts)
        {
            try
            {
                _logger.LogInfo("=== UPDATING ACCOUNT ONLINE STATUS ===");
                var currentlyOnlineAccount = await _steamService.GetCurrentlyOnlineAccountAsync();
                _logger.LogInfo($"Currently online account detected: {currentlyOnlineAccount ?? "None"}");
                System.Diagnostics.Debug.WriteLine($"Currently online account: {currentlyOnlineAccount ?? "None"}");

                // Force all accounts to offline first
                _logger.LogInfo("Setting all accounts to offline status...");
                foreach (var account in accounts)
                {
                    if (account.IsCurrentAccount)
                    {
                        _logger.LogInfo($"Setting {account.DisplayName} to offline");
                    }
                    account.IsCurrentAccount = false;
                }

                // Set only the detected online account as current
                if (!string.IsNullOrEmpty(currentlyOnlineAccount))
                {
                    var onlineAccount = accounts.FirstOrDefault(a => 
                        a.AccountName.Equals(currentlyOnlineAccount, StringComparison.OrdinalIgnoreCase));
                    
                    if (onlineAccount != null)
                    {
                        onlineAccount.IsCurrentAccount = true;
                        _logger.LogSuccess($"✅ Set {onlineAccount.DisplayName} as CURRENT/ONLINE account");
                        System.Diagnostics.Debug.WriteLine($"Set {onlineAccount.DisplayName} as online");
                    }
                    else
                    {
                        _logger.LogWarning($"⚠️ Online account '{currentlyOnlineAccount}' not found in account list");
                        System.Diagnostics.Debug.WriteLine($"Online account {currentlyOnlineAccount} not found in account list");
                    }
                }

                // If no account detected as online, try alternative method
                if (string.IsNullOrEmpty(currentlyOnlineAccount))
                {
                    _logger.LogInfo("No current account detected, trying alternative detection...");
                    await TryAlternativeOnlineDetectionAsync(accounts);
                }

                // Force UI property change notifications
                _logger.LogInfo("Triggering UI refresh for account status changes...");
                OnPropertyChanged(nameof(Accounts));
                OnPropertyChanged(nameof(FilteredAccounts));
                
                _logger.LogSuccess("=== ACCOUNT ONLINE STATUS UPDATE COMPLETED ===");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating online status: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error updating online status: {ex.Message}");
            }
        }

        private async Task TryAlternativeOnlineDetectionAsync(List<SteamAccount> accounts)
        {
            try
            {
                // Alternative method: Check which account has the most recent activity
                var mostRecentAccount = accounts
                    .OrderByDescending(a => a.LastLogin)
                    .FirstOrDefault();

                if (mostRecentAccount != null)
                {
                    // Only mark as online if the last login was very recent (within last 10 minutes)
                    var timeSinceLastLogin = DateTime.Now - mostRecentAccount.LastLogin;
                    if (timeSinceLastLogin.TotalMinutes <= 10)
                    {
                        mostRecentAccount.IsCurrentAccount = true;
                        System.Diagnostics.Debug.WriteLine($"Alternative detection: Set {mostRecentAccount.DisplayName} as online (last login: {mostRecentAccount.LastLogin})");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"No recent activity found. Most recent: {mostRecentAccount.DisplayName} at {mostRecentAccount.LastLogin}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in alternative online detection: {ex.Message}");
            }
        }

        private async Task UpdateAccountsGameCountAsync(List<SteamAccount> accounts)
        {
            try
            {
                if (_steamWebApiService == null)
                {
                    StatusMessage = "Steam Web API key required for game count data. Please set your API key.";
                    _logger.LogWarning("Steam Web API service not initialized - cannot fetch game counts");
                    
                    // Show API key input dialog with current status
                    var apiKeyDialog = new ApiKeyInputDialog(_apiKeyService);
                    if (apiKeyDialog.ShowDialog() == true && !string.IsNullOrEmpty(apiKeyDialog.ApiKey))
                    {
                        SetSteamWebApiKey(apiKeyDialog.ApiKey, apiKeyDialog.SaveKey);
                        if (_steamWebApiService == null) return; // Failed to set API key
                    }
                    else
                    {
                        return; // User cancelled or didn't provide API key
                    }
                }

                _logger.LogInfo("=== UPDATING GAME COUNT FOR ACCOUNTS ===");
                StatusMessage = "Fetching game counts from Steam Web API...";

                // Get Steam IDs for accounts that need game count updates
                var accountsToUpdate = accounts.Where(a => !string.IsNullOrEmpty(a.SteamId)).ToList();
                
                if (!accountsToUpdate.Any())
                {
                    _logger.LogWarning("No accounts with Steam IDs found");
                    StatusMessage = "No accounts with Steam IDs to update";
                    return;
                }

                // Prepare account cache data for smart fetching
                var accountsWithCache = accountsToUpdate.ToDictionary(a => a.SteamId, a => a.GameCountLastChecked);
                var gameCountResults = await _steamWebApiService.GetGameCountForMultipleAsync(accountsWithCache);

                // Update account game count information
                var updatedCount = 0;
                var cachedCount = 0;
                
                foreach (var account in accountsToUpdate)
                {
                    if (gameCountResults.TryGetValue(account.SteamId, out var gameCountInfo))
                    {
                        if (gameCountInfo.UseCache)
                        {
                            // Data was cached, no update needed
                            cachedCount++;
                            _logger.LogInfo($"Using cached game count for {account.DisplayName}: {account.PaidGameCount} paid / {account.GameCount} total");
                        }
                        else if (gameCountInfo.HasData)
                        {
                            // Fresh data received, update the account
                            var oldPaidCount = account.PaidGameCount;
                            var oldTotalCount = account.GameCount;
                            
                            account.GameCount = gameCountInfo.TotalGames;
                            account.PaidGameCount = gameCountInfo.PaidGames;
                            account.GameCountLastChecked = gameCountInfo.LastChecked;
                            updatedCount++;
                            
                            // Log if there were changes
                            if (oldPaidCount != gameCountInfo.PaidGames || oldTotalCount != gameCountInfo.TotalGames)
                            {
                                _logger.LogSuccess($"Updated game count for {account.DisplayName}: {gameCountInfo.PaidGames} paid / {gameCountInfo.TotalGames} total (was {oldPaidCount}/{oldTotalCount})");
                            }
                            else
                            {
                                _logger.LogInfo($"Game count unchanged for {account.DisplayName}: {gameCountInfo.PaidGames} paid / {gameCountInfo.TotalGames} total");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"No game count data available for {account.DisplayName}");
                        }
                    }
                }

                // Save updated accounts to file
                await SaveAccountsAsync();

                // Force comprehensive UI refresh for game count updates
                _logger.LogInfo("Triggering UI refresh for game count updates...");
                
                // Log a few sample accounts to verify game count data
                var sampleAccounts = accountsToUpdate.Take(3).ToList();
                foreach (var account in sampleAccounts)
                {
                    _logger.LogInfo($"Sample account {account.DisplayName}: GameCount={account.GameCount}, PaidGameCount={account.PaidGameCount}, HasGameCountInfo={account.HasGameCountInfo}, LastChecked={account.GameCountLastChecked}");
                }
                
                // Clear and reload the accounts collection to trigger UI updates
                var currentAccounts = Accounts.ToList();
                Accounts.Clear();
                foreach (var account in currentAccounts)
                {
                    Accounts.Add(account);
                }
                
                // Also refresh the filtered collection
                FilterAccounts(); // Rebuild the filtered accounts with updated data
                OnPropertyChanged(nameof(FilteredAccounts));
                
                var successCount = gameCountResults.Count(r => r.Value.HasData);
                
                // Create detailed status message including cache stats
                if (cachedCount > 0 && updatedCount > 0)
                {
                    StatusMessage = $"Game count update completed: {updatedCount} updated, {cachedCount} cached, {accountsToUpdate.Count} total accounts.";
                    _logger.LogSuccess($"=== GAME COUNT UPDATE COMPLETED: {updatedCount} updated, {cachedCount} cached, {accountsToUpdate.Count} total ===");
                }
                else if (cachedCount > 0)
                {
                    StatusMessage = $"Game count check completed: {cachedCount} accounts used cached data (no API calls needed).";
                    _logger.LogSuccess($"=== GAME COUNT CHECK COMPLETED: {cachedCount} accounts used cache ===");
                }
                else
                {
                    StatusMessage = $"Game count update completed: {updatedCount}/{accountsToUpdate.Count} accounts updated successfully.";
                    _logger.LogSuccess($"=== GAME COUNT UPDATE COMPLETED: {updatedCount}/{accountsToUpdate.Count} accounts ===");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating game counts: {ex.Message}");
                StatusMessage = $"Error updating game counts: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error updating game counts: {ex.Message}");
            }
        }

        private void FilterAccounts()
        {
            try
            {
                // Clear and repopulate FilteredAccounts with all accounts
                FilteredAccounts.Clear();

                foreach (var account in Accounts)
                {
                    FilteredAccounts.Add(account);
                }

                // Update status
                StatusMessage = $"Showing {FilteredAccounts.Count} accounts";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating accounts: {ex.Message}");
                StatusMessage = $"Error updating accounts: {ex.Message}";
            }
        }

        private async Task LoadAllGamesAsync()
        {
            try
            {
                if (_steamWebApiService == null)
                {
                    StatusMessage = "Steam Web API key required to load games. Please configure it in the Games tab.";
                    _logger.LogWarning("Cannot load games - Steam Web API service not initialized");
                    _logger.LogError("CACHE DEBUG: Cannot create cache - no Steam Web API service");
                    return;
                }

                if (Accounts.Count == 0)
                {
                    StatusMessage = "No accounts found. Please add accounts first.";
                    _logger.LogWarning("Cannot load games - no accounts available");
                    _logger.LogError("CACHE DEBUG: Cannot create cache - no accounts loaded");
                    return;
                }

                IsLoadingGames = true;
                StatusMessage = "Loading all games...";
                _logger.LogInfo("=== LOADING ALL GAMES STARTED ===");
                _logger.LogInfo($"Current account count: {Accounts.Count}");
                _logger.LogInfo($"CACHE DEBUG: Starting game load process with {Accounts.Count} accounts");

                // Always fetch fresh data when button is clicked manually
                _logger.LogInfo("Manual update requested - fetching fresh game data from Steam API");
                StatusMessage = "Fetching latest games data from Steam API...";
                
                _logger.LogInfo("CACHE DEBUG: About to call FetchAllGamesFromApiAsync()");
                var games = await FetchAllGamesFromApiAsync();
                _logger.LogInfo($"CACHE DEBUG: FetchAllGamesFromApiAsync returned {games?.Count ?? 0} games");
                _logger.LogInfo($"FetchAllGamesFromApiAsync returned {games?.Count ?? 0} games");
                
                if (games == null)
                {
                    _logger.LogError("FetchAllGamesFromApiAsync returned null!");
                    _logger.LogError("CACHE DEBUG: Games is null - cache will NOT be created");
                    StatusMessage = "Error: No games data received";
                    return;
                }
                
                if (games.Count == 0)
                {
                    _logger.LogWarning("FetchAllGamesFromApiAsync returned 0 games - this might indicate an issue");
                    _logger.LogWarning("CACHE DEBUG: Games count is 0 - cache will be created but empty");
                    StatusMessage = "Warning: No games found for any account";
                    // Continue to save empty cache
                }
                
                // Save fresh data to cache
                _logger.LogInfo($"About to save {games.Count} games to cache...");
                _logger.LogInfo($"CACHE DEBUG: Calling SaveCacheAsync with {games.Count} games");
                
                try
                {
                    await _gameCacheService.SaveCacheAsync(games);
                    _logger.LogSuccess("Fresh game data saved to cache");
                    _logger.LogSuccess($"CACHE DEBUG: SaveCacheAsync completed successfully for {games.Count} games");
                    
                    // VERIFICATION: Double-check that the file was actually created
                    var (exists, lastModified, sizeBytes) = _gameCacheService.GetCacheInfo();
                    if (exists)
                    {
                        _logger.LogSuccess($"CACHE DEBUG: VERIFICATION PASSED - Cache file exists, size: {sizeBytes} bytes, modified: {lastModified}");
                    }
                    else
                    {
                        _logger.LogError("CACHE DEBUG: VERIFICATION FAILED - Cache file does not exist after save!");
                    }
                }
                catch (Exception cacheEx)
                {
                    _logger.LogError($"CACHE DEBUG: SaveCacheAsync failed! Error: {cacheEx.Message}");
                    _logger.LogError($"CACHE DEBUG: SaveCacheAsync stack trace: {cacheEx.StackTrace}");
                    
                    // Try to save cache with minimal data as fallback
                    try
                    {
                        _logger.LogInfo("CACHE DEBUG: Attempting fallback cache save with empty list...");
                        await _gameCacheService.SaveCacheAsync(new List<Game>());
                        _logger.LogInfo("CACHE DEBUG: Fallback cache save succeeded");
                    }
                    catch (Exception fallbackEx)
                    {
                        _logger.LogError($"CACHE DEBUG: Even fallback cache save failed: {fallbackEx.Message}");
                    }
                    
                    // Don't re-throw - continue with the application
                    StatusMessage = $"Warning: Cache save failed, but {games.Count} games loaded successfully";
                }

                // CRITICAL: Display games immediately in UI - no waiting!
                StatusMessage = "Updating game list...";
                await DisplayGamesImmediatelyAsync(games);

                // Populate available accounts for F2P games
                StatusMessage = "Setting up F2P game account options...";
                try
                {
                    PopulateF2PAccountOptions();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to populate F2P account options: {ex.Message}");
                    // Continue even if F2P setup fails
                }

                var accountsWithGames = games.GroupBy(g => g.OwnerSteamId).Count();
                StatusMessage = $"Updated {AllGames.Count} unique games from {accountsWithGames} accounts";
                
                // Update cache status - always fresh data when manually updated
                CacheStatus = $"Fresh data fetched: {DateTime.Now:MM/dd HH:mm} (expires in 7h)";
                
                _logger.LogSuccess($"=== GAMES LIST UPDATE COMPLETED: {AllGames.Count} unique games from {accountsWithGames} accounts ===");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in LoadAllGamesAsync: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                StatusMessage = $"Error loading games: {ex.Message}";
            }
            finally
            {
                IsLoadingGames = false;
            }
        }

        /// <summary>
        /// Immediately display games in the UI after loading from cache
        /// This ensures games are visible right away without waiting for API updates
        /// </summary>
        private async Task DisplayGamesImmediatelyAsync(List<Game> games)
        {
            try
            {
                _logger.LogInfo($"DisplayGamesImmediatelyAsync: Displaying {games.Count} games in UI immediately");
                
                // Update UI collections on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Clear and populate AllGames
                    AllGames.Clear();
                    foreach (var game in games.OrderBy(g => g.Name))
                    {
                        AllGames.Add(game);
                    }
                    
                    // Immediately populate FilteredGames so games show right away
                    FilteredGames.Clear();
                    foreach (var game in AllGames)
                    {
                        FilteredGames.Add(game);
                    }
                    
                    // Force immediate UI refresh
                    OnPropertyChanged(nameof(AllGames));
                    OnPropertyChanged(nameof(FilteredGames));
                });
                
                // Prepare games for display regardless of current tab
                PrepareGamesForDisplay();
                
                _logger.LogSuccess($"DisplayGamesImmediatelyAsync: {games.Count} games now visible in UI");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in DisplayGamesImmediatelyAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to execute async operations from sync commands
        /// </summary>
        private void ExecuteAsync(Func<Task> asyncMethod)
        {
            Task.Run(async () =>
            {
                try
                {
                    await asyncMethod();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in async command execution: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Scan for demo and beta games on-demand
        /// </summary>
        private async Task ScanDemoBetaGamesAsync()
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = "Scanning for demo and beta games...";
                });

                _logger.LogInfo("=== DEMO/BETA SCAN STARTED ===");

                // Check if API key is configured
                var apiKey = _apiKeyService.LoadApiKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        StatusMessage = "Steam Web API key required for demo/beta scanning";
                    });
                    _logger.LogWarning("Cannot scan for demo/beta games: No Steam Web API key configured");
                    return;
                }

                var steamWebApiService = new SteamWebApiService(apiKey);
                
                // Perform the demo/beta scan
                var demoBetaGames = await steamWebApiService.ScanForDemoBetaGamesAsync();
                
                if (demoBetaGames.Any())
                {
                    // Convert to Game objects and add to current games
                    var gamesToAdd = new List<Game>();
                    
                    foreach (var steamGame in demoBetaGames)
                    {
                        // Check if game already exists in current games
                        if (!AllGames.Any(g => g.AppId == steamGame.AppId))
                        {
                            var game = new Game
                            {
                                AppId = steamGame.AppId,
                                Name = steamGame.Name,
                                PlaytimeForever = steamGame.PlaytimeForever,
                                ImgIconUrl = steamGame.ImgIconUrl,
                                OwnerSteamId = "DEMO",
                                OwnerAccountName = "Demo/Beta",
                                OwnerPersonaName = "Demo/Beta",
                                IsPaid = false, // Most demos/betas are free
                                IsInstalled = true,
                                LastUpdated = DateTime.UtcNow
                            };
                            
                            gamesToAdd.Add(game);
                        }
                    }

                    // Add games to UI immediately
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var game in gamesToAdd)
                        {
                            AllGames.Add(game);
                        }
                        
                        // Refresh filtered games
                        FilteredGames.Clear();
                        foreach (var game in AllGames)
                        {
                            FilteredGames.Add(game);
                        }
                        
                        OnPropertyChanged(nameof(AllGames));
                        OnPropertyChanged(nameof(FilteredGames));
                        
                        StatusMessage = $"Found {gamesToAdd.Count} new demo/beta games! Added to games list.";
                    });

                    // Save to cache so they persist across app restarts
                    await _gameCacheService.SaveCacheAsync(AllGames.ToList());
                    
                    _logger.LogSuccess($"Demo/beta scan completed: Added {gamesToAdd.Count} new games to cache and UI");
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        StatusMessage = "No new demo/beta games found";
                    });
                    _logger.LogInfo("Demo/beta scan completed: No new games found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during demo/beta scan: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = $"Error during demo/beta scan: {ex.Message}";
                });
            }
        }

        /// <summary>
        /// Clear the game cache and force fresh data on next load
        /// </summary>
        private async Task ClearGameCacheAsync()
        {
            try
            {
                // Clear the file cache
                await _gameCacheService.ClearCacheAsync();
                
                // Clear the UI collections on the UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AllGames.Clear();
                    FilteredGames.Clear();
                });
                
                // Update cache status
                CacheStatus = "Cache cleared - no games loaded";
                StatusMessage = "Game cache and UI cleared successfully. Click 'Update Games List' to load fresh data.";
                _logger.LogSuccess("Game cache and UI cleared by user");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error clearing game cache: {ex.Message}");
                StatusMessage = "Error clearing game cache";
            }
        }

        private void PopulateF2PAccountOptions()
{
    try
    {
        _logger.LogInfo("Starting F2P account options setup...");
        
        var accountSnapshot = Accounts.ToList();
        _logger.LogInfo($"Account snapshot created with {accountSnapshot.Count} accounts");
        
        var f2pGamesCount = 0;
        var totalGames = AllGames.Count;
        _logger.LogInfo($"Processing {totalGames} total games for F2P detection...");
        
        // For each F2P game, populate the list of all available accounts
        foreach (var game in AllGames.Where(g => !g.IsPaid))
        {
            try
            {
                if (game.AvailableAccounts == null)
                {
                    _logger.LogWarning($"AvailableAccounts is null for game {game.Name}, initializing...");
                    game.AvailableAccounts = new List<SteamAccount>();
                }
                
                game.AvailableAccounts.Clear();
                game.AvailableAccounts.AddRange(accountSnapshot);
                f2pGamesCount++;
                
                if (f2pGamesCount <= 5) // Log first few for debugging
                {
                    _logger.LogInfo($"Set up F2P game: {game.Name} with {accountSnapshot.Count} account options");
                }
            }
            catch (Exception gameEx)
            {
                _logger.LogError($"Error setting up F2P options for game {game.Name}: {gameEx.Message}");
            }
        }
        
        _logger.LogSuccess($"Set up account options for {f2pGamesCount} F2P games across {accountSnapshot.Count} accounts");
    }
    catch (Exception ex)
    {
        _logger.LogError($"Error setting up F2P account options: {ex.Message}");
        _logger.LogError($"Stack trace: {ex.StackTrace}");
    }
}

        private async void LaunchF2PGame(object parameter)
        {
            try
            {
                if (parameter is object[] args && args.Length == 2)
                {
                    var game = args[0] as Game;
                    var selectedAccount = args[1] as SteamAccount;
                    
                    if (game != null && selectedAccount != null)
                    {
                        _logger.LogInfo($"Launching F2P game {game.Name} ({game.AppId}) with account {selectedAccount.DisplayName}");
                        
                        // Set the pending game to launch
                        SetPendingGameToLaunch(game.AppId);
                        
                        // Switch to the selected account
                        await SwitchToAccountAsync(selectedAccount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error launching F2P game: {ex.Message}");
                StatusMessage = "Failed to launch game";
            }
        }

        private void FilterGames()
        {
            try
            {
                _logger.LogInfo($"FilterGames called - AllGames count: {AllGames.Count}, current search term: '{GameSearchText}'");
                
                FilteredGames.Clear();

                var searchTerm = GameSearchText?.ToLower() ?? "";
                
                foreach (var game in AllGames)
                {
                    if (string.IsNullOrEmpty(searchTerm) ||
                        game.Name.ToLower().Contains(searchTerm) ||
                        game.OwnerDisplay.ToLower().Contains(searchTerm) ||
                        game.GameType.ToLower().Contains(searchTerm))
                    {
                        FilteredGames.Add(game);
                    }
                }

                StatusMessage = $"Showing {FilteredGames.Count} games from {AllGames.Count} total";
                _logger.LogInfo($"FilterGames completed - FilteredGames count: {FilteredGames.Count}");
                
                // Force UI refresh to ensure games are visible
                OnPropertyChanged(nameof(FilteredGames));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in FilterGames: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error filtering games: {ex.Message}");
            }
        }

        private async Task SwitchToAccountAsync(SteamAccount account)
        {
            if (account == null) return;

            // Silent concurrency protection - just ignore rapid clicks, no dialog
            if (_isSwitchingInProgress)
            {
                StatusMessage = "Please wait, finishing previous account switch...";
                return;
            }

            try
            {
                _isSwitchingInProgress = true;
                IsLoading = true;
                StatusMessage = $"Switching to {account.DisplayName}...";

                // Steam will be automatically force-killed if running - fast and reliable

                // Check if this account has been logged in before (has a Steam ID)
                var hasSteamId = !string.IsNullOrEmpty(account.SteamId);
                System.Diagnostics.Debug.WriteLine($"Account {account.DisplayName} has Steam ID: {hasSteamId}, Steam ID: '{account.SteamId}'");
                
                // Check if it exists in Steam's saved accounts by Steam ID
                var accountExistsBySteamId = hasSteamId ? await _steamService.VerifyAccountExistsAsync(account.SteamId) : false;
                System.Diagnostics.Debug.WriteLine($"Account {account.DisplayName} exists in Steam's saved accounts by Steam ID: {accountExistsBySteamId}");
                
                // Also check if it exists by account name (for imported accounts that might have empty Steam IDs)
                var accountExistsByName = await _steamService.VerifyAccountExistsByNameAsync(account.AccountName);
                System.Diagnostics.Debug.WriteLine($"Account {account.DisplayName} exists in Steam's saved accounts by name: {accountExistsByName}");
                
                // Account exists if it has a Steam ID OR if it exists by name in Steam's saved accounts
                var accountExists = accountExistsBySteamId || accountExistsByName;
                System.Diagnostics.Debug.WriteLine($"Account {account.DisplayName} exists in Steam (final result): {accountExists}");
                
                // If account doesn't exist in Steam at all, it means it has never been logged in before
                if (!accountExists)
                {
                    System.Diagnostics.Debug.WriteLine($"Account {account.DisplayName} doesn't exist in Steam - treating as new account");
                    
                    System.Windows.MessageBox.Show(
                        $"Account '{account.DisplayName}' has never been logged into Steam before.\n\n" +
                        "To use this account with the switcher:\n" +
                        "1. Steam will start and show the login screen\n" +
                        "2. Log in manually with your username and password\n" +
                        "3. After successful login, automatic switching will work\n\n" +
                        "Steam will now start with the login screen.",
                        "First Time Login Required",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    
                    // Close Steam if running
                    if (_steamService.IsSteamRunning())
                    {
                        StatusMessage = "Closing Steam for new account login...";
                        await _steamService.CloseSteamAndWaitAsync();
                    }
                    
                    // Clear auto-login settings to force Steam to show login screen
                    StatusMessage = "Preparing Steam for new account login...";
                    await _steamService.ClearAutoLoginSettingsAsync();
                    
                    // Start Steam with force login screen
                    StatusMessage = $"Starting Steam login screen for {account.DisplayName}...";
                    await _steamService.StartSteamWithLoginScreenAsync();
                    
                    StatusMessage = $"Please log in manually to {account.DisplayName} in Steam";
                    return;
                }

                // Regular account switching for existing accounts
                System.Diagnostics.Debug.WriteLine($"Account {account.DisplayName} has Steam ID - proceeding with regular account switching");
                StatusMessage = $"Closing Steam and updating configuration for {account.DisplayName}...";
                var success = await _steamService.SwitchToAccountAsync(account);
                
                if (success)
                {
                    StatusMessage = $"Successfully switched to {account.DisplayName}";
                    
                    // Immediately update UI to show the switched account as current
                    // This provides instant visual feedback while Steam starts
                    await UpdateAccountStatusInstantly(account);
                    
                    StatusMessage = $"Account switched to {account.DisplayName}";

                    // If a game launch was requested, launch it now after a short wait
                    if (_pendingAppIdToLaunch.HasValue)
                    {
                        var appIdToLaunch = _pendingAppIdToLaunch.Value;
                        _pendingAppIdToLaunch = null;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(2000); // allow Steam a moment to settle
                                await _steamService.LaunchGameAsync(appIdToLaunch);
                                _logger.LogSuccess($"Auto-launched game {appIdToLaunch} after switching account");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Failed to auto-launch game {appIdToLaunch}: {ex.Message}");
                            }
                        });
                    }
                    
                    // Then verify with Steam in the background (non-blocking)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1500); // Reduced from 3000ms to 1500ms
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await UpdateAccountsOnlineStatusAsync(Accounts.ToList());
                            
                            // Also update CurrentAccount based on actual detection
                            var detectedCurrentAccount = await _steamService.GetCurrentAccountAsync();
                            if (detectedCurrentAccount != null)
                            {
                                CurrentAccount = detectedCurrentAccount;
                                _logger.LogInfo($"🔄 Background verification: CurrentAccount confirmed as {detectedCurrentAccount.DisplayName}");
                            }
                            
                            // Update Steam status
                            IsSteamRunning = _steamService.IsSteamRunning();
                        });
                    });
                }
                else
                {
                    StatusMessage = $"Failed to switch to {account.DisplayName} - please try logging in manually first";
                    
                    System.Windows.MessageBox.Show(
                        $"Could not switch to '{account.DisplayName}'.\n\n" +
                        "This usually means the account needs to be logged into Steam manually at least once.\n\n" +
                        "Please:\n" +
                        "1. Log into Steam manually with this account\n" +
                        "2. Log out\n" +
                        "3. Try switching again",
                        "Switch Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error switching to account: {ex.Message}");
                StatusMessage = $"Error switching to account: {ex.Message}";
                System.Windows.MessageBox.Show($"Error switching to account: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                _isSwitchingInProgress = false;
            }
        }



        private async Task ExportPasswordsToFileAsync()
        {
            try
            {
                StatusMessage = "Exporting account passwords...";
                var accounts = await _steamService.LoadSavedAccountsAsync();
                
                var exportPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"Steam_Account_Passwords_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                );

                var exportData = new StringBuilder();
                exportData.AppendLine("=== STEAM ACCOUNT PASSWORD EXPORT ===");
                exportData.AppendLine($"Exported on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                exportData.AppendLine($"Total accounts: {accounts.Count}");
                exportData.AppendLine();
                
                var accountsWithPasswords = 0;
                var accountsWithoutPasswords = 0;
                
                exportData.AppendLine("ACCOUNTS WITH STORED PASSWORDS:");
                exportData.AppendLine("===============================");
                
                foreach (var account in accounts.Where(a => a.HasStoredPassword))
                {
                    exportData.AppendLine($"Username: {account.AccountName}");
                    exportData.AppendLine($"Display Name: {account.DisplayName}");
                    exportData.AppendLine($"Password: {account.StoredPassword}");
                    exportData.AppendLine($"Steam ID: {account.SteamId}");
                    exportData.AppendLine($"Last Login: {account.LastLogin:yyyy-MM-dd HH:mm:ss}");
                    exportData.AppendLine("---");
                    accountsWithPasswords++;
                }
                
                exportData.AppendLine();
                exportData.AppendLine("ACCOUNTS WITHOUT STORED PASSWORDS:");
                exportData.AppendLine("=================================");
                
                foreach (var account in accounts.Where(a => !a.HasStoredPassword))
                {
                    exportData.AppendLine($"Username: {account.AccountName}");
                    exportData.AppendLine($"Display Name: {account.DisplayName}");
                    exportData.AppendLine($"Steam ID: {account.SteamId}");
                    exportData.AppendLine($"Last Login: {account.LastLogin:yyyy-MM-dd HH:mm:ss}");
                    exportData.AppendLine("---");
                    accountsWithoutPasswords++;
                }
                
                exportData.AppendLine();
                exportData.AppendLine("SUMMARY:");
                exportData.AppendLine($"Accounts with passwords: {accountsWithPasswords}");
                exportData.AppendLine($"Accounts without passwords: {accountsWithoutPasswords}");
                
                await File.WriteAllTextAsync(exportPath, exportData.ToString());
                
                StatusMessage = $"Passwords exported to: {Path.GetFileName(exportPath)}";
                
                System.Windows.MessageBox.Show(
                    $"Account passwords exported successfully!\n\n" +
                    $"File: {Path.GetFileName(exportPath)}\n" +
                    $"Location: {Path.GetDirectoryName(exportPath)}\n\n" +
                    $"Accounts with passwords: {accountsWithPasswords}\n" +
                    $"Accounts without passwords: {accountsWithoutPasswords}",
                    "Export Complete",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export failed: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Failed to export passwords:\n{ex.Message}",
                    "Export Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        public async Task RefreshAccountsAsync()
        {
            try
            {
                StatusMessage = "Refreshing accounts...";
                
                // First, clean up any existing duplicates in saved accounts
                await _steamService.CleanupSavedAccountsAsync();
                
                // Discover accounts from Steam
                var discoveredAccounts = await _steamService.DiscoverAccountsAsync();
                
                // Load saved accounts
                var savedAccounts = await _steamService.LoadSavedAccountsAsync();
                
                // Merge discovered and saved accounts, prioritizing discovered accounts
                var mergedAccounts = new List<SteamAccount>();
                var seenAccountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenSteamIds = new HashSet<string>();

                // Add discovered accounts first (they're more up-to-date)
                foreach (var account in discoveredAccounts)
                {
                    if (!string.IsNullOrWhiteSpace(account.AccountName) &&
                        !seenAccountNames.Contains(account.AccountName) &&
                        !seenSteamIds.Contains(account.SteamId))
                    {
                        mergedAccounts.Add(account);
                        seenAccountNames.Add(account.AccountName);
                        seenSteamIds.Add(account.SteamId);
                    }
                }

                // Add saved accounts that weren't discovered (in case they're not in Steam's current list)
                foreach (var account in savedAccounts)
                {
                    if (!string.IsNullOrWhiteSpace(account.AccountName) &&
                        !seenAccountNames.Contains(account.AccountName) &&
                        !seenSteamIds.Contains(account.SteamId))
                    {
                        // Check if this imported account actually exists in Steam's saved accounts
                        var existsInSteam = await _steamService.VerifyAccountExistsByNameAsync(account.AccountName);
                        if (existsInSteam)
                        {
                            System.Diagnostics.Debug.WriteLine($"Imported account {account.AccountName} exists in Steam - will be updated with proper data on next discovery");
                        }
                        
                        mergedAccounts.Add(account);
                        seenAccountNames.Add(account.AccountName);
                        seenSteamIds.Add(account.SteamId);
                    }
                }

                // Sort by last login (most recent first)
                mergedAccounts = mergedAccounts.OrderByDescending(a => a.LastLogin).ToList();

                // Update the accounts list
                Accounts.Clear();
                foreach (var account in mergedAccounts)
                {
                    Accounts.Add(account);
                }

                // Save the merged accounts
                await _steamService.SaveAccountsAsync(mergedAccounts);
                
                StatusMessage = $"Refreshed {mergedAccounts.Count} accounts";
                
                System.Diagnostics.Debug.WriteLine($"Refresh completed: {mergedAccounts.Count} accounts total");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error refreshing accounts: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error in RefreshAccountsAsync: {ex.Message}");
            }
        }

        private async Task DiscoverAccountsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Discovering accounts...";

                var discoveredAccounts = await _steamService.DiscoverAccountsAsync();
                System.Diagnostics.Debug.WriteLine($"Discovered {discoveredAccounts.Count} accounts from Steam");
                
                var existingSteamIds = Accounts.Select(a => a.SteamId).ToHashSet();
                var newAccountsCount = 0;

                foreach (var account in discoveredAccounts)
                {
                    System.Diagnostics.Debug.WriteLine($"Processing discovered account: {account.DisplayName} ({account.SteamId})");
                    if (!existingSteamIds.Contains(account.SteamId))
                    {
                        Accounts.Add(account);
                        newAccountsCount++;
                        System.Diagnostics.Debug.WriteLine($"Added new account: {account.DisplayName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Account already exists: {account.DisplayName}");
                    }
                }

                await SaveAccountsAsync();
                FilterAccounts(); // Update filtered accounts
                
                if (newAccountsCount > 0)
                {
                    StatusMessage = $"Discovered {newAccountsCount} new accounts";
                    System.Windows.MessageBox.Show(
                        $"Successfully discovered {newAccountsCount} new Steam accounts!\n\nTotal accounts: {Accounts.Count}",
                        "Account Discovery Complete",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = "No new accounts discovered";
                    System.Windows.MessageBox.Show(
                        $"No new accounts were discovered.\n\nTotal accounts: {Accounts.Count}\n\nAll accounts may already be saved, or Steam may not have any saved accounts.",
                        "Account Discovery Complete",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (DirectoryNotFoundException ex)
            {
                StatusMessage = $"Steam not found: {ex.Message}";
                var result = System.Windows.MessageBox.Show(
                    $"Steam installation not found.\n\nError: {ex.Message}\n\nWould you like to manually select your Steam directory?",
                    "Steam Not Found",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    await SelectSteamDirectoryAsync();
                }
            }
            catch (FileNotFoundException ex)
            {
                StatusMessage = $"Steam login file not found: {ex.Message}";
                var result = System.Windows.MessageBox.Show(
                    $"Steam login file not found.\n\nError: {ex.Message}\n\nThis might mean Steam is in a different location. Would you like to manually select your Steam directory?",
                    "Steam Login File Not Found",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    await SelectSteamDirectoryAsync();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error discovering accounts: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Error discovering accounts.\n\nError: {ex.Message}",
                    "Discovery Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RemoveAccountAsync(SteamAccount account)
        {
            if (account == null) return;

            // Show confirmation dialog
            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to remove '{account.DisplayName}' from the account switcher?\n\nThis will not delete the account from Steam, only remove it from this application.",
                "Confirm Account Removal",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                Accounts.Remove(account);
                FilteredAccounts.Remove(account); // Also remove from filtered list
                await SaveAccountsAsync();
                
                if (CurrentAccount?.SteamId == account.SteamId)
                {
                    CurrentAccount = null;
                }

                StatusMessage = $"Removed {account.DisplayName}";
            }
        }

        private async Task SaveAccountsAsync()
        {
            try
            {
                await _steamService.SaveAccountsAsync(Accounts.ToList());
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving accounts: {ex.Message}";
            }
        }

        private async Task BrowseSteamFolderAsync()
        {
            try
            {
                var currentPath = _steamService.GetCurrentSteamPath();
                var result = System.Windows.MessageBox.Show(
                    $"Current Steam path: {currentPath}\n\nWould you like to test the current Steam access?",
                    "Steam Path",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    await TestSteamAccessAsync();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async Task TestSteamAccessAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Testing Steam access...";

                var testResult = await _steamService.TestSteamAccessAsync();
                StatusMessage = "Steam access test completed";
                
                // Show detailed results in a message box
                System.Windows.MessageBox.Show(testResult, "Steam Access Test Results", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error testing Steam access: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ShowAccountsTab()
        {
            IsAccountsTabVisible = true;
            IsLoginTabVisible = false;
            IsGamesTabVisible = false;
        }

        private void ShowLoginTab()
        {
            IsAccountsTabVisible = false;
            IsLoginTabVisible = true;
            IsGamesTabVisible = false;
        }

        /// <summary>
        /// Ensure games are visible in the UI when switching to Games tab
        /// </summary>
        private void EnsureGamesVisibility()
        {
            try
            {
                if (AllGames.Count > 0)
                {
                    _logger.LogInfo($"EnsureGamesVisibility: {AllGames.Count} games available, checking UI state");
                    
                    // If FilteredGames is empty but AllGames has data, populate it
                    if (FilteredGames.Count == 0)
                    {
                        _logger.LogInfo("FilteredGames is empty, populating from AllGames");
                        FilteredGames.Clear();
                        foreach (var game in AllGames)
                        {
                            FilteredGames.Add(game);
                        }
                        
                        // Force UI refresh
                        OnPropertyChanged(nameof(FilteredGames));
                        OnPropertyChanged(nameof(AllGames));
                        
                        _logger.LogSuccess($"EnsureGamesVisibility: UI refreshed - FilteredGames: {FilteredGames.Count}, AllGames: {AllGames.Count}");
                    }
                    else
                    {
                        _logger.LogInfo($"EnsureGamesVisibility: Games already visible - FilteredGames: {FilteredGames.Count}, AllGames: {AllGames.Count}");
                    }
                }
                else
                {
                    _logger.LogInfo("EnsureGamesVisibility: No games available yet");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in EnsureGamesVisibility: {ex.Message}");
            }
        }

        private void ShowGamesTab()
        {
            IsAccountsTabVisible = false;
            IsLoginTabVisible = false;
            IsGamesTabVisible = true;
            
            // If we have games loaded, ensure they're visible in the UI
            if (AllGames.Count > 0)
            {
                _logger.LogInfo($"ShowGamesTab: {AllGames.Count} games available, ensuring UI visibility");
                
                // Force a refresh of the games display immediately
                    try
                    {
                        EnsureGamesVisibility();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error refreshing games UI in ShowGamesTab: {ex.Message}");
                    }
            }
            else
            {
                _logger.LogInfo("ShowGamesTab: No games available yet");
            }
        }

        private async Task SaveAccountAsync()
        {
            try
            {
                _logger.LogInfo($"=== SAVE ACCOUNT STARTED ===");
                _logger.LogInfo($"Input - Username: '{LoginUsername}', Password: '{(string.IsNullOrEmpty(LoginPassword) ? "EMPTY" : "PROVIDED")}'");
                _logger.LogInfo($"Current accounts count: {Accounts.Count}");

                if (string.IsNullOrWhiteSpace(LoginUsername))
                {
                    _logger.LogError("Save account failed: Username is empty");
                    LoginStatusMessage = "Please enter a username";
                    return;
                }

                if (string.IsNullOrWhiteSpace(LoginPassword))
                {
                    _logger.LogError("Save account failed: Password is empty");
                    LoginStatusMessage = "Please enter a password";
                    return;
                }

                IsLoading = true;
                LoginStatusMessage = "Saving account...";

                // Check if account already exists
                var existingAccount = Accounts.FirstOrDefault(a => 
                    a.AccountName.Equals(LoginUsername, StringComparison.OrdinalIgnoreCase));
                
                _logger.LogInfo($"Existing account check: {(existingAccount != null ? "FOUND" : "NOT FOUND")}");
                
                if (existingAccount != null)
                {
                    _logger.LogInfo($"Existing account details:");
                    _logger.LogInfo($"  - SteamId: '{existingAccount.SteamId}'");
                    _logger.LogInfo($"  - AccountName: '{existingAccount.AccountName}'");
                    _logger.LogInfo($"  - DisplayName: '{existingAccount.DisplayName}'");
                    _logger.LogInfo($"  - HasStoredPassword: {existingAccount.HasStoredPassword}");
                    _logger.LogInfo($"  - LastLogin: {existingAccount.LastLogin}");
                    
                    // If account exists but has no password, update it with the password
                    if (!existingAccount.HasStoredPassword)
                    {
                        _logger.LogInfo("=== UPDATING EXISTING ACCOUNT WITHOUT PASSWORD ===");
                        LoginStatusMessage = "Updating existing account with password...";
                        
                        existingAccount.StoredPassword = LoginPassword;
                        existingAccount.LastLogin = DateTime.Now; // Update last login time
                        
                        _logger.LogInfo($"Password assigned to existing account");
                        _logger.LogInfo($"LastLogin updated to: {existingAccount.LastLogin}");
                        _logger.LogInfo($"HasStoredPassword now: {existingAccount.HasStoredPassword}");
                        
                        // Save to file (this will encrypt the password)
                        _logger.LogInfo("Calling SaveAccountsAsync to persist changes...");
                        await SaveAccountsAsync();
                        _logger.LogSuccess("SaveAccountsAsync completed successfully");
                        
                        // Clear form
                        LoginUsername = "";
                        LoginPassword = "";
                        _logger.LogInfo("Form cleared");
                        
                        // Show success message
                        LoginStatusMessage = $"Account '{existingAccount.DisplayName}' updated with encrypted password!";
                        
                        // Switch to accounts tab
                        ShowAccountsTab();
                        
                        StatusMessage = $"Updated account with password: {existingAccount.DisplayName}";
                        _logger.LogSuccess($"=== SAVE ACCOUNT COMPLETED: UPDATED EXISTING ACCOUNT '{existingAccount.DisplayName}' ===");
                        return;
                    }
                    else
                    {
                        // Account exists and already has a password
                        _logger.LogWarning($"Account '{existingAccount.DisplayName}' already has stored password - blocking save operation");
                        LoginStatusMessage = $"Account '{existingAccount.DisplayName}' already exists with a stored password. Use a different username or remove the existing account first.";
                        return;
                    }
                }

                // Create new account with encrypted password
                _logger.LogInfo("=== CREATING NEW ACCOUNT ===");
                var newAccount = new SteamAccount
                {
                    AccountName = LoginUsername,
                    PersonaName = LoginUsername,
                    SteamId = "", // Will be set when discovered
                    LastLogin = DateTime.Now,
                    StoredPassword = LoginPassword // Store in memory for encryption
                };

                _logger.LogInfo($"New account created:");
                _logger.LogInfo($"  - AccountName: '{newAccount.AccountName}'");
                _logger.LogInfo($"  - PersonaName: '{newAccount.PersonaName}'");
                _logger.LogInfo($"  - DisplayName: '{newAccount.DisplayName}'");
                _logger.LogInfo($"  - SteamId: '{newAccount.SteamId}'");
                _logger.LogInfo($"  - HasStoredPassword: {newAccount.HasStoredPassword}");
                _logger.LogInfo($"  - LastLogin: {newAccount.LastLogin}");

                // Add to accounts list
                Accounts.Add(newAccount);
                _logger.LogInfo($"Account added to collection. Total accounts: {Accounts.Count}");
                
                // Save to file
                _logger.LogInfo("Calling SaveAccountsAsync to persist new account...");
                await SaveAccountsAsync();
                _logger.LogSuccess("SaveAccountsAsync completed successfully");
                
                // Update filtered accounts
                FilterAccounts();
                _logger.LogInfo("FilterAccounts called");
                
                // Clear form
                LoginUsername = "";
                LoginPassword = "";
                _logger.LogInfo("Form cleared");
                
                // Show success message
                LoginStatusMessage = $"Account '{newAccount.DisplayName}' saved successfully with encrypted password!";
                
                // Switch to accounts tab
                ShowAccountsTab();
                
                StatusMessage = $"Added new account: {newAccount.DisplayName}";
                _logger.LogSuccess($"=== SAVE ACCOUNT COMPLETED: NEW ACCOUNT '{newAccount.DisplayName}' CREATED ===");
                
                // Automatically fetch Steam data for this new account
                _ = Task.Run(async () => await AutoFetchSteamDataForAccountAsync(newAccount));
            }
            catch (Exception ex)
            {
                _logger.LogError($"=== SAVE ACCOUNT FAILED WITH EXCEPTION ===");
                _logger.LogError($"Exception: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                
                LoginStatusMessage = $"Error saving account: {ex.Message}";
                StatusMessage = $"Error saving account: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                _logger.LogInfo("IsLoading set to false");
            }
        }



        private async Task ExportAccountsAsync()
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = $"steam_accounts_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var json = JsonConvert.SerializeObject(Accounts.ToList(), Formatting.Indented);
                    await File.WriteAllTextAsync(saveFileDialog.FileName, json);
                    StatusMessage = $"Accounts exported to {Path.GetFileName(saveFileDialog.FileName)}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error exporting accounts: {ex.Message}";
            }
        }

        private async Task ImportAccountsAsync()
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = "json"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    _logger.LogInfo($"Importing accounts from: {openFileDialog.FileName}");
                    var json = await File.ReadAllTextAsync(openFileDialog.FileName);
                    var importedAccounts = JsonConvert.DeserializeObject<List<SteamAccount>>(json);
                    
                    if (importedAccounts != null)
                    {
                        var existingSteamIds = Accounts.Select(a => a.SteamId).ToHashSet();
                        var addedCount = 0;
                        var updatedCount = 0;
                        
                        foreach (var account in importedAccounts)
                        {
                            var existingAccount = Accounts.FirstOrDefault(a => a.SteamId == account.SteamId || a.AccountName == account.AccountName);
                            
                            if (existingAccount == null)
                            {
                                Accounts.Add(account);
                                addedCount++;
                                _logger.LogInfo($"Added new account: {account.AccountName}");
                            }
                            else
                            {
                                // Update existing account with new data, preserving passwords
                                existingAccount.PersonaName = account.PersonaName;
                                existingAccount.AvatarUrl = account.AvatarUrl;
                                existingAccount.LastLogin = account.LastLogin;
                                
                                // Import encrypted password if available and current account doesn't have one
                                if (!string.IsNullOrEmpty(account.EncryptedPassword) && string.IsNullOrEmpty(existingAccount.EncryptedPassword))
                                {
                                    existingAccount.EncryptedPassword = account.EncryptedPassword;
                                    _logger.LogInfo($"Imported password for existing account: {account.AccountName}");
                                }
                                
                                updatedCount++;
                                _logger.LogInfo($"Updated existing account: {account.AccountName}");
                            }
                        }

                        await _steamService.SaveAccountsAsync(Accounts.ToList());
                        await LoadAccountsAsync(); // Reload to ensure proper password decryption
                        
                        StatusMessage = $"Import complete: {addedCount} new, {updatedCount} updated";
                        
                        System.Windows.MessageBox.Show(
                            $"Account import completed!\n\n" +
                            $"New accounts added: {addedCount}\n" +
                            $"Existing accounts updated: {updatedCount}\n" +
                            $"Total processed: {importedAccounts.Count}",
                            "Import Complete",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error importing accounts: {ex.Message}");
                StatusMessage = $"Error importing accounts: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Failed to import accounts:\n{ex.Message}",
                    "Import Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }



        private async Task ExportAccountsWithPasswordsAsync()
        {
            try
            {
                StatusMessage = "Exporting accounts with passwords...";
                var accounts = await _steamService.LoadSavedAccountsAsync();
                
                var exportPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"Steam_Accounts_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                );

                // Create a clean export that includes encrypted passwords
                var exportAccounts = accounts.Select(account => new SteamAccount
                {
                    SteamId = account.SteamId,
                    AccountName = account.AccountName,
                    PersonaName = account.PersonaName,
                    AvatarUrl = account.AvatarUrl,
                    LastLogin = account.LastLogin,
                    IsVACBanned = account.IsVACBanned,
                    IsLimited = account.IsLimited,
                    EncryptedPassword = account.EncryptedPassword, // Include encrypted passwords
                    BanStatusJson = account.BanStatusJson,
                    BanStatusLastChecked = account.BanStatusLastChecked
                }).ToList();

                var json = JsonConvert.SerializeObject(exportAccounts, Formatting.Indented);
                await File.WriteAllTextAsync(exportPath, json);
                
                var accountsWithPasswords = accounts.Count(a => !string.IsNullOrEmpty(a.EncryptedPassword));
                
                StatusMessage = $"Accounts exported to: {Path.GetFileName(exportPath)}";
                
                System.Windows.MessageBox.Show(
                    $"Accounts exported successfully!\n\n" +
                    $"File: {Path.GetFileName(exportPath)}\n" +
                    $"Location: Desktop\n\n" +
                    $"Total accounts: {accounts.Count}\n" +
                    $"Accounts with passwords: {accountsWithPasswords}\n\n" +
                    $"This backup includes encrypted passwords and can be imported later.",
                    "Export Complete",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                    
                _logger.LogSuccess($"Exported {accounts.Count} accounts with {accountsWithPasswords} passwords to {exportPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error exporting accounts: {ex.Message}");
                StatusMessage = $"Export failed: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Failed to export accounts:\n{ex.Message}",
                    "Export Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }











        private void ToggleView()
        {
            ShowCompactView = !ShowCompactView;
        }

        private async Task BulkDeleteAccountsAsync(List<SteamAccount> accountsToDelete)
        {
            if (accountsToDelete == null || accountsToDelete.Count == 0) return;

            var confirmed = System.Windows.MessageBox.Show(
                $"Are you sure you want to remove {accountsToDelete.Count} account(s) from the account switcher?",
                "Confirm Bulk Removal",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (confirmed == System.Windows.MessageBoxResult.No) return;

            try
            {
                IsLoading = true;
                StatusMessage = $"Removing {accountsToDelete.Count} accounts...";

                var initialCount = Accounts.Count;
                var removedCount = 0;

                foreach (var account in accountsToDelete)
                {
                    if (Accounts.Remove(account))
                    {
                        FilteredAccounts.Remove(account);
                        removedCount++;
                    }
                }

                await SaveAccountsAsync();
                FilterAccounts();

                StatusMessage = $"Successfully removed {removedCount} accounts.";
                System.Windows.MessageBox.Show(
                    $"Successfully removed {removedCount} accounts from your list.\n\nTotal accounts: {Accounts.Count}",
                    "Bulk Removal Successful",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error removing accounts: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Error removing accounts: {ex.Message}\n\nPlease try again.",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        // Removed: PowerShell auto-fill methods for simplified account switching

        private async Task MonitorForSuccessfulLogin(SteamAccount account)
        {
            try
            {
                _logger.LogInfo($"Starting login monitoring for: {account.AccountName}");
                
                // Check periodically if the account is now logged in
                for (int i = 0; i < 30; i++) // Check for up to 5 minutes
                {
                    await Task.Delay(10000); // Wait 10 seconds between checks
                    
                    var currentOnline = await _steamService.GetCurrentlyOnlineAccountAsync();
                    if (!string.IsNullOrEmpty(currentOnline) && 
                        currentOnline.Equals(account.AccountName, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogSuccess($"Account {account.AccountName} successfully logged in! Restoring auto-login settings...");
                        
                        // Restore auto-login settings for this account
                        await _steamService.RestoreAutoLoginSettingsAsync(account.AccountName);
                        
                        _logger.LogSuccess($"Auto-login settings restored for: {account.AccountName}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during login monitoring for {account.AccountName}: {ex.Message}");
            }
        }

        private void OpenLogFile()
        {
            try
            {
                var logFilePath = _steamService.GetLogFilePath();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logFilePath,
                    UseShellExecute = true
                });
                StatusMessage = "Log file opened";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening log file: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Could not open log file: {ex.Message}\n\nLog file location: {_steamService.GetLogFilePath()}",
                    "Log File Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        private void OpenAppDataFolder()
        {
            try
            {
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleSteamSwitcher");
                
                // Create the directory if it doesn't exist
                Directory.CreateDirectory(appDataPath);
                
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = appDataPath,
                    UseShellExecute = true
                });
                StatusMessage = "AppData folder opened";
                _logger.LogInfo($"Opened AppData folder: {appDataPath}");
            }
            catch (Exception ex)
            {
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleSteamSwitcher");
                StatusMessage = $"Error opening AppData folder: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Could not open AppData folder: {ex.Message}\n\nFolder location: {appDataPath}",
                    "AppData Folder Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        private async Task DiagnoseGameLoadingAsync()
        {
            try
            {
                _logger.LogInfo("=== GAME LOADING DIAGNOSTICS STARTED ===");
                StatusMessage = "Running game loading diagnostics...";

                // Check accounts
                _logger.LogInfo($"Total accounts loaded: {Accounts.Count}");
                foreach (var account in Accounts.Take(5)) // Log first 5 accounts
                {
                    _logger.LogInfo($"Account: {account.DisplayName} - SteamID: {account.SteamId} - Valid: {!string.IsNullOrEmpty(account.SteamId)}");
                }

                // Check API service
                if (_steamWebApiService == null)
                {
                    _logger.LogError("Steam Web API service is NULL - this is the problem!");
                    StatusMessage = "ERROR: Steam Web API service not initialized";
                    return;
                }
                _logger.LogInfo("Steam Web API service is initialized");

                // Check API key
                var hasApiKey = _apiKeyService.HasSavedApiKey();
                _logger.LogInfo($"Has API key saved: {hasApiKey}");

                // Test one account's game loading
                var testAccount = Accounts.FirstOrDefault(a => !string.IsNullOrEmpty(a.SteamId));
                if (testAccount != null)
                {
                    _logger.LogInfo($"Testing game loading for account: {testAccount.DisplayName}");
                    try
                    {
                        var games = await _steamWebApiService.GetAccountGamesAsync(testAccount.SteamId);
                        _logger.LogInfo($"Successfully loaded {games.Count} games for {testAccount.DisplayName}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to load games for {testAccount.DisplayName}: {ex.Message}");
                    }
                }

                // Check cache file path
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleSteamSwitcher");
                var cacheFilePath = Path.Combine(appDataPath, "games_cache.json");
                _logger.LogInfo($"Cache file path: {cacheFilePath}");
                _logger.LogInfo($"Cache file exists: {File.Exists(cacheFilePath)}");
                if (File.Exists(cacheFilePath))
                {
                    var fileInfo = new FileInfo(cacheFilePath);
                    _logger.LogInfo($"Cache file size: {fileInfo.Length} bytes, Last modified: {fileInfo.LastWriteTime}");
                }

                StatusMessage = "Diagnostics completed - check log for details";
                _logger.LogInfo("=== GAME LOADING DIAGNOSTICS COMPLETED ===");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during diagnostics: {ex.Message}");
                StatusMessage = $"Diagnostics error: {ex.Message}";
            }
        }

        /// <summary>
        /// Clear the contents of the log file without deleting the file itself
        /// </summary>
        private async Task ClearLogFileAsync()
        {
            try
            {
                var logFilePath = _steamService.GetLogFilePath();
                if (File.Exists(logFilePath))
                {
                    // Clear the log file contents
                    await File.WriteAllTextAsync(logFilePath, string.Empty);
                    
                    // Log the clearing action (this will be the first entry in the cleared file)
                    _logger.LogInfo("=== LOG FILE CLEARED BY USER ===");
                    _logger.LogInfo($"Log file contents cleared at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    _logger.LogInfo("=== END OF CLEARED LOG ===");
                    
                    StatusMessage = "Log file contents cleared successfully";
                    _logger.LogSuccess("Log file cleared by user");
                }
                else
                {
                    StatusMessage = "Log file not found";
                    _logger.LogWarning("User attempted to clear non-existent log file");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error clearing log file: {ex.Message}");
                StatusMessage = $"Error clearing log file: {ex.Message}";
            }
        }

        /// <summary>
        /// Automatically fetch Steam data for a newly added account
        /// </summary>
        private async Task AutoFetchSteamDataForAccountAsync(SteamAccount account)
        {
            try
            {
                _logger.LogInfo($"=== AUTO-FETCHING STEAM DATA FOR NEW ACCOUNT: {account.AccountName} ===");
                StatusMessage = $"🔄 Fetching Steam data for {account.DisplayName}...";
                
                // For now, just log that we would fetch data
                // TODO: Implement full Steam API integration when methods are available
                _logger.LogInfo($"Would fetch Steam data for {account.AccountName} - implementation pending");
                StatusMessage = $"ℹ️ Auto-fetch feature coming soon for {account.DisplayName}";
                
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in auto-fetch placeholder for {account.AccountName}: {ex.Message}");
                StatusMessage = $"❌ Error in auto-fetch for {account.DisplayName}";
            }
        }

        /// <summary>
        /// Show detailed information about an account
        /// </summary>
        private void ShowAccountDetails(SteamAccount account)
        {
            if (account == null) return;

            var details = $"Account Details for: {account.DisplayName}\n\n" +
                         $"Account Name: {account.AccountName}\n" +
                         $"Steam ID: {account.SteamId ?? "Not available"}\n" +
                         $"Last Login: {account.LastLogin:yyyy-MM-dd HH:mm:ss}\n" +
                         $"Has Stored Password: {(account.HasStoredPassword ? "Yes" : "No")}\n" +
                         $"Is Current Account: {(account.IsCurrentAccount ? "Yes" : "No")}\n\n";

            // Add game count information
            if (account.HasGameCountInfo)
            {
                details += $"Game Information:\n" +
                          $"Total Games: {account.GameCount}\n" +
                          $"Paid Games: {account.PaidGameCount}\n" +
                          $"Free Games: {account.GameCount - account.PaidGameCount}\n" +
                          $"Last Game Count Check: {account.GameCountLastChecked:yyyy-MM-dd HH:mm:ss}\n\n";
            }
            else
            {
                details += "Game Information: Not available\n\n";
            }

            // Add ban status information
            if (account.HasBanInfo)
            {
                details += $"Ban Status: {account.BanStatusDisplay}\n" +
                          $"Last Ban Check: {account.BanStatusLastChecked:yyyy-MM-dd HH:mm:ss}\n\n";
            }
            else
            {
                details += "Ban Status: Not checked\n\n";
            }

            // Additional technical details
            details += $"Technical Information:\n" +
                      $"Display Name: {account.DisplayName}\n" +
                      $"Profile URL: https://steamcommunity.com/profiles/{account.SteamId}\n" +
                      $"Account Created: {account.LastLogin:yyyy-MM-dd} (Last Login Date)";

            // Create and show the custom details window
            Application.Current.Dispatcher.Invoke(() =>
            {
                var detailsWindow = new AccountDetailsWindow();
                var viewModel = detailsWindow.DataContext as AccountDetailsViewModel;
                if (viewModel != null)
                {
                    viewModel.DetailsText = details;
                }
                detailsWindow.Owner = Application.Current.MainWindow;
                detailsWindow.ShowDialog();
            });
        }

        private async Task CloseSteamAndClearSettings()
        {
            // Close Steam if it's running
            if (_steamService.IsSteamRunning())
            {
                await _steamService.CloseSteamAndWaitAsync();
            }
            
            // Clear auto-login settings
            await _steamService.ClearAutoLoginSettingsAsync();
        }

        private async Task AddPasswordAsync(SteamAccount account)
        {
            if (account == null) return;

            try
            {
                _logger.LogInfo($"Starting Add Password process for account: {account.AccountName}");

                // Show input dialog for password
                var result = Microsoft.VisualBasic.Interaction.InputBox(
                    $"Enter password for account '{account.DisplayName}':\n\n" +
                    "This will enable auto-fill of both username and password when switching to this account.\n\n" +
                    "The password will be encrypted and stored securely.",
                    "Add Password",
                    "",
                    -1, -1);

                if (string.IsNullOrWhiteSpace(result))
                {
                    _logger.LogInfo("Add password cancelled by user");
                    return;
                }

                _logger.LogInfo($"Password provided for account: {account.AccountName}");

                // Update the account with the password
                account.StoredPassword = result;
                _logger.LogInfo($"Set StoredPassword for {account.AccountName}: {!string.IsNullOrEmpty(account.StoredPassword)}");
                
                // Save accounts to persist the password
                _logger.LogInfo($"About to save accounts, {account.AccountName} has StoredPassword: {!string.IsNullOrEmpty(account.StoredPassword)}");
                await _steamService.SaveAccountsAsync(Accounts.ToList());
                _logger.LogSuccess($"Password added and saved for account: {account.AccountName}");

                // Small delay to ensure file write is complete
                await Task.Delay(100);

                // Reload accounts to get the updated encrypted password data and refresh UI
                _logger.LogInfo("Reloading accounts after password save...");
                await LoadAccountsAsync();
                
                _logger.LogInfo("UI refreshed after password addition");

                // Show success message in status bar only
                StatusMessage = $"Password added for '{account.DisplayName}' - auto-fill now enabled! 🔐";
                
                _logger.LogSuccess($"Add password completed successfully for: {account.AccountName}");
                
                // Automatically fetch Steam data for this account
                _ = Task.Run(async () => await AutoFetchSteamDataForAccountAsync(account));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Add password failed for {account.AccountName}: {ex.Message}");
                
                System.Windows.MessageBox.Show(
                    $"Failed to add password for '{account.DisplayName}'.\n\nError: {ex.Message}",
                    "Add Password Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                    
                StatusMessage = $"Failed to add password for '{account.DisplayName}'";
            }
        }

        // Removed: Large PowerShell auto-fill script generation methods

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Handle the case when games are loaded from cache but user is on another tab
        /// This ensures games are ready to display immediately when they navigate to Games tab
        /// </summary>
        private void PrepareGamesForDisplay()
        {
            try
            {
                if (AllGames.Count > 0 && FilteredGames.Count == 0)
                {
                    _logger.LogInfo($"PrepareGamesForDisplay: Preparing {AllGames.Count} games for immediate display");
                    
                    // Populate FilteredGames so games are ready when user navigates to Games tab
                    FilteredGames.Clear();
                    foreach (var game in AllGames)
                    {
                        FilteredGames.Add(game);
                    }
                    
                    _logger.LogSuccess($"PrepareGamesForDisplay: {FilteredGames.Count} games prepared for display");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in PrepareGamesForDisplay: {ex.Message}");
            }
        }

        /// <summary>
        /// Manually refresh the game cache (user-initiated)
        /// </summary>
        private async Task RefreshGameCacheAsync()
        {
            try
            {
                if (_steamWebApiService == null)
                {
                    StatusMessage = "Steam Web API key required to refresh games. Please configure it in the Games tab.";
                    _logger.LogWarning("Cannot refresh cache - Steam Web API service not initialized");
                    return;
                }

                if (_isAutoRefreshingCache)
                {
                    StatusMessage = "Cache refresh already in progress, please wait...";
                    _logger.LogInfo("Manual cache refresh requested but auto-refresh is already running");
                    return;
                }

                _logger.LogInfo("=== MANUAL CACHE REFRESH REQUESTED ===");
                IsLoadingGames = true;
                StatusMessage = "Refreshing games cache...";
                CacheStatus = "Refreshing cache...";

                // Fetch fresh data from Steam API
                _logger.LogInfo("Fetching fresh games data from Steam API...");
                var freshGames = await FetchAllGamesFromApiAsync();

                // Save fresh data to cache
                await _gameCacheService.SaveCacheAsync(freshGames);
                _logger.LogInfo("Fresh game data saved to cache");

                // Update UI with fresh data
                await DisplayGamesImmediatelyAsync(freshGames);

                // Set up F2P account options
                try
                {
                    PopulateF2PAccountOptions();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to populate F2P account options during manual refresh: {ex.Message}");
                }

                var accountsWithGames = freshGames.GroupBy(g => g.OwnerSteamId).Count();
                StatusMessage = $"Cache refreshed: {AllGames.Count} games from {accountsWithGames} accounts";
                CacheStatus = $"Fresh data: {DateTime.Now:MM/dd HH:mm} (expires in 7h)";

                _logger.LogSuccess($"=== MANUAL CACHE REFRESH COMPLETED: {freshGames.Count} games updated ===");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during manual cache refresh: {ex.Message}");
                StatusMessage = $"Error refreshing cache: {ex.Message}";
            }
            finally
            {
                IsLoadingGames = false;
            }
        }

        public void SetPendingGameToLaunch(int appId)
        {
            _pendingAppIdToLaunch = appId;
        }

        /// <summary>
        /// Clean up duplicate accounts by removing accounts with the same Steam ID
        /// </summary>
        private async Task CleanupDuplicateAccountsAsync()
        {
            try
            {
                _logger.LogInfo("=== CLEANING UP DUPLICATE ACCOUNTS ===");
                
                var duplicates = new List<SteamAccount>();
                var seenSteamIds = new HashSet<string>();
                var seenAccountNames = new Dictionary<string, SteamAccount>(StringComparer.OrdinalIgnoreCase);
                
                // First pass: Find duplicates by Steam ID
                foreach (var account in Accounts.ToList())
                {
                    if (string.IsNullOrEmpty(account.SteamId))
                        continue;
                        
                    if (seenSteamIds.Contains(account.SteamId))
                    {
                        _logger.LogWarning($"Found duplicate Steam ID: {account.AccountName} with Steam ID {account.SteamId}");
                        duplicates.Add(account);
                    }
                    else
                    {
                        seenSteamIds.Add(account.SteamId);
                    }
                }
                
                // Second pass: Find duplicates by account name (keep the most recent one)
                foreach (var account in Accounts.ToList())
                {
                    if (string.IsNullOrEmpty(account.AccountName))
                        continue;
                        
                    if (seenAccountNames.TryGetValue(account.AccountName, out var existingAccount))
                    {
                        // Keep the account with the most recent login, remove the older one
                        var accountToRemove = account.LastLogin < existingAccount.LastLogin ? account : existingAccount;
                        var accountToKeep = account.LastLogin >= existingAccount.LastLogin ? account : existingAccount;
                        
                        _logger.LogWarning($"Found duplicate account name: {accountToRemove.AccountName} (keeping {accountToKeep.LastLogin:yyyy-MM-dd HH:mm}, removing {accountToRemove.LastLogin:yyyy-MM-dd HH:mm})");
                        
                        duplicates.Add(accountToRemove);
                        
                        // Update the seen dictionary to keep the newer account
                        seenAccountNames[account.AccountName] = accountToKeep;
                    }
                    else
                    {
                        seenAccountNames[account.AccountName] = account;
                    }
                }
                
                if (duplicates.Any())
                {
                    _logger.LogInfo($"Found {duplicates.Count} duplicate accounts to remove");
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var duplicate in duplicates)
                        {
                            _logger.LogInfo($"Removing duplicate account: {duplicate.AccountName} (Steam ID: {duplicate.SteamId})");
                            Accounts.Remove(duplicate);
                        }
                        
                        // Trigger UI refresh
                        OnPropertyChanged(nameof(Accounts));
                        OnPropertyChanged(nameof(FilteredAccounts));
                    });
                    
                    // Save the cleaned accounts
                    await _steamService.SaveAccountsAsync(Accounts.ToList());
                    _logger.LogSuccess($"Removed {duplicates.Count} duplicate accounts and saved to file");
                }
                else
                {
                    _logger.LogInfo("No duplicate accounts found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error cleaning up duplicate accounts: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check if an account with the given Steam ID already exists
        /// </summary>
        private bool AccountWithSteamIdExists(string steamId)
        {
            if (string.IsNullOrEmpty(steamId))
                return false;
                
            return Accounts.Any(a => a.SteamId == steamId);
        }

        /// <summary>
        /// Allow user to manually select Steam directory when auto-discovery fails
        /// </summary>
        private async Task SelectSteamDirectoryAsync()
        {
            try
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                dialog.Description = "Select your Steam installation directory";
                dialog.ShowNewFolderButton = false;
                
                // Try to set initial directory to a common Steam location
                var commonPaths = new[]
                {
                    @"C:\Program Files (x86)\Steam",
                    @"C:\Program Files\Steam",
                    @"D:\Steam",
                    @"E:\Steam"
                };
                
                foreach (var path in commonPaths)
                {
                    if (Directory.Exists(path))
                    {
                        dialog.SelectedPath = path;
                        break;
                    }
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var selectedPath = dialog.SelectedPath;
                    
                    // Validate that this is actually a Steam directory
                    var steamExe = Path.Combine(selectedPath, "Steam.exe");
                    var loginUsersPath = Path.Combine(selectedPath, "config", "loginusers.vdf");
                    
                    if (!File.Exists(steamExe))
                    {
                        MessageBox.Show(
                            $"The selected directory does not appear to be a Steam installation.\n\nSteam.exe not found in: {selectedPath}",
                            "Invalid Steam Directory",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                    
                    if (!File.Exists(loginUsersPath))
                    {
                        MessageBox.Show(
                            $"Steam installation found, but no login data available.\n\nloginusers.vdf not found in: {Path.Combine(selectedPath, "config")}\n\nPlease ensure you have logged into Steam at least once with this installation.",
                            "No Login Data Found",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                    
                    // Set the custom Steam path
                    _steamService.SetCustomSteamPath(selectedPath);
                    _logger.LogSuccess($"Custom Steam path set to: {selectedPath}");
                    
                    // Now try to discover accounts with the custom path
                    StatusMessage = "Discovering accounts from custom Steam directory...";
                    await DiscoverAccountsAsync();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error selecting Steam directory: {ex.Message}";
                MessageBox.Show(
                    $"Error selecting Steam directory.\n\nError: {ex.Message}",
                    "Directory Selection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                _logger.LogError($"Error in SelectSteamDirectoryAsync: {ex.Message}");
            }
        }

        // End of MainViewModel class
    }

    // RelayCommand implementation for MVVM
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public void Execute(object? parameter)
        {
            _execute();
        }
    }

    // RelayCommand with parameter
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool>? _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke((T)parameter!) ?? true;
        }

        public void Execute(object? parameter)
        {
            _execute((T)parameter!);
        }
    }
}
