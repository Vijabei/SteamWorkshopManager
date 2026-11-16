using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopManager
{
    /// <summary>
    /// Downloads workshop preview images and keeps them on disk, so a mod
    /// stays previewable offline and after its workshop page is gone.
    ///
    /// Images are fetched lazily - one request when a mod is selected - not
    /// for a whole collection at once.
    /// </summary>
    public static class ImageCache
    {
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkshopManager", "previews");

        private const int MaxMemoryEntries = 200;

        /// <summary>Longest edge kept in the cache; the pane is far smaller.</summary>
        private const int MaxDimension = 512;

        private static readonly Dictionary<string, Image> memory = new();
        private static readonly object gate = new();
        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };

        /// <summary>
        /// Returns the preview image for a mod, downloading it once and
        /// serving it from disk afterwards. Returns null when there is no
        /// preview or it cannot be fetched - callers show a placeholder.
        /// </summary>
        public static async Task<Image> GetAsync(string modId, string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(url)) return null;

            lock (gate)
            {
                if (memory.TryGetValue(modId, out var cached)) return cached;
            }

            try
            {
                var (bytes, _) = await LoadOriginalAsync(modId, url, cancellationToken);
                if (bytes == null) return null;

                var image = DecodeStandalone(bytes);
                if (image == null) return null;

                lock (gate)
                {
                    if (memory.Count >= MaxMemoryEntries) memory.Clear();
                    memory[modId] = image;
                }

                return image;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Broken URL, offline, unsupported format - a missing preview
                // must never disrupt browsing the list.
                return null;
            }
        }

        /// <summary>
        /// Saves the preview into a folder as the original file Steam serves -
        /// not the scaled-down bitmap the detail pane uses, so an animated GIF
        /// stays animated. The file is named workshop_preview with the
        /// extension its content calls for.
        ///
        /// Returns the path written and the date Steam gives the picture, or
        /// (null, null) when there is no preview or its format is unknown.
        /// Network and disk errors are thrown; the caller decides whether they
        /// matter.
        /// </summary>
        public static async Task<(string Path, DateTime? Uploaded)> SaveOriginalAsync(
            string modId, string url, string directory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(url)) return (null, null);

            var (bytes, uploaded) = await LoadOriginalAsync(modId, url, cancellationToken);
            if (bytes == null) return (null, null);

            // Naming a file .jpg when it is not one would only mislead, so an
            // unrecognised format is skipped rather than guessed.
            var extension = ExtensionFor(bytes);
            if (extension == null) return (null, null);

            var target = Path.Combine(directory, "workshop_preview" + extension);
            await File.WriteAllBytesAsync(target, bytes, cancellationToken);
            StampDate(target, uploaded);

            return (target, uploaded);
        }

        /// <summary>Gives a file the date Steam reports for the picture.</summary>
        public static void StampDate(string file, DateTime? uploadedUtc)
        {
            if (!uploadedUtc.HasValue) return;

            try
            {
                File.SetCreationTimeUtc(file, uploadedUtc.Value);
                File.SetLastWriteTimeUtc(file, uploadedUtc.Value);
            }
            catch (Exception)
            {
                // A preview with the wrong date is still a usable preview -
                // never fail anything over it.
            }
        }

        /// <summary>
        /// Returns the preview exactly as Steam serves it, from the disk cache
        /// when the cached copy belongs to this URL, otherwise freshly
        /// downloaded. Also returns the picture's upload date.
        ///
        /// The URL is recorded beside each cached image because the cache is
        /// keyed by mod id: when an author replaces the preview, the URL
        /// changes, and an archive should get the new picture, not the one
        /// that happened to be cached first.
        /// </summary>
        private static async Task<(byte[] Bytes, DateTime? UploadedUtc)> LoadOriginalAsync(
            string modId, string url, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(CacheDirectory);
            var file = Path.Combine(CacheDirectory, modId + ".img");
            var urlFile = Path.Combine(CacheDirectory, modId + ".url.txt");

            if (File.Exists(file) && CachedUrlMatches(urlFile, url))
            {
                // The cached file was stamped with the upload date when it
                // was downloaded, so its own date is the one to pass on.
                return (await File.ReadAllBytesAsync(file, cancellationToken),
                        File.GetLastWriteTimeUtc(file));
            }

            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0) return (null, null);

            await File.WriteAllBytesAsync(file, bytes, cancellationToken);
            await File.WriteAllTextAsync(urlFile, url, cancellationToken);

            // Steam serves the date the picture was uploaded, which matches
            // the mod's creation date. Keeping it means the file carries the
            // image's own date rather than the moment we happened to fetch it
            // - the same thing a browser does when you save a preview by hand.
            var uploaded = response.Content.Headers.LastModified?.UtcDateTime;
            StampDate(file, uploaded);

            return (bytes, uploaded);
        }

        private static bool CachedUrlMatches(string urlFile, string url)
        {
            try
            {
                return File.Exists(urlFile) &&
                       string.Equals(File.ReadAllText(urlFile).Trim(), url.Trim(), StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Tells the image format from its first bytes. Steam's CDN content
        /// type is not reliable enough for this, the bytes are.
        /// </summary>
        internal static string ExtensionFor(byte[] b)
        {
            if (b == null) return null;
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
            if (b.Length >= 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return ".gif";
            if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' &&
                b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
            if (b.Length >= 2 && b[0] == 'B' && b[1] == 'M') return ".bmp";
            return null;
        }

        /// <summary>
        /// Decodes image bytes into a bitmap that no longer depends on the
        /// source stream, and that has exactly one frame.
        ///
        /// Both matter. An Image created with Image.FromStream keeps reading
        /// from that stream for as long as it lives - animated GIFs fetch each
        /// frame lazily - so releasing the stream makes GDI+ fail later, deep
        /// inside PictureBox's paint. And a multi-frame image makes PictureBox
        /// start its ImageAnimator, which is what triggers those reads in the
        /// first place. Many workshop previews are animated GIFs.
        ///
        /// Flattening to the first frame loses the animation in a thumbnail
        /// barely 110 pixels tall, which is a fair trade for not crashing.
        /// </summary>
        private static Image DecodeStandalone(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);

            var width = decoded.Width;
            var height = decoded.Height;
            if (width <= 0 || height <= 0) return null;

            // Preview panes are small, so oversized artwork is scaled down
            // instead of being kept at full size in the cache.
            if (width > MaxDimension || height > MaxDimension)
            {
                var scale = Math.Min((double)MaxDimension / width, (double)MaxDimension / height);
                width = Math.Max(1, (int)(width * scale));
                height = Math.Max(1, (int)(height * scale));
            }

            var copy = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(copy))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(decoded, new Rectangle(0, 0, width, height));
            }

            return copy;
        }

        /// <summary>True if the preview is already available without network.</summary>
        public static bool IsCached(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId)) return false;

            lock (gate)
            {
                if (memory.ContainsKey(modId)) return true;
            }

            return File.Exists(Path.Combine(CacheDirectory, modId + ".img"));
        }
    }
}
