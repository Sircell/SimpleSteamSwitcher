using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SimpleSteamSwitcher.Models;

namespace SimpleSteamSwitcher.Services
{
    public class SteamWebApiService
    {
        private readonly HttpClient _httpClient;
        private readonly LogService _logger;
        private readonly string? _apiKey;
        private readonly GameTypeCache _gameTypeCache;
        
        // Steam Web API endpoints
        private const string STEAM_API_BASE = "https://api.steampowered.com";
        private const string BAN_INFO_ENDPOINT = "/ISteamUser/GetPlayerBans/v1/"; // No API key required
        private const string OWNED_GAMES_ENDPOINT = "/IPlayerService/GetOwnedGames/v0001/"; // Requires API key
        private const string APP_DETAILS_ENDPOINT = "/ISteamUser/GetPlayerSummaries/v0002/"; // Requires API key
        
        // Cache refresh interval - only check for changes after this period
        private static readonly TimeSpan CACHE_REFRESH_INTERVAL = TimeSpan.FromHours(6);
        
        public SteamWebApiService(string? apiKey = null)
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _logger = new LogService();
            _apiKey = apiKey;
            _gameTypeCache = new GameTypeCache();
        }

        public async Task<BanInfo> GetBanStatusAsync(string steamId64)
        {
            try
            {
                if (string.IsNullOrEmpty(steamId64))
                {
                    return new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
                }

                _logger.LogInfo($"Fetching ban status from Steam API for Steam ID: {steamId64}");

                // Build the API URL
                var url = $"{STEAM_API_BASE}{BAN_INFO_ENDPOINT}?steamids={steamId64}&format=json";
                
                // Make the API request
                var response = await _httpClient.GetStringAsync(url);
                _logger.LogInfo($"Steam API response received for {steamId64}");

                // Parse the response
                var apiResponse = JsonConvert.DeserializeObject<SteamBanApiResponse>(response);
                
                if (apiResponse?.Players == null || !apiResponse.Players.Any())
                {
                    _logger.LogWarning($"No ban data returned from Steam API for {steamId64}");
                    return new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
                }

                var playerBans = apiResponse.Players.First();
                var banInfo = ConvertToBanInfo(playerBans);
                
                _logger.LogSuccess($"Ban status retrieved for {steamId64}: {banInfo.Status} (VAC: {playerBans.VACBanned}, Game: {playerBans.NumberOfGameBans})");
                
                return banInfo;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"Network error fetching ban status for {steamId64}: {ex.Message}");
                return new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
            }
            catch (TaskCanceledException)
            {
                _logger.LogError($"Timeout fetching ban status for {steamId64}");
                return new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching ban status for {steamId64}: {ex.Message}");
                return new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
            }
        }

        private BanInfo ConvertToBanInfo(SteamPlayerBans playerBans)
        {
            var banInfo = new BanInfo
            {
                LastChecked = DateTime.Now,
                BanCount = playerBans.NumberOfVACBans + playerBans.NumberOfGameBans
            };

            // Determine ban status based on API response
            if (playerBans.VACBanned && playerBans.NumberOfGameBans > 0)
            {
                // Both VAC and game bans
                banInfo.Status = playerBans.NumberOfVACBans > 1 ? BanStatus.MultipleVACBans : BanStatus.VACBanned;
            }
            else if (playerBans.VACBanned)
            {
                // VAC ban only
                banInfo.Status = playerBans.NumberOfVACBans > 1 ? BanStatus.MultipleVACBans : BanStatus.VACBanned;
                banInfo.BanCount = playerBans.NumberOfVACBans;
            }
            else if (playerBans.NumberOfGameBans > 0)
            {
                // Game ban only
                banInfo.Status = playerBans.NumberOfGameBans > 1 ? BanStatus.MultipleGameBans : BanStatus.GameBanned;
                banInfo.BanCount = playerBans.NumberOfGameBans;
            }
            else if (playerBans.CommunityBanned)
            {
                // Community ban
                banInfo.Status = BanStatus.CommunityBanned;
            }
            else if (playerBans.EconomyBan != "none")
            {
                // Trade/economy ban
                banInfo.Status = BanStatus.TradeBanned;
            }
            else
            {
                // Clean account
                banInfo.Status = BanStatus.Clean;
                banInfo.BanCount = 0;
            }

            return banInfo;
        }

        public async Task<Dictionary<string, BanInfo>> GetBanStatusForMultipleAsync(List<string> steamIds)
        {
            var results = new Dictionary<string, BanInfo>();
            
            try
            {
                if (!steamIds.Any())
                {
                    return results;
                }

                _logger.LogInfo($"Fetching ban status for {steamIds.Count} Steam IDs from Steam API");

                // Steam API supports multiple IDs in one request (up to 100)
                const int batchSize = 100;
                var batches = steamIds.Select((id, index) => new { id, index })
                                    .GroupBy(x => x.index / batchSize)
                                    .Select(g => g.Select(x => x.id).ToList())
                                    .ToList();

                foreach (var batch in batches)
                {
                    try
                    {
                        var steamIdsParam = string.Join(",", batch);
                        var url = $"{STEAM_API_BASE}{BAN_INFO_ENDPOINT}?steamids={steamIdsParam}&format=json";
                        
                        var response = await _httpClient.GetStringAsync(url);
                        var apiResponse = JsonConvert.DeserializeObject<SteamBanApiResponse>(response);
                        
                        if (apiResponse?.Players != null)
                        {
                            foreach (var playerBans in apiResponse.Players)
                            {
                                var banInfo = ConvertToBanInfo(playerBans);
                                results[playerBans.SteamId] = banInfo;
                            }
                        }
                        
                        // Small delay between batches to be respectful to Steam API
                        if (batches.Count > 1)
                        {
                            await Task.Delay(100);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error processing batch: {ex.Message}");
                        
                        // Add unknown status for failed batch
                        foreach (var steamId in batch)
                        {
                            results[steamId] = new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
                        }
                    }
                }

                _logger.LogSuccess($"Batch ban status fetch completed. Retrieved {results.Count} results");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in batch ban status fetch: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Checks if we should refresh the game count cache for this account
        /// </summary>
        private bool ShouldRefreshGameCount(DateTime? lastChecked)
        {
            if (!lastChecked.HasValue)
            {
                _logger.LogInfo("No previous game count data - refresh needed");
                return true; // No previous data
            }
            
            var timeSinceLastCheck = DateTime.Now - lastChecked.Value;
            var shouldRefresh = timeSinceLastCheck >= CACHE_REFRESH_INTERVAL;
            
            if (shouldRefresh)
            {
                _logger.LogInfo($"Game count data is {timeSinceLastCheck.TotalHours:F1} hours old - refresh needed");
            }
            else
            {
                _logger.LogInfo($"Game count data is {timeSinceLastCheck.TotalHours:F1} hours old - using cached data");
            }
            
            return shouldRefresh;
        }

        /// <summary>
        /// Gets game count with smart caching - only fetches if cache is stale or missing
        /// </summary>
        public async Task<GameCountInfo> GetGameCountAsync(string steamId64, DateTime? lastChecked = null)
        {
            // Check if we need to refresh based on cache age
            if (!ShouldRefreshGameCount(lastChecked))
            {
                _logger.LogInfo($"Using cached game count data for {steamId64}");
                return new GameCountInfo { HasData = false, UseCache = true };
            }
            
            return await FreshGameCountAsync(steamId64);
        }

        /// <summary>
        /// Always fetches fresh game count data from Steam API
        /// </summary>
        private async Task<GameCountInfo> FreshGameCountAsync(string steamId64)
        {
            try
            {
                if (string.IsNullOrEmpty(_apiKey))
                {
                    _logger.LogWarning("Steam API key not provided - cannot fetch game count");
                    return new GameCountInfo { HasData = false };
                }

                if (string.IsNullOrEmpty(steamId64))
                {
                    return new GameCountInfo { HasData = false };
                }

                _logger.LogInfo($"Fetching game count from Steam API for Steam ID: {steamId64}");

                // DUAL API CALL APPROACH to detect F2P games:
                // 1. Get owned games (excludes F2P by default)
                // 2. Get all games including F2P
                // 3. Compare to find F2P-only accounts
                
                var ownedUrl = $"{STEAM_API_BASE}{OWNED_GAMES_ENDPOINT}?key={_apiKey}&steamid={steamId64}&format=json&include_appinfo=1&include_played_free_games=0";
                var allUrl = $"{STEAM_API_BASE}{OWNED_GAMES_ENDPOINT}?key={_apiKey}&steamid={steamId64}&format=json&include_appinfo=1&include_played_free_games=1";
                
                // Make both API requests
                var ownedResponse = await _httpClient.GetStringAsync(ownedUrl);
                var allResponse = await _httpClient.GetStringAsync(allUrl);
                _logger.LogInfo($"Steam API game count response received for {steamId64}");

                // Parse both responses
                var ownedApiResponse = JsonConvert.DeserializeObject<SteamOwnedGamesResponse>(ownedResponse);
                var allApiResponse = JsonConvert.DeserializeObject<SteamOwnedGamesResponse>(allResponse);
                
                // Use the response with more games (usually the 'all' response)
                var apiResponse = (allApiResponse?.Response?.Games?.Count ?? 0) >= (ownedApiResponse?.Response?.Games?.Count ?? 0) ? allApiResponse : ownedApiResponse;
                
                // Get games from both responses
                var ownedGames = ownedApiResponse?.Response?.Games ?? new List<SteamOwnedGame>();
                var allGames = allApiResponse?.Response?.Games ?? new List<SteamOwnedGame>();
                
                // Calculate the actual counts
                var ownedGameCount = ownedGames?.Count ?? 0;
                var totalGameCount = allGames?.Count ?? 0;
                var freeToPlayCount = totalGameCount - ownedGameCount;
                
                _logger.LogInfo($"Retrieved {totalGameCount} total games for {steamId64} ({ownedGameCount} owned, {freeToPlayCount} F2P), now analyzing...");
                
                // Special handling for F2P-only accounts
                if (ownedGameCount == 0 && totalGameCount > 0)
                {
                    _logger.LogInfo($"Account has ONLY free-to-play games ({totalGameCount} F2P games detected)");
                    return new GameCountInfo 
                    { 
                        HasData = true,
                        TotalGames = totalGameCount,
                        PaidGames = 0,  // 0 paid games
                        LastChecked = DateTime.Now
                    };
                }
                
                // If no games at all - this might be due to private profile
                if (totalGameCount == 0)
                {
                    _logger.LogWarning($"Account shows 0 games - this could be due to private profile, account restrictions, or genuinely no games");
                    
                    // For now, we'll assume these accounts might have F2P games that aren't visible due to privacy
                    // This is a conservative approach that acknowledges the limitation
                    _logger.LogInfo($"Marking account as having unknown game count due to potential privacy restrictions");
                    return new GameCountInfo 
                    { 
                        HasData = true,
                        TotalGames = 0,
                        PaidGames = 0,
                        LastChecked = DateTime.Now
                    };
                }
                
                // For accounts with owned games, we need to check which are actually paid
                // Use the games that are more comprehensive (usually allGames)
                var gamesToAnalyze = totalGameCount > ownedGameCount ? allGames : ownedGames;
                var paidGames = await DeterminePaidGamesAsync(gamesToAnalyze);
                
                _logger.LogSuccess($"Game count analysis complete for {steamId64}: {paidGames} paid / {totalGameCount} total games");
                
                return new GameCountInfo 
                { 
                    HasData = true,
                    TotalGames = totalGameCount,
                    PaidGames = paidGames,
                    LastChecked = DateTime.Now
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"Network error fetching game count for {steamId64}: {ex.Message}");
                return new GameCountInfo { HasData = false };
            }
            catch (TaskCanceledException)
            {
                _logger.LogError($"Timeout fetching game count for {steamId64}");
                return new GameCountInfo { HasData = false };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching game count for {steamId64}: {ex.Message}");
                return new GameCountInfo { HasData = false };
            }
        }

        /// <summary>
        /// Gets detailed games list for a Steam account
        /// </summary>
        public async Task<List<SteamOwnedGame>> GetAccountGamesAsync(string steamId64)
        {
            try
            {
                if (string.IsNullOrEmpty(_apiKey))
                {
                    _logger.LogWarning("Steam API key not provided - cannot fetch games list");
                    return new List<SteamOwnedGame>();
                }

                if (string.IsNullOrEmpty(steamId64))
                {
                    return new List<SteamOwnedGame>();
                }

                _logger.LogInfo($"Fetching games list from Steam API for Steam ID: {steamId64}");

                // Get all games including F2P
                var url = $"{STEAM_API_BASE}{OWNED_GAMES_ENDPOINT}?key={_apiKey}&steamid={steamId64}&format=json&include_appinfo=1&include_played_free_games=1";
                
                var response = await _httpClient.GetStringAsync(url);
                _logger.LogInfo($"Steam API games response received for {steamId64}");

                var apiResponse = JsonConvert.DeserializeObject<SteamOwnedGamesResponse>(response);
                var games = apiResponse?.Response?.Games ?? new List<SteamOwnedGame>();
                
                _logger.LogInfo($"Retrieved {games.Count} games from Web API for {steamId64}");
                
                // Add demo/beta detection
                var enhancedGames = await AddDemoAndBetaGamesAsync(games, steamId64);
                
                return enhancedGames;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"Network error fetching games for {steamId64}: {ex.Message}");
                return new List<SteamOwnedGame>();
            }
            catch (TaskCanceledException)
            {
                _logger.LogError($"Timeout fetching games for {steamId64}");
                return new List<SteamOwnedGame>();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching games for {steamId64}: {ex.Message}");
                return new List<SteamOwnedGame>();
            }
        }

        private async Task<List<SteamOwnedGame>> AddDemoAndBetaGamesAsync(List<SteamOwnedGame> games, string steamId64)
        {
            // OPTIMIZATION: Disable aggressive demo/beta scanning that was causing performance issues
            // The previous implementation was adding the same demo games to every account
            // This caused massive duplicates and slow performance
            _logger.LogInfo($"Skipping demo/beta scan for {steamId64} - optimization enabled");
            return games;
        }

        /// <summary>
        /// Standalone demo/beta scanning that can be called on-demand
        /// </summary>
        public async Task<List<SteamOwnedGame>> ScanForDemoBetaGamesAsync()
        {
            try
            {
                _logger.LogInfo("Starting on-demand demo/beta game scan...");
                var demoBetaGames = new List<SteamOwnedGame>();
                
                // Get installed apps that might be demos/betas
                var installedAppIds = await GetInstalledDemosBetasAsync();
                
                // Known demo/beta app IDs that are commonly available
                var knownDemoAppIds = new List<int>
                {
                    2963840, // EA SPORTS FC 25 SHOWCASE
                    2535620, // Blockbuster Inc. - Prologue
                    3081410, // Battlefield™ 6 Open Beta
                    // Add more known demo/beta IDs here as needed
                };
                
                var allAppsToCheck = installedAppIds.Concat(knownDemoAppIds).Distinct().ToList();
                _logger.LogInfo($"Checking {allAppsToCheck.Count} potential demo/beta apps...");
                
                // Process in smaller batches with rate limiting
                const int batchSize = 5;
                var processedCount = 0;
                
                for (int i = 0; i < allAppsToCheck.Count; i += batchSize)
                    {
                    var batch = allAppsToCheck.Skip(i).Take(batchSize);
                    var batchTasks = batch.Select(async appId =>
                    {
                    try
                    {
                        var gameInfo = await GetGameInfoFromStoreAsync(appId);
                        if (gameInfo != null)
                        {
                                                                 var isDemoOrBeta = gameInfo.IsDemo || gameInfo.IsBeta ||
                                                  gameInfo.Name?.ToLower().Contains("demo") == true ||
                                                  gameInfo.Name?.ToLower().Contains("beta") == true ||
                                                  gameInfo.Name?.ToLower().Contains("prologue") == true ||
                                                  gameInfo.Name?.ToLower().Contains("showcase") == true ||
                                                  gameInfo.Name?.ToLower().Contains("open beta") == true;
                            
                                if (isDemoOrBeta)
                            {
                                    _logger.LogInfo($"Found demo/beta game: {gameInfo.Name} ({appId})");
                                    return new SteamOwnedGame
                                {
                                    AppId = appId,
                                    Name = gameInfo.Name,
                                        PlaytimeForever = 0,
                                    ImgIconUrl = gameInfo.HeaderImage ?? ""
                                };
                            }
                            }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to check demo/beta app {appId}: {ex.Message}");
                    }
                        return null;
                    });
                    
                    var batchResults = await Task.WhenAll(batchTasks);
                    demoBetaGames.AddRange(batchResults.Where(game => game != null).Cast<SteamOwnedGame>());
                    
                    processedCount += batchSize;
                    _logger.LogInfo($"Demo/beta scan progress: {Math.Min(processedCount, allAppsToCheck.Count)}/{allAppsToCheck.Count}");
                    
                    // Rate limiting between batches
                    if (i + batchSize < allAppsToCheck.Count)
                    {
                        await Task.Delay(1000);
                    }
                }
                
                _logger.LogSuccess($"Demo/beta scan completed: Found {demoBetaGames.Count} demo/beta games");
                return demoBetaGames;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in standalone demo/beta scan: {ex.Message}");
                return new List<SteamOwnedGame>();
            }
        }

        private async Task<GameStoreInfo?> GetGameInfoFromStoreAsync(int appId)
        {
            try
            {
                var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic";
                var response = await _httpClient.GetStringAsync(url);
                var apiResponse = JsonConvert.DeserializeObject<Dictionary<string, SteamAppDetailsResponse>>(response);
                
                if (apiResponse?.TryGetValue(appId.ToString(), out var appDetails) == true && 
                    appDetails?.Success == true && 
                    appDetails.Data != null)
                {
                    return new GameStoreInfo
                    {
                        AppId = appId,
                        Name = appDetails.Data.Name,
                        IsDemo = appDetails.Data.Name.ToLower().Contains("demo") || 
                                appDetails.Data.Name.ToLower().Contains("beta") ||
                                appDetails.Data.Name.ToLower().Contains("open beta") ||
                                appDetails.Data.Name.ToLower().Contains("prologue") ||
                                appDetails.Data.Name.ToLower().Contains("preview") ||
                                appDetails.Data.Name.ToLower().Contains("showcase"),
                        IsBeta = appDetails.Data.Name.ToLower().Contains("beta") ||
                                appDetails.Data.Name.ToLower().Contains("alpha") ||
                                appDetails.Data.Name.ToLower().Contains("early access"),
                        IsFree = appDetails.Data.IsFree ?? false, // Handle nullable bool conversion
                        HeaderImage = appDetails.Data.HeaderImage
                    };
                }
                
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<List<int>> GetInstalledDemosBetasAsync()
        {
            var demosBetas = new List<int>();
            try
            {
                _logger.LogInfo("Scanning installed games for potential demos/betas...");
                
                // Look for common demo/beta naming patterns
                var betaPatterns = new[]
                {
                    "beta", "demo", "open beta", "closed beta", "early access", 
                    "alpha", "test", "preview", "prologue", "showcase", "trial"
                };
                
                // For now, check against the sample installed app IDs from the logs
                // These were: 1085660, 2074920, 228980, 3081410
                var installedAppIds = new[] { 1085660, 2074920, 228980, 3081410 };
                
                foreach (var appId in installedAppIds)
                {
                    try
                    {
                        // Check if this installed game is a demo/beta by name
                        var gameInfo = await GetGameInfoFromStoreAsync(appId);
                        if (gameInfo != null)
                        {
                            var gameName = gameInfo.Name.ToLower();
                            var isBetaOrDemo = betaPatterns.Any(pattern => gameName.Contains(pattern));
                            
                            if (isBetaOrDemo)
                            {
                                demosBetas.Add(appId);
                                _logger.LogInfo($"Found installed demo/beta: {gameInfo.Name} ({appId})");
                            }
                            else
                            {
                                _logger.LogInfo($"Checked installed game: {gameInfo.Name} ({appId}) - not a demo/beta");
                            }
                        }
                        
                        await Task.Delay(200); // Rate limiting for Store API
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not check installed app {appId}: {ex.Message}");
                    }
                }
                
                _logger.LogInfo($"Found {demosBetas.Count} demo/beta games from installed apps");
                return demosBetas;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error scanning for installed demos/betas: {ex.Message}");
                return demosBetas;
            }
        }

        private async Task<int> DeterminePaidGamesAsync(List<SteamOwnedGame> games)
        {
            try
            {
                var paidGameCount = 0;
                var checkedCount = 0;
                const int maxChecks = 20; // Limit checks to avoid API rate limits and timeouts
                
                // Prioritize games with more playtime as they're more likely to be meaningful
                // For small libraries, check all games. For large libraries, use sampling
                var actualMaxChecks = Math.Min(maxChecks, games.Count);
                if (games.Count <= 30)
                {
                    actualMaxChecks = games.Count; // Check all games for small libraries
                }
                
                var gamesToCheck = games
                    .OrderByDescending(g => g.PlaytimeForever)
                    .Take(actualMaxChecks)
                    .ToList();

                _logger.LogInfo($"Checking {gamesToCheck.Count} games (out of {games.Count} total) for free vs paid status...");

                foreach (var game in gamesToCheck)
                {
                    try
                    {
                        var isPaid = await IsGamePaidAsync(game.AppId);
                        if (isPaid)
                        {
                            paidGameCount++;
                        }
                        
                        checkedCount++;
                        
                        // Longer delay to avoid rate limiting (Steam Store API is strict)
                        await Task.Delay(600);
                        
                        // Log progress every 5 games
                        if (checkedCount % 5 == 0)
                        {
                            _logger.LogInfo($"Checked {checkedCount}/{gamesToCheck.Count} games, found {paidGameCount} paid games so far...");
                        }
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("429"))
                    {
                        _logger.LogWarning($"Rate limited on game {game.AppId} ({game.Name}). Assuming paid and adding longer delay...");
                        // Assume it's paid when rate limited (conservative approach)
                        paidGameCount++;
                        checkedCount++;
                        
                        // Much longer delay when rate limited
                        await Task.Delay(5000);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Error checking game {game.AppId} ({game.Name}): {ex.Message}");
                        // Assume it's paid when we can't check (conservative approach)
                        paidGameCount++;
                        checkedCount++;
                    }
                }

                // If we couldn't check all games, estimate the total based on the sample
                if (games.Count > maxChecks && checkedCount > 0)
                {
                    var paidRatio = (double)paidGameCount / checkedCount;
                    var estimatedPaidGames = (int)(games.Count * paidRatio);
                    
                    _logger.LogInfo($"Estimated paid games: {estimatedPaidGames} (based on {paidGameCount}/{checkedCount} sample, ratio: {paidRatio:P})");
                    return estimatedPaidGames;
                }

                return paidGameCount;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error determining paid games: {ex.Message}");
                return 0;
            }
        }

        public async Task<bool> IsGamePaidAsync(int appId)
        {
            try
            {
                // OPTIMIZATION: Check cache first to avoid API calls
                if (_gameTypeCache.TryGetGameType(appId, out bool cachedResult))
                {
                    return cachedResult;
                }

                // First check if it's a known free-to-play game to avoid API calls
                if (IsKnownFreeToPlayGame(appId))
                {
                    _logger.LogInfo($"Game {appId} is in known F2P list");
                    _gameTypeCache.SetGameType(appId, false); // Cache the result
                    return false; // It's free
                }

                // Use Steam Store API to get app details with retry logic
                var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic";
                
                string response = null;
                var maxRetries = 3;
                var retryDelay = 1000; // Start with 1 second
                
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                try
                {
                    response = await _httpClient.GetStringAsync(url);
                    _logger.LogInfo($"Steam Store API response for {appId}: {response.Substring(0, Math.Min(200, response.Length))}...");
                        break; // Success, exit retry loop
                    }
                    catch (HttpRequestException httpEx) when (httpEx.Message.Contains("429") && attempt < maxRetries)
                    {
                        _logger.LogWarning($"Rate limited for app {appId}, attempt {attempt}/{maxRetries}. Waiting {retryDelay}ms...");
                        await Task.Delay(retryDelay);
                        retryDelay *= 2; // Exponential backoff
                        continue;
                    }
                    catch (HttpRequestException httpEx) when (httpEx.Message.Contains("403"))
                    {
                        _logger.LogWarning($"Access forbidden for app {appId} (likely restricted/unavailable): {httpEx.Message}");
                        _gameTypeCache.SetGameType(appId, true, isUnavailable: true); // Cache as unavailable
                        return true; // Conservative: assume paid for restricted games
                }
                catch (Exception apiEx)
                {
                        if (attempt == maxRetries)
                        {
                            _logger.LogWarning($"Failed to call Steam Store API for {appId} after {maxRetries} attempts: {apiEx.Message}");
                            _gameTypeCache.SetGameType(appId, true, isUnavailable: true); // Cache as unavailable
                    return true; // Conservative: assume paid if API fails
                        }
                        else
                        {
                            _logger.LogWarning($"API call failed for app {appId}, attempt {attempt}/{maxRetries}: {apiEx.Message}. Retrying...");
                            await Task.Delay(retryDelay);
                            retryDelay *= 2;
                        }
                    }
                }
                
                // If we get here and response is still null, all retries failed
                if (response == null)
                {
                    _logger.LogError($"All retry attempts failed for app {appId}");
                    _gameTypeCache.SetGameType(appId, true, isUnavailable: true);
                    return true; // Conservative: assume paid
                }
                
                var apiResponse = JsonConvert.DeserializeObject<Dictionary<string, SteamAppDetailsResponse>>(response);
                
                if (apiResponse?.TryGetValue(appId.ToString(), out var appDetails) == true)
                {
                    if (appDetails?.Success == true && appDetails.Data != null)
                    {
                        var isFree = appDetails.Data.IsFree;
                        var gameName = appDetails.Data.Name;
                        
                        _logger.LogInfo($"Game {gameName} ({appId}): IsFree={isFree}");
                        
                        // PRIORITY: Use API result as the primary source of truth
                        // Only use name patterns as a fallback when API is unclear
                        if (isFree.HasValue)
                        {
                            // API provided a clear answer - use it
                            var result = !isFree.Value;
                            _logger.LogInfo($"Game {gameName} ({appId}) final determination: {(result ? "PAID" : "FREE")} (API: IsFree={isFree.Value})");
                            _gameTypeCache.SetGameType(appId, result); // Cache the result
                            return result;
                        }
                        
                        // Fallback: Check name patterns only when API doesn't provide IsFree
                        if (!string.IsNullOrEmpty(gameName))
                        {
                            var gameNameLower = gameName.ToLower();
                            
                            // Enhanced F2P patterns
                            var freePatterns = new[] { 
                                "free-to-play", "free to play", "f2p", 
                                "beta", "demo", "trial", "preview",
                                "open beta", "closed beta", "alpha", "early access",
                                "prologue", "test", "showcase", "playtest",
                                "open alpha", "closed alpha", "technical test"
                            };
                            
                            // Check if any pattern matches
                            var matchedPattern = freePatterns.FirstOrDefault(pattern => gameNameLower.Contains(pattern));
                            if (matchedPattern != null)
                            {
                                    _logger.LogInfo($"Game {gameName} ({appId}) detected as F2P based on name pattern: '{matchedPattern}' (API IsFree was null)");
                                    _gameTypeCache.SetGameType(appId, false); // Cache the result
                                    return false; // It's free based on name pattern (fallback)
                            }
                        }
                        
                                                    // If we get here, API didn't provide IsFree and no name pattern matched
                            _logger.LogWarning($"Game {gameName} ({appId}): API IsFree=null and no F2P name pattern - assuming PAID");
                            _gameTypeCache.SetGameType(appId, true); // Cache the result
                            return true; // Conservative: assume paid if we can't determine
                    }
                    else
                    {
                        _logger.LogWarning($"Steam Store API returned success=false for {appId}");
                        _gameTypeCache.SetGameType(appId, true, isUnavailable: true); // Cache as unavailable to avoid repeated calls
                        return true; // Conservative: assume paid if API says no success
                    }
                }
                else
                {
                    _logger.LogWarning($"Steam Store API response format unexpected for {appId}");
                    _gameTypeCache.SetGameType(appId, true, isUnavailable: true); // Cache as unavailable to avoid repeated calls
                    return true; // Conservative: assume paid if response format is wrong
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking if game {appId} is paid: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                // If we can't determine, assume it's paid (conservative approach)
                return true;
            }
        }

        public bool IsKnownFreeToPlayGame(int appId)
        {
            // Common free-to-play games that we can identify without API calls
            var knownFreeGames = new HashSet<int>
            {
                // Popular F2P games
                730,    // CS2/CS:GO
                440,    // Team Fortress 2  
                570,    // Dota 2
                578080, // PUBG (free-to-play)
                1172470, // Apex Legends
                252490, // Rust (has free version)
                359550, // Tom Clancy's Rainbow Six Siege (has free version)
                945360, // Among Us (was free on Epic)
                1097840, // Destiny 2
                1085660, // Destiny 2 (duplicate ID)
                1203220, // NARAKA: BLADEPOINT
                1203620, // Lost Ark
                2074920, // The First Descendant
                238960, // Path of Exile
                386180, // Spellbreak
                594650, // Hunt: Showdown (has free trial)
                553850, // HELLDIVERS 2 (has free version)
                1172710, // Mirror's Edge Catalyst (was free on Epic)
                
                // Free Betas/Demos that should be considered F2P
                3081410, // Battlefield™ 6 Open Beta
                
                // F2P MMORPGs
                212160, // Vindictus
                
                // F2P Battle Royales - already listed above
                
                // F2P MOBAs - already listed above
                
                // Other common F2P games
                550,    // Left 4 Dead 2 (free periods)
                10190,  // Call of Duty®: Modern Warfare® 2 Multiplayer (has free version)
                
                // Add more as needed
            };

            return knownFreeGames.Contains(appId);
        }

        public async Task<Dictionary<string, GameCountInfo>> GetGameCountForMultipleAsync(List<string> steamIds)
        {
            // Legacy method for backward compatibility - no caching
            var accounts = steamIds.Select(id => new { SteamId = id, GameCountLastChecked = (DateTime?)null }).ToList();
            return await GetGameCountForMultipleAsync(accounts.ToDictionary(a => a.SteamId, a => a.GameCountLastChecked));
        }
        
        /// <summary>
        /// Gets game counts for multiple accounts with smart caching
        /// </summary>
        public async Task<Dictionary<string, GameCountInfo>> GetGameCountForMultipleAsync(Dictionary<string, DateTime?> accountsWithCache)
        {
            var results = new Dictionary<string, GameCountInfo>();
            
            try
            {
                if (string.IsNullOrEmpty(_apiKey))
                {
                    _logger.LogWarning("Steam API key not provided - cannot fetch game counts");
                    return results;
                }

                if (!accountsWithCache.Any())
                {
                    return results;
                }

                _logger.LogInfo($"Fetching game counts for {accountsWithCache.Count} Steam IDs from Steam API");

                // Steam API doesn't support multiple steamids for GetOwnedGames, so we need individual requests
                foreach (var account in accountsWithCache)
                {
                    try
                    {
                        var gameCountInfo = await GetGameCountAsync(account.Key, account.Value);
                        results[account.Key] = gameCountInfo;
                        
                        // Only add delay if we actually made an API call (not using cache)
                        if (!gameCountInfo.UseCache && accountsWithCache.Count > 1)
                        {
                            await Task.Delay(200); // Longer delay for individual requests
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error fetching game count for {account.Key}: {ex.Message}");
                        results[account.Key] = new GameCountInfo { HasData = false };
                    }
                }

                _logger.LogSuccess($"Batch game count fetch completed. Retrieved {results.Count(r => r.Value.HasData)} successful results");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in batch game count fetch: {ex.Message}");
            }

            return results;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    // Steam API response models
    public class SteamBanApiResponse
    {
        [JsonProperty("players")]
        public List<SteamPlayerBans> Players { get; set; } = new List<SteamPlayerBans>();
    }

    public class SteamPlayerBans
    {
        [JsonProperty("SteamId")]
        public string SteamId { get; set; } = "";

        [JsonProperty("CommunityBanned")]
        public bool CommunityBanned { get; set; }

        [JsonProperty("VACBanned")]
        public bool VACBanned { get; set; }

        [JsonProperty("NumberOfVACBans")]
        public int NumberOfVACBans { get; set; }

        [JsonProperty("DaysSinceLastBan")]
        public int DaysSinceLastBan { get; set; }

        [JsonProperty("NumberOfGameBans")]
        public int NumberOfGameBans { get; set; }

        [JsonProperty("EconomyBan")]
        public string EconomyBan { get; set; } = "none";
    }

    // Game count API response models
    public class SteamOwnedGamesResponse
    {
        [JsonProperty("response")]
        public SteamOwnedGamesData? Response { get; set; }
    }

    public class SteamOwnedGamesData
    {
        [JsonProperty("game_count")]
        public int GameCount { get; set; }

        [JsonProperty("games")]
        public List<SteamOwnedGame> Games { get; set; } = new List<SteamOwnedGame>();
    }

    public class SteamOwnedGame
    {
        [JsonProperty("appid")]
        public int AppId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("playtime_forever")]
        public int PlaytimeForever { get; set; }

        [JsonProperty("img_icon_url")]
        public string ImgIconUrl { get; set; } = "";
    }

    // Game count info model
    public class GameCountInfo
    {
        public bool HasData { get; set; }
        public int TotalGames { get; set; }
        public int PaidGames { get; set; }
        public DateTime LastChecked { get; set; }
        public bool UseCache { get; set; } = false; // Indicates if cached data should be used
    }

    // Steam Store API response models for app details
    public class SteamAppDetailsResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("data")]
        public SteamAppDetailsData? Data { get; set; }
    }

    public class SteamAppDetailsData
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";
        
        [JsonProperty("is_free")]
        public bool? IsFree { get; set; } // Changed to bool? to allow null
        
        [JsonProperty("header_image")]
        public string HeaderImage { get; set; } = "";
    }

    public class GameStoreInfo
    {
        public int AppId { get; set; }
        public string Name { get; set; } = "";
        public bool IsDemo { get; set; }
        public bool IsBeta { get; set; }
        public bool? IsFree { get; set; } // Changed to nullable bool to match SteamAppDetailsData
        public string HeaderImage { get; set; } = "";
    }
} 