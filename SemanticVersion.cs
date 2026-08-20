using System;

namespace WorkshopManager
{
    /// <summary>
    /// Version with pre-release support, as used by the release tags.
    ///
    /// The assembly version cannot be used for this: it only carries the
    /// numeric parts, so 1.2.0-beta.1 and 1.2.0-beta.2 both look like 1.2.0
    /// and no beta update would ever be detected.
    /// </summary>
    public sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }

        /// <summary>Empty for a stable release, e.g. "beta.3" otherwise.</summary>
        public string PreRelease { get; private set; } = "";

        public bool IsPreRelease => PreRelease.Length > 0;

        public static bool TryParse(string text, out SemanticVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            text = text.Trim().TrimStart('v', 'V');

            // Build metadata ("+<commit sha>") carries no ordering
            var plus = text.IndexOf('+');
            if (plus >= 0) text = text.Substring(0, plus);

            var preRelease = "";
            var dash = text.IndexOf('-');
            if (dash >= 0)
            {
                preRelease = text.Substring(dash + 1);
                text = text.Substring(0, dash);
            }

            var parts = text.Split('.');
            if (parts.Length < 2) return false;
            if (!int.TryParse(parts[0], out var major)) return false;
            if (!int.TryParse(parts[1], out var minor)) return false;

            var patch = 0;
            if (parts.Length > 2 && !int.TryParse(parts[2], out patch)) return false;

            version = new SemanticVersion
            {
                Major = major,
                Minor = minor,
                Patch = patch,
                PreRelease = preRelease
            };
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            if (other is null) return 1;

            if (Major != other.Major) return Major.CompareTo(other.Major);
            if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
            if (Patch != other.Patch) return Patch.CompareTo(other.Patch);

            // A finished release outranks any pre-release of the same numbers,
            // so 1.2.0 wins over 1.2.0-beta.3.
            if (!IsPreRelease && !other.IsPreRelease) return 0;
            if (!IsPreRelease) return 1;
            if (!other.IsPreRelease) return -1;

            return ComparePreRelease(PreRelease, other.PreRelease);
        }

        private static int ComparePreRelease(string left, string right)
        {
            var a = left.Split('.');
            var b = right.Split('.');

            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                // A shorter pre-release ranks lower: beta < beta.1
                if (i >= a.Length) return -1;
                if (i >= b.Length) return 1;

                bool aNumeric = int.TryParse(a[i], out var aValue);
                bool bNumeric = int.TryParse(b[i], out var bValue);

                if (aNumeric && bNumeric)
                {
                    if (aValue != bValue) return aValue.CompareTo(bValue);
                }
                else if (aNumeric) return -1;   // numeric ranks below alphanumeric
                else if (bNumeric) return 1;
                else
                {
                    var comparison = string.CompareOrdinal(a[i], b[i]);
                    if (comparison != 0) return comparison;
                }
            }

            return 0;
        }

        public override string ToString() =>
            IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";
    }
}
