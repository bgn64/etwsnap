[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [string]$ArtifactsDirectory,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$root = Get-RepositoryRoot
$null = Get-VersionParts -Version $Version
if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $root 'artifacts'
}
$ArtifactsDirectory = [IO.Path]::GetFullPath($ArtifactsDirectory)

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -Version $Version -SkipTests
}

$source = Join-Path $root 'src\EtwSnap.Cli\bin\x64\Release\net10.0-windows'
$packageName = "etwsnap-v$Version-win-x64"
$stage = Join-Path $ArtifactsDirectory $packageName
$zipPath = Join-Path $ArtifactsDirectory "$packageName.zip"
$checksumPath = "$zipPath.sha256"
$symbolsPath = Join-Path $ArtifactsDirectory "$packageName-symbols.zip"

foreach ($path in @($stage, $zipPath, $checksumPath, $symbolsPath)) {
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
    }
}
New-Item $stage -ItemType Directory -Force | Out-Null

foreach ($file in Get-ChildItem $source -File -Recurse | Where-Object Extension -ne '.pdb') {
    $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
    $destination = Join-Path $stage $relative
    New-Item (Split-Path $destination -Parent) -ItemType Directory -Force | Out-Null
    Copy-Item $file.FullName $destination
}
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE')
Copy-Item (Join-Path $root 'README.md') (Join-Path $stage 'README.md')
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') (Join-Path $stage 'THIRD-PARTY-NOTICES.txt')
New-Item (Join-Path $stage 'docs') -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $root 'docs\releasing.md') (Join-Path $stage 'docs\releasing.md')
Copy-Item (Join-Path $root 'docs\etw-schema.md') (Join-Path $stage 'docs\etw-schema.md')
Copy-Item (Join-Path $root 'docs\wpa-plugin.md') (Join-Path $stage 'docs\wpa-plugin.md')
Copy-Item (Join-Path $root 'docs\embedded-artifacts.md') (Join-Path $stage 'docs\embedded-artifacts.md')
New-Item (Join-Path $stage 'licenses') -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $root 'packages\robmikh.common.0.0.23-beta\LICENSE') `
    (Join-Path $stage 'licenses\robmikh.common.txt')

$required = @(
    'etwsnap.exe',
    'etwsnap.host.exe',
    'EtwSnap.Artifacts.dll',
    'EtwSnap.Native.dll',
    'etwsnap.runtimeconfig.json',
    'etwsnap.host.runtimeconfig.json',
    'profiles\EtwSnap.wprp',
    'docs\embedded-artifacts.md'
)
foreach ($relative in $required) {
    if (-not (Test-Path (Join-Path $stage $relative))) {
        throw "The package is missing $relative."
    }
}

$runtimeConfig = Get-Content (Join-Path $stage 'etwsnap.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.framework.name -ne 'Microsoft.NETCore.App' -or
    -not $runtimeConfig.runtimeOptions.framework.version.StartsWith('10.')) {
    throw 'The package does not target the expected .NET 10 runtime.'
}

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content $checksumPath "$hash  $([IO.Path]::GetFileName($zipPath))" -Encoding ascii

$symbolStage = Join-Path $ArtifactsDirectory "$packageName-symbols"
if (Test-Path $symbolStage) {
    Remove-Item $symbolStage -Recurse -Force
}
New-Item $symbolStage -ItemType Directory | Out-Null
Get-ChildItem $source -Filter '*.pdb' -File -Recurse | Copy-Item -Destination $symbolStage
$nativePdb = Join-Path $root 'x64\Release\EtwSnap.Native.pdb'
if (Test-Path $nativePdb) {
    Copy-Item $nativePdb $symbolStage
}
Compress-Archive -Path (Join-Path $symbolStage '*') -DestinationPath $symbolsPath -CompressionLevel Optimal
Remove-Item $symbolStage -Recurse -Force

$smokeRoot = Join-Path ([IO.Path]::GetTempPath()) "etwsnap-package-$([Guid]::NewGuid().ToString('N'))"
try {
    Expand-Archive $zipPath $smokeRoot
    $cli = Join-Path $smokeRoot 'etwsnap.exe'
    $versionOutput = (& $cli --version 2>&1 | Out-String).Trim()
    $versionExitCode = $LASTEXITCODE
    if ($versionExitCode -ne 0 -or $versionOutput -notmatch [regex]::Escape($Version)) {
        throw "Packaged CLI version smoke test failed: $versionOutput"
    }
    $providerOutput = (& $cli provider info 2>&1 | Out-String).Trim()
    $providerExitCode = $LASTEXITCODE
    if ($providerExitCode -ne 0 -or $providerOutput -notmatch '524507bc-3009-5e8d-c071-00a1c641849f') {
        throw "Packaged CLI provider smoke test failed: $providerOutput"
    }
}
finally {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'etwsnap.host.exe' -and $_.ExecutablePath -like "$smokeRoot*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    if (Test-Path $smokeRoot) {
        Remove-Item $smokeRoot -Recurse -Force
    }
}

[pscustomobject]@{
    Version = $Version
    PackagePath = $zipPath
    PackageSha256 = $hash
    ChecksumPath = $checksumPath
    SymbolsPath = $symbolsPath
    UncompressedBytes = (Get-ChildItem $stage -File -Recurse | Measure-Object Length -Sum).Sum
    ZipBytes = (Get-Item $zipPath).Length
}