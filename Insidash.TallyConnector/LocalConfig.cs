using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Insidash.TallyConnector
{
    public class CompanyProfile
    {
        public string SyncToken { get; set; }
        public int CompanyID { get; set; }
    }

    public class ConnectorConfig
    {
        // NEW: Stores activated profiles mapped by TallyCompanyName
        // Using StringComparer.OrdinalIgnoreCase prevents casing discrepancies
        public Dictionary<string, CompanyProfile> Profiles { get; set; } = new Dictionary<string, CompanyProfile>(StringComparer.OrdinalIgnoreCase);

        // The currently active Tally company name selected by the user
        public string TallyCompanyName { get; set; } = "";

        public string TallyHost { get; set; } = "localhost";
        public string TallyPort { get; set; } = "9000";
        public int SyncIntervalMs { get; set; } = 300000;

        // LEGACY FALLBACKS: Maintained temporarily for smooth backward compatibility migration
        public string SyncToken { get; set; } = "";
        public int CompanyID { get; set; } = 0;
    }

    public static class LocalConfig
    {
        // Stored in: C:\ProgramData\InsidashTallyConnector\config.dat
        private static readonly string ConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "InsidashTallyConnector");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.dat");

        public static bool Exists() => File.Exists(ConfigPath);

        public static void Save(ConnectorConfig config)
        {
            Directory.CreateDirectory(ConfigDir);
            string json = JsonConvert.SerializeObject(config);
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);

            // DPAPI encrypts with the machine key — only readable on this machine
            byte[] encryptedBytes = ProtectedData.Protect(
                plainBytes, null, DataProtectionScope.LocalMachine);

            File.WriteAllBytes(ConfigPath, encryptedBytes);
        }

        public static ConnectorConfig Load()
        {
            if (!Exists())
                throw new FileNotFoundException("Connector not activated. Config file missing.");

            byte[] encryptedBytes = File.ReadAllBytes(ConfigPath);
            byte[] plainBytes = ProtectedData.Unprotect(
                encryptedBytes, null, DataProtectionScope.LocalMachine);

            string json = Encoding.UTF8.GetString(plainBytes);
            var config = JsonConvert.DeserializeObject<ConnectorConfig>(json);

            // Ensure dictionary is initialized
            if (config.Profiles == null)
            {
                config.Profiles = new Dictionary<string, CompanyProfile>(StringComparer.OrdinalIgnoreCase);
            }

            // ── SEAMLESS BACKWARD COMPATIBILITY MIGRATION ──
            // If we load an old config that has root-level credentials and a target company name,
            // migrate it into the Profiles dictionary and save it immediately.
            if (!string.IsNullOrWhiteSpace(config.SyncToken) && !string.IsNullOrWhiteSpace(config.TallyCompanyName))
            {
                if (!config.Profiles.ContainsKey(config.TallyCompanyName))
                {
                    config.Profiles[config.TallyCompanyName] = new CompanyProfile
                    {
                        SyncToken = config.SyncToken,
                        CompanyID = config.CompanyID
                    };

                    // Clear legacy properties to complete migration
                    config.SyncToken = "";
                    config.CompanyID = 0;

                    Save(config); // Save the migrated config cleanly on disk
                }
            }

            return config;
        }

        public static void Delete()
        {
            if (Exists())
            {
                File.Delete(ConfigPath);
            }
        }
    }
}