using System.Runtime.InteropServices;
using Explorer.Core.FileOperations;
using FluentAssertions;

namespace Explorer.Core.Tests.FileOperations;

public sealed class FileOperationErrorMapperTests
{
    [Theory]
    [InlineData(unchecked((int)0x80070005), FileOperationError.AccessDenied)]
    [InlineData(unchecked((int)0x80070020), FileOperationError.InUse)]
    [InlineData(unchecked((int)0x80070002), FileOperationError.PathNotFound)]
    [InlineData(unchecked((int)0x80070070), FileOperationError.DiskFull)]
    [InlineData(unchecked((int)0x800704C7), FileOperationError.Cancelled)]
    [InlineData(unchecked((int)0x80270000), FileOperationError.Cancelled)]
    [InlineData(unchecked((int)0x800700B7), FileOperationError.AlreadyExists)]
    [InlineData(unchecked((int)0x8007007B), FileOperationError.InvalidName)]
    [InlineData(unchecked((int)0x80004005), FileOperationError.Unknown)]
    public void FromHResult_MapsKnownCodes(int hresult, FileOperationError expected)
    {
        FileOperationErrorMapper.FromHResult(hresult).Should().Be(expected);
    }

    [Fact]
    public void FromException_MapsBclExceptions()
    {
        FileOperationErrorMapper.FromException(new UnauthorizedAccessException())
            .Should().Be(FileOperationError.AccessDenied);
        FileOperationErrorMapper.FromException(new FileNotFoundException())
            .Should().Be(FileOperationError.PathNotFound);
        FileOperationErrorMapper.FromException(new DirectoryNotFoundException())
            .Should().Be(FileOperationError.PathNotFound);
        FileOperationErrorMapper.FromException(new PathTooLongException())
            .Should().Be(FileOperationError.NameTooLong);
        FileOperationErrorMapper.FromException(new OperationCanceledException())
            .Should().Be(FileOperationError.Cancelled);
        FileOperationErrorMapper.FromException(new ArgumentException("bad"))
            .Should().Be(FileOperationError.InvalidName);
    }

    [Fact]
    public void FromException_ComException_UsesItsHResult()
    {
        var com = new COMException("denied", unchecked((int)0x80070005));

        FileOperationErrorMapper.FromException(com).Should().Be(FileOperationError.AccessDenied);
    }

    [Fact]
    public void FromException_PlainIoException_FallsBackToInUse()
    {
        FileOperationErrorMapper.FromException(new IOException("locked"))
            .Should().Be(FileOperationError.InUse);
    }
}
