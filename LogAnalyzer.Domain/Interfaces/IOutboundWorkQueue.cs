using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Domain.Interfaces;

public interface IOutboundWorkQueue
{
    /// <summary>
    /// Attempts to enqueue without blocking. Returns false when the queue is full or integration is disabled.
    /// Must never throw to callers on the incident pipeline path.
    /// </summary>
    bool TryEnqueue(OutboundWorkItem item);
}
