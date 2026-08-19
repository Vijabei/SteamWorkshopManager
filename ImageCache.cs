using System;
using System.Collections.Generic;
using System.Drawing;
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
                    bytes = await http.GetByteArrayAsync(url, cancellationToken);
                    if (bytes.Length == 0) return null;
                    await File.WriteAllBytesAsync(file, bytes, cancellationToken);
                }

                // Copy into memory first: creating an Image straight from a
                // FileStream keeps the file locked for the image's lifetime.
                using var stream = new MemoryStream(bytes);
                var image = Image.FromStream(stream);

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
