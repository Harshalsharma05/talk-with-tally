using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Insidash.TallyConnector
{
    public class AutoUpdater
    {
        private static readonly HttpClient _client = new HttpClient();
        private readonly string _versionEndpoint;
        private readonly string _currentVersion;

        public AutoUpdater(string versionEndpoint)
        {
            _versionEndpoint = versionEndpoint;
            
            // Read from AssemblyInfo.cs — defaults to "1.0.0" if not versioned
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            _currentVersion = version != null ? version.ToString(3) : "1.0.0"; // e.g. "1.0.0"
        }

        public async Task<bool> CheckAndUpdateAsync()
        {
            try
            {
                string json     = await _client.GetStringAsync(_versionEndpoint);
                dynamic info    = JsonConvert.DeserializeObject(json);
                string latest   = (string)info.version;
                string url      = (string)info.downloadUrl;

                if (string.IsNullOrEmpty(url) || latest == _currentVersion)
                    return false; // already up to date

                if (!IsNewerVersion(latest, _currentVersion))
                    return false;

                // Download new exe to a temp file
                byte[] newExe = await _client.GetByteArrayAsync(url);
                string tempPath = Path.Combine(Path.GetTempPath(), "InsidashConnector_update.exe");
                File.WriteAllBytes(tempPath, newExe);

                // Write a small batch script that:
                // 1. Waits for current process to exit
                // 2. Copies new exe over the current one
                // 3. Restarts the service
                string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                string batchPath  = Path.Combine(Path.GetTempPath(), "insidash_update.bat");
                File.WriteAllText(batchPath,
                    $"@echo off\r\n" +
                    $"timeout /t 3 /nobreak >nul\r\n" +
                    $"copy /y \"{tempPath}\" \"{currentExe}\"\r\n" +
                    $"sc stop InsidashTallyConnector\r\n" +
                    $"timeout /t 2 /nobreak >nul\r\n" +
                    $"sc start InsidashTallyConnector\r\n" +
                    $"del \"{batchPath}\"\r\n");

                // Launch the batch as a hidden process and exit this one
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
                {
                    WindowStyle    = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });

                return true; // update triggered, caller should exit the service
            }
            catch
            {
                return false; // update failed silently — carry on
            }
        }

        private bool IsNewerVersion(string latest, string current)
        {
            return Version.TryParse(latest, out var l)
                && Version.TryParse(current, out var c)
                && l > c;
        }
    }
}
