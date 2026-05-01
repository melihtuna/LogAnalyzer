using System.Threading.Channels;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Domain.Interfaces;

namespace LogAnalyzer.Processor.Queue;

public class QueuedLogAnalysisRequest
{
    public required ILogProvider LogProvider { get; init; }

    public required bool IncludeRawAIResponse { get; init; }

    public required TaskCompletionSource<LogAnalysisResponse> CompletionSource { get; init; }

    public required CancellationToken CancellationToken { get; init; }
}
