using System.Diagnostics;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace EtwSnap.Host.Tracing;

internal sealed class WprController : IWprController
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(10);
    private readonly string _wprPath;
    private readonly string _supplementalProfilePath;

    public WprController(string supplementalProfilePath)
    {
        _wprPath = Path.Combine(Environment.SystemDirectory, "wpr.exe");
        _supplementalProfilePath = Path.GetFullPath(supplementalProfilePath);
    }

    public async Task<WprSession> StartAsync(Guid sessionId, string? userProfileSelector, CancellationToken cancellationToken)
    {
        EnsurePrerequisites();
        var supplementalHash = await HashFileAsync(_supplementalProfilePath, cancellationToken).ConfigureAwait(false);
        string? userPath = null;
        string? normalizedUserSelector = null;
        string? userHash = null;

        if (userProfileSelector is not null)
        {
            (userPath, normalizedUserSelector) = ParseProfileSelector(userProfileSelector);
            await RunCheckedAsync(["-profiles", userPath], StartTimeout, cancellationToken).ConfigureAwait(false);
            userHash = await HashFileAsync(userPath, cancellationToken).ConfigureAwait(false);
        }

        var instanceName = $"EtwSnap_{sessionId:N}";
        var arguments = CreateStartArguments(instanceName, _supplementalProfilePath, normalizedUserSelector);

        await RunCheckedAsync(arguments, StartTimeout, cancellationToken).ConfigureAwait(false);
        return new WprSession(
            instanceName,
            _supplementalProfilePath,
            supplementalHash,
            userPath,
            normalizedUserSelector,
            userHash);
    }

    public Task StopAsync(WprSession session, string outputPath, CancellationToken cancellationToken) =>
        RunCheckedAsync(
            ["-stop", Path.GetFullPath(outputPath), "-skipPdbGen", "-instancename", session.InstanceName],
            StopTimeout,
            cancellationToken);

    public Task CancelAsync(WprSession session, CancellationToken cancellationToken) =>
        CancelInstanceAsync(session.InstanceName, cancellationToken);

    public Task CancelInstanceAsync(string instanceName, CancellationToken cancellationToken) =>
        RunCheckedAsync(["-cancel", "-instancename", instanceName], StartTimeout, cancellationToken);

    private void EnsurePrerequisites()
    {
        if (!File.Exists(_wprPath))
        {
            throw new WprException($"Windows Performance Recorder was not found at {_wprPath}.");
        }
        if (!File.Exists(_supplementalProfilePath))
        {
            throw new WprException($"The ETWSnap supplemental profile was not found at {_supplementalProfilePath}.");
        }
    }

    internal static IReadOnlyList<string> CreateStartArguments(
        string instanceName,
        string supplementalProfilePath,
        string? userProfileSelector)
    {
        var arguments = new List<string> { "-start", $"{supplementalProfilePath}!EtwSnap.Verbose" };
        if (userProfileSelector is not null)
        {
            arguments.Add("-start");
            arguments.Add(userProfileSelector);
        }
        arguments.Add("-instancename");
        arguments.Add(instanceName);
        return arguments;
    }

    private async Task RunCheckedAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _wprPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new WprException("Failed to start wpr.exe.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                _ = await standardOutput.ConfigureAwait(false);
                _ = await standardError.ConfigureAwait(false);
            }
            catch
            {
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            throw new WprException($"wpr.exe did not finish within {timeout}.");
        }

        var output = await standardOutput.ConfigureAwait(false);
        var error = await standardError.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var diagnostic = string.Join(Environment.NewLine, new[] { error, output }.Where(value => !string.IsNullOrWhiteSpace(value)));
            throw new WprException($"wpr.exe exited with code {process.ExitCode}: {diagnostic.Trim()}");
        }
    }

    internal static (string Path, string Selector) ParseProfileSelector(string selector)
    {
        var separator = selector.LastIndexOf('!');
        if (separator <= 0 || separator == selector.Length - 1)
        {
            throw new WprException("A profile selector must use <file.wprp>!<ProfileName>[.light|.verbose].");
        }

        var path = Path.GetFullPath(selector[..separator]);
        if (!path.EndsWith(".wprp", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            throw new WprException($"The WPR profile does not exist: {path}");
        }

        var requestedSelector = selector[(separator + 1)..];
        ValidateProfileSelection(path, requestedSelector);
        return (path, $"{path}!{requestedSelector}");
    }

    private static void ValidateProfileSelection(string path, string selector)
    {
        var detailSeparator = selector.LastIndexOf('.');
        var detail = detailSeparator > 0 ? selector[(detailSeparator + 1)..] : null;
        var hasDetail = detail is not null &&
            (detail.Equals("light", StringComparison.OrdinalIgnoreCase) ||
             detail.Equals("verbose", StringComparison.OrdinalIgnoreCase));
        var profileName = hasDetail ? selector[..detailSeparator] : selector;

        var document = XDocument.Load(path, LoadOptions.None);
        var matches = document.Descendants()
            .Where(element => element.Name.LocalName == "Profile")
            .Where(element => string.Equals((string?)element.Attribute("Name"), profileName, StringComparison.OrdinalIgnoreCase));
        if (hasDetail)
        {
            matches = matches.Where(element =>
                string.Equals((string?)element.Attribute("DetailLevel"), detail, StringComparison.OrdinalIgnoreCase));
        }

        if (!matches.Any())
        {
            throw new WprException($"The profile selector '{selector}' was not found in {path}.");
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
