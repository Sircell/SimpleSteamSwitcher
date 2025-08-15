using System;
using System.Collections.Generic;
using SimpleSteamSwitcher.Services;

namespace SimpleSteamSwitcher.Models
{
    public class SteamAccount
    {
        public string SteamId { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string PersonaName { get; set; } = "";
        public string AvatarUrl { get; set; } = "";
        public DateTime LastLogin { get; set; }
        public bool IsVACBanned { get; set; }
        public bool IsLimited { get; set; }
        
        // Ban status information (from BanStatusService)
        [Newtonsoft.Json.JsonIgnore]
        public BanInfo? BanInfo { get; set; }
        
        // JSON-serializable ban status for persistence
        public string? BanStatusJson { get; set; }
        public DateTime? BanStatusLastChecked { get; set; }
        
        // Game count information
        public int GameCount { get; set; } = 0;
        public int PaidGameCount { get; set; } = 0;
        public DateTime? GameCountLastChecked { get; set; }
        public bool IsCurrentAccount { get; set; } = false;
        
        // Encrypted password storage (not serialized to JSON by default)
        [Newtonsoft.Json.JsonIgnore]
        public string? StoredPassword { get; set; }
        
        // Encrypted password for JSON storage
        public string? EncryptedPassword { get; set; }
        
        // Indicates if password is stored
        public bool HasStoredPassword => !string.IsNullOrEmpty(EncryptedPassword);
        
        public string DisplayName => !string.IsNullOrEmpty(PersonaName) ? PersonaName : AccountName;
        
        public string StatusDisplay => IsCurrentAccount ? "Current" : "Available";
        
        // Password status indicators
        public string PasswordStatus => HasStoredPassword ? "Yes" : "No";
        public string PasswordStatusColor => HasStoredPassword ? "#28a745" : "#dc3545"; // Green or Red
        public string PasswordStatusIcon => HasStoredPassword ? "✓" : "✗";
        
        // Ban status indicators
        public string BanStatusDisplay => BanInfo?.Description ?? "Status unknown";
        public string BanStatusColor => GetBanStatusColor();
        public string BanStatusIcon => GetBanStatusIcon();
        public bool HasBanInfo => BanInfo != null; // Show status for all accounts with ban data, including clean ones
        
        // Game count indicators
        public string GameCountDisplay => GameCountLastChecked.HasValue ? GetGameCountDisplayText() : "Game count unknown";
        public string GameCountShortDisplay => GameCountLastChecked.HasValue ? GetGameCountShortDisplayText() : "?";
        public bool HasGameCountInfo => GameCountLastChecked.HasValue;
        public string GameCountIcon => "🎮";
        
        private string GetGameCountDisplayText()
        {
            var freeToPlayCount = GameCount - PaidGameCount;
            
            if (GameCount == 0)
                return "No visible games (may have private profile)";
            else if (PaidGameCount == 0 && GameCount > 0)
                return $"{GameCount} free-to-play games";
            else if (freeToPlayCount == 0)
                return $"{PaidGameCount} paid games";
            else
                return $"{PaidGameCount} paid / {freeToPlayCount} F2P / {GameCount} total";
        }
        
        private string GetGameCountShortDisplayText()
        {
            var freeToPlayCount = GameCount - PaidGameCount;
            
            if (GameCount == 0)
                return "Private?";
            else if (PaidGameCount == 0 && GameCount > 0)
                return $"{GameCount} F2P";
            else if (freeToPlayCount == 0)
                return $"{PaidGameCount} paid";
            else
                return $"{PaidGameCount}+{freeToPlayCount}";
        }
        
        private string GetBanStatusColor()
        {
            if (BanInfo == null) return "#6c757d"; // Gray for unknown
            
            return BanInfo.Status switch
            {
                Services.BanStatus.Clean => "#28a745", // Green
                Services.BanStatus.VACBanned => "#dc3545", // Red - Make single VAC ban clearly visible
                Services.BanStatus.MultipleVACBans => "#8b0000", // Dark Red
                Services.BanStatus.GameBanned => "#dc3545", // Red - Changed from orange to red for visibility
                Services.BanStatus.MultipleGameBans => "#8b0000", // Dark Red
                Services.BanStatus.CommunityBanned => "#dc3545", // Red - Changed from purple to red
                Services.BanStatus.TradeBanned => "#dc3545", // Red - Changed from yellow to red
                Services.BanStatus.Unknown => "#6c757d", // Gray for unknown
                _ => "#6c757d" // Gray for unknown
            };
        }
        
        private string GetBanStatusIcon()
        {
            if (BanInfo == null) return "?";
            
            return BanInfo.Status switch
            {
                Services.BanStatus.Clean => "✓",
                Services.BanStatus.VACBanned => "⚠",
                Services.BanStatus.MultipleVACBans => "🚫",
                Services.BanStatus.GameBanned => "⚠",
                Services.BanStatus.MultipleGameBans => "🚫",
                Services.BanStatus.CommunityBanned => "🔒",
                Services.BanStatus.TradeBanned => "💰",
                Services.BanStatus.Unknown => "?",
                _ => "?"
            };
        }
        
        public SteamAccount Clone()
        {
            return new SteamAccount
            {
                SteamId = this.SteamId,
                AccountName = this.AccountName,
                PersonaName = this.PersonaName,
                AvatarUrl = this.AvatarUrl,
                LastLogin = this.LastLogin,
                IsVACBanned = this.IsVACBanned,
                IsLimited = this.IsLimited,
                EncryptedPassword = this.EncryptedPassword,
                StoredPassword = this.StoredPassword,
                GameCount = this.GameCount,
                PaidGameCount = this.PaidGameCount,
                GameCountLastChecked = this.GameCountLastChecked
            };
        }
    }
}

namespace SimpleSteamSwitcher.Models
{
    public class Game
    {
        public int AppId { get; set; }
        public string Name { get; set; } = "";
        public int PlaytimeForever { get; set; } // in minutes
        public string ImgIconUrl { get; set; } = "";
        public string OwnerSteamId { get; set; } = "";
        public string OwnerAccountName { get; set; } = "";
        public string OwnerPersonaName { get; set; } = "";
        public bool IsPaid { get; set; } = true; // Default to paid unless determined otherwise
        public bool IsInstalled { get; set; } = false; // Filled by scanning Steam libraries
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        
        // Computed properties
        public string PlaytimeDisplay => GetReadablePlaytime();
        
        private string GetReadablePlaytime()
        {
            if (PlaytimeForever <= 0)
                return "Not played";
            
            var totalHours = PlaytimeForever / 60;
            var remainingMinutes = PlaytimeForever % 60;
            
            if (totalHours == 0)
                return $"{remainingMinutes}m";
            else if (remainingMinutes == 0)
                return $"{totalHours}h";
            else if (totalHours >= 24)
            {
                var days = totalHours / 24;
                var remainingHours = totalHours % 24;
                if (remainingHours == 0)
                    return $"{days}d";
                else
                    return $"{days}d {remainingHours}h";
            }
            else
                return $"{totalHours}h {remainingMinutes}m";
        }
        
        public string IconUrl => !string.IsNullOrEmpty(ImgIconUrl) 
            ? $"https://media.steampowered.com/steamcommunity/public/images/apps/{AppId}/{ImgIconUrl}.jpg"
            : "";
        
        public string GameType => IsPaid ? "Paid" : "Free-to-Play";
        public string GameTypeColor => IsPaid ? "#4CAF50" : "#2196F3";
        
        public string OwnerDisplay => !string.IsNullOrEmpty(OwnerPersonaName) 
            ? OwnerPersonaName 
            : OwnerAccountName;
            
        // For F2P games, get all available accounts to launch from
        public List<SteamAccount> AvailableAccounts { get; set; } = new List<SteamAccount>();
        
        public bool IsF2PWithMultipleAccounts => !IsPaid && AvailableAccounts.Count > 1;

		// Aggregated owners (all accounts that own this AppId)
		public HashSet<string> OwnerSteamIds { get; set; } = new HashSet<string>();
		public HashSet<string> OwnerAccountNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
} 