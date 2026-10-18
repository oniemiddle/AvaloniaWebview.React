<#
.SYNOPSIS
Publishes the Avalonia + React sample as a self-contained Native AOT Windows executable.

.DESCRIPTION
Runs the win-x64-aot publish profile, then verifies that the output really is Native AOT
(ReactHost.exe plus wwwroot, no managed ReactHost.dll) and optionally launches it through
eng/verify-published-app.ps1 to confirm the native WebView and React bridge come up.

Native AOT compiles with the MSVC linker, so the machine needs:
  * the .NET 10 SDK
  * Visual Studio (or Build Tools) with the C++ desktop workload
  * a Windows 10/11 SDK, which supplies kernel32.lib and friends

.PARAMETER Configuration
Build configuration. Native AOT publishing requires Release.

.PARAMETER SkipFrontendBuild
Reuses samples/react-app/dist instead of running npm/tsc/vite during publish.

.PARAMETER SkipRuntimeCheck
Skips the launch/screenshot verification step.

.PARAMETER CertificateThumbprint
Optional Authenticode code-signing certificate thumbprint from CurrentUser\My or LocalMachine\My.
When set, the published ReactHost.exe is signed with signtool and the signature is verified.
Unsigned Native AOT builds are flagged by Windows ("unknown publisher"), which is expected.

.PARAMETER TimestampServer
RFC 3161 timestamp server used while signing. Pass 'none' to sign without a timestamp.

.PARAMETER SignToolPath
Explicit signtool.exe path; by default the Windows 10/11 SDK copy is used.

.EXAMPLE
pwsh -NoProfile -ExecutionPolicy Bypass -File eng/publish-aot.ps1

.EXAMPLE
pwsh -NoProfile -ExecutionPolicy Bypass -File eng/publish-aot.ps1 -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
#>
param(
    [string]$Configuration = "Release",
    [string]$TargetFramework = "net10.0-windows",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$PublishProfile = "win-x64-aot",
    [switch]$SkipFrontendBuild,
    [switch]$SkipRuntimeCheck,
    [string]$CertificateThumbprint,
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [string]$SignToolPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "samples/ReactHost/ReactHost.csproj"
$publishDir = Join-Path $repoRoot "artifacts/publish/ReactHost-win-x64-aot"
$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)

# Vite/esbuild and ILC both use the temp directory heavily. Keep build scratch inside the repository so
# restricted or cleaned-up Windows temp ACLs cannot break the frontend build or the native link.
$tempRoot = Join-Path $repoRoot "artifacts/tmp"
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
$env:TEMP = $tempRoot
$env:TMP = $tempRoot

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
    throw "Native AOT publishing for the Windows WebView2 host must run on Windows."
}

function Test-NativeAotPrerequisites {
    $missing = [System.Collections.Generic.List[string]]::new()

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        $missing.Add("The .NET 10 SDK (dotnet) was not found on PATH.")
    }

    $vswhere = Join-Path $programFilesX86 "Microsoft Visual Studio/Installer/vswhere.exe"
    $vsPath = $null
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    }

    if ([string]::IsNullOrWhiteSpace($vsPath)) {
        $missing.Add("Visual Studio (or Build Tools) with the 'Desktop development with C++' workload was not found.")
    }

    $windowsSdk = Get-ChildItem (Join-Path $programFilesX86 "Windows Kits/10/Lib") -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "um/x64/kernel32.lib") } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $windowsSdk) {
        $missing.Add("The Windows 10/11 SDK libraries (um/x64/kernel32.lib) were not found. Install 'Windows 11 SDK' from the Visual Studio installer or run: winget install Microsoft.WindowsSDK.10.0.26100")
    }

    if ($missing.Count -gt 0) {
        throw ("Native AOT prerequisites are missing:" + [Environment]::NewLine + (($missing | ForEach-Object { "  - $_" }) -join [Environment]::NewLine) + [Environment]::NewLine + "See https://aka.ms/nativeaot-prerequisites.")
    }

    Write-Host "Native AOT toolchain: $vsPath (MSVC), Windows SDK $($windowsSdk.Name)"
}

Test-NativeAotPrerequisites

$publishArgs = @(
    "publish", $project,
    "--configuration", $Configuration,
    "--framework", $TargetFramework,
    "--property:PublishProfile=$PublishProfile",
    "--verbosity", "minimal",
    "--nologo"
)

if ($SkipFrontendBuild) {
    # samples/react-app/dist is already the production bundle; skip npm ci/build.
    $publishArgs += "--property:AvaloniaReactBuildOnBuild=false"
}

# Publish does not delete files that a previous configuration produced (for example a managed
# ReactHost.dll from a non-AOT publish). Start from a clean folder so the output assertions below
# describe the artifact honestly.
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

Write-Host "dotnet $($publishArgs -join ' ')"
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# Verification: the AOT publish must produce a native exe, not a managed ReactHost.dll.
$exe = Join-Path $publishDir "ReactHost.exe"
$wwwrootIndex = Join-Path $publishDir "wwwroot/index.html"
$managedAssembly = Join-Path $publishDir "ReactHost.dll"

if (-not (Test-Path -LiteralPath $exe)) {
    throw "The publish did not produce $exe."
}

if (Test-Path -LiteralPath $managedAssembly) {
    throw "ReactHost.dll is present in the publish output, so the app was not published with Native AOT."
}

if (-not (Test-Path -LiteralPath $wwwrootIndex)) {
    throw "The React frontend is missing from the publish output: $wwwrootIndex. Check AvaloniaReactBuildOnBuild / samples/react-app/dist."
}

$sizeMegabytes = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 2)
Write-Host "Native AOT publish succeeded: $exe ($sizeMegabytes MB)"
Write-Host "Publish directory: $publishDir"
Get-ChildItem -LiteralPath $publishDir -File |
    Sort-Object Length -Descending |
    Select-Object -First 10 Name, @{ Name = "MB"; Expression = { [math]::Round($_.Length / 1MB, 2) } } |
    Format-Table -AutoSize

# Optional Authenticode signing. Windows shows "unknown publisher" for unsigned Native AOT
# executables; a real distribution needs a trusted code-signing certificate.
if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $signTool = $SignToolPath
    if ([string]::IsNullOrWhiteSpace($signTool)) {
        $signTool = Get-ChildItem (Join-Path $programFilesX86 "Windows Kits/10/bin") -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "x64/signtool.exe" } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
    }

    if ([string]::IsNullOrWhiteSpace($signTool) -or -not (Test-Path -LiteralPath $signTool)) {
        throw "signtool.exe was not found. Install the Windows 10/11 SDK or pass -SignToolPath."
    }

    $signArgs = @(
        "sign", "/sha1", $CertificateThumbprint,
        "/fd", "SHA256",
        "/d", "AvaloniaWebview.React ReactHost sample"
    )
    if ($TimestampServer -ne "none") {
        $signArgs += @("/tr", $TimestampServer, "/td", "SHA256")
    }

    $signArgs += $exe
    Write-Host "Signing $exe with certificate $CertificateThumbprint"
    & $signTool @signArgs
    if ($LASTEXITCODE -ne 0) {
        throw "signtool sign failed with exit code $LASTEXITCODE."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $exe
    if ($signature.Status -ne "Valid") {
        Write-Warning "The signature is present but did not validate ($($signature.Status): $($signature.StatusMessage)). Install the certificate in Trusted Root/Trusted Publishers for local runs."
    }
    else {
        Write-Host "Signed by: $($signature.SignerCertificate.Subject)"
    }
}

$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
Write-Host "SHA256: $hash"
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    Write-Host "The executable is unsigned. Windows may warn 'unknown publisher' when it is launched; pass -CertificateThumbprint to sign it."
}

if (-not $SkipRuntimeCheck) {
    $verifyScript = Join-Path $repoRoot "eng/verify-published-app.ps1"
    if (Test-Path -LiteralPath $verifyScript) {
        Write-Host "Launching the published app for a runtime check..."
        & pwsh -NoProfile -ExecutionPolicy Bypass -File $verifyScript -AppDirectory $publishDir
        if ($LASTEXITCODE -ne 0) {
            throw "The published app failed the runtime check (exit code $LASTEXITCODE)."
        }
    }
    else {
        Write-Warning "eng/verify-published-app.ps1 was not found; skipping the runtime check."
    }
}
