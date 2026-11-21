using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        /// <summary>"Dark" or "Light". Chosen in the app, independent of the
        /// system's own dark mode setting.</summary>
        public string Theme { get; set; } = "Dark";

        // Auto-update
        /// <summary>Check GitHub for a newer release on startup.</summary>
        public bool CheckForUpdates { get; set; } = true;

        /// <summary>
        /// "Stable" or "Beta". On the beta channel pre-releases are offered
        /// too, so testers get new builds without a manual download.
        /// </summary>
        public string UpdateChannel { get; set; } = "Stable";

        /// <summary>Version the user chose to skip; empty = none.</summary>
        public string SkippedUpdateVersion { get; set; } = "";

        /// <summary>
        /// "Install" or "Archive": the mode the app was left in, restored on
        /// the next start.
        /// </summary>
        public string Mode { get; set; } = "Install";

        [JsonIgnore]
        public AppMode AppMode
        {
            get => "Archive".Equals(Mode, StringComparison.OrdinalIgnoreCase) ? AppMode.Archive : AppMode.Install;
            set => Mode = value.ToString();
        }

        /// <summary>
        /// The archive mode's folder. Kept apart from the install folder, so
        /// switching modes never sends one kind of layout into the other's
        /// folder.
        /// </summary>
        public string ArchiveDirectory { get; set; } = "";

        /// <summary>Options that apply in archive mode only.</summary>
        public ArchiveOptions Archive { get; set; } = new();

        /// <summary>
        /// Set while loading when the archiving options of version 1.3 were
        /// moved into the archive mode, so the window can say so once.
        /// </summary>
        [JsonIgnore]
        public bool ArchiveSettingsMigrated { get; private set; }

        // --- Read from settings files of 1.3, never written again ----------
        // Version 1.3 had the archiving options in the one and only mode.
        // They now belong to the archive mode; see MigrateArchiveOptions.

        public string ModFolderNaming { get; set; }
        public bool? UseWorkshopDates { get; set; }
        public bool? SavePreviewImage { get; set; }
        public bool? CreateWorkshopLink { get; set; }
        public bool? InfoFileInsideModFolder { get; set; }
        public bool? CollectionFolders { get; set; }

        public bool ShouldSerializeModFolderNaming() => false;
        public bool ShouldSerializeUseWorkshopDates() => false;
        public bool ShouldSerializeSavePreviewImage() => false;
        public bool ShouldSerializeCreateWorkshopLink() => false;
        public bool ShouldSerializeInfoFileInsideModFolder() => false;
        public bool ShouldSerializeCollectionFolders() => false;

        /// <summary>
        /// Moves the archiving options of a 1.3 settings file into the
        /// archive mode. Someone who turned any of them on was building an
        /// archive, so the app starts in archive mode, on the same folder and
        /// with exactly the options they chose - nothing changes for them
        /// except where the options live. Everyone else keeps installing as
        /// before and finds the archive mode with its own defaults.
        /// </summary>
        internal void MigrateArchiveOptions(bool fileHadArchiveSection)
        {
            bool anyLegacy =
                "Title".Equals(ModFolderNaming, StringComparison.OrdinalIgnoreCase) ||
                UseWorkshopDates == true || SavePreviewImage == true || CreateWorkshopLink == true ||
                InfoFileInsideModFolder == true || CollectionFolders == true;

            if (!fileHadArchiveSection && anyLegacy)
            {
                Archive = new ArchiveOptions
                {
                    FoldersByTitle = "Title".Equals(ModFolderNaming, StringComparison.OrdinalIgnoreCase),
                    WorkshopDates = UseWorkshopDates == true,
                    PreviewImage = SavePreviewImage == true,
                    WorkshopLink = CreateWorkshopLink == true,
                    InfoInside = InfoFileInsideModFolder == true,
                    CollectionFolders = CollectionFolders == true,
                    Gallery = false
                };
                AppMode = AppMode.Archive;
                if (string.IsNullOrWhiteSpace(ArchiveDirectory)) ArchiveDirectory = LastTargetDirectory;
                ArchiveSettingsMigrated = true;
            }

            ModFolderNaming = null;
            UseWorkshopDates = SavePreviewImage = CreateWorkshopLink = null;
            InfoFileInsideModFolder = CollectionFolders = null;
            Archive ??= new ArchiveOptions();
        }

        /// <summary>Per-game overrides keyed by app id.</summary>
        public Dictionary<string, GameRule> GameRules { get; set; } = new();

        public static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = JObject.Parse(File.ReadAllText(SettingsPath));
                    var loaded = json.ToObject<Settings>() ?? new Settings();
                    loaded.MigrateArchiveOptions(json["Archive"] != null);
                    return loaded;
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
