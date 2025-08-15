using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SimpleSteamSwitcher.Services
{
    public class GameTypeCache
    {
        private readonly string _cacheFilePath;
        private readonly LogService _logger;
        private Dictionary<int, GameTypeInfo> _cache;
        private readonly object _cacheLock = new object();

        public GameTypeCache()
        {
            _logger = new LogService();
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleSteamSwitcher");
            Directory.CreateDirectory(appDataPath);
            _cacheFilePath = Path.Combine(appDataPath, "game_types_cache.json");
            _cache = new Dictionary<int, GameTypeInfo>();
            _ = LoadCacheAsync(); // Load asynchronously
        }

        public bool TryGetGameType(int appId, out bool isPaid)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(appId, out var info))
                {
                    // Check if cache entry is still valid (30 days for normal, 7 days for unavailable)
                    var expiryDays = info.IsUnavailable ? 7 : 30;
                    if (DateTime.Now - info.LastChecked < TimeSpan.FromDays(expiryDays))
                    {
                        isPaid = info.IsPaid;
                        return true;
                    }
                    else
                    {
                        // Remove expired entry
                        _cache.Remove(appId);
                    }
                }
            }
            
            isPaid = true;
            return false;
        }

        public void SetGameType(int appId, bool isPaid, bool isUnavailable = false)
        {
            lock (_cacheLock)
            {
                _cache[appId] = new GameTypeInfo
                {
                    IsPaid = isPaid,
                    LastChecked = DateTime.Now,
                    IsUnavailable = isUnavailable
                };
            }
            
            // Save asynchronously without blocking
            _ = SaveCacheAsync();
        }

        public int GetCachedCount()
        {
            lock (_cacheLock)
            {
                return _cache.Count;
            }
        }

        private async Task LoadCacheAsync()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var json = await File.ReadAllTextAsync(_cacheFilePath);
                    var loadedCache = JsonConvert.DeserializeObject<Dictionary<int, GameTypeInfo>>(json);
                    
                    if (loadedCache != null)
                    {
                        lock (_cacheLock)
                        {
                            _cache = loadedCache;
                        }
                        _logger.LogInfo($"Loaded game type cache with {loadedCache.Count} entries");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading game type cache: {ex.Message}");
            }
        }

        private async Task SaveCacheAsync()
        {
            try
            {
                Dictionary<int, GameTypeInfo> cacheToSave;
                lock (_cacheLock)
                {
                    cacheToSave = new Dictionary<int, GameTypeInfo>(_cache);
                }
                
                var json = JsonConvert.SerializeObject(cacheToSave, Formatting.Indented);
                await File.WriteAllTextAsync(_cacheFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving game type cache: {ex.Message}");
            }
        }

        public async Task ClearCacheAsync()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
            
            if (File.Exists(_cacheFilePath))
            {
                File.Delete(_cacheFilePath);
            }
            
            _logger.LogInfo("Game type cache cleared");
        }
    }

    public class GameTypeInfo
    {
        public bool IsPaid { get; set; }
        public DateTime LastChecked { get; set; }
        public bool IsUnavailable { get; set; } // Track games that failed API calls to avoid repeated failures
        public bool IsExpired => DateTime.UtcNow - LastChecked > TimeSpan.FromDays(30); // Cache for 30 days
    }
} 