using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WorkshopManager.BBCode;

namespace WorkshopManager
{
    /// <summary>
    /// Writes the library out as Markdown files.
    ///
    /// The library itself is JSON, which is right for the app but poor as a
    /// long-term archive: it needs this program to be useful. Markdown is
    /// readable in any editor, greppable, and diffs cleanly in git - so the
    /// archive keeps its value even if the app does not.
    ///
    /// Each file carries YAML front matter, which keeps it machine-readable
    /// for tools like Obsidian without hurting a human reader.
    /// </summary>
    public static class ModLibraryExport
    {
        public static int Export(ModLibrary library, string folder)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("No folder given", nameof(folder));

            Directory.CreateDirectory(folder);

            var entries = library.Entries().OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var entry in entries)
            {
                File.WriteAllText(
                    Path.Combine(folder, FileNameFor(entry)),
                    BuildDocument(entry),
                    Encoding.UTF8);
            }

            File.WriteAllText(Path.Combine(folder, "index.md"), BuildIndex(entries), Encoding.UTF8);
            return entries.Count;
        }

        private static string FileNameFor(LibraryEntry entry)
        {
            var title = string.IsNullOrWhiteSpace(entry.Title) ? "mod" : entry.Title;

            var safe = new StringBuilder();
            foreach (var c in title)
            {
                safe.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '-' : c);
            }

            // The id goes first so the files sort and dedupe by something stable
            var name = $"{entry.ModId}-{safe}".Trim();
            if (name.Length > 100) name = name.Substring(0, 100).TrimEnd();

            return name + ".md";
        }

        private static string BuildDocument(LibraryEntry entry)
        {
            var document = new StringBuilder();

            document.AppendLine("---");
            document.AppendLine($"mod_id: \"{entry.ModId}\"");
            document.AppendLine($"app_id: \"{entry.AppId}\"");
            document.AppendLine($"title: {YamlString(entry.Title)}");
            if (entry.TimeUpdated > 0)
            {
                document.AppendLine($"updated: {DateTimeOffset.FromUnixTimeSeconds(entry.TimeUpdated).LocalDateTime:yyyy-MM-dd}");
            }
            document.AppendLine($"tags: {YamlList(entry.Tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0))}");
            document.AppendLine($"requires: {YamlList(entry.RequiredMods.Select(Describe))}");
            document.AppendLine($"requires_dlc: {YamlList(entry.RequiredDlc.Select(Describe))}");
            document.AppendLine($"requirements_checked: {entry.RequirementsChecked.ToString().ToLowerInvariant()}");
            document.AppendLine($"gone_from_steam: {entry.Banned.ToString().ToLowerInvariant()}");
            if (entry.FirstInstalled != default)
            {
                document.AppendLine($"first_installed: {entry.FirstInstalled:yyyy-MM-dd}");
            }
            document.AppendLine($"install_folder: {YamlString(entry.InstallDirectory)}");
            document.AppendLine($"workshop_url: https://steamcommunity.com/sharedfiles/filedetails/?id={entry.ModId}");
            document.AppendLine("---");
            document.AppendLine();

            document.AppendLine($"# {entry.Title}");
            document.AppendLine();

            if (entry.Banned)
            {
                document.AppendLine("> This mod is no longer available on the Steam Workshop.");
                document.AppendLine();
            }

            if (entry.RequiredMods.Count > 0 || entry.RequiredDlc.Count > 0)
            {
                document.AppendLine("## Requirements");
                document.AppendLine();
                foreach (var requirement in entry.RequiredMods)
                {
                    document.AppendLine($"- [{requirement.Name}](https://steamcommunity.com/sharedfiles/filedetails/?id={requirement.Id})");
                }
                foreach (var dlc in entry.RequiredDlc)
                {
                    document.AppendLine($"- DLC: [{dlc.Name}](https://store.steampowered.com/app/{dlc.Id})");
                }
                document.AppendLine();
            }

            document.AppendLine("## Description");
            document.AppendLine();
            document.AppendLine(string.IsNullOrWhiteSpace(entry.Description)
                ? "_No description was archived for this mod._"
                : BBCodeRenderer.ToMarkdown(entry.Description));

            return document.ToString();
        }

        private static string BuildIndex(List<LibraryEntry> entries)
        {
            var index = new StringBuilder();
            index.AppendLine("# Mod library");
            index.AppendLine();
            index.AppendLine($"{entries.Count} mods archived, exported {DateTime.Now:yyyy-MM-dd HH:mm}.");
            index.AppendLine();
            index.AppendLine("| Mod | Game | Requires | Status |");
            index.AppendLine("|---|---|---|---|");

            foreach (var entry in entries)
            {
                var requires = entry.RequiredMods.Count == 0
                    ? "-"
                    : string.Join(", ", entry.RequiredMods.Select(r => r.Name));

                index.AppendLine($"| [{Escape(entry.Title)}]({FileNameFor(entry)}) " +
                                 $"| {entry.AppId} " +
                                 $"| {Escape(requires)} " +
                                 $"| {(entry.Banned ? "gone from Steam" : "ok")} |");
            }

            return index.ToString();
        }

        private static string Describe(ModRequirement requirement) =>
            string.IsNullOrEmpty(requirement.Name) ? requirement.Id : $"{requirement.Name} ({requirement.Id})";

        private static string Escape(string text) =>
            (text ?? "").Replace("|", "\\|").Replace("\n", " ");

        private static string YamlString(string value) =>
            "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string YamlList(IEnumerable<string> values)
        {
            var items = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(YamlString).ToList();
            return items.Count == 0 ? "[]" : "[" + string.Join(", ", items) + "]";
        }
    }
}
