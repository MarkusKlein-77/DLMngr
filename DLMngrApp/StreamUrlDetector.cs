using System.Net.Http;
using System.Text.RegularExpressions;

namespace DLMngrApp;

public static partial class StreamUrlDetector
{
    [GeneratedRegex(@"(?:https?://)?[^\s""'<>]+\.(?:m3u8|mpd|mp4|m4v|webm|ts)(?:\?[^\s""'<>]*)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MediaUrlRegex();

    [GeneratedRegex(@"(?:m3u8|mpd|videoUrl|video_url|manifest|streamUrl|stream_url|file\s*:\s*['""]?(?:https?://)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MediaPatternRegex();

    public static bool IsMediaUrl(Uri uri)
    {
        var path = uri.AbsolutePath ?? string.Empty;
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".m4v", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<Uri?> DetectBestUrlAsync(Uri pageUri, ICollection<string> allowedDomains, CancellationToken token = default)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            var html = await client.GetStringAsync(pageUri, token);
            return SelectBestCandidate(html, pageUri);
        }
        catch
        {
            return null;
        }
    }

    private static Uri? SelectBestCandidate(string html, Uri pageUri)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MediaUrlRegex().Matches(html))
        {
            var value = match.Value.TrimEnd('"', '\'', ')', ']', '}');
            if (!seen.Add(value))
            {
                continue;
            }

            candidates.Add(value);
        }

        foreach (Match match in MediaPatternRegex().Matches(html))
        {
            var value = ExtractUrlFromContext(html, match.Index, pageUri);
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                candidates.Add(value);
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .Select(candidate => TryResolveRelative(candidate, pageUri))
            .Where(uri => uri is not null)
            .OrderByDescending(uri => ScoreCandidate(uri!))
            .FirstOrDefault();
    }

    private static string? ExtractUrlFromContext(string html, int startIndex, Uri pageUri)
    {
        var slice = html.Substring(startIndex, Math.Min(600, html.Length - startIndex));
        var matches = MediaUrlRegex().Matches(slice);
        if (matches.Count == 0)
        {
            return null;
        }

        var candidate = matches[0].Value.TrimEnd('"', '\'', ')', ']', '}');
        return TryResolveRelative(candidate, pageUri)?.ToString();
    }

    private static Uri? TryResolveRelative(string candidate, Uri pageUri)
    {
        var cleaned = candidate.Trim();
        cleaned = cleaned.Replace("\\/", "/", StringComparison.Ordinal);
        cleaned = cleaned.Replace("\\", string.Empty, StringComparison.Ordinal);
        cleaned = cleaned.TrimEnd('"', '\'', ')', ']', '}');

        if (!Uri.TryCreate(cleaned, UriKind.Absolute, out var absolute))
        {
            if (Uri.TryCreate(pageUri, cleaned, out var relativeResult))
            {
                absolute = relativeResult;
            }
        }

        if (absolute is null || !absolute.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var urlText = absolute.ToString();
        return IsMediaUrl(absolute) || urlText.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
            || urlText.Contains(".mpd", StringComparison.OrdinalIgnoreCase)
            || urlText.Contains(".mp4", StringComparison.OrdinalIgnoreCase)
            || urlText.Contains(".m4v", StringComparison.OrdinalIgnoreCase)
            || urlText.Contains(".webm", StringComparison.OrdinalIgnoreCase)
            || urlText.Contains(".ts", StringComparison.OrdinalIgnoreCase)
            ? absolute
            : null;
    }

    private static int ScoreCandidate(Uri uri)
    {
        var path = uri.AbsolutePath ?? string.Empty;
        var score = 0;

        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase)) score += 90;
        if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) score += 40;

        if (uri.Query.Contains("720", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (uri.Query.Contains("1080", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (uri.Query.Contains("480", StringComparison.OrdinalIgnoreCase)) score += 15;

        return score;
    }
}
