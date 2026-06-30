using Orleans.Streams;

namespace LLMCostControl.Grains.Tests;

/// <summary>
/// Simple async observer that invokes a callback on each message.
/// </summary>
public sealed class SimpleAsyncObserver<T>(Action<T> onNext) : IAsyncObserver<T>
{
    /// <summary>Called when the next item is available.</summary>
    public Task OnNextAsync(T item, StreamSequenceToken? sequenceToken = null)
    {
        onNext(item);
        return Task.CompletedTask;
    }

    /// <summary>Called when the stream completes.</summary>
    public Task OnCompletedAsync() => Task.CompletedTask;

    /// <summary>Called when the stream has an error.</summary>
    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
}
