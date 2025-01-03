using System;
using System.Windows.Forms;
using System.IO;
using System.Configuration;

namespace WorkshopManager
{
    public class ConfigHandler
    {
        public static string GetSteamCmdPath()
        {
            string configPath = ConfigurationManager.AppSettings["SteamCmdPath"];
            if (string.IsNullOrEmpty(configPath))
            {
                return Path.Combine(Application.StartupPath, "steamcmd.exe");
            }
            return configPath;
        }

        public static bool ValidateSteamCmdPath(string path)
        {
            return File.Exists(path);
        }
    }
}