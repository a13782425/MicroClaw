namespace MicroClaw.Core;

/// <summary>
/// Object-local typed event dispatcher used by <see cref="MicroObject"/>.
/// </summary>
internal sealed class MicroEvent
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, List<Subscription>> _subscriptions = new();

    /// <summary>Subscribes to an event type on this dispatcher.</summary>
    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        Type eventType = typeof(TEvent);
        var subscription = new Subscription(
            this,
            eventType,
            (domainEvent, cancellationToken) => handler((TEvent)domainEvent, cancellationToken));

        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(eventType, out List<Subscription>? subscriptions))
            {
                subscriptions = [];
                _subscriptions[eventType] = subscriptions;
            }

            subscriptions.Add(subscription);
        }

        return subscription;
    }

    /// <summary>Publishes an event to subscribers registered for the event instance runtime type.</summary>
    public async ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        cancellationToken.ThrowIfCancellationRequested();

        Type eventType = domainEvent.GetType();

        Subscription[] snapshot;
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(eventType, out List<Subscription>? subscriptions) || subscriptions.Count == 0)
                return;

            snapshot = subscriptions.ToArray();
        }

        foreach (Subscription subscription in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await subscription.Handler(domainEvent, cancellationToken);
        }
    }

    /// <summary>Clears all subscriptions.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _subscriptions.Clear();
        }
    }

    private void Remove(Type eventType, Subscription subscription)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(eventType, out List<Subscription>? subscriptions))
                return;

            for (int index = subscriptions.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(subscriptions[index], subscription))
                    subscriptions.RemoveAt(index);
            }

            if (subscriptions.Count == 0)
                _subscriptions.Remove(eventType);
        }
    }

    private sealed class Subscription(
        MicroEvent owner,
        Type eventType,
        Func<object, CancellationToken, ValueTask> handler) : IDisposable
    {
        private MicroEvent? _owner = owner;

        public Func<object, CancellationToken, ValueTask> Handler { get; } = handler;

        public void Dispose()
        {
            MicroEvent? ownerSnapshot = Interlocked.Exchange(ref _owner, null);
            ownerSnapshot?.Remove(eventType, this);
        }
    }
}