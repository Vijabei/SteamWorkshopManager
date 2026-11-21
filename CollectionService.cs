using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WorkshopManager
{
    /// <summary>
    /// Resolves Steam Workshop collections and item metadata locally via the
    /// official Steam Web API (no API key required). This replaces the
    /// server-side scraping previously done by softknight.de/generate.php.
    /// </summary>
    public class CollectionService
    {
        private const string GetCollectionDetailsUrl =
            "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/";
        private const string GetPublishedFileDetailsUrl =
            "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

        // Steam expects at most 100 ids per details request; a short delay
        // between batches keeps us well below any rate limit.
        private const int DetailsBatchSize = 100;
        private const int BatchDelayMs = 250;
        private const int MaxCollectionDepth = 5;

        private static readonly HttpClient http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamWorkshopManager/1.0");
            return client;
        }

        /// <summary>
        /// Extracts a workshop id from a full workshop URL or a plain numeric id.
        /// Returns null if no id can be found.
        /// </summary>
        public static string ExtractWorkshopId(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            input = input.Trim();

            if (Regex.IsMatch(input, @"^\d+$")) return input;

            var match = Regex.Match(input, @"[?&]id=(\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Reads the structure of a collection: its own mods and the
        /// collections it links to, recursively. Returns null when the id is
        /// not a collection - a single mod, or something Steam does not know -
        /// so the caller can treat it as one item.
        /// </summary>
        public async Task<CollectionNode> GetCollectionAsync(
            string workshopId,
            CancellationToken cancellationToken,
            IProgress<string> status = null)
        {
            var visited = new HashSet<string>();
            var root = await LoadNodeAsync(workshopId, visited, 0, cancellationToken, status);
            if (root == null) return null;

            // One request for the titles of every collection in the tree
            var nodes = root.SelfAndDescendants().ToList();
            try
            {
                var details = await GetDetailsAsync(nodes.Select(n => n.Id).ToList(), cancellationToken);
                foreach (var (node, detail) in nodes.Zip(details))
                {
                    // GetDetailsAsync puts a placeholder title on a failed
                    // lookup; only a real answer carries a timestamp. Without
                    // a title a collection gets no folder of its own.
                    node.Title = detail.TimeUpdated > 0 ? detail.Title : "";
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Titles are a nicety - the mods can be added without them
            }

            return root;
        }

        /// <summary>
        /// The mods of a collection with full metadata, the linked
        /// collections in <paramref name="includedLinked"/> among them.
        ///
        /// Every mod remembers the collection that was added, and - when it
        /// came from a linked collection - which one, so it can be installed
        /// into a subfolder of that name. A linked collection's own links
        /// count as part of it: the choice offered is one level deep, and so
        /// are the folders.
        ///
        /// A mod listed in several places belongs to the collection that was
        /// added if it is listed there, otherwise to the first linked
        /// collection that has it.
        /// </summary>
        public async Task<List<WorkshopItem>> GetItemsAsync(
            CollectionNode root,
            IReadOnlyCollection<string> includedLinked,
            CancellationToken cancellationToken,
            IProgress<string> status = null)
        {
            var (ids, linkedTitleOf) = PlanItems(root, includedLinked);
            if (ids.Count == 0) return new List<WorkshopItem>();

            var items = await GetDetailsAsync(ids, cancellationToken, status);
            foreach (var item in items)
            {
                item.CollectionTitle = root.Title;
                item.LinkedCollectionTitle = linkedTitleOf.TryGetValue(item.ModId, out var title) ? title : "";
            }

            return items;
        }

        /// <summary>
        /// Which mods a collection contributes, in order, and for each one
        /// that came in through a linked collection, that collection's title.
        /// Null for <paramref name="includedLinked"/> means all of them.
        /// </summary>
        internal static (List<string> Ids, Dictionary<string, string> LinkedTitleOf) PlanItems(
            CollectionNode root, IReadOnlyCollection<string> includedLinked)
        {
            var ids = new List<string>();
            var linkedTitleOf = new Dictionary<string, string>();
            var seen = new HashSet<string>();

            foreach (var id in root.ItemIds)
            {
                if (seen.Add(id)) ids.Add(id);
            }

            foreach (var linked in root.Linked)
            {
                if (includedLinked != null && !includedLinked.Contains(linked.Id)) continue;

                foreach (var id in linked.AllItemIds())
                {
                    if (!seen.Add(id)) continue;
                    ids.Add(id);
                    linkedTitleOf[id] = linked.Title;
                }
            }

            return (ids, linkedTitleOf);
        }

        private async Task<CollectionNode> LoadNodeAsync(
            string collectionId,
            HashSet<string> visited,
            int depth,
            CancellationToken cancellationToken,
            IProgress<string> status)
        {
            // Collections may link back to each other; each is read once
            if (depth > MaxCollectionDepth || !visited.Add(collectionId)) return null;

            status?.Report($"Resolving collection {collectionId}...");

            var form = new Dictionary<string, string>
            {
                ["collectioncount"] = "1",
                ["publishedfileids[0]"] = collectionId
            };

            var json = await PostFormAsync(GetCollectionDetailsUrl, form, cancellationToken);
            var details = (json["response"]?["collectiondetails"] as JArray)?.FirstOrDefault();
            if (details == null || (int?)details["result"] != 1) return null;

            // No children -> not a collection, or an empty one
            if (details["children"] is not JArray children || children.Count == 0) return null;

            var node = new CollectionNode { Id = collectionId };

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var id = (string)child["publishedfileid"];
                if (string.IsNullOrEmpty(id)) continue;

                // filetype 2 marks a linked collection
                if (((int?)child["filetype"] ?? 0) == 2)
                {
                    var linked = await LoadNodeAsync(id, visited, depth + 1, cancellationToken, status);
                    if (linked != null) node.Linked.Add(linked);
                }
                else
                {
                    node.ItemIds.Add(id);
                }
            }

            return node;
        }

        /// <summary>
        /// Fetches title, app id, size and update timestamp for the given
        /// workshop ids, batched to respect API limits.
        /// </summary>
        public async Task<List<WorkshopItem>> GetDetailsAsync(
            IReadOnlyList<string> ids,
            CancellationToken cancellationToken,
            IProgress<string> status = null,
            string fallbackAppId = "")
        {
            var result = new List<WorkshopItem>();

            for (int offset = 0; offset < ids.Count; offset += DetailsBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = ids.Skip(offset).Take(DetailsBatchSize).ToList();
                status?.Report($"Fetching mod details {offset + 1}-{offset + batch.Count} of {ids.Count}...");

                var form = new Dictionary<string, string> { ["itemcount"] = batch.Count.ToString() };
                for (int i = 0; i < batch.Count; i++)
                {
                    form[$"publishedfileids[{i}]"] = batch[i];
                }

                var json = await PostFormAsync(GetPublishedFileDetailsUrl, form, cancellationToken);
                var details = json["response"]?["publishedfiledetails"] as JArray ?? new JArray();

                var byId = new Dictionary<string, JToken>();
                foreach (var d in details)
                {
                    var id = (string)d["publishedfileid"];
                    if (id != null) byId[id] = d;
                }

                foreach (var id in batch)
                {
                    byId.TryGetValue(id, out var d);
                    int resultCode = d != null ? ((int?)d["result"] ?? 0) : 0;
                    bool ok = resultCode == 1;

                    // result 9 = not found: the item was deleted from the
                    // Workshop. Banned items answer with result 1 and
                    // banned = 1 instead - both mean "cannot be downloaded".
                    bool notFound = resultCode == 9;

                    result.Add(new WorkshopItem
                    {
                        ModId = id,
                        AppId = ok
                            ? ((string)d["consumer_app_id"] ?? (string)d["creator_app_id"] ?? fallbackAppId)
                            : fallbackAppId,
                        Title = ok
                            ? ((string)d["title"] ?? $"Mod {id}")
                            : (notFound ? $"Mod {id} (gone from Steam)" : $"Mod {id} (details unavailable)"),
                        FileSize = ok ? ((long?)d["file_size"] ?? 0) : 0,
                        TimeUpdated = ok ? ((long?)d["time_updated"] ?? 0) : 0,
                        TimeCreated = ok ? ((long?)d["time_created"] ?? 0) : 0,
                        Description = ok ? ((string)d["description"] ?? "") : "",
                        PreviewUrl = ok ? ((string)d["preview_url"] ?? "") : "",
                        Tags = ok ? string.Join(", ", (d["tags"] as JArray ?? new JArray())
                            .Select(t => (string)t["tag"]).Where(t => !string.IsNullOrEmpty(t))) : "",
                        // The API sends banned as 0/1, not as a boolean.
                        Banned = ok ? ((int?)d["banned"] ?? 0) != 0 : notFound
                    });
                }

                if (offset + DetailsBatchSize < ids.Count)
                {
                    await Task.Delay(BatchDelayMs, cancellationToken);
                }
            }

            return result;
        }

        private static async Task<JObject> PostFormAsync(
            string url,
            Dictionary<string, string> form,
            CancellationToken cancellationToken)
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await http.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return JObject.Parse(text);
        }

        /// <summary>
        /// Generates a SteamCMD script for the given items (same format the
        /// softknight.de generator produced).
        /// </summary>
        public static string GenerateScript(IEnumerable<WorkshopItem> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// Steam Workshop Download Commands");
            sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("@ShutdownOnFailedCommand 0");
            sb.AppendLine("@NoPromptForPassword 1");
            sb.AppendLine("force_install_dir ./");
            sb.AppendLine("login anonymous");

            foreach (var item in items)
            {
                sb.AppendLine($"workshop_download_item {item.AppId} {item.ModId}");
            }

            sb.AppendLine("quit");
            return sb.ToString();
        }

        /// <summary>
        /// Parses an existing SteamCMD script (e.g. generated on
        /// softknight.de) into workshop items. Titles/metadata can be
        /// enriched later via GetDetailsAsync.
        /// </summary>
        public static List<WorkshopItem> ParseScript(string path)
        {
            var items = new List<WorkshopItem>();
            var seen = new HashSet<string>();

            foreach (var line in File.ReadAllLines(path))
            {
                var match = Regex.Match(line.Trim(), @"^workshop_download_item\s+(\d+)\s+(\d+)");
                if (match.Success && seen.Add(match.Groups[2].Value))
                {
                    items.Add(new WorkshopItem
                    {
                        AppId = match.Groups[1].Value,
                        ModId = match.Groups[2].Value,
                        Title = $"Mod {match.Groups[2].Value}"
                    });
                }
            }

            return items;
        }
    }

    /// <summary>
    /// A Workshop collection as Steam lists it: its own mods and the
    /// collections it links to.
    /// </summary>
    public class CollectionNode
    {
        public string Id { get; set; } = "";

        /// <summary>Title, empty if Steam did not tell.</summary>
        public string Title { get; set; } = "";

        /// <summary>Mods listed directly in this collection.</summary>
        public List<string> ItemIds { get; } = new();

        /// <summary>Collections this one links to.</summary>
        public List<CollectionNode> Linked { get; } = new();

        public IEnumerable<CollectionNode> SelfAndDescendants()
        {
            yield return this;
            foreach (var linked in Linked)
            {
                foreach (var node in linked.SelfAndDescendants()) yield return node;
            }
        }

        /// <summary>Mods of this collection and everything it links to, without repeats.</summary>
        public IEnumerable<string> AllItemIds() =>
            SelfAndDescendants().SelectMany(n => n.ItemIds).Distinct();
    }
}
