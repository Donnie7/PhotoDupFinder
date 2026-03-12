# PhotoDupFinder

PhotoDupFinder is a .NET 8 terminal application for finding duplicate photos by combining metadata inspection with normalized pixel comparison. It uses Spectre.Console for a guided terminal UI and CSV exports for offline review.

## Features

- Guided interactive terminal menu with a consistent slate/cyan theme
- Direct `scan` command for automation
- Supported formats: `.jpg`, `.jpeg`, `.png`, `.bmp`, `.gif`, `.tif`, `.tiff`, `.heic`, `.heif`, `.dng`, `.webp`
- Metadata-aware scan pipeline with EXIF date/orientation support
- Exact duplicate confirmation using normalized pixel fingerprints
- CSV export with suggested keep file and reclaimable space
- Persistent last-report and local settings storage

## Getting Started

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project .\src\PhotoDupFinder.Cli
```

To scan a directory directly:

```powershell
dotnet run --project .\src\PhotoDupFinder.Cli -- scan --root "D:\Photos" --csv ".\reports\scan.csv"
```

## Workflow

1. Choose `Scan directory` from the interactive menu or run the `scan` command.
2. Review the summary dashboard and duplicate groups in the terminal.
3. Export the results to CSV and keep the suggested highest-quality file from each group.

## Notes

- Duplicate matching is exact after EXIF orientation normalization.
- Files that can be discovered but not decoded are marked as `Unverified` rather than silently dropped.
- v1 recommends what to keep but does not delete, move, or edit photos.
