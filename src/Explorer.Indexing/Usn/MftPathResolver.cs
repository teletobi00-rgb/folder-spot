using Explorer.Indexing.Index;

namespace Explorer.Indexing.Usn;

/// <summary>MFT 레코드 한 건 (USN_RECORD에서 추출한 골격 — 크기/시간은 MFT 열거에 없으므로 0).</summary>
public readonly record struct MftRecord(
    ulong FileReferenceNumber,
    ulong ParentFileReferenceNumber,
    string Name,
    bool IsDirectory);

/// <summary>
/// FRN→전체경로 해석기. MFT 열거(FSCTL_ENUM_USN_DATA)는 부모 FRN과 이름만 주므로,
/// 모든 레코드를 모은 뒤 부모 체인을 따라 전체 경로를 재구성한다.
/// 고아(부모 없음)/순환 레코드는 건너뛴다. 경로 재구성은 메모이즈한다.
/// </summary>
public sealed class MftPathResolver
{
    private readonly Dictionary<ulong, MftRecord> _records = [];
    private readonly Dictionary<ulong, string?> _pathCache = [];
    private readonly ulong _rootFrn;
    private readonly string _volumeRoot;

    /// <param name="rootFrn">볼륨 루트 디렉터리의 FRN (NTFS는 보통 0x5).</param>
    /// <param name="volumeRoot">볼륨 루트 경로 (예: "C:\").</param>
    public MftPathResolver(ulong rootFrn, string volumeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeRoot);
        _rootFrn = rootFrn;
        _volumeRoot = volumeRoot.EndsWith(Path.DirectorySeparatorChar)
            ? volumeRoot
            : volumeRoot + Path.DirectorySeparatorChar;
        _pathCache[rootFrn] = _volumeRoot;
    }

    public int RecordCount => _records.Count;

    public void Add(in MftRecord record)
    {
        // 루트 자신은 경로 캐시에 이미 있으므로 레코드로 보관하지 않는다.
        if (record.FileReferenceNumber != _rootFrn && record.FileReferenceNumber != 0)
        {
            _records[record.FileReferenceNumber] = record;
        }
    }

    /// <summary>FRN의 전체 경로. 해석 불가(고아/순환)면 null.</summary>
    public string? ResolvePath(ulong frn) => ResolvePath(frn, depth: 0);

    /// <summary>수집한 모든 레코드를 인덱스 항목으로 변환한다 (해석 가능한 것만).</summary>
    public IEnumerable<IndexItem> ToIndexItems()
    {
        foreach (var record in _records.Values)
        {
            var parentPath = ResolvePath(record.ParentFileReferenceNumber, depth: 0);
            if (parentPath is null)
            {
                continue; // 부모를 해석할 수 없는 고아 — 건너뜀
            }

            yield return new IndexItem(
                ParentPath: parentPath,
                Name: record.Name,
                IsDirectory: record.IsDirectory,
                Size: 0,
                ModifiedTicks: 0);
        }
    }

    private string? ResolvePath(ulong frn, int depth)
    {
        if (_pathCache.TryGetValue(frn, out var cached))
        {
            return cached;
        }

        // 깊이 상한으로 순환/비정상 체인 방어 (실제 경로 깊이는 이보다 훨씬 얕다).
        if (depth > 256 || !_records.TryGetValue(frn, out var record))
        {
            _pathCache[frn] = null;
            return null;
        }

        var parentPath = ResolvePath(record.ParentFileReferenceNumber, depth + 1);
        var full = parentPath is null ? null : Path.Combine(parentPath, record.Name);
        _pathCache[frn] = full;
        return full;
    }
}
