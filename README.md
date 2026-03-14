# PhotoDupFinder

PhotoDupFinder is a .NET 8 CLI for finding exact duplicate photos. It combines metadata extraction with normalized pixel fingerprinting, then helps you review duplicate groups in a terminal UI, export them to CSV, copy candidates to a backup folder, or delete confirmed duplicates.

## What It Does

- Scans a directory tree for supported photo formats
- Reads metadata, including normalized dimensions and capture date when available
- Normalizes image orientation before fingerprinting pixels
- Groups files only when their normalized pixel fingerprint matches exactly
- Suggests which file to keep based on resolution, archival format preference, metadata richness, file size, and a stable path tiebreaker
- Stores the last scan locally so you can reopen it later without rescanning
- Exports every scanned file to CSV, including duplicate group membership, keep suggestions, status, and fingerprint

## Safety Notes

- `photodupfinder scan` is a scan-and-report command. It does not delete files.
- Delete and backup-copy actions are only available from the interactive review flow.
- Delete actions require confirmation before files are removed.
- Backup destinations must be outside the scanned root directory.
- Files that cannot be read or fingerprinted stay in the report as `Unverified` and are listed in scan issues instead of being silently skipped.

## Supported Formats

`.jpg`, `.jpeg`, `.png`, `.bmp`, `.gif`, `.tif`, `.tiff`, `.heic`, `.heif`, `.dng`, `.webp`

## Requirements

- .NET SDK `8.0.403` or newer with feature roll-forward enabled
- PowerShell or Command Prompt for the helper scripts in this repository

The repository includes Windows-friendly wrapper scripts, but the CLI itself targets .NET 8 and can also be run directly with `dotnet run`.

## Quick Start

Build and validate the repo:

```powershell
dotnet restore PhotoDupFinder.sln
dotnet build PhotoDupFinder.sln
dotnet test PhotoDupFinder.sln
```

Run the interactive app from the repository:

```powershell
.\photodupfinder
```

Run a direct scan and export the results:

```powershell
.\photodupfinder scan --root "D:\Photos" --csv ".\reports\scan.csv"
```

Run without the helper script:

```powershell
dotnet run --project .\src\PhotoDupFinder.Cli -- help
```

## Common Commands

| Command | Purpose |
| --- | --- |
| `photodupfinder start` | Open the interactive home menu |
| `photodupfinder menu` | Alias for `start` |
| `photodupfinder scan --root <path>` | Run a scan directly from the command line |
| `photodupfinder scan --root <path> --csv <path>` | Export the scan result to CSV |
| `photodupfinder scan --root <path> --max-degree <n>` | Override the worker limit for this scan |
| `photodupfinder scan --root <path> --non-recursive` | Scan only the top directory |
| `photodupfinder scan --root <path> --extensions .jpg,.png` | Override the supported extension list |
| `photodupfinder config` | Show the saved configuration and cache paths |
| `photodupfinder help` | Show the built-in command reference |

## Typical Workflow

1. Start the interactive app with `photodupfinder` or `photodupfinder start`.
2. Run a scan for a photo directory.
3. Review the summary, duplicate groups, and scan issues.
4. For each group, keep the suggested file or choose a different one.
5. Export the report to CSV, copy duplicates to a backup folder, or delete duplicates after review.

## How Duplicate Detection Works

1. Discover files under the selected root using the configured extension list.
2. Read metadata for each supported file.
3. Decode verified images and generate a normalized pixel fingerprint.
4. Group files that share the same fingerprint.
5. Suggest a keep file for each group using this ranking:
   resolution, archival format preference, metadata score, file size, then path.

The current format preference order is:

`DNG` > `TIFF/TIF` > `PNG` > `HEIC/HEIF` > `JPG/JPEG/WEBP` > `BMP` > `GIF`

## Interactive Review Features

After a scan, the interactive flow can:

- Browse duplicate groups one by one
- Change the keep selection for a group
- Delete all duplicate files in one group or across all groups
- Copy duplicate candidates to a backup folder while preserving relative paths when possible
- Reopen the cached last report and export it later without rescanning

## CSV Output

The CSV export includes one row per scanned file with these columns:

- `GroupId`
- `Path`
- `KeepSuggested`
- `KeepReason`
- `Format`
- `Width`
- `Height`
- `FileSizeBytes`
- `CaptureDate`
- `Status`
- `MetadataScore`
- `Fingerprint`

This makes the export useful for spreadsheet review, audit trails, and bulk follow-up outside the app.

## Local State And Outputs

PhotoDupFinder stores its local state under `%LOCALAPPDATA%\PhotoDupFinder`:

- `settings.json`: saved default scan root, last CSV directory, and worker limit
- `last-report.json.gz`: cached last scan report
- `backups\`: default root for backup-copy operations from the interactive review flow

Common output locations:

- Repo-local wrapper scans often write CSV files under `.\reports`
- Standalone publishes go to `.\artifacts\publish\<runtime>`
- Packed NuGet artifacts go to `.\artifacts\packages`

## Install As A Global Command From This Repo

The repository includes helper scripts that pack the CLI and install or update it as a global .NET tool:

```powershell
.\install-tool.ps1
photodupfinder help
```

The installed tool package id is `PhotoDupFinder.Tool`, and the command name is `photodupfinder`.

## Publish A Standalone Executable

Build a self-contained single-file executable:

```powershell
.\publish-standalone.ps1
.\artifacts\publish\win-x64\photodupfinder.exe
```

You can target a different runtime if needed:

```powershell
.\publish-standalone.ps1 -Runtime linux-x64
```

## Repository Layout

- `src/PhotoDupFinder.Cli`: interactive CLI and command handling
- `src/PhotoDupFinder.Core`: duplicate detection and CSV export logic
- `tests/PhotoDupFinder.Core.Tests`: xUnit coverage for the core library

## Development Notes

Useful commands during development:

```powershell
dotnet format PhotoDupFinder.sln --verify-no-changes --no-restore
dotnet pack .\src\PhotoDupFinder.Cli\PhotoDupFinder.Cli.csproj -c Release
dotnet pack .\src\PhotoDupFinder.Core\PhotoDupFinder.Core.csproj -c Release
```

The repository is set up to:

- validate on pull requests to `main`
- validate and package on pushes to `main`
- publish preview package versions like `0.1.0-preview.<run number>` on normal pushes
- publish release package versions like `1.2.3` when a `v1.2.3` tag is pushed
- create a GitHub release for tagged versions
