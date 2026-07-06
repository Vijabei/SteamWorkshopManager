using System;

namespace WorkshopManager
{
    public enum WorkshopItemStatus
    {
        Pending,
        Downloading,
        Installed,
        UpdateAvailable,
        Skipped,
        Failed
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

        public WorkshopItemStatus Status { get; set; } = WorkshopItemStatus.Pending;

        public string StatusText => Status switch
        {
            WorkshopItemStatus.Pending => "Pending",
            WorkshopItemStatus.Downloading => "Downloading...",
            WorkshopItemStatus.Installed => "Installed",
            WorkshopItemStatus.UpdateAvailable => "Update available",
            WorkshopItemStatus.Skipped => "Skipped (installed)",
            WorkshopItemStatus.Failed => "Failed",
            _ => Status.ToString()
        };

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
