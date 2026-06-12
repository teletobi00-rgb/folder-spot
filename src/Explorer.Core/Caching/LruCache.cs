namespace Explorer.Core.Caching;

/// <summary>스레드 안전 LRU 캐시. 용량 초과 시 가장 오래 사용되지 않은 항목을 제거한다.</summary>
public sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new();
    private readonly Lock _gate = new();

    public LruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }

            var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(new(key, value));
            _order.AddFirst(node);
            _map[key] = node;

            if (_map.Count > _capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }
}
