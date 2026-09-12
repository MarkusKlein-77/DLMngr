# DLMngr

A Windows desktop helper for clipboard-based URL capture, domain filtering, and video download queueing.

## What it does

- watches the Windows clipboard for URLs
- accepts only URLs from configured allowlist domains
- supports direct media URLs and HLS-style stream URLs via yt-dlp
- tracks a per-domain download limit
- downloads into a configurable local folder
- records activity in the app log

## Features

- domain allowlist configuration
- configurable download folder
- max parallel downloads per domain
- automatic `yt-dlp.exe` bootstrap if missing
- simple desktop UI built with WPF
- GitHub Actions workflow for building a Windows release binary

## Local run

1. Install .NET 8 SDK.
2. Open the solution in Visual Studio or VS Code.
3. Restore packages and run the app.
4. Add allowed domains and configure the output folder.
5. Copy a URL to the Windows clipboard.

## Build

```powershell
dotnet restore DLMngr.sln
dotnet build DLMngr.sln -c Release
```

## Release

The repository contains a GitHub Actions workflow that builds a Windows x64 self-contained binary and zips it for download.

## Notes

This project is intended as a practical foundation for niche video-page extraction and HLS-aware downloads. For many unsupported sites, additional rules or extraction logic may still be required.
