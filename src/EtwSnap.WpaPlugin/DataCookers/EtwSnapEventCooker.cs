using EtwSnap.WpaPlugin.Parsing;
using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Extensibility;
using Microsoft.Performance.SDK.Extensibility.DataCooking;
using Microsoft.Performance.SDK.Extensibility.DataCooking.SourceDataCooking;

namespace EtwSnap.WpaPlugin;

public sealed class EtwSnapEventCooker
    : SourceDataCooker<EtwSnapEvent, EtwSnapParsingContext, Type>
{
    public static readonly DataCookerPath DataCookerPath =
        DataCookerPath.ForSource(nameof(EtwSnapTraceParser), nameof(EtwSnapEventCooker));

    public EtwSnapEventCooker()
        : base(DataCookerPath)
    {
    }

    public override string Description => "Aggregates ETWSnap lifecycle, frame, and artifact events.";

    public override ReadOnlyHashSet<Type> DataKeys => new(new HashSet<Type>
    {
        typeof(RecordingStartedEvent),
        typeof(FrameCapturedEvent),
        typeof(FrameCaptureErrorEvent),
        typeof(RecordingStoppedEvent),
        typeof(ArtifactReferenceEvent),
        typeof(ArtifactCommittedEvent),
    });

    [DataOutput]
    public List<EtwSnapEvent> Events { get; } = new();

    public override DataProcessingResult CookDataElement(
        EtwSnapEvent data,
        EtwSnapParsingContext context,
        CancellationToken cancellationToken)
    {
        Events.Add(data);
        return DataProcessingResult.Processed;
    }
}