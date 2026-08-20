using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopManager
{
    /// <summary>A required workshop item or DLC.</summary>
    public class ModRequirement
    {
        /// <summary>Workshop id for mods, Steam app id for DLC.</summary>
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        public override string ToString() =>
            string.IsNullOrEmpty(Name) ? Id : $"{Name} ({Id})";
    }

    public class ModRequirements
    {
        public List<ModRequirement> RequiredMods { get; } = new();
        public List<ModRequirement> RequiredDlc { get; } = new();
    }

    /// <summary>
    /// Reads the "Required Items" and "Required DLC" blocks from a workshop
    /// item page.
    ///
    /// Steam does not expose this through the key-free Web API: the
    /// ISteamRemoteStorage endpoint returns no children for regular items,
    /// and IPublishedFileService/GetDetails?includechildren=true answers 401
    /// without an API key. The item page is therefore the only key-free
    /// source, which means one HTTP request per mod - call this lazily, not
    /// for a whole collection at once.
    /// </summary>
    public class RequirementsService
    {
        // Each required item is an anchor carrying the workshop id wrapped
        // around a div holding the title.
        private static readonly Regex RequiredItemPattern = new(
            @"<a\s+href=""[^""]*[?&]id=(?<id>\d+)""[^>]*>\s*<div\s+class=""requiredItem"">\s*(?<name>[^<]*?)\s*</div>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The DLC name sits in its own span, itself wrapping a store link.
        private static readonly Regex RequiredDlcPattern = new(
            @"<span\s+class=""requiredDLCName"">\s*<a\s+href=""[^""]*store\.steampowered\.com/app/(?<id>\d+)[^""]*""[^>]*>\s*(?<name>[^<]*?)\s*</a>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HttpClient http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            // Steam serves a stripped placeholder page to clients that do not
            // look like a browser, so a realistic User-Agent is required.
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

            return client;
        }

        /// <summary>
        /// Fetches the requirements declared by a single workshop item.
        /// Returns an empty result when the item declares none; throws only
        /// on network/HTTP failures.
        /// </summary>
        public async Task<ModRequirements> FetchAsync(string modId, CancellationToken cancellationToken)
        {
            var url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={modId}";

            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            return Parse(html);
        }

        /// <summary>Parses an item page. Exposed for testing.</summary>
        public static ModRequirements Parse(string html)
        {
            var result = new ModRequirements();
            if (string.IsNullOrEmpty(html)) return result;

            // Only look inside the required items container - the page links
            // plenty of other workshop items (author's other files, etc.).
            var containerIndex = html.IndexOf("id=\"RequiredItems\"", StringComparison.OrdinalIgnoreCase);
            if (containerIndex >= 0)
            {
                // Cut at the comment that starts the next page panel, so the
                // author's other workshop links can never leak in. The cap
                // guards against a page layout without that marker.
                var sectionEnd = html.IndexOf("<!--", containerIndex, StringComparison.Ordinal);
                if (sectionEnd < 0 || sectionEnd - containerIndex > 20000)
                {
                    sectionEnd = Math.Min(html.Length, containerIndex + 20000);
                }

                var section = html.Substring(containerIndex, sectionEnd - containerIndex);

                foreach (Match match in RequiredItemPattern.Matches(section))
                {
                    Add(result.RequiredMods, match.Groups["id"].Value, match.Groups["name"].Value);
                }
            }

            foreach (Match match in RequiredDlcPattern.Matches(html))
            {
                Add(result.RequiredDlc, match.Groups["id"].Value, match.Groups["name"].Value);
            }

            return result;
        }

        private static void Add(List<ModRequirement> list, string id, string name)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (list.Exists(r => r.Id == id)) return;

            list.Add(new ModRequirement
            {
                Id = id,
                Name = WebUtilityDecode(name)
            });
        }

        private static string WebUtilityDecode(string value) =>
            string.IsNullOrEmpty(value) ? "" : System.Net.WebUtility.HtmlDecode(value).Trim();
    }
}
