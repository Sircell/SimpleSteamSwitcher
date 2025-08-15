using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SimpleSteamSwitcher.Models;

namespace SimpleSteamSwitcher.Services
{
    public class BanStatusService
    {
        private readonly string _steamPath;
        private readonly LogService _logger;
        
        // Known VAC/ban status patterns from Steam profile HTML
        private readonly Dictionary<string, BanStatus> _banPatterns = new()
        {
            { @"(\d+)\s+VAC\s+bans?\s+on\s+record", BanStatus.VACBanned },
            { @"Multiple\s+VAC\s+bans\s+on\s+record", BanStatus.MultipleVACBans },
            { @"No\s+VAC\s+bans\s+on\s+record", BanStatus.Clean },
            { @"(\d+)\s+game\s+bans?\s+on\s+record", BanStatus.GameBanned },
            { @"Multiple\s+game\s+bans\s+on\s+record", BanStatus.MultipleGameBans },
            { @"No\s+game\s+bans\s+on\s+record", BanStatus.Clean },
            { @"VAC\s+banned", BanStatus.VACBanned },
            { @"Game\s+banned", BanStatus.GameBanned },
            { @"Community\s+banned", BanStatus.CommunityBanned },
            { @"Trade\s+banned", BanStatus.TradeBanned }
        };

        public BanStatusService(string steamPath)
        {
            _steamPath = steamPath;
            _logger = new LogService();
        }

        public async Task<BanInfo> GetBanStatusAsync(SteamAccount account)
        {
            try
            {
                if (string.IsNullOrEmpty(account.SteamId))
                {
                    _logger.LogInfo($"No Steam ID for account {account.AccountName}, cannot check ban status");
                    return new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
                }

                _logger.LogInfo($"Checking ban status for {account.DisplayName} (ID: {account.SteamId})");

                // Try multiple methods to get ban status
                var banInfo = await CheckHtmlCacheAsync(account.SteamId) ??
                             await CheckLocalConfigAsync(account.SteamId) ??
                             new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };

                banInfo.LastChecked = DateTime.Now;
                _logger.LogInfo($"Ban status for {account.DisplayName}: {banInfo.Status}");
                return banInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking ban status for {account.DisplayName}: {ex.Message}");
                return new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
            }
        }

        private async Task<BanInfo?> CheckHtmlCacheAsync(string steamId)
        {
            try
            {
                // Check Steam's HTML cache directories
                var cachePaths = new[]
                {
                    Path.Combine(_steamPath, "config", "htmlcache"),
                    Path.Combine(_steamPath, "cache"),
                    Path.Combine(_steamPath, "config", "cache")
                };

                foreach (var cachePath in cachePaths)
                {
                    if (!Directory.Exists(cachePath)) continue;

                    _logger.LogInfo($"Searching cache directory: {cachePath}");

                    // Look for cached profile HTML files
                    var htmlFiles = Directory.GetFiles(cachePath, "*.html", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(cachePath, "*.htm", SearchOption.AllDirectories))
                        .Concat(Directory.GetFiles(cachePath, "*", SearchOption.AllDirectories)
                            .Where(f => Path.GetExtension(f) == "" && File.Exists(f)));

                    foreach (var htmlFile in htmlFiles)
                    {
                        try
                        {
                            var content = await File.ReadAllTextAsync(htmlFile);
                            
                            // Check if this file contains our Steam ID (profile page)
                            if (content.Contains(steamId) || content.Contains($"profiles/{steamId}"))
                            {
                                _logger.LogInfo($"Found potential profile cache for {steamId} in: {Path.GetFileName(htmlFile)}");
                                
                                var banInfo = ParseBanStatusFromHtml(content);
                                if (banInfo != null)
                                {
                                    _logger.LogSuccess($"Extracted ban status from HTML cache: {banInfo.Status}");
                                    return banInfo;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Skip files we can't read
                            _logger.LogInfo($"Could not read cache file {Path.GetFileName(htmlFile)}: {ex.Message}");
                        }
                    }
                }

                _logger.LogInfo($"No HTML cache found for Steam ID: {steamId}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking HTML cache: {ex.Message}");
                return null;
            }
        }

        private async Task<BanInfo?> CheckLocalConfigAsync(string steamId)
        {
            try
            {
                // Check localconfig.vdf for cached ban status
                var userDataPath = Path.Combine(_steamPath, "userdata", steamId, "config", "localconfig.vdf");
                
                if (!File.Exists(userDataPath))
                {
                    _logger.LogInfo($"No localconfig.vdf found for Steam ID: {steamId}");
                    return null;
                }

                var content = await File.ReadAllTextAsync(userDataPath);
                
                // Look for ban-related entries in the config
                if (content.Contains("VAC") || content.Contains("ban") || content.Contains("Ban"))
                {
                    var banInfo = ParseBanStatusFromConfig(content);
                    if (banInfo != null)
                    {
                        _logger.LogInfo($"Found ban status in localconfig.vdf: {banInfo.Status}");
                        return banInfo;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking localconfig.vdf: {ex.Message}");
                return null;
            }
        }

        private BanInfo? ParseBanStatusFromHtml(string htmlContent)
        {
            try
            {
                // Remove HTML tags and normalize whitespace for better pattern matching
                var text = Regex.Replace(htmlContent, @"<[^>]+>", " ");
                text = Regex.Replace(text, @"\s+", " ");

                var banInfo = new BanInfo { Status = BanStatus.Unknown };
                var foundAnyBan = false;

                foreach (var pattern in _banPatterns)
                {
                    var matches = Regex.Matches(text, pattern.Key, RegexOptions.IgnoreCase);
                    if (matches.Count > 0)
                    {
                        foundAnyBan = true;
                        var banStatus = pattern.Value;
                        
                        // Extract ban count if available
                        var match = matches[0];
                        if (match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out int banCount))
                        {
                            banInfo.BanCount = banCount;
                        }

                        // Prioritize more severe bans
                        if (banInfo.Status == BanStatus.Unknown || 
                            IsSevereBan(banStatus) && !IsSevereBan(banInfo.Status))
                        {
                            banInfo.Status = banStatus;
                        }

                        _logger.LogInfo($"Found ban pattern: {match.Value} -> {banStatus}");
                    }
                }

                // If we found clean status and no other bans, account is clean
                if (!foundAnyBan)
                {
                    return null; // No ban information found
                }

                return banInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing HTML content: {ex.Message}");
                return null;
            }
        }

        private BanInfo? ParseBanStatusFromConfig(string configContent)
        {
            try
            {
                // Look for ban-related entries in VDF format
                // This is a simplified parser - Steam's VDF format can be complex
                var lines = configContent.Split('\n');
                
                foreach (var line in lines)
                {
                    if (line.ToLower().Contains("vac") && line.Contains("1"))
                    {
                        return new BanInfo { Status = BanStatus.VACBanned };
                    }
                    if (line.ToLower().Contains("gameban") && line.Contains("1"))
                    {
                        return new BanInfo { Status = BanStatus.GameBanned };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing config content: {ex.Message}");
                return null;
            }
        }

        private bool IsSevereBan(BanStatus status)
        {
            return status == BanStatus.VACBanned || 
                   status == BanStatus.MultipleVACBans || 
                   status == BanStatus.GameBanned || 
                   status == BanStatus.MultipleGameBans ||
                   status == BanStatus.CommunityBanned;
        }

        public async Task<Dictionary<string, BanInfo>> GetBanStatusForMultipleAccountsAsync(List<SteamAccount> accounts)
        {
            var results = new Dictionary<string, BanInfo>();
            
            foreach (var account in accounts.Where(a => !string.IsNullOrEmpty(a.SteamId)))
            {
                try
                {
                    var banInfo = await GetBanStatusAsync(account);
                    results[account.SteamId] = banInfo;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error getting ban status for {account.DisplayName}: {ex.Message}");
                    results[account.SteamId] = new BanInfo { Status = BanStatus.Unknown, LastChecked = DateTime.Now };
                }
            }

            return results;
        }
    }

    public class BanInfo
    {
        public BanStatus Status { get; set; } = BanStatus.Unknown;
        public int BanCount { get; set; } = 0;
        public DateTime LastChecked { get; set; } = DateTime.Now;
        public string Description => GetStatusDescription();

        private string GetStatusDescription()
        {
            return Status switch
            {
                BanStatus.Clean => "No bans on record",
                BanStatus.VACBanned => BanCount > 1 ? $"{BanCount} VAC bans" : "VAC banned",
                BanStatus.MultipleVACBans => "Multiple VAC bans",
                BanStatus.GameBanned => BanCount > 1 ? $"{BanCount} game bans" : "Game banned",
                BanStatus.MultipleGameBans => "Multiple game bans",
                BanStatus.CommunityBanned => "Community banned",
                BanStatus.TradeBanned => "Trade banned",
                BanStatus.Unknown => "Ban status unknown",
                _ => "Unknown status"
            };
        }
    }

    public enum BanStatus
    {
        Unknown,
        Clean,
        VACBanned,
        MultipleVACBans,
        GameBanned,
        MultipleGameBans,
        CommunityBanned,
        TradeBanned
    }
} 