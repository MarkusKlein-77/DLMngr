# Concept: Clipboard-driven video URL capture and download manager

## Goal
Build a small Windows desktop app that watches the clipboard for copied web URLs, filters them by configured domains, inspects the page for embedded video sources or JavaScript-generated streams, and adds the best video link to a download queue.

## Short answer
A single app that matches all of the requirements exactly is not common as a ready-made product. Most similar tools are split across:

- clipboard monitor tools
- browser extensions
- site-specific downloaders
- media extraction CLI tools like yt-dlp

The closest practical approach is to combine a .NET desktop front end with yt-dlp and/or a headless browser for detection.

## Updated requirements and implications

Two new constraints make this a more specialized project:

1. It must support not only direct HTML video URLs, but also advanced streaming formats such as HLS and DASH manifests.
2. It must work on small or niche sites that are not covered by mainstream extractors like YouTube, Vimeo, Twitch, or larger platforms.

These are important because many generic downloaders work well only on a broad set of common video pages. For niche sites, the app must be designed to inspect the page deeply, capture the actual network media traffic, and accept that some domains will need custom extraction logic or user-configurable patterns.

## Recommended architecture

### 1. Windows desktop shell
Use C# with WPF or WinUI 3.

Why:
- native Windows clipboard APIs are straightforward
- good for a tray app or windowed utility
- easy integration with .NET libraries and queue management

Main UI areas:
- allowlist of domains
- blocklist of domains
- download folder
- max parallel downloads per domain
- queue status and log
- per-item status: discovered, downloaded, failed, skipped

### 2. Clipboard watcher
Listen for clipboard changes using Win32 clipboard notifications or a background worker polling the clipboard every few hundred milliseconds.

Requirements:
- ignore non-URL clipboard content
- normalize URLs: trim whitespace, decode HTML entities, remove tracking params when needed
- deduplicate repeated URLs
- call a URL validation step

### 3. Domain filter
Before parsing, apply the configured rules:
- allowlist: only process domains in this list
- optional regex support for subdomains
- deny rules for known unsupported sites

Example:
- allow: example.com, player.vimeo.com, cdn.somehost.net
- deny: ad.doubleclick.net

### 4. Page inspection layer
This is the key part. The app should inspect every allowed URL, not just simple HTML links.

Best strategy:
- first fetch the page HTML with a real browser engine or HTTP client
- then inspect: classic video tags, iframes, JSON blobs, JavaScript arrays, playlist URLs, HLS streams, DASH manifests, and manifest endpoints

Possible implementation details:
- use Playwright or PuppeteerSharp for rendering and JS execution
- use AngleSharp or HtmlAgilityPack to parse HTML and scripts
- inspect Network tab for media requests, m3u8, mpd, mp4, manifest URLs
- use yt-dlp to analyze the page and extract the best available stream automatically
- for unknown sites, record the network requests and examine the final manifest URLs that the browser requested

### 5. Video detection heuristics
Search for candidates in several places:
- <video src> and <source src>
- <iframe src> to embedded players
- JSON values like "videoUrl", "src", "mpd", "m3u8", "hls"
- JavaScript variable assignments such as window.__INITIAL_STATE__
- script blocks with manifest URLs
- Media requests captured by the browser

Score each candidate by:
- host in allowlist
- known media extensions or manifest types
- preferred bitrate / resolution
- whether it is a direct media stream or a page wrapper

Then choose the main video stream.

### 6. Download queue and concurrency
Add the chosen video URL to a queue.

Queue design:
- one queue for all downloads
- per-domain concurrency limiter
- separate workers per domain
- optional priority based on file size or URL type

Example rules:
- domain A: max 2 concurrent downloads
- domain B: max 1 parallel download
- default: 1 or 2 globally

### 7. Download execution
Recommended backend:
- yt-dlp for extraction and downloading from many video sites
- aria2c for robust multi-connection downloads
- ffmpeg for HLS/DASH manifest processing, segment merging, and stream conversion if needed

This is much more reliable than writing a generic video downloader from scratch.

### 8. Storage and state
Persist:
- allowed domains
- parallel limits
- download directory
- history of already-processed clipboard items
- queue records
- last errors and skipped URLs

Use SQLite for local state.

## Candidate open-source components

### Best fit for extraction and downloads
- yt-dlp - strongest generic extractor for web video
  - GitHub: https://github.com/yt-dlp/yt-dlp
  - Very good for hidden streams, manifests, JavaScript-generated media, and many websites

- aria2 - download manager engine
  - https://github.com/aria2/aria2
  - Handles segmented downloads and concurrent connections well

- ffmpeg - stream and manifest processing
  - https://www.ffmpeg.org/
  - Essential for HLS/DASH playlist handling and conversion

### Browser and script inspection
- Playwright - headless browser with JS execution
  - https://playwright.dev/
  - Useful for loading page and collecting media requests

- PuppeteerSharp - .NET wrapper for Chrome/Headless Chromium
  - https://github.com/hardkno/puppeteer-sharp

- CefSharp - embedded Chromium for .NET
  - https://github.com/cefsharp/CefSharp
  - Good if the app should render pages natively in-process

### HTML parsing
- AngleSharp - modern HTML parser
  - https://github.com/AngleSharp/AngleSharp

- HtmlAgilityPack - pragmatic HTML parser
  - https://github.com/zzzprojects/html-agility-pack

### .NET desktop shell
- WPF - classic Windows desktop UI
- WinUI 3 - modern Windows app UI

### Queueing and state management
- Prism or MVVM Toolkit for UI structure
- SQLite + Dapper or EF Core for persistence
- Serilog or NLog for logging

## Example architecture for this project

### Option A: pragmatic and robust
- Windows desktop app in C# / WPF
- clipboard monitoring service
- domain allowlist settings
- Playwright or a browser automation layer to inspect page and capture actual media requests
- yt-dlp or ffmpeg for HLS/DASH manifest handling
- SQLite queue
- aria2 or yt-dlp download worker per domain

This is the most realistic and maintainable choice.

### Option B: browser-like app with embedded Chromium
- WPF + CefSharp
- handle clipboard + page rendering + JS execution in one process
- more flexible for hidden assets and anti-bot detection
- heavier and more complex

### Option C: pure CLI + scheduler
- clipboard hook is awkward on Windows
- keeps logic simpler but less polished as a desktop app

## Risk areas to expect

### 1. Site anti-bot protection
Some sites hide streams behind JS or require authentication.
Mitigation:
- use a headless browser
- use a real browser engine with JS execution
- keep site-specific extraction adapters for a few domains

### 2. Legal and ToS concerns
Downloading from some sites may be restricted or illegal.
Mitigation:
- keep the app compliant with site terms
- enforce user-controlled domain allowlist
- provide explicit opt-in for each site

### 3. Streaming formats and manifests
HLS and DASH segments are common and usually hidden behind playlists.
Mitigation:
- rely on yt-dlp and ffmpeg
- inspect manifest URLs found in browser network logs
- handle segment downloading as a first-class task

### 4. Niche or unknown sites
Many smaller sites do not have generic extractor support.
Mitigation:
- include a user-defined domain adapter system
- allow custom regex extraction patterns for known domains
- keep a fallback browser-inspection workflow that captures actual media requests
- maintain a per-domain ruleset and allow specific extraction logic

## Recommended implementation plan

1. Build the tray app shell and settings UI.
2. Add clipboard listener and URL validation.
3. Add domain allowlist and per-domain concurrency settings.
4. Integrate browser-based inspection with Playwright.
5. Add HLS/DASH detection and manifest extraction logic.
6. Integrate yt-dlp and ffmpeg for extraction and stream handling.
7. Add queue worker and download folder management.
8. Add per-domain custom extraction rules for niche sites.
9. Add logging, retries, and failure handling.
10. Add export / history / simple stats.

## Best open-source starting point
The strongest single open-source component in this list is yt-dlp.
It already solves most of the hardest work: extracting video URLs from many web pages, HLS/DASH manifests, and embedded players.

For niche domains, the real differentiator is the combination of: browser inspection + manifest detection + per-domain custom rules. ffmpeg is also highly useful for stream and playlist handling.

For the Windows app shell, a small C# WPF front end around yt-dlp plus a queue manager is the cleanest path.

## Conclusion
There is no widely known single Windows program that exactly matches: clipboard monitoring + domain allowlist + hidden video URL extraction + HLS/DASH handling + niche-site support + per-domain parallel downloads + configurable folder. The closest effective solution is a hybrid app built around:

- C# WPF / WinUI
- Playwright or CefSharp for JS-heavy pages and network inspection
- yt-dlp for extraction and download execution
- ffmpeg for HLS/DASH playlist and stream processing
- SQLite for queue state
- aria2 for robust downloads
- per-domain custom adapter rules for less common sites

This gives a practical, maintainable implementation without rebuilding a full media downloader from zero.
