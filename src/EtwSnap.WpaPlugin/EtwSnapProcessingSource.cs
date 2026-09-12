using Microsoft.Performance.SDK.Processing;

namespace EtwSnap.WpaPlugin;

[ProcessingSource(
    "{4E070D12-F361-4F68-8E54-1B5D78E25D09}",
    "ETWSnap",
    "ETWSnap screenshot sessions correlated with ETW timeline events")]
[FileDataSource(".etl", "ETWSnap ETL traces")]
public sealed class EtwSnapProcessingSource : ProcessingSource
{
    protected override bool IsDataSourceSupportedCore(IDataSource dataSource) =>
        dataSource is FileDataSource file &&
        string.Equals(Path.GetExtension(file.FullPath), ".etl", StringComparison.OrdinalIgnoreCase);

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