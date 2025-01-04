using System;
using System.IO;
using Newtonsoft.Json;
using System.Windows.Forms;

namespace WorkshopManager
{
    public class Settings
    {
        private static readonly string SettingsPath = Path.Combine(
            Application.StartupPath,
            "workshopmanager_settings.json"
        );

        public string SteamCmdPath { get; set; } = "";
        public string LastTargetDirectory { get; set; } = "";
        public string LastScriptFile { get; set; } = "";

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