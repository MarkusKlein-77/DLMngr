using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace DLMngrApp;

public sealed record StreamedCastPlayerConfig(string ScriptUrl, string Token, string Key);

public static partial class PlaywrightMediaInspector
{
    [GeneratedRegex("https?://[^\\s\"'<>]+(?:\\.m3u8|\\.mpd|\\.mp4|\\.m4v|\\.webm|\\.ts)(?:\\?[^\\s\"'<>]*)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MediaUrlRegex();

    [GeneratedRegex("data-snippet-url=\\\"([^\\\"]+)\\\"|/api/play-snippet/[^\"'\\s<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SnippetUrlRegex();

    [GeneratedRegex("<script[^>]+src=\\\"(?<script>https?://play\\.streamedcast\\.com/player\\.js[^\\\"]*)\\\"[^>]*data-token=\\\"(?<token>[^\\\"]+)\\\"[^>]*data-key=\\\"(?<key>[^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex StreamedCastScriptRegex();

    public static async Task<Uri?> DetectMediaUrlAsync(Uri pageUri, CancellationToken token = default)
    {
        try
        {
            await EnsureBrowserInstalledAsync();
            var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
            });

            var page = await browser.NewPageAsync(new BrowserNewPageOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
            });

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            page.Request += (_, request) =>
            {
                var url = request.Url;
                if (LooksLikeMedia(url))
                {
                    candidates.Add(url);
                }
            };

            page.Response += (_, response) =>
            {
                var url = response.Url;
                if (LooksLikeMedia(url))
                {
                    candidates.Add(url);
                }
            };

            await page.GotoAsync(pageUri.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.Load,
                Timeout = 30000
            });

            try
            {
                var ageGate = page.Locator("#ageEnter, button:has-text(\"I am 18 or older\"), button:has-text(\"Enter\")").First;
                if (await ageGate.CountAsync() > 0)
                {
                    await ageGate.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
                    await page.WaitForTimeoutAsync(1000);
                }
            }
            catch
            {
                // Age gate may not exist or may already be resolved.
            }

            var sourceHtml = await page.ContentAsync();
            var snippetUri = TryExtractSnippetEndpoint(sourceHtml, pageUri);
            var streamedCastConfig = TryExtractStreamedCastConfig(sourceHtml);

            try
            {
                var playButton = page.Locator(".play-btn, .play-facade, button[aria-label*=Play], [data-snippet-url]").First;
                if (await playButton.CountAsync() > 0)
                {
                    await playButton.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
                }
            }
            catch
            {
                // Some players do not expose a visible play control in headless mode.
            }

            try
            {
                var mediaRequest = await page.WaitForRequestAsync(request =>
                    request.Url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
                    || request.Url.Contains(".mpd", StringComparison.OrdinalIgnoreCase)
                    || request.Url.Contains("playlist", StringComparison.OrdinalIgnoreCase)
                    || request.Url.Contains("manifest", StringComparison.OrdinalIgnoreCase),
                    new PageWaitForRequestOptions { Timeout = 25000 });

                if (!string.IsNullOrWhiteSpace(mediaRequest.Url))
                {
                    candidates.Add(mediaRequest.Url);
                }
            }
            catch
            {
                // The player may be using a signed iframe stream that loads outside the immediate wait window.
            }

            if (streamedCastConfig is not null)
            {
                var iframeUri = await ResolveStreamedCastIframeAsync(page, streamedCastConfig);
                if (iframeUri is not null)
                {
                    candidates.Add(iframeUri.ToString());
                    try
                    {
                        await page.GotoAsync(iframeUri.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30000 });
                        await page.WaitForTimeoutAsync(3000);
                    }
                    catch
                    {
                        // Ignore iframe navigation issues and still inspect the current page state.
                    }
                }
            }

            await page.WaitForTimeoutAsync(6000);
            var html = await page.ContentAsync();

            foreach (Match match in MediaUrlRegex().Matches(html))
            {
                var value = match.Value.TrimEnd('"', '\'', ')', ']', '}');
                if (!string.IsNullOrWhiteSpace(value))
                {
                    candidates.Add(value);
                }
            }

            if (snippetUri is not null)
            {
                var snippetHtml = await page.EvaluateAsync<string>("() => document.body.innerHTML") ?? string.Empty;
                foreach (Match match in SnippetUrlRegex().Matches(snippetHtml))
                {
                    if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                    {
                        var candidate = TryNormalizeCandidate(match.Groups[1].Value, pageUri)?.ToString();
                        if (!string.IsNullOrWhiteSpace(candidate))
                        {
                            candidates.Add(candidate);
                        }
                    }
                }
            }

            var best = candidates
                .Select(candidate => TryNormalizeCandidate(candidate, pageUri))
                .Where(uri => uri is not null)
                .OrderByDescending(uri => ScoreCandidate(uri!))
                .FirstOrDefault();

            return best;
        }
        catch
        {
            return null;
        }
    }

    public static Uri? TryExtractSnippetEndpoint(string html, Uri pageUri)
    {
        var match = SnippetUrlRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        var relative = value.StartsWith("/", StringComparison.Ordinal) ? new Uri(pageUri, value) : new Uri(pageUri, "/" + value.TrimStart('/'));
        return relative;
    }

    public static StreamedCastPlayerConfig? TryExtractStreamedCastConfig(string html)
    {
        var match = StreamedCastScriptRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        var scriptUrl = match.Groups["script"].Value.Trim();
        var token = match.Groups["token"].Value.Trim();
        var key = match.Groups["key"].Value.Trim();
        if (string.IsNullOrWhiteSpace(scriptUrl) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return new StreamedCastPlayerConfig(scriptUrl, token, key);
    }

    private static async Task<Uri?> ResolveStreamedCastIframeAsync(IPage page, StreamedCastPlayerConfig config)
    {
        try
        {
            var json = await page.EvaluateAsync<string>("async ({ apiUrl, key }) => { try { const response = await fetch(apiUrl, { headers: { 'X-Player-Key': key, 'Accept': 'application/json' } }); if (!response.ok) return ''; return await response.text(); } catch { return ''; } }", new { apiUrl = $"https://play.streamedcast.com/api/play/t/{Uri.EscapeDataString(config.Token)}", key = config.Key });
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("iframe_src", out var iframeProp) && iframeProp.ValueKind == JsonValueKind.String)
            {
                var iframe = iframeProp.GetString();
                if (!string.IsNullOrWhiteSpace(iframe) && Uri.TryCreate(iframe, UriKind.Absolute, out var iframeUri))
                {
                    return iframeUri;
                }
            }
        }
        catch
        {
            // Ignore fetch failures; we still fall back to page HTML inspection.
        }

        return null;
    }

    private static async Task EnsureBrowserInstalledAsync()
    {
        try
        {
            _ = await Playwright.CreateAsync();
        }
        catch (Exception)
        {
            Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        }
    }

    private static Uri? TryNormalizeCandidate(string candidate, Uri pageUri)
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

        return HasMediaExtension(absolute) ? absolute : null;
    }

    private static bool LooksLikeMedia(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var lower = normalized.ToLowerInvariant();
        return HasMediaExtension(uri)
            || lower.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
            || lower.Contains(".mpd", StringComparison.OrdinalIgnoreCase)
            || lower.Contains(".mp4", StringComparison.OrdinalIgnoreCase)
            || lower.Contains(".webm", StringComparison.OrdinalIgnoreCase)
            || lower.Contains(".ts", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("master.m3u8", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("playlist.m3u8", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("manifest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasMediaExtension(Uri uri)
    {
        var path = uri.AbsolutePath ?? string.Empty;
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".m4v", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreCandidate(Uri uri)
    {
        var score = 0;
        var path = uri.AbsolutePath ?? string.Empty;

        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase)) score += 90;
        if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (uri.Query.Contains("720", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (uri.Query.Contains("1080", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (uri.Query.Contains("480", StringComparison.OrdinalIgnoreCase)) score += 15;

        return score;
    }
}
