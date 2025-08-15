using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SimpleSteamSwitcher.Models;

namespace SimpleSteamSwitcher.Services
{
    public class GameCacheService
    {
        private readonly LogService _logger;
        private readonly string _cacheFilePath;
        private const string CACHE_FILENAME = "games_cache.json";
        
        public GameCacheService(LogService logger)
        {
            _logger = logger;
            
            // Store cache in the same directory as accounts
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleSteamSwitcher");
            Directory.CreateDirectory(appDataPath);
            _cacheFilePath = Path.Combine(appDataPath, CACHE_FILENAME);
        }
        
        /// <summary>
        /// Load cached games from file
        /// </summary>
        public async Task<GameCache?> LoadCacheAsync()
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                {
                    _logger.LogInfo("No game cache file found - will fetch fresh data");
                    return null;
                }
                
                _logger.LogInfo($"Loading game cache from: {_cacheFilePath}");
                var jsonContent = await File.ReadAllTextAsync(_cacheFilePath);
                
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    _logger.LogWarning("Game cache file is empty");
                    return null;
                }
                
                var cache = JsonConvert.DeserializeObject<GameCache>(jsonContent);
                
                if (cache == null)
                {
                    _logger.LogWarning("Failed to deserialize game cache");
                    return null;
                }
                
                _logger.LogInfo($"Loaded {cache.Games.Count} games from cache (Last updated: {cache.LastUpdated})");
                
                if (cache.IsExpired)
                {
                    _logger.LogInfo($"Game cache is expired (older than {cache.CacheValidDuration.TotalHours} hours) - will fetch fresh data");
                    return null;
                }
                
                _logger.LogSuccess($"Using valid game cache with {cache.Games.Count} games");
                return cache;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading game cache: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Save games to cache file
        /// </summary>
        public async Task SaveCacheAsync(List<Game> games)
        {
            try
            {
                _logger.LogInfo($"Saving {games.Count} games to cache...");
                _logger.LogInfo($"CACHE DEBUG: Cache file path: {_cacheFilePath}");
                _logger.LogInfo($"CACHE DEBUG: Directory exists: {Directory.Exists(Path.GetDirectoryName(_cacheFilePath))}");
                
                var cache = new GameCache
                {
                    LastUpdated = DateTime.Now,
                    Games = games.Select(CachedGame.FromGame).ToList()
                };
                
                _logger.LogInfo($"CACHE DEBUG: Created GameCache object with {cache.Games.Count} games");
                
                var jsonContent = JsonConvert.SerializeObject(cache, Formatting.Indented);
                _logger.LogInfo($"CACHE DEBUG: Serialized JSON content length: {jsonContent.Length} characters");
                
                await File.WriteAllTextAsync(_cacheFilePath, jsonContent);
                _logger.LogInfo($"CACHE DEBUG: File written successfully, checking if file exists: {File.Exists(_cacheFilePath)}");
                
                if (File.Exists(_cacheFilePath))
                {
                    var fileInfo = new FileInfo(_cacheFilePath);
                    _logger.LogInfo($"CACHE DEBUG: File size: {fileInfo.Length} bytes, last modified: {fileInfo.LastWriteTime}");
                }
                
                _logger.LogSuccess($"Game cache saved successfully to: {_cacheFilePath}");
                _logger.LogInfo($"Cache contains {cache.Games.Count} games and will expire on {cache.LastUpdated.Add(cache.CacheValidDuration)}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving game cache: {ex.Message}");
                _logger.LogError($"CACHE DEBUG: SaveCacheAsync exception details: {ex}");
                _logger.LogError($"CACHE DEBUG: Stack trace: {ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Update installed status for all cached games
        /// </summary>
        public List<Game> UpdateInstalledStatus(List<Game> games, HashSet<int> installedAppIds)
        {
            try
            {
                _logger.LogInfo($"Updating installed status for {games.Count} games with {installedAppIds.Count} installed apps");
                
                var updatedCount = 0;
                foreach (var game in games)
                {
                    var wasInstalled = game.IsInstalled;
                    game.IsInstalled = installedAppIds.Contains(game.AppId);
                    
                    if (wasInstalled != game.IsInstalled)
                    {
                        updatedCount++;
                    }
                }
                
                _logger.LogInfo($"Updated installed status for {updatedCount} games");
                return games;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating installed status: {ex.Message}");
                return games;
            }
        }
        
        /// <summary>
        /// Clear the cache file
        /// </summary>
        public async Task ClearCacheAsync()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    File.Delete(_cacheFilePath);
                    _logger.LogInfo("Game cache cleared successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error clearing game cache: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get cache file info
        /// </summary>
        public (bool Exists, DateTime LastModified, long SizeBytes) GetCacheInfo()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var fileInfo = new FileInfo(_cacheFilePath);
                    return (true, fileInfo.LastWriteTime, fileInfo.Length);
                }
                return (false, DateTime.MinValue, 0);
            }
            catch
            {
                return (false, DateTime.MinValue, 0);
            }
        }
    }
} 