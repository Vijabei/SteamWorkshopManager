using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace WorkshopManager
{
    /// <summary>
    /// Where an installed mod lives on disk: its own folder, the folder that
    /// holds it, and where its mod_&lt;id&gt;.info file sits.
    /// </summary>
    public class InstallLocation
    {
        public string ModId { get; set; } = "";

        /// <summary>
        /// The folder holding the mod's own folder: a game's install folder,
        /// or a collection folder below it.
        /// </summary>
        public string BaseDir { get; set; } = "";

        /// <summary>Name of the mod's own folder inside BaseDir.</summary>
        public string FolderName { get; set; } = "";

        /// <summary>
        /// True when the info file sits inside the mod's folder rather than
        /// beside it.
        /// </summary>
        public bool InfoInside { get; set; }

        /// <summary>
        /// Title of the collection whose folder this mod was installed into,
        /// empty when it sits directly in the game's install folder.
        /// </summary>
        public string CollectionTitle { get; set; } = "";

        /// <summary>
        /// Title of the linked collection whose "[title]" subfolder of the
        /// collection folder this mod was installed into, empty otherwise.
        /// </summary>
        public string LinkedCollectionTitle { get; set; } = "";

        /// <summary>Workshop version recorded at install time, 0 if unknown.</summary>
        public long TimeUpdated { get; set; }

        public string ModDir => Path.Combine(BaseDir, FolderName);

        public string InfoPath => Path.Combine(InfoInside ? ModDir : BaseDir, InfoFileName(ModId));

        public static string InfoFileName(string modId) => $"mod_{modId}.info";
    }

    /// <summary>
    /// Finds installed mods through the info files written at install time.
    ///
    /// The info file used to sit directly in the install folder, so a single
    /// File.Exists answered "is this mod installed?". With info files inside
    /// mod folders and mods inside collection folders, it can now be up to
    /// three levels further down:
    ///
    ///   install folder\mod_1.info                       (beside, the default)
    ///   install folder\Mod\mod_1.info                   (inside the mod folder)
    ///   install folder\Collection\mod_1.info            (beside, in a collection)
    ///   install folder\Collection\Mod\mod_1.info        (inside, in a collection)
    ///   install folder\Collection\[Linked]\Mod\mod_1.info
    ///                                    (inside, in a linked collection)
    ///
    /// The third level is only searched below folders named "[...]", the
    /// way linked collection folders are named, so the walk does not reach
    /// into the subfolders of every mod.
    ///
    /// Each install folder is scanned once, on first use, and remembered.
    /// Create a fresh index when the disk may have changed under it.
    /// </summary>
    public class InstalledIndex
    {
        private const int MaxDepth = 2;

        private readonly Dictionary<string, Dictionary<string, InstallLocation>> byRoot =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The installation of a mod below an install folder, or null.</summary>
        public InstallLocation Find(string root, string modId)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrEmpty(modId)) return null;
            return Scan(root).TryGetValue(modId, out var location) ? location : null;
        }

        /// <summary>Remembers an installation that was just written.</summary>
        public void Record(string root, InstallLocation location)
        {
            if (string.IsNullOrWhiteSpace(root) || location == null) return;
            Scan(root)[location.ModId] = location;
        }

        /// <summary>Every installation found below an install folder.</summary>
        public IReadOnlyCollection<InstallLocation> All(string root) =>
            string.IsNullOrWhiteSpace(root) ? Array.Empty<InstallLocation>() : Scan(root).Values;

        /// <summary>
        /// True if the directory is the folder of an installed mod - so a
        /// collection folder is never created inside one.
        /// </summary>
        public bool IsModFolder(string root, string directory) =>
            All(root).Any(l => SamePath(l.ModDir, directory));

        private Dictionary<string, InstallLocation> Scan(string root)
        {
            var key = Normalise(root);
            if (byRoot.TryGetValue(key, out var known)) return known;

            var found = new Dictionary<string, InstallLocation>();
            if (Directory.Exists(root)) Walk(root, 0, found);

            byRoot[key] = found;
            return found;
        }

        private static void Walk(string directory, int depth, Dictionary<string, InstallLocation> found)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "mod_*.info"))
                {
                    var location = Read(file);
                    if (location == null) continue;

                    // The same mod twice - say, left over after the info file
                    // location was switched by hand. The newer version is the
                    // one worth comparing against.
                    if (!found.TryGetValue(location.ModId, out var other) ||
                        location.TimeUpdated > other.TimeUpdated)
                    {
                        found[location.ModId] = location;
                    }
                }

                if (depth > MaxDepth) return;
                if (depth == MaxDepth && !IsLinkedCollectionFolder(directory)) return;

                foreach (var sub in Directory.EnumerateDirectories(directory))
                {
                    // Half-finished or outgoing copies from a mod update
                    if (sub.EndsWith(".installing", StringComparison.OrdinalIgnoreCase) ||
                        sub.EndsWith(".previous", StringComparison.OrdinalIgnoreCase)) continue;

                    Walk(sub, depth + 1, found);
                }
            }
            catch (Exception)
            {
                // An unreadable folder hides what is below it, nothing more
            }
        }

        private static bool IsLinkedCollectionFolder(string directory)
        {
            var name = Path.GetFileName(directory);
            return name.StartsWith("[", StringComparison.Ordinal) &&
                   (name.EndsWith("]", StringComparison.Ordinal) ||
                    name.EndsWith("] (collection)", StringComparison.Ordinal));
        }

        /// <summary>
        /// Reads the location fields from an info file's header. Info files
        /// from versions before the info file could move have no "Info Inside
        /// Folder" line - they all sit beside the mod - and the oldest have no
        /// "Folder" line either, from when every folder was named by its id.
        /// </summary>
        internal static InstallLocation Read(string infoPath)
        {
            try
            {
                string id = "", folder = "", inside = "", collection = "", linked = "";
                long timeUpdated = 0;

                foreach (var line in File.ReadLines(infoPath))
                {
                    if (line.StartsWith("# Description", StringComparison.OrdinalIgnoreCase)) break;

                    int separator = line.IndexOf(':');
                    if (separator < 0 || line.StartsWith("#")) continue;

                    var value = line.Substring(separator + 1).Trim();
                    switch (line.Substring(0, separator).Trim().ToLowerInvariant())
                    {
                        case "steam workshop id": id = value; break;
                        case "folder": folder = value; break;
                        case "info inside folder": inside = value; break;
                        case "collection": collection = value; break;
                        case "linked collection": linked = value; break;
                        case "time updated":
                            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out timeUpdated);
                            break;
                    }
                }

                if (string.IsNullOrEmpty(id)) return null;

                var infoDir = Path.GetDirectoryName(infoPath);
                var location = new InstallLocation
                {
                    ModId = id,
                    CollectionTitle = collection,
                    LinkedCollectionTitle = linked,
                    TimeUpdated = timeUpdated
                };

                if (inside.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    // The folder holding the info file is the mod's own
                    location.InfoInside = true;
                    location.BaseDir = Path.GetDirectoryName(infoDir);
                    location.FolderName = Path.GetFileName(infoDir);
                }
                else
                {
                    location.BaseDir = infoDir;
                    location.FolderName = string.IsNullOrEmpty(folder) ? id : folder;
                }

                return location;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Normalise(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return path;
            }
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Normalise(a), Normalise(b), StringComparison.OrdinalIgnoreCase);
    }
}
