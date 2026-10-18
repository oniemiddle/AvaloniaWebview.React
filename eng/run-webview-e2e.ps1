param(
    [string]$Configuration = "Release",
    [string]$TargetFramework = "net10.0-windows",
    [string]$ResultPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ResultPath)) {
    $ResultPath = Join-Path $repoRoot "artifacts/webview-e2e-result.json"
}

$resultDirectory = Split-Path -Parent $ResultPath
if (-not [string]::IsNullOrWhiteSpace($resultDirectory)) {
    New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
}

Remove-Item -LiteralPath $ResultPath -Force -ErrorAction SilentlyContinue
$env:AVALONIA_REACT_E2E_RESULT = $ResultPath

dotnet run `
    --project (Join-Path $repoRoot "tests/AvaloniaWebview.React.WebViewE2E/AvaloniaWebview.React.WebViewE2E.csproj") `
    --configuration $Configuration `
    --framework $TargetFramework

if (Test-Path -LiteralPath $ResultPath) {
    Get-Content -LiteralPath $ResultPath
}
else {
    Write-Error "The WebView E2E result file was not produced: $ResultPath"
}

exit $LASTEXITCODE
