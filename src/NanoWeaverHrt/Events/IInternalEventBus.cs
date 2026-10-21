namespace NanoWeaverHrt.Events;

/// <summary>
/// High-throughput, zero-allocation internal event bus decoupling coordinate parsers,
/// simulation dynamics, telemetry collectors, and compliance loggers from downstream consumers.
/// </summary>
public interface IInternalEventBus : IAsyncDisposable
{
    /// <summary>
    /// Publishes an event to all active in-process subscribers asynchronously.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event being published.</typeparam>
    /// <param name="event">The event payload instance.</param>
    /// <param name="ct">Optional cancellation token to abort publishing.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous publication operation.</returns>
    ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : notnull;

    /// <summary>
    /// Subscribes to events of type <typeparamref name="TEvent"/> using an asynchronous stream.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to consume.</typeparam>
    /// <param name="ct">Cancellation token used to terminate the stream subscription.</param>
    /// <returns>An asynchronous stream yielding published events as they occur.</returns>
    IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken ct = default) where TEvent : notnull;

    /// <summary>
    /// Registers a callback handler that is executed whenever an event of type <typeparamref name="TEvent"/> is published.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to observe.</typeparam>
    /// <param name="handler">Asynchronous delegate invoked with the event payload and cancellation token.</param>
    /// <returns>An <see cref="IDisposable"/> token used to unregister the handler subscription.</returns>
    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler) where TEvent : notnull;

    /// <summary>
    /// Retrieves the count of active registered callback and channel subscribers for a given event type.
    /// </summary>
    /// <typeparam name="TEvent">The event type to query.</typeparam>
    /// <returns>The total number of active subscribers currently listening.</returns>
    int GetSubscriberCount<TEvent>() where TEvent : notnull;
}
