param(
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]] $CommandArgs
)

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $repoRoot

try {
  $arguments = @(
    "run"
    "--project"
    (Join-Path $repoRoot "src\PhotoDupFinder.Cli")
    "--"
  )

  if ($CommandArgs.Count -eq 0) {
    $arguments += "start"
  }
  else {
    $arguments += $CommandArgs
  }

  & dotnet @arguments
  exit $LASTEXITCODE
}
finally {
  Pop-Location
}
