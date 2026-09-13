using System.Text.RegularExpressions;

namespace DLMngrApp;

public static partial class UrlNormalizer
{
    [GeneratedRegex("https?://[^\\s<>\"'`]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlLikeRegex();

    public static bool TryNormalizeUrl(string? input, out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var candidate = NormalizeText(input);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) &&
            parsed.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            uri = parsed;
            return true;
        }

        var match = UrlLikeRegex().Match(candidate);
        if (match.Success)
        {
            var extracted = CleanupUrl(match.Value);
            if (Uri.TryCreate(extracted, UriKind.Absolute, out var extractedUri) &&
                extractedUri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                uri = extractedUri;
                return true;
            }
        }

        var fallback = FindUrlInsideText(input);
        if (fallback is not null)
        {
            candidate = fallback;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var fallbackParsed) &&
                fallbackParsed.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                uri = fallbackParsed;
                return true;
            }
        }

        return false;
    }

    public static string NormalizeText(string input)
    {
        var cleaned = input.Trim();
        cleaned = cleaned.Replace('\u00A0', ' ')
            .Replace('\u202F', ' ')
            .Replace('\u2007', ' ')
            .Replace('\uFEFF', ' ');

        cleaned = new string(cleaned.Where(ch =>
            !char.IsControl(ch)
            && ch != '\u200B'
            && ch != '\u200C'
            && ch != '\u200D'
            && ch != '\u2060').ToArray());

        cleaned = cleaned.Trim();
        cleaned = cleaned.Trim('"', '\'', '`', '“', '”', '‘', '’', '<', '>', '[', ']', '(', ')', '{', '}', '«', '»');
        cleaned = cleaned.TrimEnd(',', '.', ';', ':', '!', '?');

        while (cleaned.EndsWith("/", StringComparison.Ordinal) && cleaned.Length > 1)
        {
            cleaned = cleaned.TrimEnd('/');
        }

        return cleaned.Trim();
    }

    private static string CleanupUrl(string value)
    {
        var cleaned = value.Trim();
        cleaned = cleaned.TrimEnd(',', '.', ';', ':', '!', '?', ')', ']', '}');
        return cleaned.Trim();
    }

    private static string? FindUrlInsideText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var index = text.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            index = text.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        }

        if (index < 0)
        {
            return null;
        }

        var segment = text.Substring(index);
        var builder = new System.Text.StringBuilder();
        foreach (var ch in segment)
        {
            if (char.IsWhiteSpace(ch) || ch is '<' or '>' or '"' or '\'' or '`' or '[' or ']' or '(' or ')' or '{' or '}' or '«' or '»')
            {
                break;
            }

            builder.Append(ch);
        }

        var candidate = CleanupUrl(builder.ToString());
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    public static string NormalizeDomainRule(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var candidate = NormalizeText(input);

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host.Trim();
        }

        var bare = candidate.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Split('/', StringSplitOptions.TrimEntries)[0]
            .Split('?', StringSplitOptions.TrimEntries)[0]
            .Split('#', StringSplitOptions.TrimEntries)[0];

        return bare.Trim();
    }

    public static bool MatchesDomain(string host, string allowedRule)
    {
        var normalizedHost = string.IsNullOrWhiteSpace(host) ? string.Empty : host.Trim();
        var normalizedRule = NormalizeDomainRule(allowedRule);

        if (string.IsNullOrWhiteSpace(normalizedHost) || string.IsNullOrWhiteSpace(normalizedRule))
        {
            return false;
        }

        return normalizedHost.Equals(normalizedRule, StringComparison.OrdinalIgnoreCase)
            || normalizedHost.EndsWith($".{normalizedRule}", StringComparison.OrdinalIgnoreCase);
    }
}
