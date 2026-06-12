using Explorer.Core.FileOperations;

namespace Explorer.Core.Undo;

/// <summary>되돌리기 가능한 작업 한 건 — 역연산은 클로저로 보유한다 (세션 한정, 직렬화하지 않음).</summary>
public sealed record UndoEntry(string Description, Func<Task<FileOperationResult>> InverseAsync);

public interface IUndoService
{
    bool CanUndo { get; }

    /// <summary>마지막 작업의 설명 (스택이 비었으면 null).</summary>
    string? PeekDescription { get; }

    event EventHandler? Changed;

    void Push(UndoEntry entry);

    /// <summary>
    /// 마지막 작업을 되돌린다. 스택이 비었거나 이미 실행 중이면 null.
    /// 역연산이 실패해도 항목은 소비된다 (재시도로 인한 중복 부작용 방지).
    /// </summary>
    Task<(string Description, FileOperationResult Result)?> TryUndoAsync();
}

public sealed class UndoService : IUndoService
{
    private const int MaxEntries = 20;

    private readonly Lock _gate = new();
    private readonly LinkedList<UndoEntry> _stack = [];
    private bool _undoInFlight;

    public event EventHandler? Changed;

    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return _stack.Count > 0 && !_undoInFlight;
            }
        }
    }

    public string? PeekDescription
    {
        get
        {
            lock (_gate)
            {
                return _stack.First?.Value.Description;
            }
        }
    }

    public void Push(UndoEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            _stack.AddFirst(entry);
            while (_stack.Count > MaxEntries)
            {
                _stack.RemoveLast();
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<(string Description, FileOperationResult Result)?> TryUndoAsync()
    {
        UndoEntry entry;
        lock (_gate)
        {
            if (_undoInFlight || _stack.First is null)
            {
                return null;
            }

            entry = _stack.First.Value;
            _stack.RemoveFirst();
            _undoInFlight = true;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = await entry.InverseAsync().ConfigureAwait(false);
            return (entry.Description, result);
        }
        finally
        {
            lock (_gate)
            {
                _undoInFlight = false;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
