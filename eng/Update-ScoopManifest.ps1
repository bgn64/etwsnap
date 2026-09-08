[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Hash,
    [string]$Repository = 'bgn64/etwsnap',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$root = Get-RepositoryRoot
$null = Get-VersionParts -Version $Version
if ($Hash -notmatch '^[0-9a-fA-F]{64}$') {
    throw 'Hash must be a SHA-256 value containing 64 hexadecimal characters.'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'bucket\etwsnap.json'
}

$assetName = "etwsnap-v$Version-win-x64.zip"
$releaseUrl = "https://github.com/$Repository/releases/download/v$Version/$assetName"
$manifest = [ordered]@{
    version = $Version
    description = 'Rolling screenshot capture correlated with ETW events.'
    homepage = "https://github.com/$Repository"
    license = 'MIT'
    architecture = [ordered]@{
        '64bit' = [ordered]@{
            url = $releaseUrl
            hash = $Hash.ToLowerInvariant()
        }
    }
    bin = 'etwsnap.exe'
    checkver = 'github'
    pre_install = @(
        "if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -or -not ((dotnet --list-runtimes 2>`$null) -match '^Microsoft\.NETCore\.App 10\.')) { error 'ETWSnap requires the .NET 10 Runtime. Install it with: winget install Microsoft.DotNet.Runtime.10'; break }"
    )
    autoupdate = [ordered]@{
        architecture = [ordered]@{
            '64bit' = [ordered]@{
                url = 'https://github.com/' + $Repository + '/releases/download/v$version/etwsnap-v$version-win-x64.zip'
            }
        }
    }
    notes = @(
        'ETWSnap requires the .NET 10 Runtime (Microsoft.NETCore.App).',
        'Install it with: winget install Microsoft.DotNet.Runtime.10'
    )
}

New-Item (Split-Path $OutputPath -Parent) -ItemType Directory -Force | Out-Null
$manifest | ConvertTo-Json -Depth 8 | Set-Content $OutputPath -Encoding utf8NoBOM
Write-Output $OutputPath