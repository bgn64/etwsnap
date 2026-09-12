using Microsoft.Performance.SDK.Processing;

namespace EtwSnap.WpaPlugin;

[ProcessingSource(
    "{D8B705BC-7A8A-4E34-83A0-9DDCAC4756C8}",
    "ETWSnap Artifact ZIP",
    "ETWSnap screenshot sessions from .etwsnap.zip artifacts")]
[FileDataSource(".zip", "ETWSnap artifact ZIPs")]
public sealed class EtwSnapArtifactProcessingSource : ProcessingSource
{
    protected override bool IsDataSourceSupportedCore(IDataSource dataSource) =>
        dataSource is FileDataSource file &&
        file.FullPath.EndsWith(EtwSnap.Artifacts.EmbeddedArtifactConstants.ArtifactFileExtension, StringComparison.OrdinalIgnoreCase);

    protected override ICustomDataProcessor CreateProcessorCore(
        IEnumerable<IDataSource> dataSources,
        IProcessorEnvironment processorEnvironment,
        ProcessorOptions options) =>
        new EtwSnapProcessor(
            new Parsing.EtwSnapTraceParser(dataSources),
            options,
            ApplicationEnvironment,
            processorEnvironment);

    public override ProcessingSourceInfo GetAboutInfo() => new()
    {
        Owners = new[]
        {
            new ContactInfo
            {
                Name = "ETWSnap contributors",
                EmailAddresses = Array.Empty<string>(),
            },
        },
        LicenseInfo = new LicenseInfo
        {
            Name = "MIT",
            Uri = "https://opensource.org/license/mit",
            Text = "MIT License",
        },
        ProjectInfo = new ProjectInfo
        {
            Uri = "https://github.com/bgn64/etwsnap",
        },
    };
}