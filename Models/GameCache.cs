using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleSteamSwitcher.Models
{
    public class GameCache
    {
        public DateTime LastUpdated { get; set; }
        public List<CachedGame> Games { get; set; } = new List<CachedGame>();
        public TimeSpan CacheValidDuration { get; set; } = TimeSpan.FromHours(7);
        
        public bool IsExpired => DateTime.Now - LastUpdated > CacheValidDuration;
    }

    public class CachedGame
    {
        public int AppId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PlaytimeForever { get; set; }
        public string ImgIconUrl { get; set; } = string.Empty;
        public string OwnerSteamId { get; set; } = string.Empty;
        public string OwnerAccountName { get; set; } = string.Empty;
        public string OwnerPersonaName { get; set; } = string.Empty;
        public bool IsPaid { get; set; }
        public bool IsInstalled { get; set; }
        public DateTime LastUpdated { get; set; }
        
        // Aggregated owners (persisted for owner-aware filtering)
        		public List<string> OwnerSteamIdsAll { get; set; } = new List<string>();
		public List<string> OwnerAccountNamesAll { get; set; } = new List<string>();
        
        // Convert from Game model
        public static CachedGame FromGame(Game game)
        {
            return new CachedGame
            {
                AppId = game.AppId,
                Name = game.Name,
                PlaytimeForever = game.PlaytimeForever,
                ImgIconUrl = game.ImgIconUrl,
                OwnerSteamId = game.OwnerSteamId,
                OwnerAccountName = game.OwnerAccountName,
                OwnerPersonaName = game.OwnerPersonaName,
                IsPaid = game.IsPaid,
                IsInstalled = game.IsInstalled,
                LastUpdated = game.LastUpdated,
                				OwnerSteamIdsAll = game.OwnerSteamIds?.ToList() ?? new List<string>(),
				OwnerAccountNamesAll = game.OwnerAccountNames?.ToList() ?? new List<string>()
            };
        }
        
        // Convert to Game model
        public Game ToGame()
        {
            return new Game
            {
                AppId = this.AppId,
                Name = this.Name,
                PlaytimeForever = this.PlaytimeForever,
                ImgIconUrl = this.ImgIconUrl,
                OwnerSteamId = this.OwnerSteamId,
                OwnerAccountName = this.OwnerAccountName,
                OwnerPersonaName = this.OwnerPersonaName,
                IsPaid = this.IsPaid,
                IsInstalled = this.IsInstalled,
                LastUpdated = this.LastUpdated,
                AvailableAccounts = new List<SteamAccount>(), // Will be populated later
                				OwnerSteamIds = new HashSet<string>(this.OwnerSteamIdsAll ?? new List<string>()),
				OwnerAccountNames = new HashSet<string>(this.OwnerAccountNamesAll ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
            };
        }
    }
} 