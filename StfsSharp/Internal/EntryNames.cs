using System.Globalization;
using StfsSharp.Models;

namespace StfsSharp.Internal;

/// <summary>
/// Host-safety rules for STFS entry names. Package names are Latin1 byte
/// strings that may contain characters the host filesystem treats specially:
/// path separators, the drive-relative colon on Windows (<c>C:evil</c> would
/// resolve against another directory), and trailing dots/spaces that Win32
/// strips during path normalization (<c>".. "</c> normalizes to <c>".."</c>).
/// </summary>
internal static class EntryNames
{
    private const int MaxHostFileNameLength = 255;

    /// <summary>
    /// Returns true when <paramref name="name"/> is invalid for every walker,
    /// on every host: an empty name or one carrying a path separator.
    /// </summary>
    internal static bool IsStructurallyInvalidEntryName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return true;
        }

        if (name is "." or "..")
        {
            return false;
        }

        foreach (char c in name)
        {
            if (c is '/' or '\\')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="name"/> cannot be used as a host file
    /// name safely. Exact <c>"."</c>/<c>".."</c> records are not flagged here.
    /// </summary>
    internal static bool IsInvalidEntryName(string name)
    {
        if (IsStructurallyInvalidEntryName(name))
        {
            return true;
        }

        if (name is "." or "..")
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            if (name.Contains(':', StringComparison.Ordinal))
            {
                return true;
            }

            // Win32 strips trailing dots/spaces, so a name that normalizes to
            // nothing, to a parent reference, or to a different existing name
            // would escape or silently collide on extract.
            if (name.TrimEnd(' ', '.').Length != name.Length)
            {
                return true;
            }

            if (IsReservedWindowsName(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Replaces every host-unsafe character with <c>'_'</c>.
    /// </summary>
    internal static string SanitizeEntryName(string name)
    {
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (IsInvalidEntryChar(chars[i]))
            {
                chars[i] = '_';
            }
        }

        if (OperatingSystem.IsWindows())
        {
            if (chars.Length > 0 && IsReservedWindowsName(name))
            {
                chars[0] = '_';
            }

            int end = chars.Length;
            while (end > 0 && (chars[end - 1] == ' ' || chars[end - 1] == '.'))
            {
                end--;
            }

            bool normalizedAway = end == 0 || new string(chars, 0, end) is "." or "..";
            int replaceFrom = normalizedAway ? 0 : end;
            for (int i = replaceFrom; i < chars.Length; i++)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Resolves an entry name for extraction under <paramref name="policy"/>.
    /// Returns <c>false</c> when the entry should be skipped; otherwise
    /// <paramref name="resolvedName"/> is unique within
    /// <paramref name="usedNames"/> (the current destination directory).
    /// </summary>
    internal static bool TryResolveExtractionName(
        string name,
        ExtractNamePolicy policy,
        HashSet<string> usedNames,
        out string resolvedName,
        out bool renamed)
    {
        ArgumentNullException.ThrowIfNull(usedNames);
        renamed = false;

        switch (policy)
        {
            case ExtractNamePolicy.Sanitize:
                resolvedName = SanitizeEntryName(name);
                if (resolvedName.Length == 0 || resolvedName is "." or "..")
                {
                    resolvedName = "_";
                }

                renamed = !string.Equals(resolvedName, name, StringComparison.Ordinal);
                break;

            case ExtractNamePolicy.Skip:
                // A name that SanitizeEntryName would change is unsafe on this
                // host; skip it instead of creating a surprising replacement.
                if (IsInvalidEntryName(name) ||
                    !string.Equals(SanitizeEntryName(name), name, StringComparison.Ordinal))
                {
                    resolvedName = name;
                    return false;
                }

                resolvedName = name;
                break;

            case ExtractNamePolicy.Reject:
                if (IsInvalidEntryName(name))
                {
                    throw new InvalidDataException(
                        $"Filename '{name}' contains invalid character(s).");
                }

                resolvedName = name;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown extract name policy.");
        }

        string unique = MakeUniqueExtractionName(resolvedName, usedNames);
        if (!string.Equals(unique, resolvedName, StringComparison.Ordinal))
        {
            renamed = true;
        }

        resolvedName = unique;
        return true;
    }

    /// <summary>
    /// Adds <paramref name="name"/> to <paramref name="usedNames"/>, appending
    /// <c>~1</c>, <c>~2</c> … before the extension when it is already taken.
    /// </summary>
    private static string MakeUniqueExtractionName(string name, HashSet<string> usedNames)
    {
        if (usedNames.Add(name))
        {
            return name;
        }

        int dot = name.LastIndexOf('.');
        bool hasExtension = dot > 0 && dot < name.Length - 1;
        string stem = hasExtension ? name[..dot] : name;
        string extension = hasExtension ? name[dot..] : string.Empty;

        for (int index = 1; ; index++)
        {
            string suffix = "~" + index.ToString(CultureInfo.InvariantCulture);
            int maxStemLength = Math.Max(1, MaxHostFileNameLength - suffix.Length - extension.Length);
            string candidate = (stem.Length > maxStemLength ? stem[..maxStemLength] : stem) + suffix + extension;
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Returns true when <paramref name="name"/> is a DOS device name on
    /// Windows (case-insensitive, with or without an extension).
    /// </summary>
    internal static bool IsReservedWindowsName(string name)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(name))
        {
            return false;
        }

        int dot = name.IndexOf('.', StringComparison.Ordinal);
        string stem = (dot >= 0 ? name[..dot] : name).TrimEnd(' ');
        if (stem.Length == 0)
        {
            return false;
        }

        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (stem.Length == 4 &&
            (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
             stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            stem[3] is >= '1' and <= '9')
        {
            return true;
        }

        return false;
    }

    private static bool IsInvalidEntryChar(char c) =>
        c is '/' or '\\' ||
        (OperatingSystem.IsWindows() &&
         (c == ':' || c is '<' or '>' or '"' or '|' or '?' or '*' || c < ' '));
}
