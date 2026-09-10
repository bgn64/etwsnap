using EtwSnap.WpaPlugin.Models;
using EtwSnap.WpaPlugin.Parsing;
using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Extensibility;
using Microsoft.Performance.SDK.Processing;

namespace EtwSnap.WpaPlugin.Tables;

[Table]
public static class SessionsTable
{
    public static TableDescriptor TableDescriptor => new(
        Guid.Parse("{62C1716B-54C4-4408-A09F-AC6ED32FA4F3}"),
        "ETWSnap Sessions",
        "ETWSnap recording lifecycle and artifact status",
        "ETWSnap",
        requiredDataCookers: new[] { EtwSnapEventCooker.DataCookerPath });

    private static readonly ColumnConfiguration StartTimeColumn = Column(
        "{207E58B9-B74D-4C90-B7B2-DCED4C731570}", "Start Time", 120, TimestampFormatter.FormatMicrosecondsGrouped, AggregationMode.Min);
    private static readonly ColumnConfiguration DurationColumn = Column(
        "{5FE340A7-A2E0-409F-B696-377B0BCB4715}", "Duration", 100, TimestampFormatter.FormatMillisecondsGrouped, AggregationMode.Sum);
    private static readonly ColumnConfiguration SessionIdColumn = Column("{49AEE0FB-917C-4752-B34E-E98C5D301E3E}", "Session ID", 240);
    private static readonly ColumnConfiguration FramesPerSecondColumn = Column("{53DB1A91-A64B-4912-B087-197E7A72273F}", "FPS", 70);
    private static readonly ColumnConfiguration BufferBytesColumn = Column("{912F7928-C0A3-4F58-B10C-EE979EFA9C4E}", "Buffer Bytes", 110);
    private static readonly ColumnConfiguration TargetKindColumn = Column("{158B860D-1EC7-4361-9FE6-4EC6FBAC545E}", "Target Kind", 100);
    private static readonly ColumnConfiguration TargetHandleColumn = Column("{44819345-E048-4262-B77E-532921315585}", "Target Handle", 120);
    private static readonly ColumnConfiguration AcceptedFramesColumn = Column("{DF4F8C10-AECD-401B-B4D4-384C8ECB2040}", "Accepted", 90);
    private static readonly ColumnConfiguration RetainedFramesColumn = Column("{552D706C-84DD-4F1A-AE64-EFE8FC636E21}", "Retained", 90);
    private static readonly ColumnConfiguration EvictedFramesColumn = Column("{6117C61D-60F4-4619-9E2C-445459A3A12D}", "Evicted", 90);
    private static readonly ColumnConfiguration DroppedFramesColumn = Column("{160FA7E4-9C3E-49B4-A6BE-8975913AE6F1}", "Dropped", 90);
    private static readonly ColumnConfiguration ErrorCountColumn = Column("{B9F92C85-7FC3-4079-A2BE-02A6251B5EED}", "Errors", 80);
    private static readonly ColumnConfiguration ExportedFramesColumn = Column("{61F1773A-8057-4910-B62A-10B12F414A90}", "Exported", 90);
    private static readonly ColumnConfiguration FailedFramesColumn = Column("{35AE7710-3527-436F-92D1-0ECEB2F45E8F}", "Failed", 80);
    private static readonly ColumnConfiguration ArtifactStateColumn = Column("{8D2D59D8-FBC2-48B7-8DA0-978F0FDD791A}", "Artifact State", 130);
    private static readonly ColumnConfiguration ManifestPathColumn = Column("{77856A34-E15E-4261-B290-4D9B77482622}", "Manifest Path", 360);
    private static readonly ColumnConfiguration ArtifactDetailColumn = Column("{5E39D902-A0B4-4F24-AD3F-C37A85FF1290}", "Artifact Detail", 320);

    public static void BuildTable(ITableBuilder tableBuilder, IDataExtensionRetrieval requiredData)
    {
        var events = requiredData.QueryOutput<List<EtwSnapEvent>>(
            new DataOutputPath(EtwSnapEventCooker.DataCookerPath, nameof(EtwSnapEventCooker.Events)));
        var rows = EtwSnapDataSet.Build(events).Sessions;
        var row = Projection.Index(rows);
        var configuration = new TableConfiguration("Sessions")
        {
            Columns = new[]
            {
                SessionIdColumn,
                ArtifactStateColumn,
                TableConfiguration.PivotColumn,
                AcceptedFramesColumn,
                RetainedFramesColumn,
                EvictedFramesColumn,
                DroppedFramesColumn,
                ErrorCountColumn,
                ExportedFramesColumn,
                FailedFramesColumn,
                FramesPerSecondColumn,
                BufferBytesColumn,
                TargetKindColumn,
                TargetHandleColumn,
                ManifestPathColumn,
                ArtifactDetailColumn,
                DurationColumn,
                TableConfiguration.GraphColumn,
                StartTimeColumn,
            },
        };
        configuration.AddColumnRole(ColumnRole.StartTime, StartTimeColumn);
        configuration.AddColumnRole(ColumnRole.Duration, DurationColumn);

        tableBuilder.AddTableConfiguration(configuration);
        tableBuilder.SetDefaultTableConfiguration(configuration);
        tableBuilder.SetRowCount(rows.Count)
            .AddColumn(StartTimeColumn, row.Compose(Projectors.SessionStart))
            .AddColumn(DurationColumn, row.Compose(Projectors.SessionDuration))
            .AddColumn(SessionIdColumn, row.Compose(Projectors.SessionId))
            .AddColumn(FramesPerSecondColumn, row.Compose(Projectors.FramesPerSecond))
            .AddColumn(BufferBytesColumn, row.Compose(Projectors.BufferBytes))
            .AddColumn(TargetKindColumn, row.Compose(Projectors.TargetKind))
            .AddColumn(TargetHandleColumn, row.Compose(Projectors.TargetHandle))
            .AddColumn(AcceptedFramesColumn, row.Compose(Projectors.AcceptedFrames))
            .AddColumn(RetainedFramesColumn, row.Compose(Projectors.RetainedFrames))
            .AddColumn(EvictedFramesColumn, row.Compose(Projectors.EvictedFrames))
            .AddColumn(DroppedFramesColumn, row.Compose(Projectors.DroppedFrames))
            .AddColumn(ErrorCountColumn, row.Compose(Projectors.ErrorCount))
            .AddColumn(ExportedFramesColumn, row.Compose(Projectors.ExportedFrames))
            .AddColumn(FailedFramesColumn, row.Compose(Projectors.FailedFrames))
            .AddColumn(ArtifactStateColumn, row.Compose(Projectors.ArtifactState))
            .AddColumn(ManifestPathColumn, row.Compose(Projectors.ManifestPath))
            .AddColumn(ArtifactDetailColumn, row.Compose(Projectors.ArtifactDetail));
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