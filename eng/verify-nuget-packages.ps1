param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot "../artifacts/packages")
)

$ErrorActionPreference = "Stop"
if (-not ("System.IO.Compression.ZipFile" -as [type])) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
}

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$props = [xml](Get-Content (Join-Path $repositoryRoot "Directory.Build.props"))
$version = ($props.Project.PropertyGroup | Where-Object { $_.AvaloniaReactVersion } | Select-Object -First 1).AvaloniaReactVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read AvaloniaReactVersion from Directory.Build.props."
}

$expected = [ordered]@{
    "AvaloniaWebview.React" = @(
        "lib/net10.0/AvaloniaWebview.React.dll",
        "lib/net10.0/AvaloniaWebview.React.xml",
        "buildTransitive/AvaloniaWebview.React.targets",
        "README.md",
        "LICENSE"
    )
    "AvaloniaWebview.React.SourceGenerator" = @(
        "analyzers/dotnet/cs/AvaloniaWebview.React.SourceGenerator.dll",
        "buildTransitive/AvaloniaWebview.React.SourceGenerator.props",
        "README.md",
        "LICENSE"
    )
    "AvaloniaWebview.React.Vite" = @(
        "buildTransitive/AvaloniaWebview.React.Vite.targets",
        "README.md",
        "LICENSE"
    )
}

$failures = [System.Collections.Generic.List[string]]::new()
$verified = [System.Collections.Generic.List[string]]::new()

foreach ($packageId in $expected.Keys) {
    $pattern = "^$([regex]::Escape($packageId))\.$([regex]::Escape($version))\.nupkg$"
    $package = Get-ChildItem -LiteralPath $PackageDirectory -Filter "$packageId.*.nupkg" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $pattern } |
        Select-Object -First 1

    if ($null -eq $package) {
        $failures.Add("Missing $packageId $version package in $PackageDirectory.")
        continue
    }

    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $names = @($zip.Entries | ForEach-Object FullName)

        foreach ($required in $expected[$packageId]) {
            if ($names -notcontains $required) {
                $failures.Add("$($package.Name) is missing $required.")
            }
        }

        foreach ($name in $names) {
            if ($name -match "^(src|tests|node_modules|bin|obj)/") {
                $failures.Add("$($package.Name) contains unwanted entry $name.")
            }
        }

        $nuspecEntry = $zip.Entries | Where-Object { $_.Name -like "*.nuspec" } | Select-Object -First 1
        if ($null -eq $nuspecEntry) {
            $failures.Add("$($package.Name) has no .nuspec entry.")
        }
        else {
            $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
            try {
                [xml]$nuspec = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }

            if ($nuspec.package.metadata.id -ne $packageId) {
                $failures.Add("$($package.Name) declares package id '$($nuspec.package.metadata.id)'.")
            }

            if ($nuspec.package.metadata.version -ne $version) {
                $failures.Add("$($package.Name) declares version '$($nuspec.package.metadata.version)' instead of '$version'.")
            }
        }

        $verified.Add($package.Name)
    }
    finally {
        $zip.Dispose()
    }
}

if ($failures.Count -gt 0) {
    throw "NuGet package verification failed:`n - $($failures -join "`n - ")"
}

Write-Output "Verified $($verified.Count) NuGet packages at version ${version}:"
$verified | ForEach-Object { Write-Output " - $_" }
