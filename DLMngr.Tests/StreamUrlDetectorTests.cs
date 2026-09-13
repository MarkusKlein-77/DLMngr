using System.Net;
using System.Text;
using DLMngrApp;
using Xunit;

namespace DLMngr.Tests;

public class StreamUrlDetectorTests
{
    [Fact]
    public async Task DetectBestUrlAsync_FindsRelativeManifestFromPageHtml()
    {
        var port = GetFreePort();
        var serverAddress = $"http://127.0.0.1:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(serverAddress);
        listener.Start();

        var requestTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var payload = Encoding.UTF8.GetBytes("<html><script>const stream = '/videos/playlist.m3u8?token=abc';</script></html>");
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
            context.Response.OutputStream.Close();
        });

        var result = await StreamUrlDetector.DetectBestUrlAsync(new Uri(serverAddress + "page.html"), Array.Empty<string>());

        await requestTask;
        listener.Stop();

        Assert.NotNull(result);
        Assert.Equal("http://127.0.0.1:" + port + "/videos/playlist.m3u8?token=abc", result!.ToString());
    }

    [Fact]
    public void TryNormalizeUrl_AcceptsRealPageUrlWithLongTitle()
    {
        var input = "https://gayprohub.com/video/mr-deep-voice-bottoms-for-channing-flyn-with-a-creampie";

        var isValid = UrlNormalizer.TryNormalizeUrl(input, out var uri);

        Assert.True(isValid);
        Assert.NotNull(uri);
        Assert.Equal("https://gayprohub.com/video/mr-deep-voice-bottoms-for-channing-flyn-with-a-creampie", uri!.ToString());
    }

    [Fact]
    public async Task DetectBestUrlAsync_FindsEscapedJsonManifestUrl()
    {
        var port = GetFreePort();
        var serverAddress = $"http://127.0.0.1:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(serverAddress);
        listener.Start();

        var requestTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var payload = Encoding.UTF8.GetBytes("<script>window.__data = {\"video_url\":\"https:\\/\\/cdn.example.com\\/master.m3u8?token=abc\"};</script>");
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
            context.Response.OutputStream.Close();
        });

        var result = await StreamUrlDetector.DetectBestUrlAsync(new Uri(serverAddress + "page.html"), Array.Empty<string>());

        await requestTask;
        listener.Stop();

        Assert.NotNull(result);
        Assert.Equal("https://cdn.example.com/master.m3u8?token=abc", result!.ToString());
    }

    [Fact]
    public void TryExtractSnippetEndpoint_FindsStreamedCastPlaySnippetUrl()
    {
        var html = "<div class=\"play-facade\" data-snippet-url=\"/api/play-snippet/mr-deep-voice-bottoms-for-channing-flyn-with-a-creampie\"></div>";
        var pageUri = new Uri("https://gayprohub.com/video/mr-deep-voice-bottoms-for-channing-flyn-with-a-creampie");

        var result = PlaywrightMediaInspector.TryExtractSnippetEndpoint(html, pageUri);

        Assert.NotNull(result);
        Assert.Equal("https://gayprohub.com/api/play-snippet/mr-deep-voice-bottoms-for-channing-flyn-with-a-creampie", result!.ToString());
    }

    [Fact]
    public void TryExtractStreamedCastPlayerConfig_FindsTokenAndKey()
    {
        var html = "<script src=\"https://play.streamedcast.com/player.js?v=2\" data-token=\"v1.demo.token\" data-key=\"abc123\"></script>";

        var config = PlaywrightMediaInspector.TryExtractStreamedCastConfig(html);

        Assert.NotNull(config);
        Assert.Equal("v1.demo.token", config!.Token);
        Assert.Equal("abc123", config.Key);
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
