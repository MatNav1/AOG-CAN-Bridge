using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AogCanBridge
{
    internal sealed class LanguageInfo
    {
        internal LanguageInfo(string code, string displayName, Dictionary<string, string> strings)
        {
            Code = code;
            DisplayName = displayName;
            Strings = strings;
        }

        internal string Code { get; }
        internal string DisplayName { get; }
        internal Dictionary<string, string> Strings { get; }

        public override string ToString() => DisplayName;
    }

    // Loads UI text from Languages\*.lang files next to the executable, so a
    // translation can be added or edited without rebuilding. The dictionary
    // below is a compiled-in English fallback used for any key missing from
    // the selected file, so the UI is never blank if a translation is partial
    // or the Languages folder is missing entirely.
    internal static class Localization
    {
        private static readonly string LanguagesFolder = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Languages");

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            ["LanguageName"] = "English",
            ["Language"] = "Language",
            ["PcanChannel"] = "PCAN Channel",
            ["Start"] = "Start",
            ["Stop"] = "Stop",
            ["StatusStopped"] = "Stopped",
            ["StatusRunning"] = "Running — {0}",
            ["Clients"] = "Clients: {0}    VT (2): {1}    TC (3): {2}",
            ["Connected"] = "connected",
            ["NotConnected"] = "none",
            ["Counters"] = "RX: {0}    TX: {1}",
            ["BusLoad"] = "Bus load: {0}%",
            ["Hint"] = "Start the bridge before VT and Task Controller.",
            ["ErrorDllMissing"] = "PCANBasic.dll not found next to AogCanBridge.exe.",
            ["ErrorCannotOpenPcanTitle"] = "Cannot open PCAN",
            ["ErrorCannotOpenPort"] = "Cannot open local port {0}:\r\n{1}",
            ["ErrorPcan"] = "PCAN error 0x{0}",
            ["AlreadyRunning"] = "AOG CAN Bridge is already running."
        };

        private static Dictionary<string, string> current = Defaults;

        internal static List<LanguageInfo> DiscoverLanguages()
        {
            List<LanguageInfo> languages = new List<LanguageInfo>();
            try
            {
                if (Directory.Exists(LanguagesFolder))
                {
                    foreach (string file in Directory.GetFiles(LanguagesFolder, "*.lang")
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        string code = Path.GetFileNameWithoutExtension(file);
                        Dictionary<string, string> strings = ParseFile(file);
                        string name = strings.TryGetValue("LanguageName", out string displayName)
                            ? displayName : code;
                        languages.Add(new LanguageInfo(code, name, strings));
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            if (languages.Count == 0)
                languages.Add(new LanguageInfo("en", Defaults["LanguageName"], Defaults));

            return languages;
        }

        internal static LanguageInfo ResolveSavedLanguage(List<LanguageInfo> languages)
        {
            string savedCode = AppSettings.LoadLanguage(languages[0].Code);
            return languages.Find(language =>
                string.Equals(language.Code, savedCode, StringComparison.OrdinalIgnoreCase))
                ?? languages[0];
        }

        internal static void SetLanguage(LanguageInfo language)
        {
            current = language.Strings;
        }

        internal static string Get(string key, params object[] args)
        {
            string format = current.TryGetValue(key, out string value) ? value
                : Defaults.TryGetValue(key, out string fallback) ? fallback : key;
            return args.Length == 0 ? format : string.Format(format, args);
        }

        private static Dictionary<string, string> ParseFile(string path)
        {
            Dictionary<string, string> strings = new Dictionary<string, string>();
            foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int separator = line.IndexOf('=');
                if (separator <= 0) continue;
                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1)
                    .Replace("\\r\\n", "\r\n").Replace("\\n", "\n");
                strings[key] = value;
            }
            return strings;
        }
    }
}
