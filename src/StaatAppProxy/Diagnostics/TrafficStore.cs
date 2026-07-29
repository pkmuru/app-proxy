namespace StaatAppProxy.Diagnostics;

/// <summary>
/// Holds the most recent exchanges in memory for the diagnostics UI. Oldest entries fall off the
/// end; nothing is persisted, so a restart starts from empty.
/// </summary>
public sealed class TrafficStore
{
    public const int Capacity = 100;

    private readonly Lock _gate = new();
    private readonly Queue<CapturedExchange> _items = new(Capacity);

    public void Add(CapturedExchange exchange)
    {
        lock (_gate)
        {
            _items.Enqueue(exchange);
            while (_items.Count > Capacity)
            {
                _items.Dequeue();
            }
        }
    }

    /// <summary>Everything currently held, newest first.</summary>
    public IReadOnlyList<CapturedExchange> GetAll()
    {
        lock (_gate)
        {
            return _items.Reverse().ToList();
        }
    }

    public CapturedExchange? Get(Guid id)
    {
        lock (_gate)
        {
            return _items.FirstOrDefault(item => item.Id == id);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }
    }
}
