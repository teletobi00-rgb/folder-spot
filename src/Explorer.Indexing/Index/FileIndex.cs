using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Explorer.Indexing.Index;

/// <summary>검색 결과 한 건 (경로는 조회 시점에 재구성된 불변 스냅샷).</summary>
public sealed record SearchHit
{
    public required string FullPath { get; init; }

    public required string Name { get; init; }

    public bool IsDirectory { get; init; }

    public long Size { get; init; }

    public DateTime Modified { get; init; }

    /// <summary>일치 등급: 0=정확, 1=접두, 2=부분 — 소비자가 추가 가중(MRU 등)에 쓴다.</summary>
    public int Rank { get; init; }
}

/// <summary>인덱스에 넣을 항목 하나 (스캔/FSW 소스가 생산).</summary>
public readonly record struct IndexItem(
    string ParentPath,
    string Name,
    bool IsDirectory,
    long Size,
    long ModifiedTicks);

public interface IFileIndex
{
    int Count { get; }

    /// <summary>이름 부분 일치 검색 (대소문자 무시, 정확>접두>부분 랭킹). 빈 질의는 빈 결과.</summary>
    IReadOnlyList<SearchHit> Search(string query, int maxResults, CancellationToken cancellationToken = default);

    void AddOrUpdate(in IndexItem item);

    void AddBatch(IReadOnlyList<IndexItem> items);

    /// <summary>경로의 항목을 제거한다. 디렉터리면 하위 전체 제거.</summary>
    void RemoveSubtree(string fullPath);

    void Rename(string oldFullPath, string newName);
}

/// <summary>
/// Everything 방식 인메모리 인덱스: 모든 노드는 부모 id만 참조하고 이름을 한 번씩만 보관한다
/// (전체 경로 미보관 — 디렉터리 이름변경이 하위 경로에 O(1)로 반영된다).
/// 쓰기는 인덱싱 워커, 읽기는 UI 검색 — ReaderWriterLockSlim으로 보호한다.
/// </summary>
public sealed class FileIndex : IFileIndex, IDisposable
{
    private const int CancellationCheckInterval = 4096;

    private struct Node
    {
        public string Name;
        public string SearchName;
        public int ParentId;
        public long Size;
        public long ModifiedTicks;
        public bool IsDirectory;
        public bool Deleted;
    }

    private readonly ReaderWriterLockSlim _lock = new();
    private readonly List<Node> _nodes = [];

    // parentId(-1 = 루트 레벨) → 자식 이름 → 노드 id. 경로 해석과 증분 갱신에 쓴다.
    private readonly Dictionary<int, Dictionary<string, int>> _children = [];

    private int _liveCount;

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _liveCount;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public IReadOnlyList<SearchHit> Search(string query, int maxResults, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeQuery(query);
        if (normalized.Length == 0 || maxResults <= 0)
        {
            return [];
        }

        // 넓은 질의("a" 등)가 전 노드를 후보로 모으지 않도록 상한을 둔다 — 상한 도달 시
        // 뒤쪽의 더 좋은 순위 항목을 놓칠 수 있지만 as-you-type 지연/메모리가 우선이다.
        var candidateCap = Math.Max(maxResults * 100, 1000);

        _lock.EnterReadLock();
        try
        {
            var matches = new List<(int Id, int Rank)>();
            for (var id = 0; id < _nodes.Count && matches.Count < candidateCap; id++)
            {
                if ((id & (CancellationCheckInterval - 1)) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var node = _nodes[id];
                if (node.Deleted)
                {
                    continue;
                }

                if (!node.SearchName.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rank = string.Equals(node.SearchName, normalized, StringComparison.OrdinalIgnoreCase) ? 0
                    : node.SearchName.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ? 1
                    : 2;
                matches.Add((id, rank));
            }

            return [.. matches
                .OrderBy(m => m.Rank)
                .ThenBy(m => _nodes[m.Id].Name.Length)
                .ThenBy(m => _nodes[m.Id].Name, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(m => CreateHit(m.Id, m.Rank))];
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void AddOrUpdate(in IndexItem item)
    {
        _lock.EnterWriteLock();
        try
        {
            AddOrUpdateCore(item);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void AddBatch(IReadOnlyList<IndexItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _lock.EnterWriteLock();
        try
        {
            foreach (var item in items)
            {
                AddOrUpdateCore(item);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void RemoveSubtree(string fullPath)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!TryResolve(fullPath, out var parentId, out var name, out var nodeId))
            {
                return;
            }

            RemoveNodeRecursive(nodeId);
            if (_children.TryGetValue(parentId, out var siblings))
            {
            siblings.Remove(NameKey(name));
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Rename(string oldFullPath, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        _lock.EnterWriteLock();
        try
        {
            if (!TryResolve(oldFullPath, out var parentId, out var oldName, out var nodeId))
            {
                return;
            }

            var siblings = _children[parentId];
            siblings.Remove(NameKey(oldName));

            // 같은 이름이 이미 있으면(드문 레이스) 기존 항목을 대체한다.
            if (siblings.TryGetValue(NameKey(newName), out var existingId))
            {
                RemoveNodeRecursive(existingId);
            }

            siblings[NameKey(newName)] = nodeId;
            var node = _nodes[nodeId];
            node.Name = newName;
            node.SearchName = NormalizeName(newName);
            _nodes[nodeId] = node;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>스냅샷 저장용 — 살아있는 모든 노드를 (id, parentId, ...) 형태로 순회한다.</summary>
    public void ExportNodes(Action<int, int, string, long, long, bool> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        _lock.EnterReadLock();
        try
        {
            for (var id = 0; id < _nodes.Count; id++)
            {
                var node = _nodes[id];
                if (!node.Deleted)
                {
                    visitor(id, node.ParentId, node.Name, node.Size, node.ModifiedTicks, node.IsDirectory);
                }
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// 스냅샷 로드용 — id 오름차순으로 호출되어야 한다(부모가 자식보다 먼저).
    /// 내보낸 parent_id가 원본 id를 참조하므로 반드시 원본 id 슬롯에 복원한다
    /// (삭제로 생긴 id 공백은 삭제 표시 노드로 채워 위치를 보존).
    /// </summary>
    public void ImportNode(int originalId, int parentId, string name, long size, long modifiedTicks, bool isDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(originalId);

        _lock.EnterWriteLock();
        try
        {
            while (_nodes.Count < originalId)
            {
                _nodes.Add(new Node { Name = string.Empty, SearchName = string.Empty, ParentId = -1, Deleted = true });
            }

            if (_nodes.Count != originalId)
            {
                throw new InvalidOperationException(
                    $"ImportNode는 id 오름차순으로 호출되어야 합니다 (현재 {_nodes.Count}, 요청 {originalId}).");
            }

            _nodes.Add(new Node
            {
                Name = name,
                SearchName = NormalizeName(name),
                ParentId = parentId,
                Size = size,
                ModifiedTicks = modifiedTicks,
                IsDirectory = isDirectory,
            });
            GetChildren(parentId)[NameKey(name)] = originalId;
            _liveCount++;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Dispose() => _lock.Dispose();

    private static string NormalizeQuery(string? query) =>
        string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim().Normalize(NormalizationForm.FormC);

    private static string NormalizeName(string name) => name.Normalize(NormalizationForm.FormC);

    private static string NameKey(string name) => NormalizeName(name);

    private void AddOrUpdateCore(in IndexItem item)
    {
        var parentId = EnsureDirectoryChain(item.ParentPath);
        var siblings = GetChildren(parentId);

        if (siblings.TryGetValue(NameKey(item.Name), out var existingId))
        {
            var node = _nodes[existingId];
            node.Size = item.Size;
            node.ModifiedTicks = item.ModifiedTicks;
            node.IsDirectory = item.IsDirectory;
            _nodes[existingId] = node;
            return;
        }

        AppendNode(parentId, item.Name, item.Size, item.ModifiedTicks, item.IsDirectory);
    }

    /// <summary>"C:\a\b" 형태의 디렉터리 경로를 노드 체인으로 보장하고 마지막 디렉터리의 id를 돌려준다.</summary>
    private int EnsureDirectoryChain(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        var root = Path.GetPathRoot(directoryPath)
            ?? throw new ArgumentException($"루트가 없는 경로: {directoryPath}", nameof(directoryPath));

        var currentId = EnsureChild(-1, root, isDirectory: true);
        var remainder = directoryPath[root.Length..];
        foreach (var segment in remainder.Split(
            Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            currentId = EnsureChild(currentId, segment, isDirectory: true);
        }

        return currentId;
    }

    private int EnsureChild(int parentId, string name, bool isDirectory)
    {
        var siblings = GetChildren(parentId);
        if (siblings.TryGetValue(NameKey(name), out var existing))
        {
            return existing;
        }

        return AppendNode(parentId, name, size: 0, modifiedTicks: 0, isDirectory);
    }

    private int AppendNode(int parentId, string name, long size, long modifiedTicks, bool isDirectory)
    {
        var id = _nodes.Count;
        _nodes.Add(new Node
        {
            Name = name,
            SearchName = NormalizeName(name),
            ParentId = parentId,
            Size = size,
            ModifiedTicks = modifiedTicks,
            IsDirectory = isDirectory,
        });
        GetChildren(parentId)[NameKey(name)] = id;
        _liveCount++;
        return id;
    }

    private Dictionary<string, int> GetChildren(int parentId)
    {
        if (!_children.TryGetValue(parentId, out var map))
        {
            map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _children[parentId] = map;
        }

        return map;
    }

    private bool TryResolve(string fullPath, out int parentId, [NotNullWhen(true)] out string? name, out int nodeId)
    {
        parentId = -1;
        name = null;
        nodeId = -1;
        if (string.IsNullOrWhiteSpace(fullPath) || Path.GetPathRoot(fullPath) is not { Length: > 0 } root)
        {
            return false;
        }

        var segments = fullPath[root.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        // 루트 자체를 가리키면 루트 노드
        var currentParent = -1;
        var currentName = root;
        if (!TryGetChild(currentParent, currentName, out var currentId))
        {
            return false;
        }

        foreach (var segment in segments)
        {
            currentParent = currentId;
            currentName = segment;
            if (!TryGetChild(currentParent, currentName, out currentId))
            {
                return false;
            }
        }

        parentId = currentParent;
        name = currentName;
        nodeId = currentId;
        return true;
    }

    private bool TryGetChild(int parentId, string name, out int nodeId)
    {
        nodeId = -1;
        return _children.TryGetValue(parentId, out var siblings) && siblings.TryGetValue(NameKey(name), out nodeId);
    }

    private void RemoveNodeRecursive(int nodeId)
    {
        var node = _nodes[nodeId];
        if (node.Deleted)
        {
            return;
        }

        if (_children.TryGetValue(nodeId, out var children))
        {
            foreach (var childId in children.Values.ToArray())
            {
                RemoveNodeRecursive(childId);
            }

            _children.Remove(nodeId);
        }

        node.Deleted = true;
        _nodes[nodeId] = node;
        _liveCount--;
    }

    private SearchHit CreateHit(int nodeId, int rank)
    {
        var node = _nodes[nodeId];
        return new SearchHit
        {
            FullPath = BuildFullPath(nodeId),
            Name = node.Name,
            IsDirectory = node.IsDirectory,
            Size = node.Size,
            Modified = node.ModifiedTicks > 0 ? new DateTime(node.ModifiedTicks, DateTimeKind.Local) : default,
            Rank = rank,
        };
    }

    private string BuildFullPath(int nodeId)
    {
        var segments = new Stack<string>();
        var current = nodeId;
        while (current >= 0)
        {
            segments.Push(_nodes[current].Name);
            current = _nodes[current].ParentId;
        }

        var path = segments.Pop(); // 루트 ("C:\")
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }
}
