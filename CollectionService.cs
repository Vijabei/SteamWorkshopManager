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
        /// Resolves a workshop id (collection or single item) to a list of
        /// items with full metadata. Nested collections are resolved
        /// recursively; a plain mod id yields a single-item list.
        /// </summary>
        public async Task<List<WorkshopItem>> ResolveAsync(
            string workshopId,
            CancellationToken cancellationToken,
            IProgress<string> status = null)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>();
            var visitedCollections = new HashSet<string>();

            await CollectIdsAsync(workshopId, ids, seen, visitedCollections, 0, cancellationToken, status);

            // No children -> the id itself is a single workshop item
            if (ids.Count == 0) ids.Add(workshopId);

            return await GetDetailsAsync(ids, cancellationToken, status);
        }

        private async Task CollectIdsAsync(
            string collectionId,
            List<string> ids,
            HashSet<string> seen,
            HashSet<string> visitedCollections,
            int depth,
            CancellationToken cancellationToken,
            IProgress<string> status)
        {
            if (depth > MaxCollectionDepth || !visitedCollections.Add(collectionId)) return;

            status?.Report($"Resolving collection {collectionId}...");

            var form = new Dictionary<string, string>
            {
                ["collectioncount"] = "1",
                ["publishedfileids[0]"] = collectionId
            };

            var json = await PostFormAsync(GetCollectionDetailsUrl, form, cancellationToken);
            var details = (json["response"]?["collectiondetails"] as JArray)?.FirstOrDefault();
            if (details == null || (int?)details["result"] != 1) return;

            if (details["children"] is not JArray children) return;

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var id = (string)child["publishedfileid"];
                if (string.IsNullOrEmpty(id)) continue;

                // filetype 2 marks a nested collection
                if (((int?)child["filetype"] ?? 0) == 2)
                {
                    await CollectIdsAsync(id, ids, seen, visitedCollections, depth + 1, cancellationToken, status);
                }
                else if (seen.Add(id))
                {
                    ids.Add(id);
                }
            }
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
                    bool ok = d != null && (int?)d["result"] == 1;

                    result.Add(new WorkshopItem
                    {
                        ModId = id,
                        AppId = ok
                            ? ((string)d["consumer_app_id"] ?? (string)d["creator_app_id"] ?? fallbackAppId)
                            : fallbackAppId,
                        Title = ok ? ((string)d["title"] ?? $"Mod {id}") : $"Mod {id} (details unavailable)",
                        FileSize = ok ? ((long?)d["file_size"] ?? 0) : 0,
                        TimeUpdated = ok ? ((long?)d["time_updated"] ?? 0) : 0
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
}
