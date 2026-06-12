namespace Explorer.Core.FileOperations;

public enum FileOperationError
{
    None,
    AccessDenied,
    InUse,
    PathNotFound,
    NameTooLong,
    DiskFull,
    Cancelled,
    AlreadyExists,
    InvalidName,
    Unknown,
}

/// <summary>파일 작업 결과. 서비스는 예외 대신 항상 이 결과를 반환한다.</summary>
public sealed record FileOperationResult
{
    public required bool Succeeded { get; init; }

    /// <summary>사용자가 취소/중단했는지 (실패로 취급하되 오류 메시지는 띄우지 않음).</summary>
    public bool Aborted { get; init; }

    public FileOperationError Error { get; init; } = FileOperationError.None;

    public string? Message { get; init; }

    public static FileOperationResult Success() => new() { Succeeded = true };

    public static FileOperationResult Cancelled() => new()
    {
        Succeeded = false,
        Aborted = true,
        Error = FileOperationError.Cancelled,
    };

    public static FileOperationResult Failure(FileOperationError error, string? message = null) => new()
    {
        Succeeded = false,
        Error = error,
        Message = message,
    };
}
