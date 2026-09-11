using EtwSnap.WpaPlugin.Models;
using EtwSnap.WpaPlugin.Parsing;
using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Extensibility;
using Microsoft.Performance.SDK.Processing;

namespace EtwSnap.WpaPlugin.Tables;

[Table]
public static class ScreenshotsTable
{
    public static TableDescriptor TableDescriptor => new(
        Guid.Parse("{71DB552E-7362-45C7-9C85-4DD8E6683BA0}"),
        "ETWSnap Screenshots",
        "Screenshot frames correlated with ETWSnap ETW events",
        "ETWSnap",
        requiredDataCookers: new[] { EtwSnapEventCooker.DataCookerPath });

    private static readonly ColumnConfiguration StartTimeColumn = Column(
        "{873042B1-A847-437F-BCA3-F8C2C3719B0C}", "Start Time", 120, TimestampFormatter.FormatMicrosecondsGrouped, AggregationMode.Min);
    private static readonly ColumnConfiguration DurationColumn = Column(
        "{01F2F90B-FEEE-4635-BC03-F4B668AA5AD3}", "Duration", 100, TimestampFormatter.FormatMillisecondsGrouped, AggregationMode.Sum);
    private static readonly ColumnConfiguration SessionIdColumn = Column("{7F964B80-F6A4-4CD6-BD03-4A0C15D82866}", "Session ID", 240);
    private static readonly ColumnConfiguration FrameNumberColumn = Column("{A90F6A94-F3DA-4965-85FD-8C1B5A172820}", "Frame Number", 110);
    private static readonly ColumnConfiguration AvailabilityColumn = Column("{7DE13404-DC47-41B0-8B1A-E1068822FCED}", "Availability", 120);
    private static readonly ColumnConfiguration ImagePathColumn = Column("{01F5A12E-7282-4139-8A21-D2572EFC3D87}", "Image Path", 360);
    private static readonly ColumnConfiguration WidthColumn = Column("{B8BF7D47-644A-4B6C-B97F-8FB516B7F270}", "Width", 80);
    private static readonly ColumnConfiguration HeightColumn = Column("{6E0E3A7B-A6BD-4C9D-8F47-D4C99813B194}", "Height", 80);
    private static readonly ColumnConfiguration PixelFormatColumn = Column("{8103A70D-CC82-45CB-BCC3-BA8629345F6A}", "Pixel Format", 100);
    private static readonly ColumnConfiguration PresentationTimeColumn = Column("{5EF73E16-A77D-4546-BB57-C70D852266EB}", "Presentation Time (100ns)", 170);
    private static readonly ColumnConfiguration CallbackQpcColumn = Column("{440F35D9-F86D-4C74-998C-54FD6318ED23}", "Callback QPC", 140);

    public static void BuildTable(ITableBuilder tableBuilder, IDataExtensionRetrieval requiredData)
    {
        var events = requiredData.QueryOutput<List<EtwSnapEvent>>(
            new DataOutputPath(EtwSnapEventCooker.DataCookerPath, nameof(EtwSnapEventCooker.Events)));
        var rows = EtwSnapDataSet.Build(events).Screenshots;
        var row = Projection.Index(rows);

        var savedScreenshots = CreateConfiguration("Saved Screenshots");
        savedScreenshots.InitialFilterQuery = "[Availability]:=\"Saved\"";
        savedScreenshots.InitialFilterShouldKeep = true;
        var allFrames = CreateConfiguration("All Frames");

        ScreenshotTableCommands.Register(tableBuilder, rows);
        tableBuilder.AddTableConfiguration(savedScreenshots);
        tableBuilder.AddTableConfiguration(allFrames);
        tableBuilder.SetDefaultTableConfiguration(allFrames);

        tableBuilder.SetRowCount(rows.Count)
            .AddColumn(StartTimeColumn, row.Compose(Projectors.ScreenshotStart))
            .AddColumn(DurationColumn, row.Compose(Projectors.ScreenshotDuration))
            .AddColumn(SessionIdColumn, row.Compose(Projectors.ScreenshotSessionId))
            .AddColumn(FrameNumberColumn, row.Compose(Projectors.FrameNumber))
            .AddColumn(AvailabilityColumn, row.Compose(Projectors.Availability))
            .AddColumn(ImagePathColumn, row.Compose(Projectors.ImagePath))
            .AddColumn(WidthColumn, row.Compose(Projectors.Width))
            .AddColumn(HeightColumn, row.Compose(Projectors.Height))
            .AddColumn(PixelFormatColumn, row.Compose(Projectors.PixelFormat))
            .AddColumn(PresentationTimeColumn, row.Compose(Projectors.PresentationTime))
            .AddColumn(CallbackQpcColumn, row.Compose(Projectors.CallbackQpc));
    }

    private static TableConfiguration CreateConfiguration(string name)
    {
        var configuration = new TableConfiguration(name)
        {
            Columns = new[]
            {
                SessionIdColumn,
                AvailabilityColumn,
                TableConfiguration.PivotColumn,
                FrameNumberColumn,
                ImagePathColumn,
                WidthColumn,
                HeightColumn,
                PixelFormatColumn,
                PresentationTimeColumn,
                CallbackQpcColumn,
                DurationColumn,
                TableConfiguration.GraphColumn,
                StartTimeColumn,
            },
        };
        configuration.AddColumnRole(ColumnRole.StartTime, StartTimeColumn);
        configuration.AddColumnRole(ColumnRole.Duration, DurationColumn);
        return configuration;
    }

    private static ColumnConfiguration Column(
        string id,
        string name,
        int width,
        string? cellFormat = null,
        AggregationMode aggregationMode = AggregationMode.None) => new(
            new ColumnMetadata(Guid.Parse(id), name),
            new UIHints
            {
                IsVisible = true,
                Width = width,
                CellFormat = cellFormat,
                AggregationMode = aggregationMode,
            });
}