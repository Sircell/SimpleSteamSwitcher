using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SimpleSteamSwitcher.Models;
using System.Linq;

namespace SimpleSteamSwitcher.Services
{
    public class SimpleBanDetectionService
    {
        private readonly HttpClient _httpClient;
        private readonly LogService _logger;
        
        public SimpleBanDetectionService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
            _logger = new LogService();
        }

        public async Task<BanInfo> GetBanStatusAsync(string steamId64)
        {
            try
            {
                if (string.IsNullOrEmpty(steamId64))
                {
                    return new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
                }

                _logger.LogInfo($"Checking ban status for Steam ID: {steamId64}");

                // Use Steam community profile URL
                var profileUrl = $"https://steamcommunity.com/profiles/{steamId64}";
                
                try
                {
                    var response = await _httpClient.GetStringAsync(profileUrl);
                    var banInfo = ParseBanStatusFromProfile(response);
                    banInfo.LastChecked = DateTime.Now;
                    
                    _logger.LogSuccess($"Ban status retrieved for {steamId64}: {banInfo.Status}");
                    return banInfo;
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("404"))
                {
                    _logger.LogWarning($"Steam profile not found for {steamId64} (404)");
                    return new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error fetching profile for {steamId64}: {ex.Message}");
                    
                    // Fallback: assume clean if we can't determine
                    return new BanInfo { Status = BanStatus.Clean, LastChecked = DateTime.Now };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in ban detection for {steamId64}: {ex.Message}");
                return new BanInfo { Status = BanStatus.Clean, LastChecked = DateTime.Now };
            }
        }

        private BanInfo ParseBanStatusFromProfile(string htmlContent)
        {
            var banInfo = new BanInfo
            {
                Status = BanStatus.Clean,
                BanCount = 0
            };

            try
            {
                // Look for common ban indicators in Steam profile HTML
                var banPatterns = new Dictionary<string, BanStatus>
                {
                    { @"(\d+)\s+VAC\s+ban[s]?\s+on\s+record", BanStatus.VACBanned },
                    { @"(\d+)\s+game\s+ban[s]?\s+on\s+record", BanStatus.GameBanned },
                    { @"Multiple\s+VAC\s+bans\s+on\s+record", BanStatus.MultipleVACBans },
                    { @"Multiple\s+game\s+bans\s+on\s+record", BanStatus.MultipleGameBans },
                    { @"Community\s+banned", BanStatus.CommunityBanned },
                    { @"Trade\s+banned", BanStatus.TradeBanned },
                    { @"VAC\s+banned", BanStatus.VACBanned },
                    { @"Game\s+banned", BanStatus.GameBanned }
                };

                // Check for private profile
                if (htmlContent.Contains("This profile is private") || 
                    htmlContent.Contains("profile_private_info"))
                {
                    _logger.LogInfo("Profile is private, assuming clean");
                    return banInfo;
                }

                // Look for ban text patterns
                foreach (var pattern in banPatterns)
                {
                    var regex = new Regex(pattern.Key, RegexOptions.IgnoreCase);
                    var match = regex.Match(htmlContent);
                    
                    if (match.Success)
                    {
                        banInfo.Status = pattern.Value;
                        
                        // Try to extract ban count if available
                        if (match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out int count))
                        {
                            banInfo.BanCount = count;
                        }
                        else
                        {
                            banInfo.BanCount = 1;
                        }
                        
                        _logger.LogInfo($"Ban detected: {banInfo.Status} (Count: {banInfo.BanCount})");
                        break; // Stop at first match
                    }
                }

                // If no bans found, it's clean
                if (banInfo.Status == BanStatus.Clean)
                {
                    _logger.LogInfo("No ban indicators found - profile appears clean");
                }

                return banInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing ban status from profile: {ex.Message}");
                return new BanInfo { Status = BanStatus.Clean, BanCount = 0 };
            }
        }

        public async Task<Dictionary<string, BanInfo>> GetBanStatusForMultipleAsync(List<string> steamIds)
        {
            var results = new Dictionary<string, BanInfo>();
            
            try
            {
                _logger.LogInfo($"Checking ban status for {steamIds.Count} Steam IDs via profile scraping");

                // Process accounts with small delays to avoid rate limiting
                foreach (var steamId in steamIds)
                {
                    try
                    {
                        var banInfo = await GetBanStatusAsync(steamId);
                        results[steamId] = banInfo;
                        
                        // Small delay between requests to be respectful
                        if (steamIds.Count > 1)
                        {
                            await Task.Delay(500); // 500ms delay between requests
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error checking {steamId}: {ex.Message}");
                        results[steamId] = new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
                    }
                }

                _logger.LogSuccess($"Profile scraping completed. Retrieved {results.Count} results");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in batch profile checking: {ex.Message}");
            }

            return results;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
} 