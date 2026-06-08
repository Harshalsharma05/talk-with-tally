using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Insidash.TallyConnector
{
    public class ConnectorConfig
    {
        public string SyncToken      { get; set; } = "twt_dev_token_E5E7568D-B344-4B94-9028-FA823D6299AF";
        public int    CompanyID      { get; set; } = 5;
        public string TallyHost      { get; set; } = "localhost";
        public string TallyPort      { get; set; } = "9000";
        public int    SyncIntervalMs { get; set; } = 300000;
    }

    public static class LocalConfig
    {
        // Stored in: C:\ProgramData\InsidashTallyConnector\config.dat
        private static readonly string ConfigDir  =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "InsidashTallyConnector");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.dat");

        public static bool Exists() => File.Exists(ConfigPath);

        public static void Save(ConnectorConfig config)
        {
            Directory.CreateDirectory(ConfigDir);
            string json       = JsonConvert.SerializeObject(config);
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
            byte[] plainBytes     = ProtectedData.Unprotect(
                encryptedBytes, null, DataProtectionScope.LocalMachine);

            string json = Encoding.UTF8.GetString(plainBytes);
            return JsonConvert.DeserializeObject<ConnectorConfig>(json);
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
