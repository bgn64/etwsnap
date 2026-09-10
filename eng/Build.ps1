[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [switch]$SkipTests,
    [switch]$IncludeInteractiveTests
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$root = Get-RepositoryRoot
$parts = Get-VersionParts -Version $Version
$msbuild = Get-MSBuildPath
$configuration = 'Release'
$platform = 'x64'
$commonProperties = @(
    "/p:Configuration=$configuration",
    "/p:Platform=$platform",
    "/p:Version=$Version",
    "/p:EtwSnapVersionMajor=$($parts.Major)",
    "/p:EtwSnapVersionMinor=$($parts.Minor)",
    "/p:EtwSnapVersionPatch=$($parts.Patch)",
    "/p:EtwSnapVersionBuild=$($parts.Build)"
)

$nativeTargets = Join-Path $root 'packages\robmikh.common.0.0.23-beta\build\native\robmikh.common.targets'
if (-not (Test-Path $nativeTargets)) {
    throw 'The vendored robmikh.common 0.0.23-beta dependency is missing.'
}

Push-Location $root
try {
    Invoke-Checked -FilePath $msbuild -ArgumentList (@(
            'src\EtwSnap.Native\EtwSnap.Native.vcxproj',
            '/t:Rebuild',
            '/m',
            '/nologo',
            '/verbosity:minimal'
        ) + $commonProperties)

    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    Invoke-Checked $dotnet @(
        'clean',
        'src\EtwSnap.Cli\EtwSnap.Cli.csproj',
        '-c', $configuration,
        '-p:Platform=x64',
        '-p:SkipNativeBuild=true',
        '--nologo'
    )
    Invoke-Checked $dotnet @(
        'build',
        'src\EtwSnap.Cli\EtwSnap.Cli.csproj',
        '-c', $configuration,
        '-p:Platform=x64',
        '-p:SkipNativeBuild=true',
        "-p:Version=$Version",
        '--no-incremental',
        '--nologo'
    )
    Invoke-Checked $dotnet @(
        'build',
        'src\EtwSnap.WpaPlugin\EtwSnap.WpaPlugin.csproj',
        '-c', $configuration,
        "-p:Version=$Version",
        '--no-incremental',
        '--nologo'
    )

    if (-not $SkipTests) {
        foreach ($project in @(
            'tests\EtwSnap.UnitTests\EtwSnap.UnitTests.csproj',
            'tests\EtwSnap.IntegrationTests\EtwSnap.IntegrationTests.csproj',
            'tests\EtwSnap.WpaPlugin.Tests\EtwSnap.WpaPlugin.Tests.csproj'
        )) {
            Invoke-Checked $dotnet @(
                'build',
                $project,
                '-c', $configuration,
                '-p:Platform=x64',
                '-p:SkipNativeBuild=true',
                "-p:Version=$Version",
                '--nologo'
            )
        }

        Invoke-Checked $dotnet @(
            'test',
            'tests\EtwSnap.UnitTests\EtwSnap.UnitTests.csproj',
            '-c', $configuration,
            '-p:Platform=x64',
            '--no-build',
            '--nologo',
            '--verbosity', 'minimal'
        )

        Invoke-Checked $dotnet @(
            'test',
            'tests\EtwSnap.WpaPlugin.Tests\EtwSnap.WpaPlugin.Tests.csproj',
            '-c', $configuration,
            '--no-build',
            '--nologo',
            '--verbosity', 'minimal'
        )

        $integrationArguments = @(
            'test',
            'tests\EtwSnap.IntegrationTests\EtwSnap.IntegrationTests.csproj',
            '-c', $configuration,
            '-p:Platform=x64',
            '--no-build',
            '--nologo',
            '--verbosity', 'minimal'
        )
        if (-not $IncludeInteractiveTests) {
            $integrationArguments += @('--filter', 'Category!=Interactive')
        }
        Invoke-Checked $dotnet $integrationArguments
    }

    $wpr = Join-Path $env:SystemRoot 'System32\wpr.exe'
    if (Test-Path $wpr) {
        Invoke-Checked $wpr @('-profiles', (Join-Path $root 'profiles\EtwSnap.wprp'))
    }
    else {
        Write-Warning 'wpr.exe was not found; the bundled WPR profile was not validated.'
    }
}
finally {
    Pop-Location
}