using System.Collections.Immutable;

namespace Explorer.Core.FileOperations;

public enum OperationKind
{
    Copy,
    Move,
    Delete,
    DeletePermanent,
}

/// <summary>큐에 들어가는 파일 작업 요청 (불변).</summary>
public sealed record OperationRequest
{
    public required OperationKind Kind { get; init; }

    public required ImmutableArray<string> Sources { get; init; }

    /// <summary>Copy/Move의 대상 폴더. Delete 계열은 null.</summary>
    public string? Destination { get; init; }

    public static OperationRequest Copy(IEnumerable<string> sources, string destination) => new()
    {
        Kind = OperationKind.Copy,
        Sources = [.. sources],
        Destination = destination,
    };

    public static OperationRequest Move(IEnumerable<string> sources, string destination) => new()
    {
        Kind = OperationKind.Move,
        Sources = [.. sources],
        Destination = destination,
    };

    public static OperationRequest Delete(IEnumerable<string> sources, bool permanent) => new()
    {
        Kind = permanent ? OperationKind.DeletePermanent : OperationKind.Delete,
        Sources = [.. sources],
    };

    public string Describe()
    {
        var subject = Sources.Length == 1
            ? Path.GetFileName(Sources[0].TrimEnd(Path.DirectorySeparatorChar))
            : $"{Sources.Length}개 항목";

        return Kind switch
        {
            OperationKind.Copy => $"{subject} 복사 → {Destination}",
            OperationKind.Move => $"{subject} 이동 → {Destination}",
            OperationKind.Delete => $"{subject} 휴지통으로 삭제",
            OperationKind.DeletePermanent => $"{subject} 영구 삭제",
            _ => subject,
        };
    }
}
