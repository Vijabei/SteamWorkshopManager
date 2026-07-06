using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopManager
{
    /// <summary>
    /// Downloads and extracts SteamCMD from the official Valve CDN so users
    /// don't have to set it up manually.
    /// </summary>
    public static class SteamCmdDownloader
    {
        private const string DownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

        /// <summary>
        /// Downloads steamcmd.zip into a temp file, extracts it to
        /// <paramref name="targetFolder"/> and returns the path to steamcmd.exe.
        /// </summary>
        public static async Task<string> DownloadAndExtractAsync(string targetFolder, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(targetFolder);

            string zipPath = Path.Combine(Path.GetTempPath(), $"steamcmd_{Guid.NewGuid():N}.zip");

            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
                {
                    var bytes = await http.GetByteArrayAsync(DownloadUrl, cancellationToken);
                    await File.WriteAllBytesAsync(zipPath, bytes, cancellationToken);
                }

                ZipFile.ExtractToDirectory(zipPath, targetFolder, overwriteFiles: true);
            }
            finally
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
            }

            string exePath = Path.Combine(targetFolder, "steamcmd.exe");
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("steamcmd.exe was not found after extraction.", exePath);
            }

            return exePath;
        }
    }
}
