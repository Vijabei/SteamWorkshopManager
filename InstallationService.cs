using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Linq;

namespace WorkshopManager
{
    public class InstallationProgress
    {
        public int TotalMods { get; set; }
        public int ProcessedMods { get; set; }
        public string CurrentOperation { get; set; }
        public int ProgressPercentage => TotalMods == 0 ? 0 : (ProcessedMods * 100) / TotalMods;
    }

    public class InstallationService
    {
        private readonly Logger logger;
        private readonly string steamCmdPath;
        private readonly string targetDir;
        private readonly string scriptFile;
        private readonly bool cleanup;
        private string currentGameId;  // Neue Variable für die Game ID

        public InstallationService(Logger logger, string steamCmdPath, string targetDir, string scriptFile, bool cleanup)
        {
            this.logger = logger;
            this.steamCmdPath = steamCmdPath;
            this.targetDir = targetDir;
            this.scriptFile = scriptFile;
            this.cleanup = cleanup;
        }

        public async Task InstallModsAsync(IProgress<InstallationProgress> progress, CancellationToken cancellationToken)
        {
            var installProgress = new InstallationProgress();
            
            try
            {
                // Count total mods from script file
                installProgress.TotalMods = File.ReadAllLines(scriptFile)
                    .Count(line => line.Trim().StartsWith("workshop_download_item"));
                progress.Report(installProgress);

                // Run SteamCMD
                installProgress.CurrentOperation = "Running SteamCMD...";
                progress.Report(installProgress);
                
                await RunSteamCmdAsync(cancellationToken);

                // Get workshop directory path
                string workshopBase = Path.Combine(
                    Path.GetDirectoryName(steamCmdPath),
                    "steamapps",
                    "workshop",
                    "content"
                );

                if (!Directory.Exists(workshopBase))
                {
                    throw new Exception("Workshop directory not found");
                }

                // Process downloaded mods
                foreach (var gameDir in Directory.GetDirectories(workshopBase))
                {
                    string gameId = Path.GetFileName(gameDir);
                    currentGameId = gameId;  // Speichern der aktuellen Game ID
                    foreach (var modDir in Directory.GetDirectories(gameDir))
                    {
                        await ProcessModAsync(modDir, gameId, cancellationToken);
                        installProgress.ProcessedMods++;
                        installProgress.CurrentOperation = $"Processing mod {installProgress.ProcessedMods} of {installProgress.TotalMods}";
                        progress.Report(installProgress);
                    }
                }

                // Cleanup if requested
                if (cleanup)
                {
                    await CleanupWorkshopFilesAsync(cancellationToken);
                }
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

        private async Task RunSteamCmdAsync(CancellationToken cancellationToken)
        {
            logger.Info("Starting SteamCMD...");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = steamCmdPath,
                    Arguments = $"+runscript \"{scriptFile}\" +quit",
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
                    process.Kill(); 
                    tcs.TrySetCanceled();
                } 
                catch {}
            }))
            {
                process.Start();
                process.BeginOutputReadLine();
                
                await tcs.Task;

                if (process.ExitCode != 0)
                {
                    throw new Exception($"SteamCMD exited with code {process.ExitCode}");
                }
            }
        }

        private async Task ProcessModAsync(string modDir, string gameId, CancellationToken cancellationToken)
        {
            try
            {
                string modId = Path.GetFileName(modDir);
                logger.Info($"Processing mod {modId}...");

                var modsDir = Directory.GetDirectories(modDir, "mods").FirstOrDefault();
                if (modsDir != null)
                {
                    Directory.CreateDirectory(targetDir);
                    await Task.Run(() => CopyDirectory(modsDir, targetDir, cancellationToken));

                    string infoFile = Path.Combine(targetDir, $"mod_{modId}.info");
                    await File.WriteAllTextAsync(
                        infoFile,
                        $"# Mod Info\nSteam Workshop ID: {modId}\n" +
                        $"Game ID: {gameId}\nInstallation Date: {DateTime.Now}",
                        cancellationToken
                    );

                    logger.Info($"Mod {modId} installed successfully");
                }
                else
                {
                    logger.Warning($"No mods directory found in mod {modId}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error processing mod in directory {modDir}: {ex.Message}");
                throw;
            }
        }

        private async Task CleanupWorkshopFilesAsync(CancellationToken cancellationToken)
        {
            string workshopBase = Path.Combine(
                Path.GetDirectoryName(steamCmdPath),
                "steamapps",
                "workshop",
                "content"
            );

            logger.Info("Cleaning up workshop files...");

            if (Directory.Exists(workshopBase))
            {
                try
                {
                    if (string.IsNullOrEmpty(currentGameId))
                    {
                        throw new Exception("No game ID available for cleanup");
                    }

                    string gameDir = Path.Combine(workshopBase, currentGameId);
                    
                    if (Directory.Exists(gameDir))
                    {
                        await Task.Run(() =>
                        {
                            // Delete contents of specific game directory but keep the directory itself
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
                        
                        logger.Info($"Workshop content for game {currentGameId} cleaned up successfully");
                    }
                    else
                    {
                        logger.Warning($"Game directory {currentGameId} not found for cleanup");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Failed to cleanup workshop content: {ex.Message}");
                    throw;
                }
            }
            else
            {
                logger.Warning("Workshop directory not found for cleanup");
            }
        }

        private void CopyDirectory(string sourceDir, string targetDir, CancellationToken cancellationToken)
        {
            try
            {
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(targetDir, fileName);
                    File.Copy(file, destFile, true);
                }

                foreach (var dir in Directory.GetDirectories(sourceDir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    string dirName = Path.GetFileName(dir);
                    string destDir = Path.Combine(targetDir, dirName);
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