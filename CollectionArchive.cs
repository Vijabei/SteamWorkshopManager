using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopManager
{
    /// <summary>
    /// Stores a collection's own details in its folder, the way every mod
    /// folder holds the mod's (#7): a collection_&lt;id&gt;.info with title,
    /// dates, links and description, the preview image, the background
    /// image of its Workshop page and a link to that page.
    ///
    /// Archive mode with collection folders only. The details were fetched
    /// together with the collection's title when it was added; only the
    /// background image needs a request of its own - one per collection, a
    /// handful per run. Nothing in here may fail an installation, so problems
    /// are logged and skipped.
    /// </summary>
    public class CollectionArchive
    {
        public static string InfoFileName(string collectionId) => $"collection_{collectionId}.info";

        private const string PreviewName = "collection_preview";
        private const string BackgroundName = "collection_background";

        // <img class="collectionBackgroundImage" id="CollectionBackgroundImage" src="...">
        private static readonly Regex BackgroundTag = new(
            @"<img\b[^>]*\bid=""CollectionBackgroundImage""[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SrcAttribute = new(
            @"\bsrc=""(?<url>https://[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly Logger logger;
        private readonly ModLayout layout;

        public CollectionArchive(Logger logger, ModLayout layout)
        {
            this.logger = logger;
            this.layout = layout;
        }

        /// <summary>
        /// The folder of each collection the mods were added from, as the
        /// installation will lay it out. A linked collection's folder is the
        /// "[title]" folder its mods go into, the collection's own folder the
        /// one above. A mod that is installed already counts only if it sits
        /// in the folder of this very collection - one installed from another
        /// collection stays where it is and says nothing about this one.
        /// </summary>
        public static Dictionary<CollectionNode, (string Directory, CollectionNode Parent)> PlanFolders(
            IEnumerable<WorkshopItem> mods, Func<WorkshopItem, InstallLocation> plan)
        {
            var folders = new Dictionary<CollectionNode, (string, CollectionNode)>();

            foreach (var mod in mods)
            {
                if (mod.Collection == null) continue;

                var location = plan(mod);
                if (location == null || string.IsNullOrEmpty(location.CollectionTitle)) continue;
                if (location.CollectionTitle != mod.Collection.Title) continue;

                var rootDir = location.BaseDir;
                if (mod.LinkedCollection != null)
                {
                    if (location.LinkedCollectionTitle != mod.LinkedCollection.Title) continue;

                    if (!folders.ContainsKey(mod.LinkedCollection))
                    {
                        folders[mod.LinkedCollection] = (location.BaseDir, mod.Collection);
                    }
                    rootDir = Path.GetDirectoryName(location.BaseDir);
                }
                else if (!string.IsNullOrEmpty(location.LinkedCollectionTitle))
                {
                    continue;
                }

                if (!folders.ContainsKey(mod.Collection)) folders[mod.Collection] = (rootDir, null);
            }

            return folders;
        }

        /// <summary>Writes one collection's details into its folder.</summary>
        public async Task SaveAsync(CollectionNode node, CollectionNode parent, string directory,
            IReadOnlyCollection<CollectionNode> linkedInRun, CancellationToken cancellationToken)
        {
            try
            {
                Directory.CreateDirectory(directory);

                var info = Path.Combine(directory, InfoFileName(node.Id));
                await File.WriteAllTextAsync(info, InfoText(node, parent, linkedInRun), cancellationToken);
                var dated = new List<string> { info };

                if (layout.WorkshopLink)
                {
                    var link = Path.Combine(directory, InstallationService.WorkshopLinkFileName);
                    await File.WriteAllTextAsync(link,
                        "[InternetShortcut]\r\nURL=" + node.WorkshopUrl + "\r\n", cancellationToken);
                    dated.Add(link);
                }

                if (layout.PreviewImage)
                {
                    await SaveImagesAsync(node, directory, cancellationToken);
                }

                // The text files say what the Workshop says, so they carry its
                // dates; the pictures keep their upload dates.
                if (layout.WorkshopDates && node.TimeUpdated > 0)
                {
                    var updated = DateTimeOffset.FromUnixTimeSeconds(node.TimeUpdated).UtcDateTime;
                    var created = node.TimeCreated > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(node.TimeCreated).UtcDateTime
                        : updated;

                    foreach (var file in dated)
                    {
                        File.SetCreationTimeUtc(file, created);
                        File.SetLastWriteTimeUtc(file, updated);
                    }
                }

                logger.Info($"Archived the details of collection {node.Id} ({node.Title})");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Warning($"Could not archive the details of collection {node.Id}: {ex.Message}");
            }
        }

        private async Task SaveImagesAsync(CollectionNode node, string directory, CancellationToken cancellationToken)
        {
            var preview = GalleryService.OriginalUrl(node.PreviewUrl);
            if (!string.IsNullOrWhiteSpace(preview))
            {
                await SaveImageAsync(preview, directory, PreviewName, "preview", node, cancellationToken);
            }

            // The large picture behind the collection's page is not in the API
            string background = null;
            try
            {
                background = ParseBackground(await RequirementsService.FetchPageAsync(node.Id, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Warning($"Could not read the Workshop page of collection {node.Id}: {ex.Message}");
            }

            if (background != null && background != preview)
            {
                await SaveImageAsync(background, directory, BackgroundName, "background image", node, cancellationToken);
            }
        }

        private async Task SaveImageAsync(string url, string directory, string name, string what,
            CollectionNode node, CancellationToken cancellationToken)
        {
            try
            {
                var image = await GalleryService.FetchOriginalAsync(url, cancellationToken);
                if (image.Extension == null)
                {
                    logger.Warning($"The {what} of collection {node.Id} is in a format that was not recognised; not saved");
                    return;
                }

                // A picture replaced by one in another format must not linger
                foreach (var old in Directory.EnumerateFiles(directory, name + ".*").ToList()) File.Delete(old);

                var file = Path.Combine(directory, name + image.Extension);
                await File.WriteAllBytesAsync(file, image.Bytes, cancellationToken);
                ImageCache.StampDate(file, image.Uploaded);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Warning($"Could not save the {what} of collection {node.Id}: {ex.Message}");
            }
        }

        /// <summary>
        /// The original-size URL of a collection page's background image, or
        /// null when it has none - most collections do not.
        /// </summary>
        public static string ParseBackground(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            var tag = BackgroundTag.Match(html);
            if (!tag.Success) return null;

            var src = SrcAttribute.Match(tag.Value);
            if (!src.Success) return null;

            var url = GalleryService.OriginalUrl(WebUtility.HtmlDecode(src.Groups["url"].Value));
            return url.Contains("/ugc/", StringComparison.Ordinal) ? url : null;
        }

        /// <summary>The collection_&lt;id&gt;.info text. Exposed for testing.</summary>
        public static string InfoText(CollectionNode node, CollectionNode parent,
            IReadOnlyCollection<CollectionNode> linkedInRun)
        {
            var text = new StringBuilder();
            text.Append("# Collection Info\n");
            text.Append($"Steam Workshop ID: {node.Id}\n");
            text.Append($"Title: {node.Title}\n");
            text.Append($"Workshop Page: {node.WorkshopUrl}\n");

            if (parent != null)
            {
                text.Append($"Linked From: {parent.Id}|{parent.Title}\n");
                text.Append($"Mods: {node.AllItemIds().Count()}\n");
            }
            else
            {
                text.Append($"Mods: {node.ItemIds.Distinct().Count()}\n");
                foreach (var linked in node.Linked.Where(l => linkedInRun.Contains(l)))
                {
                    text.Append($"Linked Collection: {linked.Id}|{linked.Title}\n");
                }
            }

            text.Append($"Time Updated: {node.TimeUpdated}\n");
            text.Append($"Time Updated (readable): {Readable(node.TimeUpdated)}\n");
            text.Append($"Time Created: {node.TimeCreated}\n");
            text.Append($"Time Created (readable): {Readable(node.TimeCreated)}\n");
            text.Append($"Preview Image: {node.PreviewUrl}\n");
            text.Append("\n# Description\n");
            text.Append(node.Description ?? "");
            return text.ToString();
        }

        private static string Readable(long unix) =>
            unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime.ToString("yyyy-MM-dd") : "";
    }
}
