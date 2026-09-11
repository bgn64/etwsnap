[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TracePath,
    [string]$WpaPath,
    [switch]$SkipBuild,
    [switch]$NoDefault,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$root = Get-RepositoryRoot
$trace = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($TracePath)
if (-not (Test-Path $trace -PathType Leaf)) {
    throw "The ETL does not exist: $trace"
}

if ([string]::IsNullOrWhiteSpace($WpaPath)) {
    $wpaCommand = Get-Command wpa.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    $candidates = @(
        $wpaCommand.Source,
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Windows Performance Toolkit\wpa.exe'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\Windows Performance Toolkit\wpa.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    $WpaPath = $candidates | Where-Object { Test-Path $_ -PathType Leaf } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($WpaPath) -or -not (Test-Path $WpaPath -PathType Leaf)) {
    throw 'Windows Performance Analyzer was not found. Pass its executable path with -WpaPath.'
}
$WpaPath = [IO.Path]::GetFullPath($WpaPath)

$project = Join-Path $root 'src\EtwSnap.WpaPlugin\EtwSnap.WpaPlugin.csproj'
if (-not $SkipBuild) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    Invoke-Checked $dotnet @('build', $project, '-c', 'Release', '--nologo')
}

$pluginDirectory = Join-Path $root 'src\EtwSnap.WpaPlugin\bin\Release\net8.0-windows'
foreach ($file in @(
    'EtwSnap.WpaPlugin.dll',
    'Microsoft.Diagnostics.Tracing.TraceEvent.dll'
)) {
    if (-not (Test-Path (Join-Path $pluginDirectory $file) -PathType Leaf)) {
        throw "The loose plugin output is missing $file. Build the plugin before launching WPA."
    }
}

$arguments = [Collections.Generic.List[string]]::new()
if ($NoDefault) {
    $arguments.Add('-nodefault')
}
$arguments.Add('-addsearchdir')
$arguments.Add($pluginDirectory)
$arguments.Add('-i')
$arguments.Add($trace)

if ($DryRun) {
    [pscustomobject]@{
        WpaPath = $WpaPath
        PluginDirectory = $pluginDirectory
        TracePath = $trace
        Arguments = $arguments.ToArray()
    }
    return
}

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $WpaPath
$startInfo.WorkingDirectory = $root
$startInfo.UseShellExecute = $false
foreach ($argument in $arguments) {
    $startInfo.ArgumentList.Add($argument)
}

$process = [Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) {
    throw 'WPA did not start.'
}
Write-Host "Started WPA process $($process.Id) with ETWSnap plugin directory $pluginDirectory"