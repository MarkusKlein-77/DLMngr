using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace DLMngrApp;

public sealed class DownloadQueueManager
{
    private readonly string _downloadDirectory;
    private readonly int _maxConcurrencyPerDomain;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _domainSemaphores = new(StringComparer.OrdinalIgnoreCase);

    public DownloadQueueManager(string downloadDirectory, int maxConcurrencyPerDomain, Action<string> log)
    {
        _downloadDirectory = downloadDirectory;
        _maxConcurrencyPerDomain = Math.Max(1, maxConcurrencyPerDomain);
        _log = log;
    }

    public Task EnqueueAsync(string url)
    {
        var domain = ExtractDomain(url);
        var limiter = _domainSemaphores.GetOrAdd(domain, _ => new SemaphoreSlim(_maxConcurrencyPerDomain, _maxConcurrencyPerDomain));

        return Task.Run(async () =>
        {
            await limiter.WaitAsync();
            try
            {
                await DownloadAsync(url);
            }
            finally
            {
                limiter.Release();
            }
        });
    }

    private async Task DownloadAsync(string url)
    {
        var domain = ExtractDomain(url);
        var domainFolder = SanitizeFileName(domain);
        var outputFolder = Path.Combine(_downloadDirectory, domainFolder);
        Directory.CreateDirectory(outputFolder);

        var ytDlpPath = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
        if (!File.Exists(ytDlpPath))
        {
            _log("Downloading yt-dlp helper...");
            await DownloadYtDlpAsync(ytDlpPath);
        }

        var arguments = $"--no-playlist -P \"{outputFolder}\" -o \"%(title)s.%(ext)s\" \"{url}\"";
        var psi = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _log($"Starting download for: {url}");

        using var process = Process.Start(psi);
        if (process is null)
        {
            _log($"Failed to start process for: {url}");
            return;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            _log($"Success: {url}");
        }
        else
        {
            _log($"Download failed: {url}. {stderr.Trim()}");
        }
    }

    private static async Task DownloadYtDlpAsync(string path)
    {
        using var client = new HttpClient();
        using var stream = await client.GetStreamAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
        await using var file = File.Create(path);
        await stream.CopyToAsync(file);
    }

    private static string ExtractDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "unknown";
        }

        return uri.Host.Trim();
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var result = value;
        foreach (var invalid in invalidChars)
        {
            result = result.Replace(invalid, '-');
        }

        result = result.Replace('.', '-');
        return string.IsNullOrWhiteSpace(result) ? "download" : result;
    }
}
