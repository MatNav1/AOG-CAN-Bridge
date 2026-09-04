using System;
using System.IO;

namespace AogCanBridge
{
    // A single persisted setting (the chosen language code) is not worth a
    // serialization framework; a one-line "Language=code" file is enough.
    internal static class AppSettings
    {
        private static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "AogCanBridge.settings");

        internal static string LoadLanguage(string fallback)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int separator = line.IndexOf('=');
                        if (separator <= 0) continue;
                        string key = line.Substring(0, separator).Trim();
                        if (string.Equals(key, "Language", StringComparison.OrdinalIgnoreCase))
                            return line.Substring(separator + 1).Trim();
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return fallback;
        }

        internal static void SaveLanguage(string code)
        {
            try
            {
                File.WriteAllText(FilePath, "Language=" + code + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
