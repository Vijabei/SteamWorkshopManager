using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopManager
{
    /// <summary>
    /// Saves the image gallery of a mod's Workshop page - the screenshots
    /// beyond the one preview the Web API returns. Archive mode only.
    ///
    /// The key-free API knows only the main preview, so the gallery is read
    /// from the Workshop page: one extra page request per mod plus one per
    /// image. That is many requests for a large collection, and Steam answers
    /// too many with a temporary block (HTTP 429). The option says so, the
    /// requests are spaced out, and on a block the gallery is skipped for the
    /// rest of the run instead of hammering on.
    ///
    /// It also depends on the page's markup and stops finding anything when
    /// Steam changes it. It then saves nothing; installing is not affected.
    /// </summary>
    public class GalleryService
    {
        /// <summary>Subfolder of a mod's folder that holds its gallery.</summary>
        public const string FolderName = "Workshop gallery";

        private const int DelayBetweenImagesMs = 400;

        // The page builds its image viewer from a script array:
        //   var rgFullScreenshotURLs = [
        //       { 'previewid' : '6849942', 'url': 'https://images.steamusercontent.com/ugc/221.../F54.../?imw=5000&...' },
        //   ];
        // Only screenshots are listed there - not the main preview, and not
        // videos, which are YouTube links.
        private static readonly Regex FullScreenshotPattern = new(
            @"\{\s*'previewid'\s*:\s*'(?<id>\d+)'\s*,\s*'url'\s*:\s*'(?<url>https://[^']+)'\s*\}",
            RegexOptions.Compiled);

        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private readonly Logger logger;

        /// <summary>
        /// Set once Steam has refused a request as too many. Later mods of the
        /// same run then skip the gallery.
        /// </summary>
        public bool Blocked { get; private set; }

        public GalleryService(Logger logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// The original-size URLs of the gallery images on a Workshop page,
        /// in page order. The query string asks Steam's image service for a
        /// letterboxed copy; without it the CDN serves the file as uploaded.
        /// </summary>
        public static List<string> Parse(string html)
        {
            var urls = new List<string>();
            if (string.IsNullOrEmpty(html)) return urls;

            var start = html.IndexOf("rgFullScreenshotURLs", StringComparison.Ordinal);
            if (start < 0) return urls;

            var end = html.IndexOf("];", start, StringComparison.Ordinal);
            var section = end > start ? html.Substring(start, end - start) : html.Substring(start);

            foreach (Match match in FullScreenshotPattern.Matches(section))
            {
                var url = match.Groups["url"].Value;
                var query = url.IndexOf('?');
                if (query >= 0) url = url.Substring(0, query);

                if (!urls.Contains(url)) urls.Add(url);
            }

            return urls;
        }

        /// <summary>
        /// Downloads the gallery of a mod into its folder, each image as the
        /// original file and dated by its upload. Returns the saved files with
        /// their dates, so they can be restored after the folder is stamped
        /// with the Workshop dates. Never throws for a mod that has no gallery
        /// or whose gallery cannot be read - only cancellation propagates.
        /// </summary>
        public async Task<List<(string Path, DateTime? Uploaded)>> SaveAsync(
            WorkshopItem mod, string modDir, CancellationToken cancellationToken)
        {
            var saved = new List<(string, DateTime?)>();
            if (Blocked) return saved;

            List<string> urls;
            try
            {
                urls = Parse(await RequirementsService.FetchPageAsync(mod.ModId, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                NoteFailure(mod, "read the Workshop page", ex);
                return saved;
            }

            if (urls.Count == 0) return saved;

            var directory = Path.Combine(modDir, FolderName);
            Directory.CreateDirectory(directory);

            for (int i = 0; i < urls.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (i > 0) await Task.Delay(DelayBetweenImagesMs, cancellationToken);

                try
                {
                    var file = await SaveImageAsync(urls[i], directory, i + 1, cancellationToken);
                    if (file.Path != null) saved.Add(file);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    NoteFailure(mod, $"save gallery image {i + 1} of {urls.Count}", ex);
                    if (Blocked) break;
                }
            }

            logger.Info($"Saved {saved.Count} of {urls.Count} gallery images of mod {mod.ModId}");
            return saved;
        }

        private static async Task<(string Path, DateTime? Uploaded)> SaveImageAsync(
            string url, string directory, int number, CancellationToken cancellationToken)
        {
            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var extension = ImageCache.ExtensionFor(bytes);
            if (extension == null) return (null, null);

            var file = Path.Combine(directory, FileName(number, OriginalName(response), extension));
            await File.WriteAllBytesAsync(file, bytes, cancellationToken);

            // The CDN serves the upload date, as for the preview image
            var uploaded = response.Content.Headers.LastModified?.UtcDateTime;
            ImageCache.StampDate(file, uploaded);

            return (file, uploaded);
        }

        /// <summary>
        /// The file name the author uploaded, which Steam still sends along.
        /// Worth keeping: it often holds the original capture date.
        /// </summary>
        private static string OriginalName(HttpResponseMessage response)
        {
            // Read raw: Steam ends the header with a stray ";", and .NET's
            // parser rejects the whole header for it.
            if (!response.Content.Headers.TryGetValues("Content-Disposition", out var values)) return "";
            return OriginalName(string.Join(";", values));
        }

        /// <summary>The file name in a Content-Disposition header, "" if none.</summary>
        internal static string OriginalName(string disposition)
        {
            if (string.IsNullOrEmpty(disposition)) return "";

            var encoded = Regex.Match(disposition, @"filename\*\s*=\s*[^']*'[^']*'(?<name>[^;]+)", RegexOptions.IgnoreCase);
            if (encoded.Success)
            {
                try
                {
                    return Uri.UnescapeDataString(encoded.Groups["name"].Value.Trim());
                }
                catch (Exception)
                {
                    // Badly encoded - try the plain form below
                }
            }

            var plain = Regex.Match(disposition, @"filename\s*=\s*""?(?<name>[^"";]+)", RegexOptions.IgnoreCase);
            return plain.Success ? plain.Groups["name"].Value.Trim() : "";
        }

        /// <summary>
        /// "01 - original name.jpg": numbered so the page order survives and
        /// two images with the same original name cannot collide.
        /// </summary>
        internal static string FileName(int number, string originalName, string extension)
        {
            var prefix = number.ToString("00");

            var stem = InstallationService.SanitiseFolderName(Path.GetFileNameWithoutExtension(originalName ?? ""));
            if (stem.Length > 100) stem = stem.Substring(0, 100).TrimEnd(' ', '.');

            return stem.Length == 0 ? prefix + extension : $"{prefix} - {stem}{extension}";
        }

        private void NoteFailure(WorkshopItem mod, string what, Exception ex)
        {
            if (ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
            {
                if (!Blocked)
                {
                    logger.Warning("Steam is refusing further requests for now (too many requests). " +
                                   "Gallery images are skipped for the rest of this run; the mods themselves " +
                                   "are installed as usual. Try again later for the missing galleries.");
                }
                Blocked = true;
                return;
            }

            logger.Warning($"Could not {what} for mod {mod.ModId}: {ex.Message}");
        }
    }
}
