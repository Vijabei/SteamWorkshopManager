using System;
using System.Collections.Generic;
using System.Linq;

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

        /// <summary>
        /// When the item was first published, as a unix timestamp. Recorded
        /// alongside TimeUpdated because the two answer different questions:
        /// when the mod appeared, and which version is on disk.
        /// </summary>
        public long TimeCreated { get; set; }

        /// <summary>Workshop description. Archived locally so it survives
        /// the item being removed from the Workshop.</summary>
        public string Description { get; set; } = "";

        /// <summary>Workshop tags, comma separated. Includes the supported
        /// game versions for many games.</summary>
        public string Tags { get; set; } = "";

        /// <summary>
        /// Title of the collection this mod was added from, empty when it was
        /// added on its own. Decides the collection folder it is installed
        /// into, when that option is on.
        /// </summary>
        public string CollectionTitle { get; set; } = "";

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

        /// <summary>
        /// Compact requirement summary for the list column. Empty while the
        /// requirements have not been looked up, so "checked and none" stays
        /// distinguishable from "not checked".
        /// </summary>
        public string RequirementsText
        {
            get
            {
                if (!RequirementsChecked) return "";
                if (RequiredMods.Count == 0 && RequiredDlc.Count == 0) return "-";

                var parts = new List<string>();
                if (RequiredMods.Count > 0)
                {
                    parts.Add(string.Join(", ", RequiredMods.Select(r => r.Name)));
                }
                if (RequiredDlc.Count > 0)
                {
                    parts.Add("DLC: " + string.Join(", ", RequiredDlc.Select(r => r.Name)));
                }

                return string.Join("  -  ", parts);
            }
        }

        /// <summary>Workshop page of this item.</summary>
        public string WorkshopUrl => $"https://steamcommunity.com/sharedfiles/filedetails/?id={ModId}";

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

        /// <summary>
        /// Takes over what the Workshop reports about this item right now, as
        /// returned by CollectionService.GetDetailsAsync. Returns false when
        /// the lookup told us nothing, in which case the item is left as it was.
        ///
        /// Requirements are not touched: the details endpoint does not carry
        /// them, so an empty list there would mean "unknown", not "none".
        /// </summary>
        public bool ApplyWorkshopDetails(WorkshopItem fresh)
        {
            if (fresh == null || fresh.ModId != ModId) return false;

            // GetDetailsAsync only fills in a timestamp for a successful
            // lookup, so it doubles as the "Steam answered" marker.
            if (fresh.TimeUpdated > 0)
            {
                Title = fresh.Title;
                FileSize = fresh.FileSize;
                TimeUpdated = fresh.TimeUpdated;
                TimeCreated = fresh.TimeCreated;
                Description = fresh.Description;
                Tags = fresh.Tags;
                PreviewUrl = fresh.PreviewUrl;
                Banned = fresh.Banned;
                if (string.IsNullOrEmpty(AppId)) AppId = fresh.AppId;
                return true;
            }

            // Deleted from the Workshop. Keep the title and description we
            // already have - they are the archived copy, and the lookup only
            // has a placeholder to offer.
            if (fresh.Banned)
            {
                Banned = true;
                return true;
            }

            // Any other failure: nothing learned, nothing changed.
            return false;
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

        public string TimeCreatedText =>
            TimeCreated > 0
                ? DateTimeOffset.FromUnixTimeSeconds(TimeCreated).LocalDateTime.ToString("yyyy-MM-dd")
                : "";

        public string TimeUpdatedText =>
            TimeUpdated > 0
                ? DateTimeOffset.FromUnixTimeSeconds(TimeUpdated).LocalDateTime.ToString("yyyy-MM-dd")
                : "";
    }
}
