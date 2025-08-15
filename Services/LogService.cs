using System;
using System.IO;
using System.Threading.Tasks;

namespace SimpleSteamSwitcher.Services
{
    public class LogService
    {
        private readonly string _logFilePath;
        private readonly object _lockObject = new object();

        public LogService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDirectory = Path.Combine(appDataPath, "SimpleSteamSwitcher", "Logs");
            Directory.CreateDirectory(logDirectory);
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd");
            _logFilePath = Path.Combine(logDirectory, $"SimpleSteamSwitcher_{timestamp}.log");
        }

        public void LogInfo(string message, string category = "INFO")
        {
            Console.WriteLine($"[{category}] {message}");
            WriteLog("INFO", category, message);
        }

        public void LogError(string message, string category = "ERROR", Exception ex = null)
        {
            var fullMessage = ex != null ? $"{message} - Exception: {ex.Message}\nStackTrace: {ex.StackTrace}" : message;
            Console.WriteLine($"[{category}] {fullMessage}");
            WriteLog("ERROR", category, fullMessage);
        }

        public void LogSuccess(string message, string category = "SUCCESS")
        {
            Console.WriteLine($"[{category}] {message}");
            WriteLog("SUCCESS", category, message);
        }

        public void LogWarning(string message, string category = "WARNING")
        {
            Console.WriteLine($"[{category}] {message}");
            WriteLog("WARNING", category, message);
        }

        public void LogFillMethod(string methodName, string username, bool success, string details = "")
        {
            var status = success ? "SUCCESS" : "FAILED";
            var message = $"Fill Method: {methodName} | Username: {username} | Status: {status}";
            if (!string.IsNullOrEmpty(details))
                message += $" | Details: {details}";
            
            Console.WriteLine($"[FILL] {message}");
            WriteLog("FILL", "FILL_METHOD", message);
        }

        public void LogSwitchOperation(string accountName, string phase, bool success, string details = "")
        {
            var status = success ? "SUCCESS" : "FAILED";
            var message = $"Switch Operation: {phase} | Account: {accountName} | Status: {status}";
            if (!string.IsNullOrEmpty(details))
                message += $" | Details: {details}";
            
            Console.WriteLine($"[SWITCH] {message}");
            WriteLog("SWITCH", "SWITCH_OPERATION", message);
        }

        private void WriteLog(string level, string category, string message)
        {
            lock (_lockObject)
            {
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var logEntry = $"[{timestamp}] [{level}] [{category}] {message}";
                    
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                    
                    // Also write to debug output
                    System.Diagnostics.Debug.WriteLine(logEntry);
                }
                catch (Exception ex)
                {
                    // If logging fails, at least write to debug output
                    System.Diagnostics.Debug.WriteLine($"LOGGING ERROR: {ex.Message} - Original message: {message}");
                }
            }
        }

        public string GetLogFilePath()
        {
            return _logFilePath;
        }

        public async Task<string> GetRecentLogContentsAsync(int lines = 50)
        {
            try
            {
                if (!File.Exists(_logFilePath))
                    return "No log file found.";

                var allLines = await File.ReadAllLinesAsync(_logFilePath);
                var recentLines = allLines.Length > lines 
                    ? allLines[^lines..] 
                    : allLines;

                return string.Join(Environment.NewLine, recentLines);
            }
            catch (Exception ex)
            {
                return $"Error reading log file: {ex.Message}";
            }
        }
    }
} 