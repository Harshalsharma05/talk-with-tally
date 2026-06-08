using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace Insidash.TallyConnector
{
    public static class MachineIdentifier
    {
        public static string Get()
        {
            try
            {
                // Combine CPU ID + Motherboard serial for a stable fingerprint
                string cpuId    = GetWmiValue("Win32_Processor", "ProcessorId");
                string boardId  = GetWmiValue("Win32_BaseBoard", "SerialNumber");
                string raw      = cpuId + boardId;

                // Hash it so we never store raw hardware IDs
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                    return BitConverter.ToString(hash).Replace("-", "").ToLower().Substring(0, 32);
                }
            }
            catch
            {
                // Fallback: use machine name hash
                return Environment.MachineName.GetHashCode().ToString("X8");
            }
        }

        private static string GetWmiValue(string className, string propertyName)
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT {propertyName} FROM {className}"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        return obj[propertyName]?.ToString()?.Trim() ?? "";
                    }
                }
            }
            catch
            {
                // Ignore WMI lookup failures and let main flow catch it
            }
            return "";
        }
    }
}
