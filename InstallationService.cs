using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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

        public static string GetInfoFilePath(Settings settings, string defaultTargetDir, WorkshopItem mod)
        {
            var dir = ResolveTargetDir(settings, defaultTargetDir, mod.AppId);
            return Path.Combine(dir, $"mod_{mod.ModId}.info");
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

            void ReportProcessed(string operation)
            {
                installProgress.ProcessedMods++;
                installProgress.CurrentOperation = operation;
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
                        ReportProcessed($"Skipped mod {mod.ModId} (no game id)");
                        continue;
                    }

                    processedGameIds.Add(mod.AppId);

                    if (options.SkipInstalledMods &&
                        mod.Status != WorkshopItemStatus.UpdateAvailable &&
                        File.Exists(GetInfoFilePath(settings, targetDir, mod)))
                    {
                        mod.Status = WorkshopItemStatus.Skipped;
                        statusChanged?.Invoke(mod);
                        logger.Info($"Skipping already installed mod {mod.ModId} ({mod.Title})");
                        result.Skipped++;
                        ReportProcessed($"Skipped installed mod {mod.ModId}");
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
                            ReportProcessed($"Download failed for mod {mod.ModId}");
                            continue;
                        }

                        await InstallModAsync(mod, cancellationToken);
                        statusChanged?.Invoke(mod);
                        result.Installed++;
                        ReportProcessed($"Installed {installProgress.ProcessedMods} of {installProgress.TotalMods} mods");
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
                Directory.CreateDirectory(gameTarget);

                // Some games (e.g. mods shipping a "mods" folder) expect the
                // folder contents merged into the target; everything else is
                // installed as its own subfolder named after the mod id.
                string modsSubDir = Path.Combine(sourceDir, "mods");

                await Task.Run(() =>
                {
                    if (Directory.Exists(modsSubDir))
                    {
                        CopyDirectory(modsSubDir, gameTarget, cancellationToken);
                    }
                    else
                    {
                        string destDir = Path.Combine(gameTarget, mod.ModId);
                        Directory.CreateDirectory(destDir);
                        CopyDirectory(sourceDir, destDir, cancellationToken);
                    }
                }, cancellationToken);

                string infoFile = Path.Combine(gameTarget, $"mod_{mod.ModId}.info");
                await File.WriteAllTextAsync(
                    infoFile,
                    "# Mod Info\n" +
                    $"Steam Workshop ID: {mod.ModId}\n" +
                    $"Game ID: {mod.AppId}\n" +
                    $"Title: {mod.Title}\n" +
                    $"Time Updated: {mod.TimeUpdated}\n" +
                    $"Installation Date: {DateTime.Now}",
                    cancellationToken
                );

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
