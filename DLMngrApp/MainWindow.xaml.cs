using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Forms;

namespace DLMngrApp;

public partial class MainWindow : Window
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DLMngr",
        "settings.json");

    private AppSettings _settings = new();
    private DownloadQueueManager? _queueManager;

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        StartClipboardMonitor();
        Log("DLMngr ready.");
    }

    private void LoadSettings()
    {
        _settings = AppSettings.Load(_settingsPath);
        DownloadFolderBox.Text = _settings.DownloadFolder;
        ParallelDownloadsBox.Text = _settings.MaxParallelDownloadsPerDomain.ToString();
        AllowedDomainsBox.Text = string.Join(Environment.NewLine, _settings.AllowedDomains);
        _queueManager = new DownloadQueueManager(_settings.DownloadFolder, _settings.MaxParallelDownloadsPerDomain, Log);
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.DownloadFolder = DownloadFolderBox.Text.Trim();
        _settings.MaxParallelDownloadsPerDomain = int.TryParse(ParallelDownloadsBox.Text, out var value) ? Math.Max(1, value) : 1;
        _settings.AllowedDomains = AllowedDomainsBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _settings.Save(_settingsPath);
        _queueManager = new DownloadQueueManager(_settings.DownloadFolder, _settings.MaxParallelDownloadsPerDomain, Log);
        Log("Settings saved.");
    }

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose download folder",
            SelectedPath = DownloadFolderBox.Text
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            DownloadFolderBox.Text = dialog.SelectedPath;
        }
    }

    private void ProcessClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = System.Windows.Clipboard.GetText();
            ProcessUrl(text);
        }
        catch (Exception ex)
        {
            Log($"Clipboard access failed: {ex.Message}");
        }
    }

    private void StartClipboardMonitor()
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        timer.Tick += (_, _) =>
        {
            try
            {
                var text = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text) && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
                {
                    ClipboardUrlBox.Text = text.Trim();
                    if (ShouldProcessDomain(uri.Host))
                    {
                        Log($"Eligible clipboard URL detected: {uri.Host}");
                    }
                }
            }
            catch
            {
                // ignore clipboard errors
            }
        };

        timer.Start();
    }

    private bool ShouldProcessDomain(string host)
    {
        if (_settings.AllowedDomains.Count == 0)
        {
            return false;
        }

        return _settings.AllowedDomains.Any(rule =>
            host.Equals(rule, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith($".{rule}", StringComparison.OrdinalIgnoreCase));
    }

    private void ProcessUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Log("Clipboard does not contain a URL.");
            return;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || !uri.Scheme.StartsWith("http"))
        {
            Log("Clipboard content is not a supported HTTP/HTTPS URL.");
            return;
        }

        if (!ShouldProcessDomain(uri.Host))
        {
            Log($"Domain is not allowed: {uri.Host}");
            return;
        }

        Log($"Queued for processing: {trimmed}");
        _ = _queueManager?.EnqueueAsync(trimmed);
    }

    private void Log(string message)
    {
        var time = DateTime.Now.ToString("HH:mm:ss");
        LogBox.AppendText($"[{time}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}
