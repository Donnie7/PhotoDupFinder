param(
  [string] $Configuration = "Release",
  [string] $Runtime = "win-x64"
)

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot "src\PhotoDupFinder.Cli\PhotoDupFinder.Cli.csproj"
$publishDirectory = Join-Path $repoRoot "artifacts\publish\$Runtime"

Push-Location $repoRoot

try {
  & dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDirectory

  exit $LASTEXITCODE
}
finally {
  Pop-Location
}
