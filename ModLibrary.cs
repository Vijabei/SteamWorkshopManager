using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WorkshopManager
{
    /// <summary>One archived mod, as stored in the library file.</summary>
    public class LibraryEntry
    {
        public string ModId { get; set; } = "";
        public string AppId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Tags { get; set; } = "";
        public string PreviewUrl { get; set; } = "";
        public long TimeUpdated { get; set; }
        public bool Banned { get; set; }

        public List<ModRequirement> RequiredMods { get; set; } = new();
        public List<ModRequirement> RequiredDlc { get; set; } = new();
        public bool RequirementsChecked { get; set; }

        /// <summary>Where this mod was installed to, for reference.</summary>
        public string InstallDirectory { get; set; } = "";

        public DateTime FirstInstalled { get; set; }
        public DateTime LastInstalled { get; set; }

        public WorkshopItem ToWorkshopItem() => new()
        {
            ModId = ModId,
            AppId = AppId,
            Title = Title,
            Description = Description,
            Tags = Tags,
            PreviewUrl = PreviewUrl,
            TimeUpdated = TimeUpdated,
            Banned = Banned,
            RequiredMods = new List<ModRequirement>(RequiredMods),
            RequiredDlc = new List<ModRequirement>(RequiredDlc),
            RequirementsChecked = RequirementsChecked
        };
    }

    /// <summary>
    /// A flat-file inventory of every mod ever installed with this app.
    ///
    /// This is deliberately separate from the mod_&lt;id&gt;.info files, and the
    /// two have different jobs. An info file sits next to the installed mod
    /// and answers "is this currently installed?" - it disappears with the
    /// game folder, which is exactly right. The library sits outside the game
    /// and answers "what did I ever have, and what was it?" - it survives a
    /// wiped game folder, a reinstall, and the mod vanishing from Steam.
    ///
    /// Recording is not optional. The metadata cannot be fetched again once
    /// an item is gone from the Workshop, so anything not written at install
    /// time is lost for good.
    /// </summary>
    public class ModLibrary
    {
        private static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkshopManager", "library.json");

        private readonly string path;
        private readonly Dictionary<string, LibraryEntry> entries = new();
        private readonly object gate = new();

        public ModLibrary(string libraryPath = null)
        {
            path = string.IsNullOrWhiteSpace(libraryPath) ? DefaultPath : libraryPath;
            Load();
        }

        /// <summary>Where the library file lives.</summary>
        public string FilePath => path;

        public int Count
        {
            get { lock (gate) return entries.Count; }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(path)) return;

                var stored = JsonConvert.DeserializeObject<List<LibraryEntry>>(File.ReadAllText(path));
                if (stored == null) return;

                lock (gate)
                {
                    foreach (var entry in stored.Where(e => !string.IsNullOrEmpty(e.ModId)))
                    {
                        entries[entry.ModId] = entry;
                    }
                }
            }
            catch
            {
                // A damaged library must not stop the app; it is a cache of
                // metadata, not the source of truth for what is installed.
            }
        }

        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));

                List<LibraryEntry> snapshot;
                lock (gate)
                {
                    snapshot = entries.Values.OrderBy(e => e.Title).ToList();
                }

                File.WriteAllText(path, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Adds or refreshes a mod. Existing metadata is kept when the new
        /// record has nothing better - a mod installed from a plain script has
        /// no description, and that must not erase an archived one.
        /// </summary>
        public void Record(WorkshopItem item, string installDirectory)
        {
            if (item == null || string.IsNullOrEmpty(item.ModId)) return;

            lock (gate)
            {
                if (!entries.TryGetValue(item.ModId, out var entry))
                {
                    entry = new LibraryEntry { ModId = item.ModId, FirstInstalled = DateTime.Now };
                    entries[item.ModId] = entry;
                }

                entry.AppId = Prefer(item.AppId, entry.AppId);
                entry.Title = Prefer(item.Title, entry.Title);
                entry.Description = Prefer(item.Description, entry.Description);
                entry.Tags = Prefer(item.Tags, entry.Tags);
                entry.PreviewUrl = Prefer(item.PreviewUrl, entry.PreviewUrl);
                entry.InstallDirectory = Prefer(installDirectory, entry.InstallDirectory);

                if (item.TimeUpdated > 0) entry.TimeUpdated = item.TimeUpdated;
                entry.Banned = item.Banned;

                if (item.RequirementsChecked)
                {
                    entry.RequiredMods = new List<ModRequirement>(item.RequiredMods);
                    entry.RequiredDlc = new List<ModRequirement>(item.RequiredDlc);
                    entry.RequirementsChecked = true;
                }

                entry.LastInstalled = DateTime.Now;
            }
        }

        private static string Prefer(string candidate, string existing) =>
            string.IsNullOrWhiteSpace(candidate) ? existing : candidate;

        public LibraryEntry Find(string modId)
        {
            lock (gate)
            {
                return entries.TryGetValue(modId, out var entry) ? entry : null;
            }
        }

        /// <summary>The stored records themselves, for the Markdown export.</summary>
        public List<LibraryEntry> Entries()
        {
            lock (gate)
            {
                return entries.Values.ToList();
            }
        }

        public List<WorkshopItem> All()
        {
            lock (gate)
            {
                return entries.Values.OrderBy(e => e.Title).Select(e => e.ToWorkshopItem()).ToList();
            }
        }
    }
}
