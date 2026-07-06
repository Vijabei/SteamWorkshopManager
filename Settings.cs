using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Windows.Forms;

namespace WorkshopManager
{
    /// <summary>
    /// Optional per-game installation rule. If a game has no rule (or an
    /// empty target directory), the global target directory is used.
    /// </summary>
    public class GameRule
    {
        public string TargetDirectory { get; set; } = "";
    }

    public class Settings
    {
        private static readonly string SettingsPath = Path.Combine(
            Application.StartupPath,
            "workshopmanager_settings.json"
        );

        public string SteamCmdPath { get; set; } = "";
        public string LastTargetDirectory { get; set; } = "";
        public string LastScriptFile { get; set; } = "";

        // Installation behaviour
        public bool CleanupAfterInstall { get; set; } = false;
        public bool SkipInstalledMods { get; set; } = true;

        /// <summary>Mods per SteamCMD invocation; large collections are split
        /// into batches to avoid SteamCMD timeouts.</summary>
        public int BatchSize { get; set; } = 30;

        /// <summary>How often a failed download is retried before giving up.</summary>
        public int MaxRetries { get; set; } = 2;

        // Internal browser
        public string BrowserHomeUrl { get; set; } = "https://steamcommunity.com/workshop/";

        /// <summary>Per-game overrides keyed by app id.</summary>
        public Dictionary<string, GameRule> GameRules { get; set; } = new();

        public static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonConvert.DeserializeObject<Settings>(json) ?? new Settings();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error loading settings: {ex.Message}\nDefault settings will be used.",
                    "Settings Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }

            return new Settings();
        }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error saving settings: {ex.Message}",
                    "Settings Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        public static bool ValidateSteamCmdPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return File.Exists(path) &&
                   Path.GetFileName(path).Equals("steamcmd.exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
