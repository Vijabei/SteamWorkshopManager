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
        public SemanticVersion CurrentVersion { get; set; }
        public SemanticVersion LatestVersion { get; set; }
        public string ReleaseName { get; set; } = "";
        public string ReleasePageUrl { get; set; } = "";
        public string ZipDownloadUrl { get; set; } = "";
        public bool IsPreRelease { get; set; }

        public bool UpdateAvailable =>
            LatestVersion != null && LatestVersion.CompareTo(CurrentVersion) > 0;
    }

    /// <summary>
    /// Checks GitHub releases for a newer version and performs the
    /// self-update. The app is a portable single-file exe, so updating
    /// means: rename the running exe to .old (allowed by Windows),
    /// move the downloaded exe into place and restart.
    /// </summary>
    public class UpdateService
    {
        // /releases/latest deliberately excludes pre-releases, which is exactly
        // what the stable channel needs. The beta channel has to look at the
        // full list and pick the highest version itself.
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/Vijabei/SteamWorkshopManager/releases/latest";

        private const string AllReleasesUrl =
            "https://api.github.com/repos/Vijabei/SteamWorkshopManager/releases?per_page=30";

        private static readonly HttpClient http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            // GitHub's API rejects requests without a User-Agent
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamWorkshopManager-Updater");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        /// <summary>
        /// Reads the informational version, which keeps the "-beta.N" suffix.
        /// The assembly version drops it, so two betas of the same release
        /// would look identical and no beta update would ever be detected.
        /// </summary>
        public static SemanticVersion GetCurrentVersion()
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (SemanticVersion.TryParse(informational, out var parsed)) return parsed;

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            SemanticVersion.TryParse(
                assemblyVersion.Major + "." + assemblyVersion.Minor + "." + Math.Max(0, assemblyVersion.Build),
                out var fallback);
            return fallback;
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

        /// <summary>
        /// Looks for a newer release. On the beta channel pre-releases count
        /// too, so the newest build wins even when it is a beta.
        /// </summary>
        public async Task<UpdateInfo> CheckForUpdateAsync(bool includePreReleases, CancellationToken cancellationToken)
        {
            var current = GetCurrentVersion();

            var release = includePreReleases
                ? await FindNewestReleaseAsync(cancellationToken)
                : await GetStableReleaseAsync(cancellationToken);

            if (release == null) return new UpdateInfo { CurrentVersion = current };

            var info = Describe(release);
            info.CurrentVersion = current;
            return info;
        }

        private async Task<JObject> GetStableReleaseAsync(CancellationToken cancellationToken)
        {
            using var response = await http.GetAsync(LatestReleaseUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            return JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        private async Task<JObject> FindNewestReleaseAsync(CancellationToken cancellationToken)
        {
            using var response = await http.GetAsync(AllReleasesUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var releases = JArray.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            JObject newest = null;
            SemanticVersion newestVersion = null;

            foreach (var entry in releases.OfType<JObject>())
            {
                if ((bool?)entry["draft"] == true) continue;
                if (!SemanticVersion.TryParse((string)entry["tag_name"], out var version)) continue;

                if (newestVersion == null || version.CompareTo(newestVersion) > 0)
                {
                    newest = entry;
                    newestVersion = version;
                }
            }

            return newest;
        }

        private static UpdateInfo Describe(JObject release)
        {
            var tag = (string)release["tag_name"] ?? "";
            if (!SemanticVersion.TryParse(tag, out var version))
            {
                throw new Exception("Unexpected release tag format: " + tag);
            }

            var zipAsset = (release["assets"] as JArray)?
                .FirstOrDefault(a => ((string)a["name"] ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            return new UpdateInfo
            {
                LatestVersion = version,
                ReleaseName = (string)release["name"] ?? tag,
                ReleasePageUrl = (string)release["html_url"] ?? "https://github.com/Vijabei/SteamWorkshopManager/releases",
                ZipDownloadUrl = (string)zipAsset?["browser_download_url"] ?? "",
                IsPreRelease = (bool?)release["prerelease"] == true
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
