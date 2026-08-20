using System;
using System.Collections.Generic;
using System.Text;

namespace WorkshopManager.BBCode
{
    /// <summary>A piece of a parsed document.</summary>
    public abstract class BBNode
    {
    }

    /// <summary>Literal text.</summary>
    public sealed class BBText : BBNode
    {
        public BBText(string text) => Text = text;

        public string Text { get; }

        public override string ToString() => Text;
    }

    /// <summary>A tag with its content, e.g. [b]...[/b] or [url=...]...[/url].</summary>
    public sealed class BBElement : BBNode
    {
        public BBElement(string name, string attribute = "")
        {
            Name = name;
            Attribute = attribute ?? "";
        }

        /// <summary>Lower-case tag name.</summary>
        public string Name { get; }

        /// <summary>Value after "=", empty when the tag had none.</summary>
        public string Attribute { get; }

        public List<BBNode> Children { get; } = new();
    }

    /// <summary>
    /// A small, deliberately forgiving parser for the BBCode dialect Steam
    /// uses in workshop descriptions.
    ///
    /// Forgiving is the point. Descriptions are written by hand in a web form,
    /// so unclosed tags, stray brackets and unknown tags are everyday
    /// occurrences. Anything this parser does not understand is passed through
    /// as literal text rather than throwing - a description must always be
    /// displayable, even when its markup is a mess.
    ///
    /// The parser has no dependencies and knows nothing about the app, so it
    /// can be lifted out into a library of its own if that is ever wanted.
    /// </summary>
    public static class BBCodeParser
    {
        /// <summary>
        /// Tags carrying content. Anything outside this set stays literal
        /// text, which keeps false positives like "[WIP]" in a title intact.
        /// </summary>
        private static readonly HashSet<string> ContainerTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "b", "i", "u", "strike", "s", "spoiler", "noparse",
            "url", "quote", "code", "h1", "h2", "h3",
            "list", "olist", "table", "tr", "th", "td",
            "img", "previewyoutube"
        };

        /// <summary>Tags that stand alone and never close.</summary>
        private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "hr", "br"
        };

        /// <summary>
        /// List items close themselves: the next [*] or the end of the list
        /// ends the previous one.
        /// </summary>
        private const string ListItem = "*";

        public static IReadOnlyList<BBNode> Parse(string input)
        {
            var root = new List<BBNode>();
            if (string.IsNullOrEmpty(input)) return root;

            // Open elements, innermost last. The list the parser appends to is
            // always the deepest open element's children.
            var open = new List<BBElement>();
            var text = new StringBuilder();

            List<BBNode> Current() => open.Count == 0 ? root : open[^1].Children;

            void FlushText()
            {
                if (text.Length == 0) return;
                Current().Add(new BBText(text.ToString()));
                text.Clear();
            }

            int position = 0;
            while (position < input.Length)
            {
                var bracket = input.IndexOf('[', position);
                if (bracket < 0)
                {
                    text.Append(input, position, input.Length - position);
                    break;
                }

                text.Append(input, position, bracket - position);

                if (!TryReadTag(input, bracket, out var name, out var attribute, out var isClosing, out var after))
                {
                    // Not a tag at all - a lone bracket is just a character
                    text.Append('[');
                    position = bracket + 1;
                    continue;
                }

                var known = ContainerTags.Contains(name) || VoidTags.Contains(name) || name == ListItem;
                if (!known)
                {
                    // Unknown tags stay visible rather than disappearing, so a
                    // description never silently loses content.
                    text.Append(input, bracket, after - bracket);
                    position = after;
                    continue;
                }

                FlushText();

                if (isClosing)
                {
                    CloseTag(open, name);
                }
                else if (VoidTags.Contains(name))
                {
                    Current().Add(new BBElement(name));
                }
                else if (name == ListItem)
                {
                    CloseTag(open, ListItem);           // end the previous item
                    var item = new BBElement(ListItem);
                    Current().Add(item);
                    open.Add(item);
                }
                else
                {
                    var element = new BBElement(name, attribute);
                    Current().Add(element);
                    open.Add(element);

                    if (name.Equals("noparse", StringComparison.OrdinalIgnoreCase))
                    {
                        // Everything up to [/noparse] is literal by definition
                        after = ReadRaw(input, after, "noparse", element);
                        open.Remove(element);
                    }
                }

                position = after;
            }

            FlushText();

            // Whatever is still open simply ends here - the content is already
            // attached to it, so nothing is lost.
            return root;
        }

        /// <summary>
        /// Closes the innermost matching tag. When there is no match the tag is
        /// ignored: a stray [/b] should not tear the document apart.
        /// </summary>
        private static void CloseTag(List<BBElement> open, string name)
        {
            for (int i = open.Count - 1; i >= 0; i--)
            {
                if (!open[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

                // Anything still open inside it is closed with it
                open.RemoveRange(i, open.Count - i);
                return;
            }
        }

        /// <summary>Reads [name], [name=value] or [/name] starting at a bracket.</summary>
        private static bool TryReadTag(string input, int start, out string name,
            out string attribute, out bool isClosing, out int after)
        {
            name = "";
            attribute = "";
            isClosing = false;
            after = start;

            var end = input.IndexOf(']', start + 1);
            if (end < 0) return false;

            var inner = input.Substring(start + 1, end - start - 1).Trim();
            if (inner.Length == 0) return false;

            // A newline inside means this was never a tag
            if (inner.IndexOf('\n') >= 0 || inner.IndexOf('[') >= 0) return false;

            if (inner[0] == '/')
            {
                isClosing = true;
                inner = inner.Substring(1).Trim();
                if (inner.Length == 0) return false;
            }

            var equals = inner.IndexOf('=');
            if (equals >= 0)
            {
                name = inner.Substring(0, equals).Trim();
                attribute = inner.Substring(equals + 1).Trim().Trim('"');
            }
            else
            {
                name = inner;
            }

            if (name.Length == 0) return false;

            name = name.ToLowerInvariant();
            after = end + 1;
            return true;
        }

        /// <summary>Consumes text verbatim until the matching closing tag.</summary>
        private static int ReadRaw(string input, int start, string tag, BBElement target)
        {
            var closing = "[/" + tag + "]";
            var end = input.IndexOf(closing, start, StringComparison.OrdinalIgnoreCase);

            if (end < 0)
            {
                target.Children.Add(new BBText(input.Substring(start)));
                return input.Length;
            }

            target.Children.Add(new BBText(input.Substring(start, end - start)));
            return end + closing.Length;
        }
    }
}
