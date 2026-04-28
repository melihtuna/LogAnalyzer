using System.Threading.Channels;
using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Processor.Queue;

public class QueuedLogAnalysisRequest
{
    public required LogRequest Request { get; init; }

    public required TaskCompletionSource<LogAnalysisResponse> CompletionSource { get; init; }

    public required CancellationToken CancellationToken { get; init; }
}
