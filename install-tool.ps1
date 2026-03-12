param(
  [string] $Configuration = "Release"
)

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageOutput = Join-Path $repoRoot "artifacts\packages"
$projectPath = Join-Path $repoRoot "src\PhotoDupFinder.Cli\PhotoDupFinder.Cli.csproj"
$toolPackageId = "PhotoDupFinder.Tool"
$toolShimPath = Join-Path $HOME ".dotnet\tools"

Push-Location $repoRoot

try {
  & dotnet pack $projectPath -c $Configuration -o $packageOutput
  if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
  }

  & dotnet tool update --global $toolPackageId --add-source $packageOutput --ignore-failed-sources
  if ($LASTEXITCODE -ne 0) {
    & dotnet tool install --global $toolPackageId --add-source $packageOutput --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) {
      exit $LASTEXITCODE
    }
  }

  Write-Host ""
  Write-Host "PhotoDupFinder is installed as a global command." -ForegroundColor Green
  Write-Host "Run: photodupfinder"

  $pathEntries = $env:PATH -split ';'
  if ($pathEntries -notcontains $toolShimPath) {
    Write-Host ""
    Write-Warning "The .NET global tools folder is not on PATH: $toolShimPath"
    Write-Host "Add it to PATH if 'photodupfinder' is not recognized in a new terminal."
  }
}
finally {
  Pop-Location
}
