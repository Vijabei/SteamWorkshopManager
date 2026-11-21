using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopManager
{
    public class InstallationProgress
    {
        public int TotalMods { get; set; }
        public int ProcessedMods { get; set; }
        public string CurrentOperation { get; set; }
        public int ProgressPercentage => TotalMods == 0 ? 0 : (ProcessedMods * 100) / TotalMods;
    }

    public class InstallationOptions
    {
        public bool CleanupWorkshopFiles { get; set; }
        public bool SkipInstalledMods { get; set; } = true;
        public int BatchSize { get; set; } = 30;
        public int MaxRetries { get; set; } = 2;
    }

    public class InstallationResult
    {
        public int Installed { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
    }

    /// <summary>
    /// Downloads workshop items via SteamCMD (in batches, with retries) and
    /// installs them into the per-game target directory.
    /// </summary>
    public class InstallationService
    {
        private readonly Logger logger;
        private readonly string steamCmdPath;
        private readonly string targetDir;
        private readonly Settings settings;

        /// <summary>
        /// Installed mods as found on disk. Built lazily and kept for the
        /// lifetime of this service, which is one installation run; every mod
        /// installed in the run is recorded in it as it goes.
        /// </summary>
        private InstalledIndex index = new();

        public InstallationService(Logger logger, string steamCmdPath, string targetDir, Settings settings)
        {
            this.logger = logger;
            this.steamCmdPath = steamCmdPath;
            this.targetDir = targetDir;
            this.settings = settings;
        }

        private string WorkshopContentBase => Path.Combine(
            Path.GetDirectoryName(steamCmdPath),
            "steamapps",
            "workshop",
            "content"
        );

        /// <summary>
        /// Resolves the install directory for a game: a per-game rule from
        /// the settings wins, otherwise the global target directory is used.
        /// </summary>
        public static string ResolveTargetDir(Settings settings, string defaultTargetDir, string appId)
        {
            if (settings?.GameRules != null &&
                !string.IsNullOrEmpty(appId) &&
                settings.GameRules.TryGetValue(appId, out var rule) &&
                !string.IsNullOrWhiteSpace(rule?.TargetDirectory))
            {
                return rule.TargetDirectory;
            }

            return defaultTargetDir;
        }

        /// <summary>
        /// Turns a Workshop title into something a file system accepts.
        /// Returns "" when nothing usable is left, so callers fall back to
        /// the mod id.
        /// </summary>
        public static string SanitiseFolderName(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(title.Length);

            foreach (var c in title)
            {
                // Control characters survive GetInvalidFileNameChars on some
                // platforms, and they have no business in a folder name.
                if (Array.IndexOf(invalid, c) >= 0 || char.IsControl(c)) builder.Append(' ');
                else builder.Append(c);
            }

            // Collapse the runs of spaces the replacements leave behind
            var name = Regex.Replace(builder.ToString(), @"\s+", " ").Trim();

            // Windows rejects a trailing dot or space, and very long names
            // break once the path around them grows.
            name = name.TrimEnd('.', ' ');
            if (name.Length > 100) name = name.Substring(0, 100).TrimEnd('.', ' ');

            return name;
        }

        /// <summary>
        /// Where a mod's existing installation is, or null if it is not
        /// installed below its game's install folder.
        /// </summary>
        public static InstallLocation FindInstalled(
            Settings settings, string defaultTargetDir, WorkshopItem mod, InstalledIndex index)
        {
            var gameTarget = ResolveTargetDir(settings, defaultTargetDir, mod.AppId);
            return index.Find(gameTarget, mod.ModId);
        }

        /// <summary>
        /// Decides where a mod is installed.
        ///
        /// A mod that is already installed is updated where it is, whatever
        /// the settings say now. Moving it would mean deleting the old copy,
        /// and a mod that belongs to two collections would hop between their
        /// folders. New settings therefore apply to mods installed from then
        /// on - the same rule folders named by title have always followed.
        /// </summary>
        public static InstallLocation PlanLocation(
            Settings settings, string defaultTargetDir, WorkshopItem mod, InstalledIndex index)
        {
            var gameTarget = ResolveTargetDir(settings, defaultTargetDir, mod.AppId);

            var existing = index.Find(gameTarget, mod.ModId);
            if (existing != null) return existing;

            var (baseDir, collection) = CollectionDir(settings, gameTarget, mod, index);

            return new InstallLocation
            {
                ModId = mod.ModId,
                BaseDir = baseDir,
                FolderName = NewFolderName(settings, mod, baseDir),
                InfoInside = settings?.InfoFileInsideModFolder == true,
                CollectionTitle = collection
            };
        }

        /// <summary>
        /// The folder a new mod goes into: a folder named after the
        /// collection it was added from, when that option is on, otherwise
        /// the game's install folder itself.
        /// </summary>
        private static (string Dir, string Collection) CollectionDir(
            Settings settings, string gameTarget, WorkshopItem mod, InstalledIndex index)
        {
            if (settings?.CollectionFolders != true) return (gameTarget, "");

            var name = SanitiseFolderName(mod.CollectionTitle);
            if (name.Length == 0) return (gameTarget, "");

            var dir = Path.Combine(gameTarget, name);

            // A mod named like the collection may already have that folder.
            // Mods must never end up inside another mod.
            if (index.IsModFolder(gameTarget, dir)) dir = Path.Combine(gameTarget, name + " (collection)");

            return (dir, mod.CollectionTitle);
        }

        /// <summary>
        /// The name for a new mod's own folder. The id is the default because
        /// games and this app identify mods by it; titles are for people who
        /// archive mods.
        /// </summary>
        private static string NewFolderName(Settings settings, WorkshopItem mod, string baseDir)
        {
            var byTitle = settings != null &&
                          "Title".Equals(settings.ModFolderNaming, StringComparison.OrdinalIgnoreCase);

            if (!byTitle) return mod.ModId;

            var name = SanitiseFolderName(mod.Title);
            if (name.Length == 0) return mod.ModId;

            // Titles are not unique, and this mod is not installed yet - so a
            // folder of that name belongs to something else. Keep them apart
            // rather than merging two mods into one folder.
            if (Directory.Exists(Path.Combine(baseDir, name))) return $"{name} [{mod.ModId}]";

            return name;
        }

        /// <summary>
        /// Stamps a freshly installed mod with the dates the Workshop reports
        /// instead of the moment SteamCMD happened to write the files.
        ///
        /// Steam does not transmit the authors' original file dates - every
        /// download carries the time it was written - so this is the closest
        /// truthful answer available: the date this version was published.
        /// </summary>
        private void ApplyWorkshopDates(string root, WorkshopItem mod)
        {
            if (mod.TimeUpdated <= 0 || !Directory.Exists(root)) return;

            var updated = DateTimeOffset.FromUnixTimeSeconds(mod.TimeUpdated).UtcDateTime;
            var created = mod.TimeCreated > 0
                ? DateTimeOffset.FromUnixTimeSeconds(mod.TimeCreated).UtcDateTime
                : updated;

            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    File.SetCreationTimeUtc(file, created);
                    File.SetLastWriteTimeUtc(file, updated);
                }

                // Directories last: writing a file inside one updates its own
                // timestamp again, so stamping them first would be undone.
                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    Directory.SetCreationTimeUtc(dir, created);
                    Directory.SetLastWriteTimeUtc(dir, updated);
                }

                Directory.SetCreationTimeUtc(root, created);
                Directory.SetLastWriteTimeUtc(root, updated);
            }
            catch (Exception ex)
            {
                // Not worth failing an installation over - the mod is on disk
                logger.Warning($"Could not apply the workshop dates to {root}: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the "Time Updated" unix timestamp from a mod info file.
        /// Returns null for legacy info files that don't carry it.
        /// </summary>
        public static long? GetInstalledTimeUpdated(string infoFilePath)
        {
            try
            {
                if (!File.Exists(infoFilePath)) return null;

                foreach (var line in File.ReadAllLines(infoFilePath))
                {
                    if (line.StartsWith("Time Updated:", StringComparison.OrdinalIgnoreCase) &&
                        long.TryParse(line.Substring("Time Updated:".Length).Trim(),
                            NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
                // Unreadable info file -> treat as unknown
            }

            return null;
        }

        /// <summary>
        /// Reads a mod_&lt;id&gt;.info file back into a WorkshopItem.
        /// Returns null if the file carries no workshop id. Tolerates the
        /// shorter info files written by earlier versions, which have no
        /// tags, preview URL or description.
        /// </summary>
        public static WorkshopItem ReadInfoFile(string infoFilePath)
        {
            try
            {
                var item = new WorkshopItem { Status = WorkshopItemStatus.Installed };
                var description = new List<string>();
                bool inDescription = false;

                foreach (var line in File.ReadAllLines(infoFilePath))
                {
                    if (line.StartsWith("# Description", StringComparison.OrdinalIgnoreCase))
                    {
                        inDescription = true;
                        continue;
                    }

                    if (inDescription)
                    {
                        description.Add(line);
                        continue;
                    }

                    // Split on the first colon only - values such as the
                    // preview URL contain colons themselves.
                    int separator = line.IndexOf(':');
                    if (separator < 0 || line.StartsWith("#")) continue;

                    var key = line.Substring(0, separator).Trim();
                    var value = line.Substring(separator + 1).Trim();

                    switch (key.ToLowerInvariant())
                    {
                        case "steam workshop id": item.ModId = value; break;
                        case "game id": item.AppId = value; break;
                        case "title": item.Title = value; break;
                        case "collection": item.CollectionTitle = value; break;
                        case "tags": item.Tags = value; break;
                        case "preview image": item.PreviewUrl = value; break;
                        case "requires mod": AddRequirement(item.RequiredMods, value); break;
                        case "requires dlc": AddRequirement(item.RequiredDlc, value); break;
                        case "requirements checked":
                            item.RequirementsChecked = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "time updated":
                            if (long.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out var updated))
                            {
                                item.TimeUpdated = updated;
                            }
                            break;
                        case "time created":
                            if (long.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out var created))
                            {
                                item.TimeCreated = created;
                            }
                            break;
                    }
                }

                if (string.IsNullOrEmpty(item.ModId)) return null;

                item.Description = string.Join(Environment.NewLine, description).Trim();
                if (string.IsNullOrEmpty(item.Title)) item.Title = $"Mod {item.ModId}";

                return item;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Parses an "id|name" requirement line from an info file.</summary>
        private static void AddRequirement(List<ModRequirement> target, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            var parts = value.Split('|', 2);
            var id = parts[0].Trim();
            if (id.Length == 0) return;

            target.Add(new ModRequirement
            {
                Id = id,
                Name = parts.Length > 1 ? parts[1].Trim() : ""
            });
        }

        /// <summary>
        /// Collects every mod installed under the configured target
        /// directories by reading the mod_&lt;id&gt;.info files written at
        /// install time. Works entirely offline.
        /// </summary>
        public static List<WorkshopItem> LoadInstalledMods(Settings settings, string defaultTargetDir)
        {
            var directories = new List<string>();
            if (!string.IsNullOrWhiteSpace(defaultTargetDir)) directories.Add(defaultTargetDir);

            if (settings?.GameRules != null)
            {
                foreach (var rule in settings.GameRules.Values)
                {
                    if (!string.IsNullOrWhiteSpace(rule?.TargetDirectory))
                    {
                        directories.Add(rule.TargetDirectory);
                    }
                }
            }

            var mods = new List<WorkshopItem>();
            var seen = new HashSet<string>();
            var index = new InstalledIndex();

            foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // Includes info files inside mod folders and collection folders
                foreach (var location in index.All(directory))
                {
                    var item = ReadInfoFile(location.InfoPath);
                    if (item != null && seen.Add(item.ModId)) mods.Add(item);
                }
            }

            return mods;
        }

        public async Task<InstallationResult> InstallModsAsync(
            IReadOnlyList<WorkshopItem> mods,
            InstallationOptions options,
            IProgress<InstallationProgress> progress,
            CancellationToken cancellationToken,
            Action<WorkshopItem> statusChanged = null)
        {
            var result = new InstallationResult();
            var installProgress = new InstallationProgress { TotalMods = mods.Count };
            var processedGameIds = new HashSet<string>();
            var pending = new List<WorkshopItem>();

            // The disk may have changed since this service was created
            index = new InstalledIndex();

            // The message is built after the counter is raised, so a message
            // can name the number of mods finished including this one. Passing
            // a ready-made string would report the count from before it.
            void ReportProcessed(Func<string> describe)
            {
                installProgress.ProcessedMods++;
                installProgress.CurrentOperation = describe();
                progress.Report(installProgress);
            }

            try
            {
                // Pre-filter: mods without a game id can't be downloaded;
                // already installed mods are skipped on request.
                foreach (var mod in mods)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(mod.AppId))
                    {
                        mod.Status = WorkshopItemStatus.Failed;
                        statusChanged?.Invoke(mod);
                        logger.Error($"Mod {mod.ModId} has no game id and cannot be downloaded");
                        result.Failed++;
                        ReportProcessed(() => $"Skipped mod {mod.ModId} (no game id)");
                        continue;
                    }

                    processedGameIds.Add(mod.AppId);

                    if (options.SkipInstalledMods &&
                        mod.Status != WorkshopItemStatus.UpdateAvailable &&
                        FindInstalled(settings, targetDir, mod, index) != null)
                    {
                        mod.Status = WorkshopItemStatus.Skipped;
                        statusChanged?.Invoke(mod);
                        logger.Info($"Skipping already installed mod {mod.ModId} ({mod.Title})");
                        result.Skipped++;
                        ReportProcessed(() => $"Skipped installed mod {mod.ModId}");
                        continue;
                    }

                    pending.Add(mod);
                }

                // Download and install in batches to keep SteamCMD sessions
                // short (large sessions tend to run into timeouts).
                int batchSize = Math.Max(1, options.BatchSize);
                for (int offset = 0; offset < pending.Count; offset += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = pending.Skip(offset).Take(batchSize).ToList();
                    await DownloadBatchAsync(batch, options.MaxRetries, installProgress, progress,
                        cancellationToken, statusChanged);

                    foreach (var mod in batch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (mod.Status == WorkshopItemStatus.Failed)
                        {
                            result.Failed++;
                            ReportProcessed(() => $"Download failed for mod {mod.ModId}");
                            continue;
                        }

                        await InstallModAsync(mod, cancellationToken);
                        statusChanged?.Invoke(mod);
                        result.Installed++;
                        ReportProcessed(() => $"Installed {installProgress.ProcessedMods} of {installProgress.TotalMods} mods");
                    }
                }

                if (options.CleanupWorkshopFiles)
                {
                    await CleanupWorkshopFilesAsync(processedGameIds, cancellationToken);
                }

                logger.Info($"Installation finished: {result.Installed} installed, " +
                            $"{result.Skipped} skipped, {result.Failed} failed");
                return result;
            }
            catch (OperationCanceledException)
            {
                logger.Warning("Installation was cancelled by user");
                throw;
            }
            catch (Exception ex)
            {
                logger.Error($"Installation failed: {ex.Message}");
                throw;
            }
        }

        private async Task DownloadBatchAsync(
            List<WorkshopItem> batch,
            int maxRetries,
            InstallationProgress installProgress,
            IProgress<InstallationProgress> progress,
            CancellationToken cancellationToken,
            Action<WorkshopItem> statusChanged)
        {
            var remaining = batch.ToList();

            for (int attempt = 0; attempt <= maxRetries && remaining.Count > 0; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt > 0)
                {
                    logger.Warning($"Retrying {remaining.Count} incomplete downloads (attempt {attempt + 1})");
                    await Task.Delay(2000, cancellationToken);
                }

                foreach (var mod in remaining)
                {
                    mod.Status = WorkshopItemStatus.Downloading;
                    statusChanged?.Invoke(mod);
                }

                installProgress.CurrentOperation = $"Downloading {remaining.Count} mods via SteamCMD...";
                progress.Report(installProgress);

                await RunSteamCmdAsync(CollectionService.GenerateScript(remaining), cancellationToken);

                remaining = remaining.Where(mod => !DownloadSucceeded(mod)).ToList();
            }

            foreach (var mod in remaining)
            {
                mod.Status = WorkshopItemStatus.Failed;
                statusChanged?.Invoke(mod);
                logger.Error($"Download failed for mod {mod.ModId} ({mod.Title}) after retries. " +
                             "The item may require a Steam login or is no longer available.");
            }
        }

        private bool DownloadSucceeded(WorkshopItem mod)
        {
            var dir = Path.Combine(WorkshopContentBase, mod.AppId, mod.ModId);
            return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();
        }

        private async Task RunSteamCmdAsync(string scriptContent, CancellationToken cancellationToken)
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"workshop_script_{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken);

            try
            {
                logger.Info("Starting SteamCMD...");

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = steamCmdPath,
                        Arguments = $"+runscript \"{scriptPath}\"",
                        WorkingDirectory = Path.GetDirectoryName(steamCmdPath),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                var tcs = new TaskCompletionSource<bool>();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        logger.Info($"SteamCMD: {e.Data}");
                };

                process.Exited += (s, e) => tcs.TrySetResult(true);

                using (cancellationToken.Register(() =>
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        tcs.TrySetCanceled();
                    }
                    catch { }
                }))
                {
                    process.Start();
                    process.BeginOutputReadLine();

                    await tcs.Task;

                    // SteamCMD frequently exits non-zero even when downloads
                    // succeeded; success is verified per mod on disk instead.
                    if (process.ExitCode != 0)
                    {
                        logger.Warning($"SteamCMD exited with code {process.ExitCode}; verifying downloads on disk");
                    }
                }
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }

        private async Task InstallModAsync(WorkshopItem mod, CancellationToken cancellationToken)
        {
            try
            {
                logger.Info($"Installing mod {mod.ModId} ({mod.Title})...");

                string sourceDir = Path.Combine(WorkshopContentBase, mod.AppId, mod.ModId);
                string gameTarget = ResolveTargetDir(settings, targetDir, mod.AppId);

                var location = PlanLocation(settings, targetDir, mod, index);
                Directory.CreateDirectory(location.BaseDir);

                // Some games (e.g. mods shipping a "mods" folder) expect the
                // folder contents merged into the target; everything else is
                // installed as its own subfolder.
                string modsSubDir = Path.Combine(sourceDir, "mods");
                bool merged = Directory.Exists(modsSubDir);

                // Merged mods have no folder of their own to hold the info file
                if (merged) location.InfoInside = false;

                string installedTo = null;

                await Task.Run(() =>
                {
                    if (merged)
                    {
                        // The mod brings its own layout; the folder names in
                        // there are the game's business, not ours. Merged into
                        // a shared folder, it cannot be replaced as a whole,
                        // so files removed by an update stay behind here.
                        CopyDirectory(modsSubDir, location.BaseDir, cancellationToken);
                    }
                    else
                    {
                        ReplaceDirectory(sourceDir, location.ModDir, cancellationToken);
                        installedTo = location.ModDir;
                    }
                }, cancellationToken);

                // The order matters: files are added to the mod's folder
                // first - extras, then the info file if it lives in there -
                // because creating a file changes its folder's date, and the
                // dates are set last.
                (string Path, DateTime? Uploaded) preview = (null, null);
                if (installedTo != null)
                {
                    preview = await AddArchiveExtrasAsync(installedTo, mod, cancellationToken);
                }

                await WriteInfoFileAsync(location, mod,
                    settings?.UseWorkshopDates == true, cancellationToken);
                index.Record(gameTarget, location);

                if (installedTo != null && settings?.UseWorkshopDates == true)
                {
                    ApplyWorkshopDates(installedTo, mod);

                    // The picture has a date of its own - the day it was
                    // uploaded - and that is the truer one for it. Changing a
                    // file's date leaves its folder's alone.
                    if (preview.Path != null) ImageCache.StampDate(preview.Path, preview.Uploaded);
                }

                mod.Status = WorkshopItemStatus.Installed;
                logger.Info($"Mod {mod.ModId} installed successfully");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                mod.Status = WorkshopItemStatus.Failed;
                logger.Error($"Error installing mod {mod.ModId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes the raw workshop downloads for all games that were part of
        /// this run (not just the last one, which was a bug in earlier versions).
        /// </summary>
        private async Task CleanupWorkshopFilesAsync(IEnumerable<string> gameIds, CancellationToken cancellationToken)
        {
            logger.Info("Cleaning up workshop files...");

            if (!Directory.Exists(WorkshopContentBase))
            {
                logger.Warning("Workshop directory not found for cleanup");
                return;
            }

            foreach (var gameId in gameIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string gameDir = Path.Combine(WorkshopContentBase, gameId);
                if (!Directory.Exists(gameDir))
                {
                    logger.Warning($"Game directory {gameId} not found for cleanup");
                    continue;
                }

                try
                {
                    await Task.Run(() =>
                    {
                        // Delete contents of the game directory but keep the
                        // directory itself
                        foreach (var item in Directory.GetFileSystemEntries(gameDir))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (Directory.Exists(item))
                            {
                                Directory.Delete(item, true);
                            }
                            else
                            {
                                File.Delete(item);
                            }
                        }
                    }, cancellationToken);

                    logger.Info($"Workshop content for game {gameId} cleaned up successfully");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.Error($"Failed to cleanup workshop content for game {gameId}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Writes the mod_&lt;id&gt;.info file, beside the mod's folder or
        /// inside it.
        ///
        /// The description is archived here on purpose: once an item is
        /// removed from the Workshop its metadata can no longer be fetched, so
        /// it has to be captured at install time. One line per requirement
        /// keeps names containing separators harmless and makes the file
        /// readable by hand.
        ///
        /// With Workshop dates the file is dated by the Workshop too, so it
        /// leaves out the local installation date: the file would otherwise
        /// contradict its own date, and that date says when this copy was
        /// made rather than anything about the mod. Nothing reads the line
        /// back; the library keeps its own record of first installation.
        /// </summary>
        private static async Task WriteInfoFileAsync(
            InstallLocation location, WorkshopItem mod, bool workshopDates,
            CancellationToken cancellationToken)
        {
            string requirementLines =
                string.Concat(mod.RequiredMods.Select(r => $"Requires Mod: {r.Id}|{r.Name}\n")) +
                string.Concat(mod.RequiredDlc.Select(r => $"Requires DLC: {r.Id}|{r.Name}\n"));

            location.TimeUpdated = mod.TimeUpdated;

            await File.WriteAllTextAsync(
                location.InfoPath,
                "# Mod Info\n" +
                $"Steam Workshop ID: {mod.ModId}\n" +
                $"Game ID: {mod.AppId}\n" +
                $"Title: {mod.Title}\n" +
                $"Folder: {location.FolderName}\n" +
                // Lets the app find this file again when it is not beside
                // the mod folder, where older versions always put it.
                $"Info Inside Folder: {(location.InfoInside ? "yes" : "no")}\n" +
                $"Collection: {location.CollectionTitle}\n" +
                $"Time Updated: {mod.TimeUpdated}\n" +
                $"Time Updated (readable): {mod.TimeUpdatedText}\n" +
                $"Time Created: {mod.TimeCreated}\n" +
                $"Time Created (readable): {mod.TimeCreatedText}\n" +
                $"Tags: {mod.Tags}\n" +
                $"Preview Image: {mod.PreviewUrl}\n" +
                $"Workshop Page: {mod.WorkshopUrl}\n" +
                $"Requirements Checked: {(mod.RequirementsChecked ? "yes" : "no")}\n" +
                requirementLines +
                (workshopDates ? "" : $"Installation Date: {DateTime.Now}\n") +
                "\n# Description\n" +
                mod.Description,
                cancellationToken
            );
        }

        /// <summary>
        /// Adds the optional archive files to a mod's folder: the preview
        /// image and a shortcut to the Workshop page. Neither is worth
        /// failing an installation over, so problems are only logged.
        /// Returns where the preview went, so its date can be restored after
        /// the folder has been stamped.
        /// </summary>
        private async Task<(string Path, DateTime? Uploaded)> AddArchiveExtrasAsync(
            string modDir, WorkshopItem mod, CancellationToken cancellationToken)
        {
            (string Path, DateTime? Uploaded) preview = (null, null);

            if (settings?.CreateWorkshopLink == true)
            {
                try
                {
                    await File.WriteAllTextAsync(
                        Path.Combine(modDir, WorkshopLinkFileName),
                        "[InternetShortcut]\r\nURL=" + mod.WorkshopUrl + "\r\n",
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.Warning($"Could not create the Workshop link for mod {mod.ModId}: {ex.Message}");
                }
            }

            if (settings?.SavePreviewImage == true)
            {
                if (string.IsNullOrWhiteSpace(mod.PreviewUrl))
                {
                    logger.Info($"Mod {mod.ModId} has no preview image to save");
                }
                else
                {
                    try
                    {
                        preview = await ImageCache.SaveOriginalAsync(mod.ModId, mod.PreviewUrl, modDir, cancellationToken);
                        if (preview.Path == null)
                        {
                            logger.Warning($"The preview of mod {mod.ModId} is in a format that was not recognised; not saved");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.Warning($"Could not save the preview image of mod {mod.ModId}: {ex.Message}");
                    }
                }
            }

            return preview;
        }

        /// <summary>Name of the shortcut to the Workshop page in a mod folder.</summary>
        public const string WorkshopLinkFileName = "Workshop page.url";

        /// <summary>
        /// Installs a fresh copy of a mod in place of the old one.
        ///
        /// Copying over the old folder would leave behind every file the
        /// author removed in an update - a stale assembly is enough to break a
        /// mod. The new copy is assembled beside the old one first and only
        /// swapped in once it is complete, so a failed or cancelled copy
        /// leaves the installed version untouched.
        ///
        /// Only call this for a folder that belongs to this one mod.
        /// </summary>
        private void ReplaceDirectory(string sourceDir, string destDir, CancellationToken cancellationToken)
        {
            var staging = destDir + ".installing";
            var previous = destDir + ".previous";

            // Left over from a run that was killed halfway
            DeleteIfExists(staging);
            DeleteIfExists(previous);

            Directory.CreateDirectory(staging);
            try
            {
                CopyDirectory(sourceDir, staging, cancellationToken);
            }
            catch
            {
                TryDelete(staging);
                throw;
            }

            if (Directory.Exists(destDir))
            {
                // A rename either succeeds as a whole or not at all. Deleting
                // first could stop halfway on a locked file - say, the game is
                // running - and leave a half-deleted mod behind.
                try
                {
                    Directory.Move(destDir, previous);
                }
                catch
                {
                    TryDelete(staging);
                    throw;
                }
            }

            try
            {
                Directory.Move(staging, destDir);
            }
            catch
            {
                // Put the old version back rather than leave nothing at all
                if (Directory.Exists(previous) && !Directory.Exists(destDir)) Directory.Move(previous, destDir);
                TryDelete(staging);
                throw;
            }

            try
            {
                DeleteIfExists(previous);
            }
            catch (Exception ex)
            {
                // The new version is in place; the old copy is only clutter
                logger.Warning($"Installed, but could not remove the previous version at {previous}: {ex.Message}");
            }
        }

        private static void DeleteIfExists(string directory)
        {
            if (!Directory.Exists(directory)) return;

            // Directory.Delete refuses read-only files, and mods occasionally
            // ship some.
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(directory, recursive: true);
        }

        /// <summary>Cleanup on an error path: must not hide the original error.</summary>
        private static void TryDelete(string directory)
        {
            try { DeleteIfExists(directory); } catch { }
        }

        private void CopyDirectory(string sourceDir, string destinationDir, CancellationToken cancellationToken)
        {
            try
            {
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(destinationDir, fileName);
                    File.Copy(file, destFile, true);
                }

                foreach (var dir in Directory.GetDirectories(sourceDir))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string dirName = Path.GetFileName(dir);
                    string destDir = Path.Combine(destinationDir, dirName);
                    Directory.CreateDirectory(destDir);
                    CopyDirectory(dir, destDir, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new IOException($"Error copying directory: {ex.Message}", ex);
            }
        }
    }
}
