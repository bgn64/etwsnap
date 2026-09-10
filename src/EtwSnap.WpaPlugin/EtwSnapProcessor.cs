using EtwSnap.WpaPlugin.Parsing;
using Microsoft.Performance.SDK.Extensibility.SourceParsing;
using Microsoft.Performance.SDK.Processing;

namespace EtwSnap.WpaPlugin;

public sealed class EtwSnapProcessor
    : CustomDataProcessorWithSourceParser<EtwSnapEvent, EtwSnapParsingContext, Type>
{
    public EtwSnapProcessor(
        ISourceParser<EtwSnapEvent, EtwSnapParsingContext, Type> sourceParser,
        ProcessorOptions options,
        IApplicationEnvironment applicationEnvironment,
        IProcessorEnvironment processorEnvironment)
        : base(sourceParser, options, applicationEnvironment, processorEnvironment)
    {
    }
}