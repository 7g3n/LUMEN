using System.Globalization;
using System.Text;

namespace Lumen.Core.Profiles;

public enum PlayerNameError
{
    None = 0,
    Empty,
    TooLong,
    InvalidCharacters,
}

/// <summary>Outcome of validating a candidate player name.</summary>
public readonly record struct PlayerNameResult(bool IsValid, string Normalized, PlayerNameError Error)
{
    public string Message => Error switch
    {
        PlayerNameError.None => "",
        PlayerNameError.Empty => "Enter a name.",
        PlayerNameError.TooLong => $"Keep it to {PlayerName.MaxLength} characters or fewer.",
        PlayerNameError.InvalidCharacters => "That name has characters that aren't allowed.",
        _ => "Invalid name.",
    };
}

/// <summary>
/// Player-name rules (spec §5): 1–16 characters, Unicode/Japanese/alphanumeric and
/// common punctuation allowed, surrounding whitespace trimmed, no empty result, no
/// control or formatting characters. Length is counted in grapheme clusters so an
/// emoji or a combining sequence counts as one "character".
///
/// The name is display-only. The primary key is a separate UUID (see
/// <see cref="Profile.PlayerId"/>), so a rename never affects stored scores (§6, §80).
/// </summary>
public static class PlayerName
{
    public const int MinLength = 1;
    public const int MaxLength = 16;

    /// <summary>Trims the ends and collapses internal whitespace runs to a single space.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "";
        }

        var sb = new StringBuilder(raw.Length);
        bool pendingSpace = false;
        bool started = false;

        foreach (Rune rune in raw.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = started;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(rune.ToString());
            started = true;
        }

        return sb.ToString();
    }

    public static PlayerNameResult Validate(string? raw)
    {
        string normalized = Normalize(raw);

        if (normalized.Length == 0)
        {
            return new PlayerNameResult(false, normalized, PlayerNameError.Empty);
        }

        if (ContainsDisallowed(normalized))
        {
            return new PlayerNameResult(false, normalized, PlayerNameError.InvalidCharacters);
        }

        int graphemes = CountGraphemes(normalized);
        if (graphemes < MinLength)
        {
            return new PlayerNameResult(false, normalized, PlayerNameError.Empty);
        }

        if (graphemes > MaxLength)
        {
            return new PlayerNameResult(false, normalized, PlayerNameError.TooLong);
        }

        return new PlayerNameResult(true, normalized, PlayerNameError.None);
    }

    public static bool IsValid(string? raw) => Validate(raw).IsValid;

    public static int CountGraphemes(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        int count = 0;
        while (enumerator.MoveNext())
        {
            count++;
        }

        return count;
    }

    private static bool ContainsDisallowed(string value)
    {
        foreach (Rune rune in value.EnumerateRunes())
        {
            // U+FFFD appears when the input had an ill-formed / lone surrogate sequence.
            if (rune.Value == 0xFFFD)
            {
                return true;
            }

            // Joiners and variation selectors are Format-category but are required to
            // build emoji sequences, which we otherwise support.
            if (rune.Value is 0x200D or 0xFE0E or 0xFE0F)
            {
                continue;
            }

            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                    return true;
            }
        }

        return false;
    }
}
