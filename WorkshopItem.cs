using System;
using System.Collections.Generic;

namespace WorkshopManager
{
    public enum WorkshopItemStatus
    {
        Pending,
        Downloading,
        Installed,
        UpdateAvailable,
        Skipped,
        Failed,

        /// <summary>No longer available on the Workshop (banned/removed).</summary>
        Removed
    }

    /// <summary>
    /// A single Steam Workshop item (mod) with the metadata needed for
    /// download, installation and update checks.
    /// </summary>
    public class WorkshopItem
    {
        public string ModId { get; set; } = "";
        public string AppId { get; set; } = "";
        public string Title { get; set; } = "";
        public long FileSize { get; set; }

        /// <summary>Unix timestamp of the last workshop update, 0 if unknown.</summary>
        public long TimeUpdated { get; set; }

        /// <summary>Workshop description. Archived locally so it survives
        /// the item being removed from the Workshop.</summary>
        public string Description { get; set; } = "";

        /// <summary>Workshop tags, comma separated. Includes the supported
        /// game versions for many games.</summary>
        public string Tags { get; set; } = "";

        /// <summary>URL of the preview image, empty if unknown.</summary>
        public string PreviewUrl { get; set; } = "";

        /// <summary>
        /// True if the item is no longer obtainable from the Workshop -
        /// either banned by Valve or deleted by its author.
        /// </summary>
        public bool Banned { get; set; }

        /// <summary>Workshop items this mod declares as required.</summary>
        public List<ModRequirement> RequiredMods { get; set; } = new();

        /// <summary>Steam DLC this mod declares as required.</summary>
        public List<ModRequirement> RequiredDlc { get; set; } = new();

        /// <summary>
        /// True once the requirements were looked up, so an empty list can be
        /// told apart from "not checked yet".
        /// </summary>
        public bool RequirementsChecked { get; set; }

        public WorkshopItemStatus Status { get; set; } = WorkshopItemStatus.Pending;

        public string StatusText
        {
            get
            {
                var text = Status switch
                {
                    WorkshopItemStatus.Pending => "Pending",
                    WorkshopItemStatus.Downloading => "Downloading...",
                    WorkshopItemStatus.Installed => "Installed",
                    WorkshopItemStatus.UpdateAvailable => "Update available",
                    WorkshopItemStatus.Skipped => "Skipped (installed)",
                    WorkshopItemStatus.Failed => "Failed",
                    WorkshopItemStatus.Removed => "Removed from Steam",
                    _ => Status.ToString()
                };

                // A local copy of an item that is gone from the Workshop is
                // exactly the archival case this metadata exists for.
                return Banned && Status != WorkshopItemStatus.Removed
                    ? $"{text} - gone from Steam"
                    : text;
            }
        }

        /// <summary>True if the given text occurs in any searchable field.</summary>
        public bool Matches(string needle)
        {
            if (string.IsNullOrWhiteSpace(needle)) return true;

            return Contains(Title, needle)
                || Contains(ModId, needle)
                || Contains(Tags, needle)
                || Contains(Description, needle);

            static bool Contains(string haystack, string value) =>
                haystack != null && haystack.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        public string FileSizeText
        {
            get
            {
                if (FileSize <= 0) return "";
                string[] units = { "B", "KB", "MB", "GB" };
                double size = FileSize;
                int unit = 0;
                while (size >= 1024 && unit < units.Length - 1)
                {
                    size /= 1024;
                    unit++;
                }
                return $"{size:0.#} {units[unit]}";
            }
        }

        public string TimeUpdatedText =>
            TimeUpdated > 0
                ? DateTimeOffset.FromUnixTimeSeconds(TimeUpdated).LocalDateTime.ToString("yyyy-MM-dd")
                : "";
    }
}
