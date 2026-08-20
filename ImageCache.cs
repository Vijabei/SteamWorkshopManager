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
                Directory.CreateDirectory(CacheDirectory);
                var file = Path.Combine(CacheDirectory, modId + ".img");

                byte[] bytes;
                if (File.Exists(file))
                {
                    bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                }
                else
                {
                    using var response = await http.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    if (bytes.Length == 0) return null;
                    await File.WriteAllBytesAsync(file, bytes, cancellationToken);

                    // Steam serves the date the picture was uploaded, which
                    // matches the mod's creation date. Keeping it means the
                    // cached file carries the image's own date rather than the
                    // moment we happened to fetch it - the same thing a browser
                    // does when you save a preview by hand.
                    var uploaded = response.Content.Headers.LastModified;
                    if (uploaded.HasValue)
                    {
                        try
                        {
                            File.SetCreationTimeUtc(file, uploaded.Value.UtcDateTime);
                            File.SetLastWriteTimeUtc(file, uploaded.Value.UtcDateTime);
                        }
                        catch (Exception)
                        {
                            // A cached preview with the wrong date is still a
                            // usable preview - never fail the download over it.
                        }
                    }
                }

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
