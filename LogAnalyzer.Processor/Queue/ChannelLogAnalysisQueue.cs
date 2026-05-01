using System.Threading.Channels;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Providers;
using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Processor.Queue;

public class ChannelLogAnalysisQueue(Channel<QueuedLogAnalysisRequest> channel) : ILogAnalysisQueue
{
    private static readonly TimeSpan QueueWaitTimeout = TimeSpan.FromMilliseconds(200);

    public async Task<LogAnalysisResponse> EnqueueAsync(ILogProvider logProvider, bool includeRawAIResponse, CancellationToken cancellationToken = default)
    {
        if (logProvider is null)
        {
            throw new ArgumentNullException(nameof(logProvider));
        }

        var completionSource = new TaskCompletionSource<LogAnalysisResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedRequest = new QueuedLogAnalysisRequest
        {
            LogProvider = logProvider,
            IncludeRawAIResponse = includeRawAIResponse,
            CompletionSource = completionSource,
            CancellationToken = cancellationToken
        };

        if (!channel.Writer.TryWrite(queuedRequest))
        {
            using var timeoutCts = new CancellationTokenSource(QueueWaitTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                var canWrite = await channel.Writer.WaitToWriteAsync(linkedCts.Token);
                if (!canWrite || !channel.Writer.TryWrite(queuedRequest))
                {
                    throw new QueueFullException("Log analysis queue is full. Please retry shortly.");
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new QueueFullException("Log analysis queue is full. Please retry shortly.");
            }
        }

        using var registration = cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));
        return await completionSource.Task;
    }

    public async Task<LogAnalysisResponse> EnqueueAsync(LogRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return await EnqueueAsync(new StaticLogProvider(request.Logs), request.IncludeRawAIResponse, cancellationToken);
    }
}
