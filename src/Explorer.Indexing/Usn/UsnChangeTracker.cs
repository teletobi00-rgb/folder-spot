namespace Explorer.Indexing.Usn;

/// <summary>
/// MFT 시드 위에서 USN 저널 원시 레코드를 인덱스 델타(생성/삭제/이름변경/수정)로 변환한다.
/// FRN→레코드 맵을 직접 유지하므로 경로 해석과 이름변경(폴더 이동 포함)을 정확히 처리한다.
/// 순수 로직 — 테스트 가능. 호출자는 단일 스레드(인덱싱 워커)에서 순차 호출한다.
/// </summary>
public sealed class UsnChangeTracker
{
    private const uint ReasonFileCreate = 0x00000100;
    private const uint ReasonFileDelete = 0x00000200;
    private const uint ReasonRenameOldName = 0x00001000;
    private const uint ReasonRenameNewName = 0x00002000;

    private readonly Dictionary<ulong, Node> _nodes = [];
    private readonly Dictionary<ulong, string?> _pendingRenameOldPaths = [];
    private readonly ulong _rootFrn;
    private readonly string _volumeRoot;

    private readonly record struct Node(ulong ParentFrn, string Name, bool IsDirectory);

    public UsnChangeTracker(ulong rootFrn, string volumeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeRoot);
        _rootFrn = rootFrn;
        _volumeRoot = volumeRoot.EndsWith(Path.DirectorySeparatorChar)
            ? volumeRoot
            : volumeRoot + Path.DirectorySeparatorChar;
    }

    /// <summary>MFT 열거 레코드로 FRN 맵을 시드한다.</summary>
    public void Seed(in RawUsnRecord record)
    {
        if (record.FileReferenceNumber != _rootFrn && record.FileReferenceNumber != 0)
        {
            _nodes[record.FileReferenceNumber] = new Node(record.ParentFileReferenceNumber, record.Name, record.IsDirectory);
        }
    }

    /// <summary>저널 레코드 하나를 처리해 0개 이상의 인덱스 델타를 돌려준다.</summary>
    public IReadOnlyList<UsnChange> Process(in RawUsnRecord record)
    {
        var reason = record.Reason;
        var frn = record.FileReferenceNumber;

        // 이름변경은 OLD 레코드(옛 위치) → NEW 레코드(새 위치) 두 건으로 온다.
        if ((reason & ReasonRenameOldName) != 0 && (reason & ReasonRenameNewName) == 0)
        {
            _pendingRenameOldPaths[frn] = ResolveRecordPath(record);
            return [];
        }

        if ((reason & ReasonRenameNewName) != 0)
        {
            var newPath = ResolveRecordPath(record);
            UpdateNode(record);
            if (_pendingRenameOldPaths.Remove(frn, out var oldPath) && oldPath is not null && newPath is not null)
            {
                return [new UsnChange(FileChangeKind.Renamed, newPath, oldPath, record.IsDirectory)];
            }

            return newPath is null ? [] : [new UsnChange(FileChangeKind.Created, newPath, null, record.IsDirectory)];
        }

        if ((reason & ReasonFileDelete) != 0)
        {
            var path = CurrentPath(frn) ?? ResolveRecordPath(record);
            _nodes.Remove(frn);
            return path is null ? [] : [new UsnChange(FileChangeKind.Deleted, path, null, record.IsDirectory)];
        }

        if ((reason & ReasonFileCreate) != 0)
        {
            UpdateNode(record);
            var path = ResolveRecordPath(record);
            return path is null ? [] : [new UsnChange(FileChangeKind.Created, path, null, record.IsDirectory)];
        }

        // 데이터/기타 수정
        UpdateNode(record);
        var modifiedPath = ResolveRecordPath(record);
        return modifiedPath is null ? [] : [new UsnChange(FileChangeKind.Modified, modifiedPath, null, record.IsDirectory)];
    }

    private void UpdateNode(in RawUsnRecord record)
    {
        if (record.FileReferenceNumber != _rootFrn && record.FileReferenceNumber != 0)
        {
            _nodes[record.FileReferenceNumber] = new Node(
                record.ParentFileReferenceNumber, record.Name, record.IsDirectory);
        }
    }

    /// <summary>레코드의 부모 경로 + 자기 이름으로 전체 경로를 만든다.</summary>
    private string? ResolveRecordPath(in RawUsnRecord record)
    {
        var parentPath = ResolveFrn(record.ParentFileReferenceNumber, depth: 0);
        return parentPath is null ? null : Path.Combine(parentPath, record.Name);
    }

    /// <summary>맵에 저장된 현재 상태 기준으로 FRN의 전체 경로를 만든다 (삭제 직전 위치 등).</summary>
    private string? CurrentPath(ulong frn)
    {
        if (!_nodes.TryGetValue(frn, out var node))
        {
            return null;
        }

        var parentPath = ResolveFrn(node.ParentFrn, depth: 0);
        return parentPath is null ? null : Path.Combine(parentPath, node.Name);
    }

    private string? ResolveFrn(ulong frn, int depth)
    {
        if (frn == _rootFrn)
        {
            return _volumeRoot;
        }

        if (depth > 256 || !_nodes.TryGetValue(frn, out var node))
        {
            return null;
        }

        var parentPath = ResolveFrn(node.ParentFrn, depth + 1);
        return parentPath is null ? null : Path.Combine(parentPath, node.Name);
    }
}
