using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace WorkshopManager
{
    public class UpdateInfo
    {
        public Version CurrentVersion { get; set; }
        public Version LatestVersion { get; set; }
        public string ReleaseName { get; set; } = "";
        public string ReleasePageUrl { get; set; } = "";
        public string ZipDownloadUrl { get; set; } = "";
        public bool UpdateAvailable => LatestVersion > CurrentVersion;
    }

    /// <summary>
    /// Checks GitHub releases for a newer version and performs the
    /// self-update. The app is a portable single-file exe, so updating
    /// means: rename the running exe to .old (allowed by Windows),
    /// move the downloaded exe into place and restart.
    /// </summary>
    public class UpdateService
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/Vijabei/SteamWorkshopManager/releases/latest";

        private static readonly HttpClient http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            // GitHub's API rejects requests without a User-Agent
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamWorkshopManager-Updater");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public static Version GetCurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            // Normalize to 3 components so 1.1.0.0 == tag v1.1.0
            return new Version(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build);
        }

        /// <summary>
        /// Deletes the leftover .old file from a previous update. Runs in
        /// the background because the old process keeps its exe locked
        /// until it has fully exited.
        /// </summary>
        public static void CleanupAfterUpdate()
        {
            try
            {
                string oldExe = Environment.ProcessPath + ".old";
                if (!File.Exists(oldExe)) return;

                Task.Run(async () =>
                {
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        try
                        {
                            File.Delete(oldExe);
                            return;
                        }
                        catch
                        {
                            await Task.Delay(1000);
                        }
                    }
                });
            }
            catch
            {
                // Never block startup because of cleanup
            }
        }

        public async Task<UpdateInfo> CheckForUpdateAsync(CancellationToken cancellationToken)
        {
            using var response = await http.GetAsync(LatestReleaseUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            string tag = ((string)json["tag_name"] ?? "").TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var latest))
            {
                throw new Exception($"Unexpected release tag format: {json["tag_name"]}");
            }

            var zipAsset = (json["assets"] as JArray)?
                .FirstOrDefault(a => ((string)a["name"] ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            return new UpdateInfo
            {
                CurrentVersion = GetCurrentVersion(),
                LatestVersion = new Version(latest.Major, latest.Minor, latest.Build < 0 ? 0 : latest.Build),
                ReleaseName = (string)json["name"] ?? tag,
                ReleasePageUrl = (string)json["html_url"] ?? "https://github.com/Vijabei/SteamWorkshopManager/releases",
                ZipDownloadUrl = (string)zipAsset?["browser_download_url"] ?? ""
            };
        }

        /// <summary>
        /// Downloads the release zip, swaps the running exe and restarts
        /// the application. Throws if anything goes wrong before the swap;
        /// the swap itself is rolled back on failure.
        /// </summary>
        public async Task DownloadAndApplyAsync(UpdateInfo update, IProgress<string> status, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(update.ZipDownloadUrl))
            {
                throw new Exception("The release has no zip asset to download.");
            }

            status?.Report("Downloading update...");
            var zipBytes = await http.GetByteArrayAsync(update.ZipDownloadUrl, cancellationToken);

            status?.Report("Extracting update...");
            string tempExe = Path.Combine(Path.GetTempPath(), $"WorkshopManager_update_{Guid.NewGuid():N}.exe");

            using (var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
            {
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.Name.Equals("WorkshopManager.exe", StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    throw new Exception("WorkshopManager.exe not found in the release zip.");
                }

                entry.ExtractToFile(tempExe, overwrite: true);
            }

            var newInfo = new FileInfo(tempExe);
            if (newInfo.Length < 100_000)
            {
                File.Delete(tempExe);
                throw new Exception("Downloaded file looks incomplete.");
            }

            status?.Report("Installing update...");
            string currentExe = Environment.ProcessPath
                ?? throw new Exception("Could not determine the running executable path.");
            string oldExe = currentExe + ".old";

            if (File.Exists(oldExe)) File.Delete(oldExe);

            // Windows allows renaming a running exe - only overwriting is blocked
            File.Move(currentExe, oldExe);
            try
            {
                File.Move(tempExe, currentExe);
            }
            catch
            {
                // Roll back so the app keeps working from its old file
                File.Move(oldExe, currentExe);
                throw;
            }

            status?.Report("Restarting...");
            Process.Start(new ProcessStartInfo
            {
                FileName = currentExe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(currentExe) ?? ""
            });

            Application.Exit();
        }
    }
}
