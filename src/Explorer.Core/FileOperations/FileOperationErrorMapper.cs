using System.Runtime.InteropServices;

namespace Explorer.Core.FileOperations;

/// <summary>Win32/COM HRESULT와 BCL 예외를 도메인 오류로 매핑한다.</summary>
public static class FileOperationErrorMapper
{
    public static FileOperationError FromHResult(int hresult) => unchecked((uint)hresult) switch
    {
        0x80070005 => FileOperationError.AccessDenied,        // E_ACCESSDENIED
        0x80070020 or 0x80070021 => FileOperationError.InUse, // SHARING/LOCK_VIOLATION
        0x80070002 or 0x80070003 => FileOperationError.PathNotFound,
        0x800700CE or 0x8007006F => FileOperationError.NameTooLong,
        0x80070070 => FileOperationError.DiskFull,
        0x800704C7 or 0x80270000 => FileOperationError.Cancelled, // ERROR_CANCELLED, COPYENGINE_E_USER_CANCELLED
        0x800700B7 or 0x80070050 => FileOperationError.AlreadyExists,
        0x8007007B => FileOperationError.InvalidName,         // ERROR_INVALID_NAME
        _ => FileOperationError.Unknown,
    };

    public static FileOperationError FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            UnauthorizedAccessException => FileOperationError.AccessDenied,
            FileNotFoundException or DirectoryNotFoundException => FileOperationError.PathNotFound,
            PathTooLongException => FileOperationError.NameTooLong,
            OperationCanceledException => FileOperationError.Cancelled,
            COMException com => FromHResult(com.HResult),
            ArgumentException => FileOperationError.InvalidName,
            IOException io when FromHResult(io.HResult) is var mapped && mapped != FileOperationError.Unknown => mapped,
            IOException => FileOperationError.InUse,
            _ => FileOperationError.Unknown,
        };
    }
}
