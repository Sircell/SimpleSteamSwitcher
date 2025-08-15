using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json;
using SimpleSteamSwitcher.Models;
using System.Runtime.InteropServices;
using System.Text;

namespace SimpleSteamSwitcher.Services
{
    public class SteamService
    {
        private string _steamPath;
        private string _loginUsersPath;
        private string _configPath;
        private readonly string _accountsDataPath;
        private readonly LogService _logger;
        private readonly PasswordService _passwordService;
        private readonly SimpleBanDetectionService _banDetectionService;
        
        // Performance optimization: Cache frequently accessed data
        private List<SteamAccount>? _cachedAccounts;
        private DateTime _lastAccountsCacheTime = DateTime.MinValue;
        private readonly TimeSpan _cacheValidityDuration = TimeSpan.FromSeconds(30);
        
        public SteamService()
        {
            _logger = new LogService();
            _passwordService = new PasswordService();
            _steamPath = GetSteamPath();
            _banDetectionService = new SimpleBanDetectionService();
            _loginUsersPath = Path.Combine(_steamPath, "config", "loginusers.vdf");
            _configPath = Path.Combine(_steamPath, "config", "config.vdf");
            _accountsDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                "SimpleSteamSwitcher", "accounts.json");
            
            // Ensure data directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(_accountsDataPath)!);
            
            _logger.LogInfo($"SteamService initialized - Steam Path: {_steamPath}", "INIT");
        }

        public void SetCustomSteamPath(string customPath)
        {
            if (Directory.Exists(customPath))
            {
                _steamPath = customPath;
                _loginUsersPath = Path.Combine(_steamPath, "config", "loginusers.vdf");
                _configPath = Path.Combine(_steamPath, "config", "config.vdf");
            }
        }

        public string GetCurrentSteamPath()
        {
            return _steamPath;
        }

        public string GetSteamPath()
        {
            // Try to find Steam installation path
            var possiblePaths = new[]
            {
                GetSteamPathFromRegistry(),  // Try registry first (most reliable)
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                @"D:\Steam",  // Common alternative drive
                @"E:\Steam",  // Common alternative drive
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
            };

            foreach (var path in possiblePaths)
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    // Verify Steam.exe exists in this directory
                    var steamExe = Path.Combine(path, "Steam.exe");
                    if (File.Exists(steamExe))
                    {
                        System.Diagnostics.Debug.WriteLine($"Found Steam installation at: {path}");
                        return path;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Steam directory found but Steam.exe missing at: {path}");
                    }
                }
            }

            throw new DirectoryNotFoundException(
                "Steam installation not found. Please ensure Steam is installed.\n\n" +
                "Searched locations:\n" +
                string.Join("\n", possiblePaths.Where(p => !string.IsNullOrEmpty(p))));
        }

        private string GetSteamPathFromRegistry()
        {
            try
            {
                // Try multiple registry locations
                var registryPaths = new[]
                {
                    @"SOFTWARE\WOW6432Node\Valve\Steam",  // 64-bit Windows, 32-bit Steam
                    @"SOFTWARE\Valve\Steam",              // 32-bit Windows or 64-bit Steam
                    @"SOFTWARE\Wow6432Node\Valve\Steam"   // Alternative casing
                };

                foreach (var regPath in registryPaths)
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(regPath);
                        var installPath = key?.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"Found Steam path in registry: {installPath}");
                            return installPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error reading registry path {regPath}: {ex.Message}");
                    }
                }

                // Also try current user registry
                try
                {
                    using var userKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                    var userInstallPath = userKey?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(userInstallPath) && Directory.Exists(userInstallPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"Found Steam path in user registry: {userInstallPath}");
                        return userInstallPath;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading user registry: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("Steam path not found in registry");
                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting Steam path from registry: {ex.Message}");
                return "";
            }
        }

        public async Task<List<SteamAccount>> LoadSavedAccountsAsync()
        {
            // Performance optimization: Use cache if valid
            if (_cachedAccounts != null && 
                DateTime.Now - _lastAccountsCacheTime < _cacheValidityDuration)
            {
                _logger.LogInfo($"Using cached accounts data ({_cachedAccounts.Count} accounts)");
                return _cachedAccounts.Select(a => a.Clone()).ToList(); // Return clones to prevent modification
            }
            
            _logger.LogInfo($"=== LOAD SAVED ACCOUNTS STARTED ===");
            _logger.LogInfo($"Accounts file path: {_accountsDataPath}");
            
            if (!File.Exists(_accountsDataPath))
            {
                _logger.LogInfo("Accounts file does not exist, returning empty list");
                var emptyList = new List<SteamAccount>();
                _cachedAccounts = emptyList;
                _lastAccountsCacheTime = DateTime.Now;
                return emptyList;
            }

            try
            {
                _logger.LogInfo("Reading accounts file...");
                var json = await File.ReadAllTextAsync(_accountsDataPath);
                _logger.LogInfo($"File read successfully. Size: {json.Length} characters");
                
                _logger.LogInfo("Deserializing JSON to accounts...");
                var accounts = JsonConvert.DeserializeObject<List<SteamAccount>>(json) ?? new List<SteamAccount>();
                _logger.LogInfo($"Deserialized {accounts.Count} accounts from file");
                
                // Decrypt passwords for accounts that have them stored
                _logger.LogInfo("Processing accounts for password decryption...");
                int decryptedCount = 0;
                foreach (var account in accounts)
                {
                    if (!string.IsNullOrEmpty(account.EncryptedPassword))
                    {
                        _logger.LogInfo($"Decrypting password for account: {account.AccountName}");
                        try
                        {
                            account.StoredPassword = _passwordService.DecryptPassword(account.EncryptedPassword);
                            _logger.LogSuccess($"Password decrypted successfully for: {account.AccountName}");
                            decryptedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to decrypt password for {account.AccountName}: {ex.Message}");
                            account.StoredPassword = null; // Clear invalid password
                        }
                    }
                    else
                    {
                        _logger.LogInfo($"Account {account.AccountName} has no encrypted password");
                    }
                    
                    // Deserialize ban status if available
                    if (!string.IsNullOrEmpty(account.BanStatusJson))
                    {
                        try
                        {
                            account.BanInfo = JsonConvert.DeserializeObject<BanInfo>(account.BanStatusJson);
                            _logger.LogInfo($"Ban status loaded for {account.AccountName}: {account.BanInfo.Status}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error deserializing ban status for {account.AccountName}: {ex.Message}");
                            account.BanInfo = new BanInfo { Status = BanStatus.Unknown };
                        }
                    }
                }
                
                _logger.LogInfo($"Password decryption completed. {decryptedCount} passwords decrypted successfully");
                
                // Log summary of loaded accounts
                _logger.LogInfo("=== LOAD SUMMARY ===");
                foreach (var account in accounts)
                {
                    _logger.LogInfo($"  - {account.AccountName}: HasStoredPassword={account.HasStoredPassword}, SteamId={account.SteamId}");
                }
                
                _logger.LogSuccess($"=== LOAD SAVED ACCOUNTS COMPLETED: {accounts.Count} accounts loaded ===");
                
                // Cache the loaded accounts for performance
                _cachedAccounts = accounts.Select(a => a.Clone()).ToList();
                _lastAccountsCacheTime = DateTime.Now;
                
                return accounts;
            }
            catch (Exception ex)
            {
                _logger.LogError($"=== LOAD SAVED ACCOUNTS FAILED ===");
                _logger.LogError($"Exception: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                _logger.LogWarning("Returning empty account list due to error");
                
                // Cache empty list to prevent repeated failed attempts
                var emptyList = new List<SteamAccount>();
                _cachedAccounts = emptyList;
                _lastAccountsCacheTime = DateTime.Now;
                
                return emptyList;
            }
        }

        public async Task LoadBanStatusForAccountsAsync(List<SteamAccount> accounts)
        {
            try
            {
                _logger.LogInfo($"Loading ban status for {accounts.Count} accounts");
                
                // Only check accounts that don't have recent ban status data
                var accountsToCheck = accounts.Where(a => 
                    !string.IsNullOrEmpty(a.SteamId) && 
                    (a.BanStatusLastChecked == null || 
                     DateTime.Now - a.BanStatusLastChecked > TimeSpan.FromHours(6)) // Refresh every 6 hours
                ).ToList();

                if (!accountsToCheck.Any())
                {
                    _logger.LogInfo("All accounts have recent ban status data, skipping check");
                    return;
                }

                _logger.LogInfo($"Checking ban status for {accountsToCheck.Count} accounts");

                // Use profile scraping method for reliable detection
                var steamIds = accountsToCheck.Select(a => a.SteamId).ToList();
                var banResults = await _banDetectionService.GetBanStatusForMultipleAsync(steamIds);
                
                foreach (var account in accountsToCheck)
                {
                    try
                    {
                        if (banResults.TryGetValue(account.SteamId, out var banInfo))
                        {
                            account.BanInfo = banInfo;
                            account.BanStatusLastChecked = DateTime.Now;
                            
                            // Serialize ban info for persistence
                            account.BanStatusJson = JsonConvert.SerializeObject(banInfo);
                            
                            _logger.LogInfo($"Ban status for {account.DisplayName}: {banInfo.Status}");
                        }
                        else
                        {
                            _logger.LogWarning($"No ban data received for {account.DisplayName}");
                            account.BanInfo = new BanInfo { Status = BanStatus.Clean };
                            account.BanStatusLastChecked = DateTime.Now;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error processing ban status for {account.DisplayName}: {ex.Message}");
                        account.BanInfo = new BanInfo { Status = BanStatus.Clean };
                        account.BanStatusLastChecked = DateTime.Now;
                    }
                }

                _logger.LogSuccess($"Ban status check completed for {accountsToCheck.Count} accounts");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in LoadBanStatusForAccountsAsync: {ex.Message}");
            }
        }

        public async Task SaveAccountsAsync(List<SteamAccount> accounts)
        {
            _logger.LogInfo($"=== SAVE ACCOUNTS TO FILE STARTED ===");
            _logger.LogInfo($"Input accounts count: {accounts.Count}");
            _logger.LogInfo($"Target file path: {_accountsDataPath}");
            
            try
            {
                // Encrypt passwords before saving
                _logger.LogInfo("Processing accounts for encryption...");
                var accountsToSave = accounts.Select(account => 
                {
                    var accountCopy = account.Clone();
                    _logger.LogInfo($"Processing account {account.AccountName}: StoredPassword={!string.IsNullOrEmpty(account.StoredPassword)}, EncryptedPassword={!string.IsNullOrEmpty(account.EncryptedPassword)}");
                    
                    if (!string.IsNullOrEmpty(account.StoredPassword))
                    {
                        _logger.LogInfo($"Encrypting password for account: {account.AccountName}");
                        try
                        {
                            accountCopy.EncryptedPassword = _passwordService.EncryptPassword(account.StoredPassword);
                            _logger.LogSuccess($"Password encrypted successfully for: {account.AccountName} (Length: {accountCopy.EncryptedPassword?.Length ?? 0})");
                            
                            // Clear the stored password from the copy (security)
                            accountCopy.StoredPassword = null;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to encrypt password for {account.AccountName}: {ex.Message}");
                            throw;
                        }
                    }
                    else if (!string.IsNullOrEmpty(account.EncryptedPassword))
                    {
                        _logger.LogInfo($"Account {account.AccountName} already has encrypted password, preserving it");
                        // Keep existing encrypted password
                        accountCopy.EncryptedPassword = account.EncryptedPassword;
                    }
                    else
                    {
                        _logger.LogInfo($"Account {account.AccountName} has no password data");
                    }
                    
                    return accountCopy;
                }).ToList();
                
                _logger.LogInfo($"Account processing completed. {accountsToSave.Count} accounts ready for saving");
                
                // Serialize to JSON
                _logger.LogInfo("Serializing accounts to JSON...");
                var json = JsonConvert.SerializeObject(accountsToSave, Formatting.Indented);
                _logger.LogInfo($"JSON serialization completed. Size: {json.Length} characters");
                
                // Write to file
                _logger.LogInfo($"Writing accounts to file: {_accountsDataPath}");
                await File.WriteAllTextAsync(_accountsDataPath, json);
                _logger.LogSuccess($"Accounts saved successfully to file");
                
                // Invalidate cache since accounts have been updated
                _cachedAccounts = null;
                _lastAccountsCacheTime = DateTime.MinValue;
                _logger.LogInfo("Accounts cache invalidated due to save operation");
                
                // Log summary of what was saved
                _logger.LogInfo("=== SAVE SUMMARY ===");
                foreach (var account in accountsToSave)
                {
                    _logger.LogInfo($"  - {account.AccountName}: HasEncryptedPassword={!string.IsNullOrEmpty(account.EncryptedPassword)}");
                }
                
                _logger.LogSuccess($"=== SAVE ACCOUNTS TO FILE COMPLETED ===");
            }
            catch (Exception ex)
            {
                _logger.LogError($"=== SAVE ACCOUNTS TO FILE FAILED ===");
                _logger.LogError($"Exception: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                throw; // Re-throw to let caller handle
            }
        }

        public async Task<SteamAccount?> GetCurrentAccountAsync()
        {
            try
            {
                // Use the same improved detection logic as GetCurrentlyOnlineAccountAsync
                var currentAccountName = await GetCurrentlyOnlineAccountAsync();
                
                if (string.IsNullOrEmpty(currentAccountName))
                {
                    System.Diagnostics.Debug.WriteLine("No current account name detected");
                    return null;
                }

                var accounts = await LoadSavedAccountsAsync();
                var currentAccount = accounts.FirstOrDefault(a => 
                    a.AccountName.Equals(currentAccountName, StringComparison.OrdinalIgnoreCase));
                
                if (currentAccount != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Found current account: {currentAccount.DisplayName} ({currentAccount.AccountName})");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Current account '{currentAccountName}' not found in saved accounts");
                }
                
                return currentAccount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting current account: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> VerifyAccountExistsAsync(string steamId)
        {
            try
            {
                if (!File.Exists(_loginUsersPath))
                {
                    System.Diagnostics.Debug.WriteLine($"loginusers.vdf file not found at: {_loginUsersPath}");
                    return false;
                }

                var content = await File.ReadAllTextAsync(_loginUsersPath);
                System.Diagnostics.Debug.WriteLine($"Checking if Steam ID {steamId} exists in loginusers.vdf");
                
                // Look for the Steam ID in the file
                var steamIdPattern = $@"""{steamId}""";
                var match = Regex.Match(content, steamIdPattern);
                
                var exists = match.Success;
                System.Diagnostics.Debug.WriteLine($"Steam ID {steamId} exists in loginusers.vdf: {exists}");
                
                return exists;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error verifying account exists: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> VerifyAccountExistsByNameAsync(string accountName)
        {
            try
            {
                if (!File.Exists(_loginUsersPath))
                {
                    System.Diagnostics.Debug.WriteLine($"loginusers.vdf file not found at: {_loginUsersPath}");
                    return false;
                }

                var content = await File.ReadAllTextAsync(_loginUsersPath);
                System.Diagnostics.Debug.WriteLine($"Checking if account name '{accountName}' exists in loginusers.vdf");
                
                // Look for the account name in the file
                var accountNamePattern = $@"""AccountName""\s+""{Regex.Escape(accountName)}""";
                var match = Regex.Match(content, accountNamePattern, RegexOptions.IgnoreCase);
                
                var exists = match.Success;
                System.Diagnostics.Debug.WriteLine($"Account name '{accountName}' exists in loginusers.vdf: {exists}");
                
                return exists;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error verifying account exists by name: {ex.Message}");
                return false;
            }
        }

        public event Action<string, string, bool>? SwitchMethodUpdate;

        private void NotifySwitchMethodUpdate(string method, string details, bool success)
        {
            SwitchMethodUpdate?.Invoke(method, details, success);
        }

        public async Task<bool> SwitchToAccountAsync(SteamAccount account)
        {
            try
            {
                _logger.LogSwitchOperation(account.DisplayName, "STARTED", true, $"Steam ID: {account.SteamId}, Account Name: {account.AccountName}");
                System.Diagnostics.Debug.WriteLine($"Attempting to switch to account: {account.DisplayName} ({account.SteamId})");
                
                // Close Steam if it's running
                _logger.LogSwitchOperation(account.DisplayName, "CLOSING_STEAM", true, "Closing Steam if running");
                await CloseSteamAsync();
                
                // Wait a moment for Steam to fully close
                _logger.LogSwitchOperation(account.DisplayName, "WAITING", true, "Waiting 2 seconds for Steam to fully close");
                await Task.Delay(2000);

                // Method 1: Try using Steam's -login parameter
                _logger.LogSwitchOperation(account.DisplayName, "METHOD_1_STEAM_ARGS", true, $"Trying Steam -login parameter with account: {account.AccountName}");
                NotifySwitchMethodUpdate("Steam Login Args", $"Trying -login {account.AccountName}...", true);
                var loginSuccess = await TryLoginWithSteamArgsAsync(account.AccountName);
                if (loginSuccess)
                {
                    _logger.LogSwitchOperation(account.DisplayName, "METHOD_1_SUCCESS", true, "Steam args method succeeded - account verified as logged in");
                    _logger.LogSuccess($"Account switch completed using Steam args method for {account.DisplayName}", "SWITCH_COMPLETE");
                    System.Diagnostics.Debug.WriteLine($"Successfully started Steam with login args for {account.AccountName}");
                    NotifySwitchMethodUpdate("Steam Login Args", $"✅ Successfully logged in {account.DisplayName}", true);
                    return true;
                }
                _logger.LogSwitchOperation(account.DisplayName, "METHOD_1_FAILED", false, "Steam args method failed - Steam started but account not logged in (login screen shown)");
                NotifySwitchMethodUpdate("Steam Login Args", $"❌ Failed - Steam login screen shown. Trying backup method...", false);

                // Method 2: Try using registry auto-login
                _logger.LogSwitchOperation(account.DisplayName, "METHOD_2_REGISTRY", true, $"Trying registry auto-login with account: {account.AccountName}");
                NotifySwitchMethodUpdate("Registry Auto-Login", $"Configuring auto-login for {account.AccountName}...", true);
                var registrySuccess = await TryRegistryAutoLoginAsync(account.AccountName);
                if (registrySuccess)
                {
                    _logger.LogSwitchOperation(account.DisplayName, "METHOD_2_SUCCESS", true, "Registry method succeeded, starting Steam");
                    System.Diagnostics.Debug.WriteLine($"Successfully set registry auto-login for {account.AccountName}");
                    await StartSteamAsync();
                    
                    // Verify the registry method actually logged in the account
                    await Task.Delay(3000); // Give Steam time to start with registry settings
                    var registryVerified = await VerifyAccountLoggedInAsync(account.AccountName);
                    if (registryVerified)
                    {
                        _logger.LogSuccess($"Account switch completed using registry method for {account.DisplayName}", "SWITCH_COMPLETE");
                        NotifySwitchMethodUpdate("Registry Auto-Login", $"✅ Successfully logged in {account.DisplayName}", true);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning($"Registry method started Steam but {account.AccountName} not verified as logged in");
                        NotifySwitchMethodUpdate("Registry Auto-Login", $"❌ Failed - Registry method didn't work. Trying final method...", false);
                    }
                }
                else
                {
                    _logger.LogSwitchOperation(account.DisplayName, "METHOD_2_FAILED", false, "Registry method failed");
                    NotifySwitchMethodUpdate("Registry Auto-Login", $"❌ Failed - Registry config error. Trying final method...", false);
                }

                // Method 3: Try the VDF file method as fallback
                _logger.LogSwitchOperation(account.DisplayName, "METHOD_3_VDF", true, $"Trying VDF file method with account: {account.AccountName}");
                NotifySwitchMethodUpdate("VDF File Method", $"Modifying Steam config files for {account.AccountName}...", true);
                var vdfSuccess = await TryVdfFileMethodAsync(account.SteamId);
                if (vdfSuccess)
                {
                    _logger.LogSwitchOperation(account.DisplayName, "METHOD_3_SUCCESS", true, "VDF method succeeded, starting Steam");
                    System.Diagnostics.Debug.WriteLine($"Successfully updated VDF for {account.AccountName}");
                    await StartSteamAsync();
                    _logger.LogSuccess($"Account switch completed using VDF method for {account.DisplayName}", "SWITCH_COMPLETE");
                    NotifySwitchMethodUpdate("VDF File Method", $"✅ Successfully configured {account.DisplayName}", true);
                    return true;
                }

                _logger.LogSwitchOperation(account.DisplayName, "ALL_METHODS_FAILED", false, "All switch methods failed");
                NotifySwitchMethodUpdate("All Methods", $"❌ All switching methods failed for {account.DisplayName}", false);
                System.Diagnostics.Debug.WriteLine($"All switch methods failed for {account.AccountName}");
                
                // Log summary of why each method failed
                _logger.LogError($"SWITCH FAILURE SUMMARY for {account.DisplayName}:");
                _logger.LogError($"- Method 1 (Steam Args): Failed - Steam showed login screen instead of logging in");
                _logger.LogError($"- Method 2 (Registry): Failed - Steam didn't start properly or showed login screen");
                _logger.LogError($"- Method 3 (VDF Files): Failed - Account not found in Steam's loginusers.vdf (never logged in on this machine)");
                _logger.LogError($"RECOMMENDATION: This account needs to be logged into Steam manually at least once before automatic switching will work");
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogSwitchOperation(account.DisplayName, "ERROR", false, $"Exception: {ex.Message}");
                _logger.LogError($"Error switching to account {account.DisplayName}", "SWITCH_ERROR", ex);
                System.Diagnostics.Debug.WriteLine($"Error switching to account: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TryLoginWithSteamArgsAsync(string accountName)
        {
            try
            {
                var steamExe = Path.Combine(_steamPath, "Steam.exe");
                if (!File.Exists(steamExe))
                    return false;

                _logger.LogInfo($"Starting Steam with -login {accountName}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = steamExe,
                    Arguments = $"-login {accountName}",
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                if (process != null)
                {
                    _logger.LogInfo($"Steam process started, waiting for login verification...");
                    
                    // Wait longer and actually verify the login
                    await Task.Delay(5000); // Give Steam more time to start and process login
                    
                    // Check if the account is actually logged in
                    var isLoggedIn = await VerifyAccountLoggedInAsync(accountName);
                    
                    if (isLoggedIn)
                    {
                        _logger.LogSuccess($"Account {accountName} successfully logged in and verified");
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning($"Steam started but account {accountName} is not logged in - login page shown instead");
                        return false;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error with Steam login args: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error with Steam login args: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> VerifyAccountLoggedInAsync(string expectedAccountName)
        {
            try
            {
                _logger.LogInfo($"Verifying if {expectedAccountName} is actually logged in...");
                
                // Wait a moment for Steam to settle
                await Task.Delay(2000);
                
                // CRITICAL: First check if Steam is showing login screen - this overrides everything
                _logger.LogInfo($"Step 1: Checking if Steam is showing login screen...");
                var isAtLoginScreen = await IsAtSteamLoginScreenAsync();
                if (isAtLoginScreen)
                {
                    _logger.LogWarning($"Steam is showing login screen - account {expectedAccountName} not logged in");
                    return false;
                }
                _logger.LogInfo($"Step 1 complete: No login screen detected");
                
                // Method 1: Check currently online account (most reliable)
                _logger.LogInfo($"Step 2: Checking currently online account...");
                var currentOnlineAccount = await GetCurrentlyOnlineAccountAsync();
                _logger.LogInfo($"Currently online account detected: '{currentOnlineAccount ?? "NONE"}'");
                
                if (!string.IsNullOrEmpty(currentOnlineAccount) && 
                    currentOnlineAccount.Equals(expectedAccountName, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogSuccess($"Verification successful: {expectedAccountName} is logged in");
                    return true;
                }
                _logger.LogInfo($"Step 2 complete: Expected account '{expectedAccountName}' not found as currently online");
                
                // Method 2: Check if Steam is running normally (not at login screen)
                _logger.LogInfo($"Step 3: Checking Steam process and window state...");
                var steamProcesses = Process.GetProcessesByName("steam");
                _logger.LogInfo($"Steam processes found: {steamProcesses.Length}");
                
                if (steamProcesses.Length > 0)
                {
                    _logger.LogInfo($"Steam is running normally, checking additional indicators...");
                    
                    // Look for Steam main window to confirm it's not stuck at login
                    var mainWindowFound = FindSteamMainWindow() != IntPtr.Zero;
                    _logger.LogInfo($"Steam main window found: {mainWindowFound}");
                    
                    if (mainWindowFound)
                    {
                        _logger.LogInfo($"Steam main window detected - user appears to be logged in");
                        
                        // Method 3: Check registry as additional confirmation (only if not at login screen)
                        try
                        {
                            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                            var autoLoginUser = key?.GetValue("AutoLoginUser") as string;
                            _logger.LogInfo($"Registry AutoLoginUser: '{autoLoginUser ?? "NONE"}'");
                            
                            if (!string.IsNullOrEmpty(autoLoginUser) && 
                                autoLoginUser.Equals(expectedAccountName, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogSuccess($"Verification successful: {expectedAccountName} appears to be logged in via combined checks");
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Registry check failed: {ex.Message}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Steam is running but no main window found - possible login screen or startup");
                    }
                }
                else
                {
                    _logger.LogWarning($"Steam is not running - verification failed");
                }
                
                _logger.LogWarning($"Verification failed: {expectedAccountName} does not appear to be logged in");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error verifying login for {expectedAccountName}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> IsAtSteamLoginScreenAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    Console.WriteLine("=== LIVE STEAM DETECTION DEBUG ===");
                    _logger.LogInfo("=== SIMPLIFIED STEAM LOGIN DETECTION ===");
                    
                    // First check if any Steam processes are running
                    var steamProcesses = Process.GetProcessesByName("steam");
                    Console.WriteLine($"[DEBUG] Found {steamProcesses.Length} Steam process(es)");
                    _logger.LogInfo($"Found {steamProcesses.Length} Steam process(es) running");
                    
                    if (steamProcesses.Length == 0)
                    {
                        Console.WriteLine("[DEBUG] No Steam processes - returning FALSE");
                        _logger.LogInfo("No Steam processes found - Steam is not running");
                        return false;
                    }
                    
                    // NEW APPROACH: Use window enumeration to find Steam login windows
                    Console.WriteLine("[DEBUG] Using window enumeration to find Steam login windows...");
                    var steamWindow = FindSteamLoginWindow();
                    bool loginDetected = steamWindow != IntPtr.Zero;
                    
                    if (loginDetected)
                    {
                        Console.WriteLine($"[DEBUG] ✅ LOGIN DETECTED via window enumeration! Handle={steamWindow}");
                        _logger.LogSuccess($"✅ Steam login window DETECTED via enumeration: Handle={steamWindow}");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("[DEBUG] ❌ No login windows found via enumeration");
                        _logger.LogWarning("❌ No visible Steam login windows found via enumeration");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG] DETECTION ERROR: {ex.Message}");
                    _logger.LogError($"Error in Steam login screen detection: {ex.Message}");
                    return false;
                }
            });
        }

        private IntPtr FindSteamMainWindow()
        {
            var steamMainWindow = IntPtr.Zero;
            
            EnumWindows((hWnd, lParam) =>
            {
                var title = GetWindowText(hWnd);
                
                // Look for Steam main window patterns
                if (title.Equals("Steam", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Steam") && 
                    (title.Contains("Friends") || title.Contains("Library") || title.Contains("Store")))
                {
                    if (IsWindowVisible(hWnd))
                    {
                        steamMainWindow = hWnd;
                        return false; // Stop enumeration
                    }
                }
                
                return true; // Continue enumeration
            }, IntPtr.Zero);
            
            return steamMainWindow;
        }

        private async Task<bool> TryRegistryAutoLoginAsync(string accountName)
        {
            try
            {
                // Set registry values for auto-login
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam");
                if (key != null)
                {
                    key.SetValue("AutoLoginUser", accountName);
                    key.SetValue("RememberPassword", 1);
                    key.SetValue("LoginUser", accountName);
                    
                    // Also try the WOW6432Node path
                    using var wowKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                    if (wowKey != null)
                    {
                        wowKey.SetValue("AutoLoginUser", accountName);
                        wowKey.SetValue("RememberPassword", 1);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Set registry auto-login for {accountName}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting registry auto-login: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TryVdfFileMethodAsync(string steamId)
        {
            try
            {
                _logger.LogInfo($"VDF Method: Attempting to switch using Steam ID {steamId}");
                
                if (string.IsNullOrEmpty(steamId))
                {
                    _logger.LogWarning($"VDF Method: Cannot proceed - Steam ID is empty or null");
                    return false;
                }
                
                // Update loginusers.vdf to set the target account as most recent
                _logger.LogInfo($"VDF Method: Updating loginusers.vdf for Steam ID {steamId}");
                var success = await UpdateLoginUsersAsync(steamId);
                if (!success)
                {
                    _logger.LogWarning($"VDF Method: Failed to update loginusers.vdf for Steam ID {steamId} - account likely never logged in on this machine");
                    System.Diagnostics.Debug.WriteLine($"Failed to update loginusers.vdf for account {steamId}");
                    return false;
                }

                _logger.LogInfo($"VDF Method: Successfully updated loginusers.vdf, now updating config.vdf");
                // Update config.vdf for persona state
                await UpdateConfigAsync(steamId, "1"); // Default to online
                
                _logger.LogSuccess($"VDF Method: Successfully completed for Steam ID {steamId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"VDF Method: Error with VDF file method for Steam ID {steamId}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error with VDF file method: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> LoginAndSwitchAsync(SteamAccount account, bool rememberPassword = true)
        {
            try
            {
                // Close Steam if it's running
                await CloseSteamAsync();

                // Set up registry for auto-login (like TcNo does)
                await SetAutoLoginRegistryAsync(account.AccountName, rememberPassword);

                // Update config.vdf for persona state
                await UpdateConfigAsync("", "1"); // Default to online

                // Start Steam normally - it will use the registry settings for auto-login
                await StartSteamAsync();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during login and switch: {ex.Message}");
                return false;
            }
        }

        public async Task CloseSteamAndWaitAsync()
        {
            await CloseSteamAsync();
            
            // Reduced wait time since we're force-killing processes for speed  
            var maxWait = TimeSpan.FromSeconds(3);
            var startTime = DateTime.Now;
            
            while (DateTime.Now - startTime < maxWait && IsSteamRunning())
            {
                await Task.Delay(50); // Check every 50ms for faster response
            }
            
            // Minimal buffer for cleanup since processes are force-killed
            await Task.Delay(100);
        }

        public async Task StartSteamNormallyAsync()
        {
            await StartSteamAsync();
        }

        public async Task ClearAutoLoginSettingsAsync()
        {
            try
            {
                _logger.LogInfo("Clearing auto-login settings to force login screen", "CLEAR_AUTOLOGIN");
                
                // Clear registry auto-login settings
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam");
                    if (key != null)
                    {
                        // Clear auto-login user
                        key.DeleteValue("AutoLoginUser", false);
                        key.DeleteValue("RememberPassword", false);
                        key.DeleteValue("LoginUser", false);
                        
                        // Set to not remember password
                        key.SetValue("RememberPassword", 0);
                        _logger.LogInfo("Cleared CurrentUser registry auto-login settings", "CLEAR_AUTOLOGIN");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not clear CurrentUser registry: {ex.Message}", "CLEAR_AUTOLOGIN");
                }

                // Also try LocalMachine registry
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                    if (key != null)
                    {
                        key.DeleteValue("AutoLoginUser", false);
                        key.DeleteValue("RememberPassword", false);
                        _logger.LogInfo("Cleared LocalMachine registry auto-login settings", "CLEAR_AUTOLOGIN");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not clear LocalMachine registry: {ex.Message}", "CLEAR_AUTOLOGIN");
                }

                // Also clear MostRecent flag from loginusers.vdf
                await ClearMostRecentLoginAsync();

                _logger.LogSuccess("Auto-login settings cleared successfully", "CLEAR_AUTOLOGIN");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error clearing auto-login settings", "CLEAR_AUTOLOGIN", ex);
            }
        }

        private async Task ClearMostRecentLoginAsync()
        {
            try
            {
                if (!File.Exists(_loginUsersPath))
                {
                    _logger.LogWarning("loginusers.vdf file not found, cannot clear MostRecent", "CLEAR_MOSTRECENT");
                    return;
                }

                var content = await File.ReadAllTextAsync(_loginUsersPath);
                _logger.LogInfo("Clearing MostRecent flags from loginusers.vdf", "CLEAR_MOSTRECENT");

                // Remove all MostRecent "1" entries and set them to "0"
                var updatedContent = Regex.Replace(content, 
                    @"""MostRecent""\s*""1""", 
                    @"""MostRecent""		""0""", 
                    RegexOptions.IgnoreCase);

                if (updatedContent != content)
                {
                    await File.WriteAllTextAsync(_loginUsersPath, updatedContent);
                    _logger.LogSuccess("Cleared MostRecent flags from loginusers.vdf", "CLEAR_MOSTRECENT");
                }
                else
                {
                    _logger.LogInfo("No MostRecent flags found to clear", "CLEAR_MOSTRECENT");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error clearing MostRecent from loginusers.vdf", "CLEAR_MOSTRECENT", ex);
            }
        }

        public async Task RestoreAutoLoginSettingsAsync(string accountName)
        {
            try
            {
                _logger.LogInfo($"Restoring auto-login settings for account: {accountName}", "RESTORE_AUTOLOGIN");
                
                // Restore registry auto-login settings
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam");
                    if (key != null)
                    {
                        // Set auto-login user
                        key.SetValue("AutoLoginUser", accountName);
                        key.SetValue("RememberPassword", 1);
                        
                        _logger.LogInfo($"Set CurrentUser registry auto-login for: {accountName}", "RESTORE_AUTOLOGIN");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not set CurrentUser registry: {ex.Message}", "RESTORE_AUTOLOGIN");
                }

                // Also try LocalMachine registry
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                    if (key != null)
                    {
                        key.SetValue("AutoLoginUser", accountName);
                        key.SetValue("RememberPassword", 1);
                        _logger.LogInfo($"Set LocalMachine registry auto-login for: {accountName}", "RESTORE_AUTOLOGIN");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not set LocalMachine registry: {ex.Message}", "RESTORE_AUTOLOGIN");
                }

                _logger.LogSuccess($"Auto-login settings restored successfully for: {accountName}", "RESTORE_AUTOLOGIN");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error restoring auto-login settings for {accountName}", "RESTORE_AUTOLOGIN", ex);
                // Don't throw - this is not critical, just log the error
            }
        }

        public async Task StartSteamWithLoginScreenAsync()
        {
            try
            {
                var steamDirectoryPath = GetSteamPath();
                var steamExecutablePath = Path.Combine(steamDirectoryPath, "Steam.exe");
                
                if (!File.Exists(steamExecutablePath))
                {
                    throw new FileNotFoundException($"Steam executable not found at: {steamExecutablePath}");
                }

                _logger.LogInfo($"Starting Steam with login screen from: {steamExecutablePath}", "START_STEAM_LOGIN");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = steamExecutablePath,
                    Arguments = "-login", // Force login screen
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(startInfo);
                _logger.LogSuccess("Steam started with login screen", "START_STEAM_LOGIN");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error starting Steam with login screen", "START_STEAM_LOGIN", ex);
                throw;
            }
        }

        public async Task CloseSteamAsync()
        {
            try
            {
                if (IsSteamRunning())
                {
                    System.Diagnostics.Debug.WriteLine("Force-killing Steam for fast account switching...");
                    _logger.LogInfo("Force-killing all Steam processes for fast account switching");
                    
                    // Get all Steam-related processes for immediate termination
                    var steamProcessNames = new[] { "steam", "steamservice", "steamwebhelper", "gameoverlayui" };
                    int killedProcesses = 0;
                    
                    foreach (var processName in steamProcessNames)
                    {
                        foreach (var process in Process.GetProcessesByName(processName))
                        {
                            try
                            {
                                process.Kill(); // Immediate force kill for speed
                                process.WaitForExit(1000); // Wait max 1 second for cleanup
                                killedProcesses++;
                                System.Diagnostics.Debug.WriteLine($"Force-killed {processName} process (PID: {process.Id})");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error killing {processName} process: {ex.Message}");
                            }
                        }
                    }
                    
                    _logger.LogInfo($"Force-killed {killedProcesses} Steam processes");
                    
                    // Minimal wait for process cleanup
                    await Task.Delay(500);
                    System.Diagnostics.Debug.WriteLine("Steam force-kill completed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error force-killing Steam: {ex.Message}");
                _logger.LogError($"Error force-killing Steam: {ex.Message}");
                throw;
            }
        }

        public async Task StartSteamAsync()
        {
            try
            {
                var steamDirectoryPath = GetSteamPath();
                var steamExecutablePath = Path.Combine(steamDirectoryPath, "Steam.exe");
                
                if (!File.Exists(steamExecutablePath))
                {
                    throw new FileNotFoundException($"Steam executable not found at: {steamExecutablePath}");
                }

                System.Diagnostics.Debug.WriteLine($"Starting Steam from: {steamExecutablePath}");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = steamExecutablePath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(startInfo);
                System.Diagnostics.Debug.WriteLine("Steam started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting Steam: {ex.Message}");
                throw;
            }
        }

        private async Task<bool> UpdateLoginUsersAsync(string steamId)
        {
            if (!File.Exists(_loginUsersPath))
            {
                System.Diagnostics.Debug.WriteLine($"loginusers.vdf file not found at: {_loginUsersPath}");
                return false;
            }

            try
            {
                var content = await File.ReadAllTextAsync(_loginUsersPath);
                System.Diagnostics.Debug.WriteLine($"Original loginusers.vdf content length: {content.Length}");
                
                // First, reset all MostRec to 0
                content = Regex.Replace(content, @"""MostRec""\s+""1""", @"""MostRec"" ""0""");
                
                // Find the specific account block and update its MostRec to 1
                // Use a more flexible pattern to find the account block
                var steamIdPattern = $@"""{steamId}""";
                var steamIdMatch = Regex.Match(content, steamIdPattern);
                
                if (steamIdMatch.Success)
                {
                    var startIndex = steamIdMatch.Index;
                    var endIndex = FindClosingBrace(content, startIndex);
                    
                    if (endIndex > startIndex)
                    {
                        var accountBlock = content.Substring(startIndex, endIndex - startIndex + 1);
                        System.Diagnostics.Debug.WriteLine($"Found account block: {accountBlock}");
                        
                        // Update MostRec to 1 in this specific block
                        var updatedBlock = Regex.Replace(accountBlock, @"""MostRec""\s+""0""", @"""MostRec"" ""1""");
                        
                        // Replace the original block with the updated one
                        content = content.Remove(startIndex, endIndex - startIndex + 1);
                        content = content.Insert(startIndex, updatedBlock);
                        
                        await File.WriteAllTextAsync(_loginUsersPath, content);
                        System.Diagnostics.Debug.WriteLine($"Successfully updated loginusers.vdf for account {steamId}");
                        return true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not find closing brace for account {steamId}");
                        return false;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Could not find Steam ID {steamId} in loginusers.vdf");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating loginusers.vdf: {ex.Message}");
                return false;
            }
        }

        private async Task UpdateConfigAsync(string steamId, string personaState)
        {
            if (!File.Exists(_configPath))
                return;

            var content = await File.ReadAllTextAsync(_configPath);
            
            // Update persona state
            var personaStateValue = personaState.ToLower() switch
            {
                "offline" => "0",
                "online" => "1", 
                "busy" => "2",
                "away" => "3",
                "snooze" => "4",
                "looking to trade" => "5",
                "looking to play" => "6",
                _ => "1" // Default to online
            };

            // Update or add PersonaState setting
            var personaStatePattern = @"""PersonaState""\s+""\d+""";
            if (Regex.IsMatch(content, personaStatePattern))
            {
                content = Regex.Replace(content, personaStatePattern, $@"""PersonaState"" ""{personaStateValue}""");
            }
            else
            {
                // Add PersonaState setting if it doesn't exist
                var insertPoint = content.LastIndexOf("}");
                if (insertPoint > 0)
                {
                    content = content.Insert(insertPoint, $"\n\t\t\"PersonaState\"\t\t\"{personaStateValue}\"");
                }
            }

            await File.WriteAllTextAsync(_configPath, content);
        }

        private Task StartSteamWithLoginAsync(string username)
        {
            var steamExe = Path.Combine(_steamPath, "Steam.exe");
            if (File.Exists(steamExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    Arguments = $"-login {username}",
                    UseShellExecute = true
                });
            }
            return Task.CompletedTask;
        }

        private Task SetAutoLoginRegistryAsync(string username, bool rememberPassword)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam");
                key?.SetValue("AutoLoginUser", username);
                key?.SetValue("RememberPassword", rememberPassword ? 1 : 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting registry: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public async Task<List<SteamAccount>> DiscoverAccountsAsync()
        {
            var accounts = new List<SteamAccount>();
            
            if (!File.Exists(_loginUsersPath))
            {
                System.Diagnostics.Debug.WriteLine("loginusers.vdf file not found");
                return accounts;
            }

            try
            {
                var content = await File.ReadAllTextAsync(_loginUsersPath);
                System.Diagnostics.Debug.WriteLine($"Reading loginusers.vdf content length: {content.Length}");
                
                // Get the currently online account
                var currentlyOnlineAccount = await GetCurrentlyOnlineAccountAsync();
                System.Diagnostics.Debug.WriteLine($"Currently online account: {currentlyOnlineAccount ?? "None"}");

                // Find all account blocks
                var accountMatches = Regex.Matches(content, @"""(\d{17})""\s*{([^}]*)}", RegexOptions.Singleline);
                System.Diagnostics.Debug.WriteLine($"Found {accountMatches.Count} account blocks");

                foreach (Match match in accountMatches)
                {
                    var steamId = match.Groups[1].Value;
                    var accountBlock = match.Groups[2].Value;
                    
                    System.Diagnostics.Debug.WriteLine($"Processing account block for Steam ID: {steamId}");

                    var accountName = ExtractValue(accountBlock, "AccountName");
                    var personaName = ExtractValue(accountBlock, "PersonaName");
                    var timestamp = ExtractValue(accountBlock, "Timestamp");
                    var mostRec = ExtractValue(accountBlock, "MostRec");
                    
                    System.Diagnostics.Debug.WriteLine($"  AccountName: {accountName}");
                    System.Diagnostics.Debug.WriteLine($"  PersonaName: {personaName}");
                    System.Diagnostics.Debug.WriteLine($"  MostRec: {mostRec}");
                    
                    // Only add if we have at least an account name and it's not empty/whitespace
                    if (!string.IsNullOrWhiteSpace(accountName))
                    {
                        var lastLogin = DateTime.Now;
                        if (!string.IsNullOrEmpty(timestamp) && long.TryParse(timestamp, out var unixTime))
                        {
                            lastLogin = DateTimeOffset.FromUnixTimeSeconds(unixTime).DateTime;
                        }

                        // Determine if this account is currently online
                        var isCurrentlyOnline = !string.IsNullOrEmpty(currentlyOnlineAccount) && 
                                               currentlyOnlineAccount.Equals(accountName, StringComparison.OrdinalIgnoreCase);

                        var account = new SteamAccount
                        {
                            SteamId = steamId,
                            AccountName = accountName.Trim(),
                            PersonaName = !string.IsNullOrWhiteSpace(personaName) ? personaName.Trim() : accountName.Trim(),
                            LastLogin = lastLogin,
                            IsCurrentAccount = isCurrentlyOnline
                        };

                        System.Diagnostics.Debug.WriteLine($"  IsCurrentlyOnline: {isCurrentlyOnline}");

                        // Check for duplicates by Steam ID, Account Name, and Persona Name
                        var isDuplicate = accounts.Any(a => 
                            a.SteamId == steamId || 
                            a.AccountName.Equals(accountName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(personaName) && 
                             a.PersonaName.Equals(personaName.Trim(), StringComparison.OrdinalIgnoreCase)));

                        if (!isDuplicate)
                        {
                            accounts.Add(account);
                            System.Diagnostics.Debug.WriteLine($"  Added account: {account.AccountName} ({account.SteamId})");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"  Skipped duplicate account: {accountName} ({steamId})");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"  Skipped account with empty AccountName: {steamId}");
                    }
                }

                // Sort accounts by last login (most recent first)
                accounts = accounts.OrderByDescending(a => a.LastLogin).ToList();
                
                System.Diagnostics.Debug.WriteLine($"Final account count after deduplication: {accounts.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error discovering accounts: {ex.Message}");
            }

            return accounts;
        }

        private List<SteamAccount> ParseAllAccounts(string usersSection)
        {
            var accounts = new List<SteamAccount>();
            
            // Multiple patterns to catch different Steam ID formats
            var patterns = new[]
            {
                @"""(\d{17})""\s*{",  // Standard 17-digit Steam ID
                @"""(\d{16})""\s*{",  // 16-digit Steam ID (some older accounts)
                @"""(\d{15})""\s*{",  // 15-digit Steam ID
                @"""(\d{14})""\s*{",  // 14-digit Steam ID
                @"""(\d{13})""\s*{",  // 13-digit Steam ID
                @"""(\d{12})""\s*{",  // 12-digit Steam ID
                @"""(\d{11})""\s*{",  // 11-digit Steam ID
                @"""(\d{10})""\s*{",  // 10-digit Steam ID
                @"""(\d{9})""\s*{",   // 9-digit Steam ID
                @"""(\d{8})""\s*{"    // 8-digit Steam ID
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(usersSection, pattern);
                System.Diagnostics.Debug.WriteLine($"Pattern '{pattern}' found {matches.Count} matches");

                foreach (Match match in matches)
                {
                    var steamId = match.Groups[1].Value;
                    var startIndex = match.Index;
                    
                    // Find the end of this account block
                    var endIndex = FindClosingBrace(usersSection, startIndex);
                    if (endIndex == -1) continue;
                    
                    var accountBlock = usersSection.Substring(startIndex, endIndex - startIndex + 1);
                    
                    // Extract account details from this block
                    var accountName = ExtractValue(accountBlock, "AccountName");
                    var personaName = ExtractValue(accountBlock, "PersonaName");
                    var timestamp = ExtractValue(accountBlock, "Timestamp");
                    var mostRec = ExtractValue(accountBlock, "MostRec");
                    
                    // Only add if we have at least an account name and it's not empty/whitespace
                    if (!string.IsNullOrWhiteSpace(accountName))
                    {
                        var lastLogin = DateTime.Now;
                        if (!string.IsNullOrEmpty(timestamp) && long.TryParse(timestamp, out var unixTime))
                        {
                            lastLogin = DateTimeOffset.FromUnixTimeSeconds(unixTime).DateTime;
                        }

                        var account = new SteamAccount
                        {
                            SteamId = steamId,
                            AccountName = accountName.Trim(),
                            PersonaName = !string.IsNullOrWhiteSpace(personaName) ? personaName.Trim() : accountName.Trim(),
                            LastLogin = lastLogin,
                            IsCurrentAccount = mostRec == "1"
                        };

                        // Check for duplicates by Steam ID, Account Name, and Persona Name
                        var isDuplicate = accounts.Any(a => 
                            a.SteamId == steamId || 
                            a.AccountName.Equals(accountName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(personaName) && 
                             a.PersonaName.Equals(personaName.Trim(), StringComparison.OrdinalIgnoreCase)));

                        if (!isDuplicate)
                        {
                            accounts.Add(account);
                            System.Diagnostics.Debug.WriteLine($"  Added account: {account.AccountName} ({account.SteamId})");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"  Skipped duplicate account: {accountName} ({steamId})");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"  Skipped account with empty AccountName: {steamId}");
                    }
                }
            }

            // Sort accounts by last login (most recent first)
            accounts = accounts.OrderByDescending(a => a.LastLogin).ToList();
            
            return accounts;
        }

        private string? ExtractValue(string accountBlock, string key)
        {
            var pattern = $@"""{key}""\s+""([^""]*)""";
            var match = Regex.Match(accountBlock, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        private int FindClosingBrace(string content, int startIndex)
        {
            var braceCount = 0;
            var foundOpening = false;
            
            for (int i = startIndex; i < content.Length; i++)
            {
                var c = content[i];
                if (c == '{')
                {
                    braceCount++;
                    foundOpening = true;
                }
                else if (c == '}')
                {
                    braceCount--;
                    if (foundOpening && braceCount == 0)
                    {
                        return i;
                    }
                }
            }
            
            return -1;
        }

        private async Task<bool> VerifyAccountSwitchAsync(string expectedSteamId)
        {
            try
            {
                // Wait a bit more for Steam to fully load
                await Task.Delay(2000);
                
                // Check if the loginusers.vdf file has been updated correctly
                if (!File.Exists(_loginUsersPath))
                    return false;

                var content = await File.ReadAllTextAsync(_loginUsersPath);
                var mostRecentMatch = Regex.Match(content, @"""MostRec""\s+""1""");
                
                if (!mostRecentMatch.Success)
                {
                    System.Diagnostics.Debug.WriteLine("No account marked as MostRec=1");
                    return false;
                }

                // Find the Steam ID of the most recent account
                var beforeMostRec = content.Substring(0, mostRecentMatch.Index);
                var lastAccountMatch = Regex.Match(beforeMostRec, @"""(\d{17})""");
                
                if (!lastAccountMatch.Success)
                {
                    System.Diagnostics.Debug.WriteLine("Could not find Steam ID for most recent account");
                    return false;
                }

                var actualSteamId = lastAccountMatch.Groups[1].Value;
                var isCorrect = actualSteamId == expectedSteamId;
                
                System.Diagnostics.Debug.WriteLine($"Expected Steam ID: {expectedSteamId}, Actual: {actualSteamId}, Match: {isCorrect}");
                
                return isCorrect;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error verifying account switch: {ex.Message}");
                return false;
            }
        }

        public bool IsSteamRunning()
        {
            return Process.GetProcessesByName("Steam").Length > 0;
        }

        public async Task<string> TestSteamAccessAsync()
        {
            var result = new List<string>();
            
            // Test Steam path
            if (!Directory.Exists(_steamPath))
            {
                return $"❌ Steam path not found: {_steamPath}";
            }
            result.Add($"✅ Steam path found: {_steamPath}");

            // Test config directory
            var configDir = Path.Combine(_steamPath, "config");
            if (!Directory.Exists(configDir))
            {
                return $"❌ Steam config directory not found: {configDir}";
            }
            result.Add($"✅ Config directory found: {configDir}");

            // Test loginusers.vdf
            if (!File.Exists(_loginUsersPath))
            {
                return $"❌ loginusers.vdf not found: {_loginUsersPath}";
            }
            result.Add($"✅ loginusers.vdf found: {_loginUsersPath}");

            // Test reading the file
            try
            {
                var content = await File.ReadAllTextAsync(_loginUsersPath);
                result.Add($"✅ Can read loginusers.vdf (size: {content.Length} characters)");
                
                // Test parsing accounts using comprehensive method
                var usersSectionStart = content.IndexOf("\"users\"");
                if (usersSectionStart != -1)
                {
                    var usersBraceStart = content.IndexOf('{', usersSectionStart);
                    if (usersBraceStart != -1)
                    {
                        var usersSectionEnd = FindClosingBrace(content, usersBraceStart);
                        if (usersSectionEnd != -1)
                        {
                            var usersSection = content.Substring(usersBraceStart + 1, usersSectionEnd - usersBraceStart - 1);
                            var testAccounts = ParseAllAccounts(usersSection);
                            result.Add($"✅ Found {testAccounts.Count} accounts in file using comprehensive parsing");
                            
                            if (testAccounts.Count > 0)
                            {
                                foreach (var account in testAccounts.Take(3)) // Show first 3 accounts
                                {
                                    result.Add($"   📋 {account.AccountName} ({account.SteamId}) - {account.PersonaName}" + (account.IsCurrentAccount ? " [Current]" : ""));
                                }
                            }
                        }
                    }
                }
                else
                {
                    result.Add("❌ Could not find 'users' section in VDF file");
                }
            }
            catch (Exception ex)
            {
                return $"❌ Error reading loginusers.vdf: {ex.Message}";
            }

            return string.Join("\n", result);
        }

        public async Task<string> GetSteamAccountsDebugInfoAsync()
        {
            try
            {
                var info = new List<string>();
                info.Add($"Steam Path: {_steamPath}");
                info.Add($"Login Users Path: {_loginUsersPath}");
                info.Add($"Config Path: {_configPath}");
                
                if (!File.Exists(_loginUsersPath))
                {
                    info.Add("❌ loginusers.vdf file not found!");
                    return string.Join("\n", info);
                }
                
                info.Add("✅ loginusers.vdf file found");
                
                var content = await File.ReadAllTextAsync(_loginUsersPath);
                info.Add($"File size: {content.Length} characters");
                
                // Find all Steam IDs in the file
                var steamIdMatches = Regex.Matches(content, @"""(\d{17})""");
                info.Add($"Found {steamIdMatches.Count} Steam IDs in loginusers.vdf:");
                
                foreach (Match match in steamIdMatches)
                {
                    var steamId = match.Groups[1].Value;
                    info.Add($"  - {steamId}");
                }
                
                // Check which accounts are marked as most recent
                var mostRecMatches = Regex.Matches(content, @"""MostRec""\s+""1""");
                info.Add($"Accounts marked as MostRec=1: {mostRecMatches.Count}");
                
                return string.Join("\n", info);
            }
            catch (Exception ex)
            {
                return $"Error getting debug info: {ex.Message}";
            }
        }

        public async Task<List<SteamConfigFile>> GetSteamConfigFilesAsync()
        {
            var configFiles = new List<SteamConfigFile>();
            
            try
            {
                // Check loginusers.vdf
                if (File.Exists(_loginUsersPath))
                {
                    var content = await File.ReadAllTextAsync(_loginUsersPath);
                    configFiles.Add(new SteamConfigFile
                    {
                        Name = "loginusers.vdf",
                        Path = _loginUsersPath,
                        Content = content
                    });
                }
                
                // Check config.vdf
                if (File.Exists(_configPath))
                {
                    var content = await File.ReadAllTextAsync(_configPath);
                    configFiles.Add(new SteamConfigFile
                    {
                        Name = "config.vdf",
                        Path = _configPath,
                        Content = content
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading Steam config files: {ex.Message}");
            }
            
            return configFiles;
        }

        public async Task<bool> CreateAccountSwitchBatchFileAsync(SteamAccount account)
        {
            try
            {
                var batchContent = $@"@echo off
echo Switching to Steam account: {account.DisplayName}
echo.

REM Close Steam if running
taskkill /f /im Steam.exe >nul 2>&1
timeout /t 2 /nobreak >nul

REM Set registry for auto-login
reg add ""HKCU\Software\Valve\Steam"" /v ""AutoLoginUser"" /t REG_SZ /d ""{account.AccountName}"" /f
reg add ""HKCU\Software\Valve\Steam"" /v ""RememberPassword"" /t REG_DWORD /d 1 /f
reg add ""HKCU\Software\Valve\Steam"" /v ""LoginUser"" /t REG_SZ /d ""{account.AccountName}"" /f

REM Start Steam
start """" ""{Path.Combine(_steamPath, "Steam.exe")}""

echo Steam is starting with account: {account.DisplayName}
echo.
pause";

                var batchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                    $"SwitchTo_{account.AccountName}.bat");
                
                await File.WriteAllTextAsync(batchPath, batchContent);
                
                System.Diagnostics.Debug.WriteLine($"Created batch file: {batchPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating batch file: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SwitchToAccountWithBatchFileAsync(SteamAccount account)
        {
            try
            {
                var success = await CreateAccountSwitchBatchFileAsync(account);
                if (success)
                {
                    var batchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                        $"SwitchTo_{account.AccountName}.bat");
                    
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = batchPath,
                        UseShellExecute = true
                    };
                    
                    Process.Start(startInfo);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error running batch file: {ex.Message}");
                return false;
            }
        }



        public async Task<string?> GetCurrentlyOnlineAccountAsync()
        {
            try
            {
                // Check if Steam is running
                var steamProcesses = Process.GetProcessesByName("Steam");
                if (steamProcesses.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("Steam is not running - no accounts online");
                    return null;
                }

                // Method 1: Check registry for current login user (most reliable)
                var registryAccount = GetCurrentUserFromRegistry();
                if (!string.IsNullOrEmpty(registryAccount))
                {
                    System.Diagnostics.Debug.WriteLine($"Current user from registry: {registryAccount}");
                    return registryAccount;
                }

                // Method 2: Check loginusers.vdf for most recent account with RememberPassword=1
                var mostRecentLoggedInAccount = await GetMostRecentLoggedInAccountAsync();
                if (!string.IsNullOrEmpty(mostRecentLoggedInAccount))
                {
                    System.Diagnostics.Debug.WriteLine($"Most recent logged in account: {mostRecentLoggedInAccount}");
                    return mostRecentLoggedInAccount;
                }

                System.Diagnostics.Debug.WriteLine("Could not determine currently online account");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting currently online account: {ex.Message}");
                return null;
            }
        }

        private string? GetCurrentUserFromRegistry()
        {
            try
            {
                // Fast path: Check HKCU first (most reliable and fastest)
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key != null)
                {
                    // Check AutoLoginUser first (most reliable indicator)
                    var autoLoginUser = key.GetValue("AutoLoginUser") as string;
                    if (!string.IsNullOrEmpty(autoLoginUser))
                    {
                        System.Diagnostics.Debug.WriteLine($"Fast registry detection: AutoLoginUser = {autoLoginUser}");
                        return autoLoginUser;
                    }
                    
                    // Check LoginUser as backup
                    var loginUser = key.GetValue("LoginUser") as string;
                    if (!string.IsNullOrEmpty(loginUser))
                    {
                        System.Diagnostics.Debug.WriteLine($"Fast registry detection: LoginUser = {loginUser}");
                        return loginUser;
                    }
                }

                return null; // Skip HKLM check for speed - HKCU is almost always sufficient
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading registry: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> GetMostRecentLoggedInAccountAsync()
        {
            try
            {
                if (!File.Exists(_loginUsersPath))
                    return null;

                var content = await File.ReadAllTextAsync(_loginUsersPath);
                
                // Fast approach: Find MostRecent first, then get the account name
                var mostRecentMatch = Regex.Match(content, @"""MostRecent""\s+""1""");
                if (mostRecentMatch.Success)
                {
                    // Find the account name in the same block (faster than complex pattern)
                    var beforeMostRec = content.Substring(0, mostRecentMatch.Index);
                    var lastBraceIndex = beforeMostRec.LastIndexOf('{');
                    
                    if (lastBraceIndex >= 0)
                    {
                        var blockStart = beforeMostRec.LastIndexOf('"', lastBraceIndex - 1);
                        if (blockStart >= 0)
                        {
                            blockStart = beforeMostRec.LastIndexOf('"', blockStart - 1);
                            if (blockStart >= 0)
                            {
                                var steamIdMatch = Regex.Match(beforeMostRec.Substring(blockStart), @"""(\d{17})""");
                                if (steamIdMatch.Success)
                                {
                                    // Now find the AccountName in the same block
                                    var blockEnd = content.IndexOf('}', mostRecentMatch.Index);
                                    if (blockEnd > 0)
                                    {
                                        var accountBlock = content.Substring(lastBraceIndex, blockEnd - lastBraceIndex);
                                        var accountNameMatch = Regex.Match(accountBlock, @"""AccountName""\s+""([^""]+)""");
                                        
                                        if (accountNameMatch.Success)
                                        {
                                            var accountName = accountNameMatch.Groups[1].Value;
                                            System.Diagnostics.Debug.WriteLine($"Fast VDF detection: Found most recent account = {accountName}");
                                            return accountName;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting most recent logged in account: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> GetMostRecentAccountFromVdfAsync()
        {
            try
            {
                if (!File.Exists(_loginUsersPath))
                    return null;

                var content = await File.ReadAllTextAsync(_loginUsersPath);
                var mostRecentMatch = Regex.Match(content, @"""MostRec""\s+""1""");
                
                if (!mostRecentMatch.Success)
                    return null;

                // Find the Steam ID of the most recent account
                var beforeMostRec = content.Substring(0, mostRecentMatch.Index);
                var lastAccountMatch = Regex.Match(beforeMostRec, @"""(\d{17})""");
                
                if (!lastAccountMatch.Success)
                    return null;

                var steamId = lastAccountMatch.Groups[1].Value;
                
                // Find the account name for this Steam ID
                var accountBlockPattern = $@"{steamId}\s*{{[^}}]*}}";
                var accountBlockMatch = Regex.Match(content, accountBlockPattern, RegexOptions.Singleline);
                
                if (accountBlockMatch.Success)
                {
                    var accountName = ExtractValue(accountBlockMatch.Value, "AccountName");
                    return accountName;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading VDF file: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> GetActiveSteamAccountAsync()
        {
            try
            {
                // Check Steam's active connections by looking at network connections
                var steamProcesses = Process.GetProcessesByName("Steam");
                if (steamProcesses.Length == 0) return null;

                // Check if Steam has active network connections (indicates logged in)
                var hasActiveConnections = await CheckSteamNetworkConnectionsAsync();
                if (!hasActiveConnections)
                {
                    System.Diagnostics.Debug.WriteLine("Steam has no active network connections");
                    return null;
                }

                // Check Steam's registry for active session
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key != null)
                {
                    var autoLoginUser = key.GetValue("AutoLoginUser") as string;
                    var loginUser = key.GetValue("LoginUser") as string;
                    var steamPath = key.GetValue("SteamPath") as string;
                    
                    if (!string.IsNullOrEmpty(autoLoginUser))
                        return autoLoginUser;
                    if (!string.IsNullOrEmpty(loginUser))
                        return loginUser;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking active Steam account: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> CheckSteamNetworkConnectionsAsync()
        {
            try
            {
                // Use netstat to check if Steam has active connections
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-an",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return false;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Check for Steam's typical connection patterns
                var steamConnections = output.Contains(":27015") || // Steam's default port
                                     output.Contains(":27017") || // Steam's query port
                                     output.Contains("steamcommunity.com") ||
                                     output.Contains("steampowered.com");

                System.Diagnostics.Debug.WriteLine($"Steam network connections detected: {steamConnections}");
                return steamConnections;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking Steam network connections: {ex.Message}");
                return false;
            }
        }

        private async Task<string?> GetAccountFromUserDataAsync()
        {
            try
            {
                // Check Steam's userdata folder for active sessions
                var userDataPath = Path.Combine(_steamPath, "userdata");
                if (!Directory.Exists(userDataPath))
                {
                    System.Diagnostics.Debug.WriteLine("Steam userdata folder not found");
                    return null;
                }

                // Look for recently modified folders in userdata
                var userFolders = Directory.GetDirectories(userDataPath);
                var mostRecentFolder = userFolders
                    .Select(folder => new DirectoryInfo(folder))
                    .Where(info => info.Exists)
                    .OrderByDescending(info => info.LastWriteTime)
                    .FirstOrDefault();

                if (mostRecentFolder != null)
                {
                    var steamId = Path.GetFileName(mostRecentFolder.FullName);
                    System.Diagnostics.Debug.WriteLine($"Most recent userdata folder: {steamId} (modified: {mostRecentFolder.LastWriteTime})");

                    // Check if this folder was modified recently (within last 5 minutes)
                    var timeSinceLastWrite = DateTime.Now - mostRecentFolder.LastWriteTime;
                    if (timeSinceLastWrite.TotalMinutes <= 5)
                    {
                        // Find the account name for this Steam ID
                        var accountName = await GetAccountNameFromSteamIdAsync(steamId);
                        if (!string.IsNullOrEmpty(accountName))
                        {
                            System.Diagnostics.Debug.WriteLine($"Active account from userdata: {accountName}");
                            return accountName;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking userdata: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> GetAccountNameFromSteamIdAsync(string steamId)
        {
            try
            {
                if (!File.Exists(_loginUsersPath))
                    return null;

                var content = await File.ReadAllTextAsync(_loginUsersPath);
                var accountBlockPattern = $@"{steamId}\s*{{[^}}]*}}";
                var accountBlockMatch = Regex.Match(content, accountBlockPattern, RegexOptions.Singleline);
                
                if (accountBlockMatch.Success)
                {
                    var accountName = ExtractValue(accountBlockMatch.Value, "AccountName");
                    return accountName;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting account name from Steam ID: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> SwitchToAccountWithCredentialsAsync(SteamAccount account, string password)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Attempting to switch to account with credentials: {account.DisplayName} ({account.AccountName})");
                
                // Close Steam if it's running
                await CloseSteamAsync();
                
                // Wait a moment for Steam to fully close
                await Task.Delay(2000);

                // Method 1: Try using Steam's -login parameter
                var loginSuccess = await TryLoginWithCredentialsAsync(account.AccountName, password);
                if (loginSuccess)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully started Steam with login args for {account.AccountName}");
                    return true;
                }

                // Method 2: Try using registry auto-login
                var registrySuccess = await TryRegistryAutoLoginAsync(account.AccountName);
                if (registrySuccess)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully set registry auto-login for {account.AccountName}");
                    await StartSteamAsync();
                    return true;
                }

                // Method 3: Try the VDF file method as fallback
                var vdfSuccess = await TryVdfFileMethodAsync(account.SteamId);
                if (vdfSuccess)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully updated VDF files for {account.SteamId}");
                    await StartSteamAsync();
                    return true;
                }

                System.Diagnostics.Debug.WriteLine("All account switching methods failed");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error switching to account with credentials: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TryLoginWithCredentialsAsync(string accountName, string password)
        {
            try
            {
                var steamExe = Path.Combine(_steamPath, "Steam.exe");
                if (!File.Exists(steamExe))
                    return false;

                // Note: Steam doesn't support password via command line for security reasons
                // So we'll use the -login parameter which will prompt for password
                var startInfo = new ProcessStartInfo
                {
                    FileName = steamExe,
                    Arguments = $"-login {accountName}",
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                if (process != null)
                {
                    // Wait a bit to see if Steam starts successfully
                    await Task.Delay(3000);
                    return !process.HasExited;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error with Steam login with credentials: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CreateSteamLoginBatchFileAsync(SteamAccount account, string password)
        {
            try
            {
                // Create a simple batch file that starts Steam with username and provides instructions
                var batchContent = $@"@echo off
echo ========================================
echo Steam Account Login Helper
echo ========================================
echo.
echo Account: {account.DisplayName} ({account.AccountName})
echo.
echo Instructions:
echo 1. Steam will start with username pre-filled
echo 2. Enter your password manually
echo 3. Click Sign In
echo.
echo ========================================
echo.

REM Close Steam if running
taskkill /f /im Steam.exe >nul 2>&1
timeout /t 2 /nobreak >nul

REM Start Steam with login parameter (username pre-filled)
start """" ""{Path.Combine(_steamPath, "Steam.exe")}"" -login {account.AccountName}

echo Steam started with username: {account.AccountName}
echo.
echo Please enter your password in the Steam login window.
echo.
echo After logging in, this account will be saved for future use.
echo.
pause";

                var batchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                    $"LoginTo_{account.AccountName}.bat");
                
                await File.WriteAllTextAsync(batchPath, batchContent);
                
                System.Diagnostics.Debug.WriteLine($"Created login batch file: {batchPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating login batch file: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ExecuteSteamLoginBatchFileAsync(SteamAccount account, string password)
        {
            try
            {
                var success = await CreateSteamLoginBatchFileAsync(account, password);
                if (success)
                {
                    var batchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                        $"LoginTo_{account.AccountName}.bat");
                    
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = batchPath,
                        UseShellExecute = true
                    };
                    
                    Process.Start(startInfo);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error executing login batch file: {ex.Message}");
                return false;
            }
        }

        // Windows API declarations

        private const int WM_CHAR = 0x0102;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);







        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private string GetWindowText(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }





        public async Task FillSteamLoginUsernameAsync(string username)
        {
            try
            {
                _logger.LogInfo($"Starting Fill operation for username: {username}", "FILL_START");
                System.Diagnostics.Debug.WriteLine($"=== GLOBAL KEYBOARD FILL METHOD ===");
                System.Diagnostics.Debug.WriteLine($"Attempting to fill username: {username}");

                // Use the proven working method: Global Keyboard Simulation
                _logger.LogInfo("Using Global Keyboard Simulation method (keybd_event)", "FILL_METHOD");
                bool success = await TryGlobalKeyboardMethod(username);
                if (success)
                {
                    _logger.LogFillMethod("Global Keyboard Simulation", username, true, "Successfully typed username using keybd_event API");
                    _logger.LogSuccess($"Fill operation completed successfully for username: {username}", "FILL_COMPLETE");
                    System.Diagnostics.Debug.WriteLine("SUCCESS: Global keyboard method worked");
                }
                else
                {
                    _logger.LogFillMethod("Global Keyboard Simulation", username, false, "Method failed, showing manual entry message");
                    
                    // If the proven method fails, show manual entry message
                    System.Windows.MessageBox.Show(
                        $"Username: {username}\n\n" +
                        "Automatic typing failed. Please manually type this username in Steam.\n\n" +
                        "The username has been copied to your clipboard for easy pasting with Ctrl+V.",
                        "Manual Entry Required",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in Fill operation for username: {username}", "FILL_ERROR", ex);
                System.Diagnostics.Debug.WriteLine($"Error in FillSteamLoginUsernameAsync: {ex.Message}");
                
                // Copy to clipboard as fallback
                try
                {
                    System.Windows.Clipboard.SetText(username);
                }
                catch { /* Ignore clipboard errors */ }
                
                System.Windows.MessageBox.Show(
                    $"Username: {username}\n\n" +
                    "Please manually type this username in Steam.",
                    "Manual Entry Required",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        public async Task FillSteamLoginCredentialsAsync(string username, string password)
        {
            try
            {
                _logger.LogInfo($"Starting Full Credentials Fill operation for username: {username}", "FILL_CREDENTIALS_START");
                System.Diagnostics.Debug.WriteLine($"=== FULL CREDENTIALS FILL METHOD ===");
                System.Diagnostics.Debug.WriteLine($"Attempting to fill username: {username} and password");

                // Use the proven working method: Global Keyboard Simulation with Tab sequence
                _logger.LogInfo("Using Global Keyboard Simulation for username + password (keybd_event)", "FILL_CREDENTIALS_METHOD");
                bool success = await TryGlobalKeyboardCredentialsMethod(username, password);
                if (success)
                {
                    _logger.LogFillMethod("Global Keyboard Credentials", username, true, "Successfully typed username and password using keybd_event API");
                    _logger.LogSuccess($"Credentials fill operation completed successfully for username: {username}", "FILL_CREDENTIALS_COMPLETE");
                    System.Diagnostics.Debug.WriteLine("SUCCESS: Global keyboard credentials method worked");
                }
                else
                {
                    _logger.LogFillMethod("Global Keyboard Credentials", username, false, "Method failed, falling back to username only");
                    
                    // Fallback to username only
                    await FillSteamLoginUsernameAsync(username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in Credentials Fill operation for username: {username}", "FILL_CREDENTIALS_ERROR", ex);
                System.Diagnostics.Debug.WriteLine($"Error in FillSteamLoginCredentialsAsync: {ex.Message}");
                
                // Fallback to username only
                await FillSteamLoginUsernameAsync(username);
            }
        }



        private async Task<bool> TryGlobalKeyboardMethod(string username)
        {
            try
            {
                _logger.LogInfo("Starting Global Keyboard Method (keybd_event)", "FILL_KEYBOARD");
                System.Diagnostics.Debug.WriteLine("Using Global Keyboard Simulation");
                
                var steamWindow = FindSteamLoginWindow();
                if (steamWindow != IntPtr.Zero)
                {
                    _logger.LogInfo($"Steam window found: {steamWindow}, bringing to foreground", "FILL_KEYBOARD");
                    SetForegroundWindow(steamWindow);
                    await Task.Delay(500); // Reduced from 1000ms
                }
                else
                {
                    _logger.LogWarning("Steam window not found, attempting global input anyway", "FILL_KEYBOARD");
                }

                // Copy to clipboard first
                System.Windows.Clipboard.SetText(username);
                _logger.LogInfo("Username copied to clipboard", "FILL_KEYBOARD");
                await Task.Delay(100); // Reduced from 200ms

                // Use keybd_event for global key simulation
                const byte VK_CONTROL = 0x11;
                const byte VK_A = 0x41;
                const byte VK_V = 0x56;

                _logger.LogInfo("Sending Ctrl+A to select all text", "FILL_KEYBOARD");
                // Ctrl+A (select all)
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_A, 0, 0, UIntPtr.Zero);
                await Task.Delay(30); // Reduced from 50ms
                keybd_event(VK_A, 0, 2, UIntPtr.Zero); // KEYUP
                keybd_event(VK_CONTROL, 0, 2, UIntPtr.Zero); // KEYUP
                
                await Task.Delay(100); // Reduced from 200ms

                _logger.LogInfo("Sending Ctrl+V to paste username", "FILL_KEYBOARD");
                // Ctrl+V (paste)
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                await Task.Delay(30); // Reduced from 50ms
                keybd_event(VK_V, 0, 2, UIntPtr.Zero); // KEYUP
                keybd_event(VK_CONTROL, 0, 2, UIntPtr.Zero); // KEYUP

                _logger.LogSuccess("Global keyboard simulation completed successfully", "FILL_KEYBOARD");
                System.Diagnostics.Debug.WriteLine("Global keyboard simulation completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Global keyboard method failed", "FILL_KEYBOARD", ex);
                System.Diagnostics.Debug.WriteLine($"Global keyboard method failed: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TryGlobalKeyboardCredentialsMethod(string username, string password)
        {
            try
            {
                _logger.LogInfo("Starting Global Keyboard Credentials Method (keybd_event)", "FILL_CREDENTIALS_KEYBOARD");
                System.Diagnostics.Debug.WriteLine("Using Global Keyboard Simulation for Username + Password");
                
                var steamWindow = FindSteamLoginWindow();
                if (steamWindow != IntPtr.Zero)
                {
                    _logger.LogInfo($"Steam window found: {steamWindow}, bringing to foreground", "FILL_CREDENTIALS_KEYBOARD");
                    SetForegroundWindow(steamWindow);
                    await Task.Delay(500); // Reduced from 1000ms
                }
                else
                {
                    _logger.LogWarning("Steam window not found, attempting global input anyway", "FILL_CREDENTIALS_KEYBOARD");
                }

                // Step 1: Fill Username
                System.Windows.Clipboard.SetText(username);
                _logger.LogInfo("Username copied to clipboard", "FILL_CREDENTIALS_KEYBOARD");
                await Task.Delay(100); // Reduced from 200ms

                // Use keybd_event for global key simulation
                const byte VK_CONTROL = 0x11;
                const byte VK_A = 0x41;
                const byte VK_V = 0x56;
                const byte VK_TAB = 0x09;

                _logger.LogInfo("Sending Ctrl+A to select all text in username field", "FILL_CREDENTIALS_KEYBOARD");
                // Ctrl+A (select all)
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_A, 0, 0, UIntPtr.Zero);
                await Task.Delay(30); // Reduced from 50ms
                keybd_event(VK_A, 0, 2, UIntPtr.Zero); // KEYUP
                keybd_event(VK_CONTROL, 0, 2, UIntPtr.Zero); // KEYUP
                
                await Task.Delay(100); // Reduced from 200ms

                _logger.LogInfo("Sending Ctrl+V to paste username", "FILL_CREDENTIALS_KEYBOARD");
                // Ctrl+V (paste username)
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                await Task.Delay(30); // Reduced from 50ms
                keybd_event(VK_V, 0, 2, UIntPtr.Zero); // KEYUP
                keybd_event(VK_CONTROL, 0, 2, UIntPtr.Zero); // KEYUP

                await Task.Delay(150); // Reduced from 300ms

                // Step 2: Tab to Password Field
                _logger.LogInfo("Sending Tab to move to password field", "FILL_CREDENTIALS_KEYBOARD");
                keybd_event(VK_TAB, 0, 0, UIntPtr.Zero);
                await Task.Delay(30); // Reduced from 50ms
                keybd_event(VK_TAB, 0, 2, UIntPtr.Zero); // KEYUP
                
                await Task.Delay(150); // Reduced from 300ms

                // Step 3: Fill Password
                System.Windows.Clipboard.SetText(password);
                _logger.LogInfo("Password copied to clipboard", "FILL_CREDENTIALS_KEYBOARD");
                await Task.Delay(100); // Reduced from 200ms

                _logger.LogInfo("Sending Ctrl+A to select all text in password field", "FILL_CREDENTIALS_KEYBOARD");
                // Ctrl+A (select all in password field)
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_A, 0, 0, UIntPtr.Zero);
                await Task.Delay(50);
                keybd_event(VK_A, 0, 2, UIntPtr.Zero); // KEYUP
                keybd_event(VK_CONTROL, 0, 2, UIntPtr.Zero); // KEYUP
                
                await Task.Delay(200);

                _logger.LogInfo("Sending Ctrl+V to paste password", "FILL_CREDENTIALS_KEYBOARD");
                // Ctrl+V (paste password)
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                await Task.Delay(50);
                keybd_event(VK_V, 0, 2, UIntPtr.Zero); // KEYUP
                keybd_event(VK_CONTROL, 0, 2, UIntPtr.Zero); // KEYUP

                _logger.LogSuccess("Global keyboard credentials simulation completed successfully", "FILL_CREDENTIALS_KEYBOARD");
                System.Diagnostics.Debug.WriteLine("Global keyboard credentials simulation completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Global keyboard credentials method failed", "FILL_CREDENTIALS_KEYBOARD", ex);
                System.Diagnostics.Debug.WriteLine($"Global keyboard credentials method failed: {ex.Message}");
                return false;
            }
        }



        public async Task FillSteamLoginUsernameAltAsync(string username)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Using alternative method to fill username: {username}");

                // Find the Steam login window
                var steamWindow = FindSteamLoginWindow();
                if (steamWindow == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("Steam login window not found (Alt method)");
                    return;
                }

                // Force window to foreground using multiple methods
                await BringWindowToForegroundForced(steamWindow);

                // Use global keyboard simulation
                await SendKeysAlternative(username);

                System.Diagnostics.Debug.WriteLine($"Alternative method completed for username: {username}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in alternative fill method: {ex.Message}");
            }
        }

        private async Task BringWindowToForegroundForced(IntPtr window)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Forcing Steam window to foreground...");
                
                // Multiple attempts to bring window to front
                ShowWindow(window, SW_RESTORE);
                await Task.Delay(200);
                
                ShowWindow(window, SW_SHOW);
                await Task.Delay(200);
                
                BringWindowToTop(window);
                await Task.Delay(200);
                
                SetForegroundWindow(window);
                await Task.Delay(500);
                
                // Click in window to ensure focus
                await ClickInWindow(window);
                await Task.Delay(300);
                
                // Verify foreground
                var foregroundWindow = GetForegroundWindow();
                System.Diagnostics.Debug.WriteLine($"Current foreground: {foregroundWindow}, Target: {window}");
                
                if (foregroundWindow != window)
                {
                    System.Diagnostics.Debug.WriteLine("Steam window not in foreground - trying Alt+Tab approach");
                    // Try Alt+Tab to cycle to Steam
                    await SimulateKeyPress(0x12, 0x09); // Alt+Tab
                    await Task.Delay(200);
                    await SimulateKeyPress(0x1B); // Escape to close Alt+Tab
                    await Task.Delay(200);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error bringing window to foreground: {ex.Message}");
            }
        }

        private async Task ClickInWindow(IntPtr window)
        {
            try
            {
                // Send a left mouse click to the center of the window to ensure focus
                const int WM_LBUTTONDOWN = 0x0201;
                const int WM_LBUTTONUP = 0x0202;
                
                PostMessage(window, WM_LBUTTONDOWN, IntPtr.Zero, IntPtr.Zero);
                await Task.Delay(50);
                PostMessage(window, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
                
                System.Diagnostics.Debug.WriteLine("Clicked in Steam window to ensure focus");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clicking in window: {ex.Message}");
            }
        }

        private async Task SendKeysAlternative(string text)
        {
            try
            {
                // Clear field first
                await SimulateKeyPress(0x11, 0x41); // Ctrl+A
                await Task.Delay(100);
                await SimulateKeyPress(0x2E); // Delete
                await Task.Delay(100);
                
                // Type each character
                foreach (char c in text)
                {
                    await SimulateKeyPress((byte)char.ToUpper(c));
                    await Task.Delay(50);
                }
                
                System.Diagnostics.Debug.WriteLine($"Alternative method: typed '{text}'");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error with alternative SendKeys: {ex.Message}");
            }
        }

        private async Task SimulateKeyPress(params byte[] keys)
        {
            try
            {
                // Send key down for all keys
                foreach (byte key in keys)
                {
                    keybd_event(key, 0, 0, UIntPtr.Zero);
                    await Task.Delay(10);
                }
                
                // Send key up for all keys (in reverse order)
                for (int i = keys.Length - 1; i >= 0; i--)
                {
                    keybd_event(keys[i], 0, 2, UIntPtr.Zero); // KEYEVENTF_KEYUP = 2
                    await Task.Delay(10);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error simulating key press: {ex.Message}");
            }
        }

        private async Task TypeCharacter(IntPtr window, char character)
        {
            // Convert character to virtual key code
            byte vkCode = (byte)VkKeyScan(character);
            
            // Handle special characters
            if (char.IsLetter(character))
            {
                vkCode = (byte)char.ToUpper(character);
            }
            else if (char.IsDigit(character))
            {
                vkCode = (byte)character;
            }

            // Send key down and key up messages
            PostMessage(window, WM_KEYDOWN, (IntPtr)vkCode, IntPtr.Zero);
            await Task.Delay(10);
            PostMessage(window, WM_KEYUP, (IntPtr)vkCode, IntPtr.Zero);
        }

        private void SendKeys(IntPtr window, byte[] keyCodes)
        {
            foreach (byte keyCode in keyCodes)
            {
                PostMessage(window, WM_KEYDOWN, (IntPtr)keyCode, IntPtr.Zero);
                System.Threading.Thread.Sleep(10);
            }
            
            // Release keys in reverse order
            for (int i = keyCodes.Length - 1; i >= 0; i--)
            {
                PostMessage(window, WM_KEYUP, (IntPtr)keyCodes[i], IntPtr.Zero);
                System.Threading.Thread.Sleep(10);
            }
        }

        public async Task DebugAllWindowsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== ALL WINDOWS DEBUG ===");
                var windowCount = 0;
                
                EnumWindows((hwnd, lParam) =>
                {
                    var windowText = GetWindowText(hwnd);
                    var isVisible = IsWindowVisible(hwnd);
                    
                    if (!string.IsNullOrEmpty(windowText) && isVisible)
                    {
                        windowCount++;
                        System.Diagnostics.Debug.WriteLine($"Window {windowCount}: '{windowText}' (Handle: {hwnd}, Visible: {isVisible})");
                        
                        // Check if this looks like Steam
                        if (windowText.ToLower().Contains("steam"))
                        {
                            System.Diagnostics.Debug.WriteLine($"  *** STEAM WINDOW FOUND: '{windowText}' ***");
                        }
                    }
                    
                    return true; // Continue enumeration
                }, IntPtr.Zero);
                
                System.Diagnostics.Debug.WriteLine($"=== TOTAL VISIBLE WINDOWS: {windowCount} ===");
                
                // Also specifically test Steam window finding
                System.Diagnostics.Debug.WriteLine("=== TESTING STEAM WINDOW FINDER ===");
                var steamWindow = FindSteamLoginWindow();
                if (steamWindow != IntPtr.Zero)
                {
                    var steamWindowText = GetWindowText(steamWindow);
                    System.Diagnostics.Debug.WriteLine($"Steam window finder result: '{steamWindowText}' (Handle: {steamWindow})");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Steam window finder: NO STEAM WINDOW FOUND");
                }
                
                await Task.Delay(100); // Just to make it async
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in DebugAllWindowsAsync: {ex.Message}");
            }
        }

        private IntPtr FindSteamLoginWindow()
        {
            IntPtr foundWindow = IntPtr.Zero;
            
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true; // Continue enumeration
                
                var windowTitle = GetWindowText(hWnd);
                var className = GetWindowClassName(hWnd);
                
                _logger.LogInfo($"Checking window: Title='{windowTitle}', Class='{className}', Handle={hWnd}");
                
                // Check for various Steam login window patterns (case-insensitive)
                var titleLower = windowTitle.ToLower();
                var classLower = className.ToLower();
                
                bool isSteamLoginWindow = 
                    // Modern Steam login window titles
                    titleLower.Contains("steam") ||
                    titleLower.Contains("sign in") ||
                    titleLower.Contains("login") ||
                    titleLower.Contains("sign-in") ||
                    titleLower == "steam" ||
                    // Steam process with empty title (very common for login screens)
                    (titleLower == "" && IsSteamProcessWindow(hWnd)) ||
                    // Steam with minimal title and Steam class
                    (titleLower.Length <= 10 && classLower.Contains("steam")) ||
                    // Steam client window classes
                    classLower.Contains("steam") ||
                    classLower.Contains("valve");
                
                if (isSteamLoginWindow)
                {
                    _logger.LogInfo($"✅ POTENTIAL login window: Title='{windowTitle}' Class='{className}' Handle={hWnd}");
                    
                    // Additional validation - make sure it's actually a Steam process
                    if (IsSteamProcessWindow(hWnd))
                    {
                        _logger.LogSuccess($"✅ CONFIRMED Steam login window: '{windowTitle}' (Class: '{className}')");
                        foundWindow = hWnd;
                        return false; // Stop enumeration
                    }
                    else
                    {
                        _logger.LogWarning($"⚠️ Window matches criteria but not from Steam process: '{windowTitle}'");
                    }
                }
                
                return true; // Continue enumeration
            }, IntPtr.Zero);
            
            if (foundWindow != IntPtr.Zero)
            {
                _logger.LogSuccess($"Steam login window found: {foundWindow}");
            }
            else
            {
                _logger.LogWarning("Steam login window not found");
            }
            
            return foundWindow;
        }
        
        private string GetWindowClassName(IntPtr hWnd)
        {
            StringBuilder className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            return className.ToString();
        }
        
        private bool IsSteamProcessWindow(IntPtr hWnd)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint processId);
                var process = Process.GetProcessById((int)processId);
                var processName = process.ProcessName.ToLower();
                
                // Accept both "steam" and "steamwebhelper" processes as valid Steam windows
                // Modern Steam uses steamwebhelper for login interface
                bool isSteamProcess = processName == "steam" || processName == "steamwebhelper";
                
                _logger.LogInfo($"Window process check: PID={processId}, Name={processName}, IsSteam={isSteamProcess}");
                
                return isSteamProcess;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking window process: {ex.Message}");
                return false;
            }
        }

        public async Task<IntPtr> FindSteamLoginWindowAsync()
        {
            return await Task.Run(() => FindSteamLoginWindow());
        }



        public async Task CleanupSavedAccountsAsync()
        {
            try
            {
                var accounts = await LoadSavedAccountsAsync();
                var cleanedAccounts = new List<SteamAccount>();
                var seenAccountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenSteamIds = new HashSet<string>();

                foreach (var account in accounts.OrderByDescending(a => a.LastLogin))
                {
                    // Skip if we've already seen this account name or Steam ID
                    if (string.IsNullOrWhiteSpace(account.AccountName) ||
                        seenAccountNames.Contains(account.AccountName) ||
                        seenSteamIds.Contains(account.SteamId))
                    {
                        System.Diagnostics.Debug.WriteLine($"Removing duplicate/invalid account: {account.AccountName} ({account.SteamId})");
                        continue;
                    }

                    // Clean up the account data
                    account.AccountName = account.AccountName.Trim();
                    account.PersonaName = !string.IsNullOrWhiteSpace(account.PersonaName) ? account.PersonaName.Trim() : account.AccountName;

                    seenAccountNames.Add(account.AccountName);
                    seenSteamIds.Add(account.SteamId);
                    cleanedAccounts.Add(account);
                }

                // Save the cleaned accounts
                await SaveAccountsAsync(cleanedAccounts);
                System.Diagnostics.Debug.WriteLine($"Cleaned up accounts: {accounts.Count} -> {cleanedAccounts.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cleaning up saved accounts: {ex.Message}");
            }
        }

        public string GetLogFilePath()
        {
            return _logger.GetLogFilePath();
        }

        public async Task<string> GetRecentLogContentsAsync(int lines = 50)
        {
            return await _logger.GetRecentLogContentsAsync(lines);
        }

        public async Task LaunchGameAsync(int appId)
        {
            try
            {
                var steamDirectoryPath = GetSteamPath();
                var steamExecutablePath = Path.Combine(steamDirectoryPath, "Steam.exe");
                if (!File.Exists(steamExecutablePath))
                {
                    throw new FileNotFoundException($"Steam executable not found at: {steamExecutablePath}");
                }

                // Prefer -applaunch; Steam will open install dialog if not installed
                var startInfo = new ProcessStartInfo
                {
                    FileName = steamExecutablePath,
                    Arguments = $"-applaunch {appId}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                // Fallback to steam://run/{appid}
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = $"steam://run/{appId}",
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }
                catch (Exception inner)
                {
                    _logger.LogWarning($"Failed to launch game {appId}: {ex.Message}; fallback also failed: {inner.Message}");
                    throw;
                }
            }
        }

        public async Task<HashSet<int>> GetInstalledAppIdsAsync()
        {
            var installed = new HashSet<int>();
            
            try
            {
                // Method 1: Steam Registry approach
                var registryInstalled = GetInstalledAppsFromRegistry();
                foreach (var appId in registryInstalled)
                    installed.Add(appId);
                
                _logger.LogInfo($"Registry method found {registryInstalled.Count} installed apps");
                
                // Method 2: File system scanning (backup)
                var fileSystemInstalled = await GetInstalledAppsFromFileSystemAsync();
                foreach (var appId in fileSystemInstalled)
                    installed.Add(appId);
                
                _logger.LogInfo($"File system method found {fileSystemInstalled.Count} apps");
                _logger.LogInfo($"Total unique installed apps detected: {installed.Count}");
                
                // Debug: log first few found apps
                var sample = installed.Take(10).ToList();
                if (sample.Any())
                {
                    _logger.LogInfo($"Sample installed app IDs: {string.Join(", ", sample)}");
                }
                else
                {
                    _logger.LogWarning("No installed games detected by any method!");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error detecting installed games: {ex.Message}");
            }

            return installed;
        }

        private HashSet<int> GetInstalledAppsFromRegistry()
        {
            var installed = new HashSet<int>();
            try
            {
                // Check Steam's app cache in registry
                var registryPaths = new[]
                {
                    @"SOFTWARE\WOW6432Node\Valve\Steam\Apps",
                    @"SOFTWARE\Valve\Steam\Apps"
                };

                foreach (var regPath in registryPaths)
                {
                    try
                    {
                        using var appsKey = Registry.LocalMachine.OpenSubKey(regPath);
                        if (appsKey != null)
                        {
                            foreach (var appIdStr in appsKey.GetSubKeyNames())
                            {
                                if (int.TryParse(appIdStr, out var appId))
                                {
                                    using var appKey = appsKey.OpenSubKey(appIdStr);
                                    var isInstalled = appKey?.GetValue("Installed")?.ToString();
                                    if (isInstalled == "1")
                                    {
                                        installed.Add(appId);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Registry path {regPath} failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Registry detection failed: {ex.Message}");
            }

            return installed;
        }

        private async Task<HashSet<int>> GetInstalledAppsFromFileSystemAsync()
        {
            var installed = new HashSet<int>();
            try
            {
                var steamRoot = GetSteamPath();
                var defaultSteamApps = Path.Combine(steamRoot, "steamapps");
                var libraryFoldersVdf = Path.Combine(defaultSteamApps, "libraryfolders.vdf");

                var steamAppsDirs = new List<string>();
                if (Directory.Exists(defaultSteamApps))
                {
                    steamAppsDirs.Add(defaultSteamApps);
                }

                // Parse library folders
                if (File.Exists(libraryFoldersVdf))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(libraryFoldersVdf);
                        _logger.LogInfo($"Found libraryfolders.vdf with {content.Length} characters");
                        
                        var matches = Regex.Matches(content, "\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                        _logger.LogInfo($"Found {matches.Count} library path matches");
                        
                        foreach (Match m in matches)
                        {
                            var libPath = m.Groups[1].Value;
                            libPath = Regex.Unescape(libPath);
                            libPath = libPath.Replace("/", "\\").Replace("\\\\", "\\");
                            var steamAppsPath = Path.Combine(libPath, "steamapps");
                            
                            if (Directory.Exists(steamAppsPath))
                            {
                                steamAppsDirs.Add(steamAppsPath);
                                _logger.LogInfo($"Added library: {steamAppsPath}");
                            }
                            else
                            {
                                _logger.LogWarning($"Library path doesn't exist: {steamAppsPath}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to parse libraryfolders.vdf: {ex.Message}");
                    }
                }
                else
                {
                    _logger.LogWarning($"libraryfolders.vdf not found at: {libraryFoldersVdf}");
                }

                // Scan each steamapps directory
                foreach (var saDir in steamAppsDirs.Distinct())
                {
                    try
                    {
                        var manifests = Directory.GetFiles(saDir, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
                        _logger.LogInfo($"Found {manifests.Length} manifest files in {saDir}");
                        
                        foreach (var mf in manifests)
                        {
                            var fileName = Path.GetFileName(mf);
                            var match = Regex.Match(fileName, @"appmanifest_(\d+)\.acf", RegexOptions.IgnoreCase);
                            if (match.Success && int.TryParse(match.Groups[1].Value, out var appId))
                            {
                                installed.Add(appId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Error scanning {saDir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"File system detection failed: {ex.Message}");
            }

            return installed;
        }
    }

    public class SteamConfigFile
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
    }
} 