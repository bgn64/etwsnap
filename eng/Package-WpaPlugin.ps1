[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [string]$ArtifactsDirectory,
    [string]$PluginToolPath,
    [string]$PluginToolNuGetSource,
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
New-Item $ArtifactsDirectory -ItemType Directory -Force | Out-Null

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$project = Join-Path $root 'src\EtwSnap.WpaPlugin\EtwSnap.WpaPlugin.csproj'
if (-not $SkipBuild) {
    Invoke-Checked $dotnet @('build', $project, '-c', 'Release', "-p:Version=$Version", '--nologo')
}

$pluginOutput = Join-Path $root 'src\EtwSnap.WpaPlugin\bin\Release\net8.0-windows'
$pluginDll = Join-Path $pluginOutput 'EtwSnap.WpaPlugin.dll'
if (-not (Test-Path $pluginDll -PathType Leaf)) {
    throw "The WPA plugin has not been built: $pluginDll"
}
if (Test-Path (Join-Path $pluginOutput 'Microsoft.Performance.SDK.dll')) {
    throw 'The plugin output must not contain Microsoft.Performance.SDK.dll; WPA supplies the shared SDK runtime.'
}

$packageName = "etwsnap-wpa-plugin-v$Version"
$stage = Join-Path $ArtifactsDirectory $packageName
$ptixPath = Join-Path $ArtifactsDirectory "$packageName.ptix"
$checksumPath = "$ptixPath.sha256"
$symbolsPath = Join-Path $ArtifactsDirectory "$packageName-symbols.zip"
foreach ($path in @($stage, $ptixPath, $checksumPath, $symbolsPath)) {
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
    }
}

New-Item $stage -ItemType Directory | Out-Null
Get-ChildItem $pluginOutput -Force | Where-Object Extension -ne '.pdb' | Copy-Item -Destination $stage -Recurse
$manifest = Get-Content (Join-Path $root 'src\EtwSnap.WpaPlugin\pluginManifest.json') -Raw | ConvertFrom-Json
$manifest.identity.version = $Version
$manifest | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $stage 'pluginManifest.json') -Encoding utf8

$temporaryToolRoot = $null
if ([string]::IsNullOrWhiteSpace($PluginToolPath)) {
    $toolVersion = '0.1.77-preview'
    $temporaryToolRoot = Join-Path $root 'artifacts\.pt'
    if (Test-Path $temporaryToolRoot) {
        Remove-Item $temporaryToolRoot -Recurse -Force
    }
    $installArguments = @(
        'tool', 'install',
        '--tool-path', $temporaryToolRoot,
        'Microsoft.Performance.Toolkit.Plugins.Cli',
        '--version', $toolVersion
    )
    if (-not [string]::IsNullOrWhiteSpace($PluginToolNuGetSource)) {
        $installArguments += @('--add-source', $PluginToolNuGetSource, '--ignore-failed-sources')
    }
    Invoke-Checked $dotnet $installArguments
    $PluginToolPath = Join-Path $temporaryToolRoot 'plugintool.exe'
}
$PluginToolPath = [IO.Path]::GetFullPath($PluginToolPath)
if (-not (Test-Path $PluginToolPath -PathType Leaf)) {
    throw "plugintool was not found: $PluginToolPath"
}

try {
    $packArguments = @('pack', '-s', $stage, '-o', $ptixPath, '-m', (Join-Path $stage 'pluginManifest.json'), '-w')
    if ([IO.Path]::GetExtension($PluginToolPath) -eq '.dll') {
        $previousRollForward = $env:DOTNET_ROLL_FORWARD
        try {
            $env:DOTNET_ROLL_FORWARD = 'Major'
            Invoke-Checked $dotnet (@($PluginToolPath) + $packArguments)
        }
        finally {
            $env:DOTNET_ROLL_FORWARD = $previousRollForward
        }
    }
    else {
        Invoke-Checked $PluginToolPath $packArguments
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ptixPath)
    try {
        foreach ($entry in @(
            'metadata.json',
            'contentsmetadata.json',
            'plugin/EtwSnap.WpaPlugin.dll',
            'plugin/Microsoft.Diagnostics.Tracing.TraceEvent.dll'
        )) {
            if ($null -eq $archive.GetEntry($entry)) {
                throw "The WPA plugin package is missing $entry."
            }
        }
        if ($null -ne $archive.GetEntry('plugin/Microsoft.Performance.SDK.dll')) {
            throw 'The WPA plugin package contains Microsoft.Performance.SDK.dll.'
        }

        $metadataReader = [IO.StreamReader]::new($archive.GetEntry('metadata.json').Open())
        try {
            $metadata = $metadataReader.ReadToEnd() | ConvertFrom-Json
        }
        finally {
            $metadataReader.Dispose()
        }
        if ($metadata.Identity.Id -ne 'bgn64.EtwSnap.WpaPlugin' -or $metadata.Identity.Version -ne $Version) {
            throw "Unexpected WPA plugin identity: $($metadata.Identity.Id) $($metadata.Identity.Version)"
        }
        if ($metadata.DisplayName -ne 'ETWSnap' -or
            $metadata.Description -ne 'Loads ETWSnap screenshot sessions from ETL traces and .etwsnap.zip artifacts in Windows Performance Analyzer.') {
            throw "Unexpected WPA plugin display metadata: $($metadata.DisplayName) — $($metadata.Description)"
        }

        $contentsReader = [IO.StreamReader]::new($archive.GetEntry('contentsmetadata.json').Open())
        try {
            $contents = $contentsReader.ReadToEnd() | ConvertFrom-Json
        }
        finally {
            $contentsReader.Dispose()
        }
        $tableNames = @($contents.ExtensibleTables.Name)
        foreach ($table in @('ETWSnap Screenshots', 'ETWSnap Sessions')) {
            if ($table -notin $tableNames) {
                throw "The WPA plugin package metadata is missing table '$table'."
            }
        }
        $sourceNames = @($contents.ProcessingSources.Name)
        foreach ($sourceName in @('ETWSnap', 'ETWSnap Artifact ZIP')) {
            if ($sourceName -notin $sourceNames) {
                throw "The WPA plugin package metadata is missing processing source '$sourceName'."
            }
        }
        $extensions = @($contents.ProcessingSources.SupportedDataSources.Name)
        foreach ($extension in @('etl', 'zip')) {
            if ($extension -notin $extensions) {
                throw "The WPA plugin package metadata is missing extension '$extension'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $hash = (Get-FileHash $ptixPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content $checksumPath "$hash  $([IO.Path]::GetFileName($ptixPath))" -Encoding ascii

    $pluginPdb = Join-Path $pluginOutput 'EtwSnap.WpaPlugin.pdb'
    if (-not (Test-Path $pluginPdb -PathType Leaf)) {
        throw "The WPA plugin symbols are missing: $pluginPdb"
    }
    Compress-Archive -Path $pluginPdb -DestinationPath $symbolsPath -CompressionLevel Optimal

    [pscustomobject]@{
        Version = $Version
        PackagePath = $ptixPath
        PackageSha256 = $hash
        ChecksumPath = $checksumPath
        SymbolsPath = $symbolsPath
        PackageBytes = (Get-Item $ptixPath).Length
    }
}
finally {
    if (Test-Path $stage) {
        Remove-Item $stage -Recurse -Force
    }
    if ($null -ne $temporaryToolRoot -and (Test-Path $temporaryToolRoot)) {
        Remove-Item $temporaryToolRoot -Recurse -Force
    }
}