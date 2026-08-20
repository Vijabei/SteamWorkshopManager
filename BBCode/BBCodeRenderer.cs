using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WorkshopManager.BBCode
{
    /// <summary>
    /// Turns a parsed description into the formats the app needs. All three
    /// walk the same tree, which is the whole reason the parser exists as a
    /// separate step: adding an output format costs one visitor, not a second
    /// parser.
    /// </summary>
    public static class BBCodeRenderer
    {
        // ------------------------------------------------------------ plain

        /// <summary>Readable text with the markup removed.</summary>
        public static string ToPlainText(string bbcode)
        {
            var builder = new StringBuilder();
            WritePlain(BBCodeParser.Parse(bbcode), builder);
            return Tidy(builder.ToString());
        }

        private static void WritePlain(IEnumerable<BBNode> nodes, StringBuilder output)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case BBText text:
                        output.Append(text.Text);
                        break;

                    case BBElement element:
                        switch (element.Name)
                        {
                            case "hr":
                                output.Append("\n----------\n");
                                break;
                            case "br":
                                output.Append('\n');
                                break;
                            case "*":
                                output.Append("\n  • ");
                                WritePlain(element.Children, output);
                                break;
                            case "img":
                            case "previewyoutube":
                                break; // a URL on its own line helps nobody here
                            case "url":
                                WritePlain(element.Children, output);
                                if (element.Attribute.Length > 0) output.Append($" ({element.Attribute})");
                                break;
                            case "h1":
                            case "h2":
                            case "h3":
                                output.Append('\n');
                                WritePlain(element.Children, output);
                                output.Append('\n');
                                break;
                            case "td":
                            case "th":
                                WritePlain(element.Children, output);
                                output.Append('\t');
                                break;
                            case "tr":
                                WritePlain(element.Children, output);
                                output.Append('\n');
                                break;
                            default:
                                WritePlain(element.Children, output);
                                break;
                        }
                        break;
                }
            }
        }

        // --------------------------------------------------------- markdown

        /// <summary>
        /// Markdown for the archive files. Deliberately plain: the point is a
        /// description that stays readable in any editor in ten years.
        /// </summary>
        public static string ToMarkdown(string bbcode)
        {
            var builder = new StringBuilder();
            WriteMarkdown(BBCodeParser.Parse(bbcode), builder, false);
            return Tidy(builder.ToString());
        }

        private static void WriteMarkdown(IEnumerable<BBNode> nodes, StringBuilder output, bool ordered)
        {
            var itemNumber = 0;

            foreach (var node in nodes)
            {
                switch (node)
                {
                    case BBText text:
                        output.Append(EscapeMarkdown(text.Text));
                        break;

                    case BBElement element:
                        switch (element.Name)
                        {
                            case "b":
                                Wrap(element, output, "**", ordered);
                                break;
                            case "i":
                                Wrap(element, output, "*", ordered);
                                break;
                            case "strike":
                            case "s":
                                Wrap(element, output, "~~", ordered);
                                break;
                            case "u":
                                // Markdown has no underline; emphasis is the
                                // closest thing that stays readable as source.
                                Wrap(element, output, "*", ordered);
                                break;
                            case "h1":
                                Heading(element, output, "## ");
                                break;
                            case "h2":
                                Heading(element, output, "### ");
                                break;
                            case "h3":
                                Heading(element, output, "#### ");
                                break;
                            case "hr":
                                output.Append("\n\n---\n\n");
                                break;
                            case "br":
                                output.Append("  \n");
                                break;
                            case "list":
                                output.Append('\n');
                                WriteMarkdown(element.Children, output, false);
                                output.Append('\n');
                                break;
                            case "olist":
                                output.Append('\n');
                                WriteMarkdown(element.Children, output, true);
                                output.Append('\n');
                                break;
                            case "*":
                            {
                                // Trimmed, because the source usually has a
                                // newline after each item and that would turn
                                // every list into a loose one.
                                itemNumber++;
                                var item = new StringBuilder();
                                WriteMarkdown(element.Children, item, false);
                                output.Append(ordered ? $"\n{itemNumber}. " : "\n- ")
                                      .Append(item.ToString().Trim());
                                break;
                            }
                            case "url":
                            {
                                var inner = new StringBuilder();
                                WriteMarkdown(element.Children, inner, ordered);
                                var label = inner.ToString().Trim();
                                var target = element.Attribute.Length > 0 ? element.Attribute : label;

                                if (label.Length == 0) label = target;
                                output.Append($"[{label}]({target})");
                                break;
                            }
                            case "img":
                            {
                                var source = element.Attribute.Length > 0
                                    ? element.Attribute
                                    : PlainOf(element.Children).Trim();
                                if (source.Length > 0) output.Append($"\n\n![]({source})\n\n");
                                break;
                            }
                            case "previewyoutube":
                            {
                                var id = element.Attribute.Split(';')[0];
                                if (id.Length > 0) output.Append($"\n\n[YouTube](https://www.youtube.com/watch?v={id})\n\n");
                                break;
                            }
                            case "code":
                                output.Append("\n\n```\n").Append(PlainOf(element.Children).Trim()).Append("\n```\n\n");
                                break;
                            case "quote":
                            {
                                var inner = new StringBuilder();
                                WriteMarkdown(element.Children, inner, ordered);
                                foreach (var line in inner.ToString().Trim().Split('\n'))
                                {
                                    output.Append("\n> ").Append(line.TrimEnd());
                                }
                                output.Append("\n\n");
                                break;
                            }
                            case "noparse":
                                output.Append(PlainOf(element.Children));
                                break;
                            case "th":
                            case "td":
                            {
                                var inner = new StringBuilder();
                                WriteMarkdown(element.Children, inner, ordered);
                                output.Append("| ").Append(inner.ToString().Trim().Replace("\n", " ")).Append(' ');
                                break;
                            }
                            case "tr":
                                output.Append('\n');
                                WriteMarkdown(element.Children, output, ordered);
                                output.Append('|');
                                break;
                            case "table":
                                output.Append('\n');
                                WriteMarkdown(element.Children, output, ordered);
                                output.Append('\n');
                                break;
                            default:
                                WriteMarkdown(element.Children, output, ordered);
                                break;
                        }
                        break;
                }
            }
        }

        private static void Wrap(BBElement element, StringBuilder output, string marker, bool ordered)
        {
            var inner = new StringBuilder();
            WriteMarkdown(element.Children, inner, ordered);

            var content = inner.ToString();
            if (content.Trim().Length == 0)
            {
                output.Append(content);
                return;
            }

            // Markers must hug the text or Markdown ignores them
            var leading = content.Length - content.TrimStart().Length;
            var trailing = content.Length - content.TrimEnd().Length;

            output.Append(content, 0, leading);
            output.Append(marker).Append(content.Trim()).Append(marker);
            output.Append(content, content.Length - trailing, trailing);
        }

        private static void Heading(BBElement element, StringBuilder output, string prefix)
        {
            var inner = new StringBuilder();
            WriteMarkdown(element.Children, inner, false);
            output.Append("\n\n").Append(prefix).Append(inner.ToString().Trim()).Append("\n\n");
        }

        private static string PlainOf(IEnumerable<BBNode> nodes)
        {
            var builder = new StringBuilder();
            WritePlain(nodes, builder);
            return builder.ToString();
        }

        private static string EscapeMarkdown(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                // Only the characters that would otherwise start formatting
                if (c is '*' or '_' or '`' or '#' or '[' or ']' or '<' or '>' or '|') builder.Append('\\');
                builder.Append(c);
            }
            return builder.ToString();
        }

        // -------------------------------------------------------------- rtf

        /// <summary>
        /// RTF for the detail pane's RichTextBox, coloured from the current
        /// theme. Images are dropped on purpose: the pane is a few hundred
        /// pixels tall and the mod's preview image is shown above it anyway.
        /// </summary>
        public static string ToRtf(string bbcode, Color textColor, Color linkColor, Color dimColor)
        {
            var body = new StringBuilder();
            WriteRtf(BBCodeParser.Parse(bbcode), body, false);

            var rtf = new StringBuilder();
            rtf.Append(@"{\rtf1\ansi\ansicpg1252\deff0");
            rtf.Append(@"{\fonttbl{\f0\fswiss Segoe UI;}{\f1\fmodern Consolas;}}");
            rtf.Append(@"{\colortbl;")
               .Append(RtfColor(textColor))
               .Append(RtfColor(linkColor))
               .Append(RtfColor(dimColor))
               .Append('}');
            rtf.Append(@"\f0\fs18\cf1 ");
            rtf.Append(body);
            rtf.Append('}');
            return rtf.ToString();
        }

        private static string RtfColor(Color c) =>
            $@"\red{c.R}\green{c.G}\blue{c.B};";

        private static void WriteRtf(IEnumerable<BBNode> nodes, StringBuilder output, bool ordered)
        {
            var itemNumber = 0;

            foreach (var node in nodes)
            {
                switch (node)
                {
                    case BBText text:
                        output.Append(EscapeRtf(text.Text));
                        break;

                    case BBElement element:
                        switch (element.Name)
                        {
                            case "b":
                                output.Append(@"{\b ");
                                WriteRtf(element.Children, output, ordered);
                                output.Append('}');
                                break;
                            case "i":
                                output.Append(@"{\i ");
                                WriteRtf(element.Children, output, ordered);
                                output.Append('}');
                                break;
                            case "u":
                                output.Append(@"{\ul ");
                                WriteRtf(element.Children, output, ordered);
                                output.Append('}');
                                break;
                            case "strike":
                            case "s":
                                output.Append(@"{\strike ");
                                WriteRtf(element.Children, output, ordered);
                                output.Append('}');
                                break;
                            case "h1":
                            case "h2":
                            case "h3":
                            {
                                var size = element.Name == "h1" ? 26 : element.Name == "h2" ? 22 : 20;
                                output.Append(@"\par{\b\fs").Append(size).Append(' ');
                                WriteRtf(element.Children, output, ordered);
                                output.Append(@"}\fs18\par ");
                                break;
                            }
                            case "hr":
                                output.Append(@"\par{\cf3 ").Append(new string('—', 24)).Append(@"}\par ");
                                break;
                            case "br":
                                output.Append(@"\line ");
                                break;
                            case "list":
                                output.Append(@"\par ");
                                WriteRtf(element.Children, output, false);
                                output.Append(@"\par ");
                                break;
                            case "olist":
                                output.Append(@"\par ");
                                WriteRtf(element.Children, output, true);
                                output.Append(@"\par ");
                                break;
                            case "*":
                                itemNumber++;
                                output.Append(@"\par\tab ");
                                output.Append(ordered ? EscapeRtf(itemNumber + ". ") : @"\u8226?  ");
                                WriteRtf(element.Children, output, false);
                                break;
                            case "url":
                            {
                                var label = new StringBuilder();
                                WriteRtf(element.Children, label, ordered);

                                var target = element.Attribute.Length > 0
                                    ? element.Attribute
                                    : PlainOf(element.Children).Trim();
                                var shown = label.ToString().Trim();
                                if (shown.Length == 0) shown = EscapeRtf(target);

                                // A proper hyperlink field: RichTextBox raises
                                // LinkClicked for these and hands us the target.
                                output.Append(@"{\field{\*\fldinst{HYPERLINK """)
                                      .Append(EscapeRtf(target))
                                      .Append(@"""}}{\fldrslt{\cf2\ul ")
                                      .Append(shown)
                                      .Append("}}}");
                                break;
                            }
                            case "img":
                            case "previewyoutube":
                                break;
                            case "code":
                                output.Append(@"\par{\f1 ").Append(EscapeRtf(PlainOf(element.Children).Trim())).Append(@"}\f0\par ");
                                break;
                            case "quote":
                                output.Append(@"\par{\li360\cf3 ");
                                WriteRtf(element.Children, output, ordered);
                                output.Append(@"}\li0\par ");
                                break;
                            case "noparse":
                                output.Append(EscapeRtf(PlainOf(element.Children)));
                                break;
                            case "td":
                            case "th":
                                WriteRtf(element.Children, output, ordered);
                                output.Append(@"\tab ");
                                break;
                            case "tr":
                                WriteRtf(element.Children, output, ordered);
                                output.Append(@"\par ");
                                break;
                            default:
                                WriteRtf(element.Children, output, ordered);
                                break;
                        }
                        break;
                }
            }
        }

        private static string EscapeRtf(string text)
        {
            var builder = new StringBuilder(text.Length);

            foreach (var c in text)
            {
                switch (c)
                {
                    case '\\': builder.Append(@"\\"); break;
                    case '{': builder.Append(@"\{"); break;
                    case '}': builder.Append(@"\}"); break;
                    case '\r': break;
                    case '\n': builder.Append(@"\par "); break;
                    default:
                        if (c > 127)
                        {
                            // RTF wants a signed 16 bit code point plus a
                            // replacement character for readers that cannot
                            // handle it.
                            builder.Append(@"\u")
                                   .Append(((short)c).ToString(CultureInfo.InvariantCulture))
                                   .Append('?');
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            return builder.ToString();
        }

        // ----------------------------------------------------------- shared

        /// <summary>Collapses the blank lines that block tags leave behind.</summary>
        private static string Tidy(string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd());
            var result = new List<string>();
            var blanks = 0;

            foreach (var line in lines)
            {
                if (line.Length == 0)
                {
                    blanks++;
                    if (blanks > 2) continue;
                }
                else
                {
                    blanks = 0;
                }

                result.Add(line);
            }

            return string.Join(Environment.NewLine, result).Trim();
        }
    }
}
