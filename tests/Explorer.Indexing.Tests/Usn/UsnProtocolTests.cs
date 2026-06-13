using System.Text;
using Explorer.Indexing.Index;
using Explorer.Indexing.Usn;
using FluentAssertions;

namespace Explorer.Indexing.Tests.Usn;

public sealed class UsnProtocolTests
{
    private static (BinaryWriter Writer, MemoryStream Stream) NewWriter()
    {
        var stream = new MemoryStream();
        return (new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true), stream);
    }

    private static BinaryReader ReaderOver(MemoryStream stream)
    {
        stream.Position = 0;
        return new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
    }

    [Fact]
    public void Batch_Roundtrips_AllFields()
    {
        var (writer, stream) = NewWriter();
        var items = new List<IndexItem>
        {
            new(@"C:\문서", "회의록.hwp", IsDirectory: false, 1234, 5678),
            new(@"C:\", "Windows", IsDirectory: true, 0, 0),
        };
        UsnProtocol.WriteBatch(writer, items);
        writer.Flush();

        var message = UsnProtocol.Read(ReaderOver(stream));

        message.Should().NotBeNull();
        message!.Type.Should().Be(UsnMessageType.Batch);
        message.Batch.Should().HaveCount(2);
        message.Batch[0].Should().Be(new IndexItem(@"C:\문서", "회의록.hwp", false, 1234, 5678));
        message.Batch[1].Should().Be(new IndexItem(@"C:\", "Windows", true, 0, 0));
    }

    [Fact]
    public void EnumDone_RoundtripsNextUsn()
    {
        var (writer, stream) = NewWriter();
        UsnProtocol.WriteEnumDone(writer, 0xDEADBEEF_CAFEUL);
        writer.Flush();

        var message = UsnProtocol.Read(ReaderOver(stream));

        message!.Type.Should().Be(UsnMessageType.EnumDone);
        message.NextUsn.Should().Be(0xDEADBEEF_CAFEUL);
    }

    [Theory]
    [InlineData(FileChangeKind.Created)]
    [InlineData(FileChangeKind.Deleted)]
    [InlineData(FileChangeKind.Modified)]
    public void Change_Roundtrips(FileChangeKind kind)
    {
        var (writer, stream) = NewWriter();
        UsnProtocol.WriteChange(writer, new UsnChange(kind, @"C:\새파일.txt", null, IsDirectory: false));
        writer.Flush();

        var message = UsnProtocol.Read(ReaderOver(stream));

        message!.Type.Should().Be(UsnMessageType.Change);
        message.Change.Kind.Should().Be(kind);
        message.Change.FullPath.Should().Be(@"C:\새파일.txt");
        message.Change.OldFullPath.Should().BeNull();
    }

    [Fact]
    public void Change_RenameWithOldPath_Roundtrips()
    {
        var (writer, stream) = NewWriter();
        UsnProtocol.WriteChange(writer, new UsnChange(
            FileChangeKind.Renamed, @"C:\새이름.txt", @"C:\옛이름.txt", IsDirectory: false));
        writer.Flush();

        var message = UsnProtocol.Read(ReaderOver(stream));

        message!.Change.OldFullPath.Should().Be(@"C:\옛이름.txt");
        message.Change.FullPath.Should().Be(@"C:\새이름.txt");
    }

    [Fact]
    public void Error_Roundtrips()
    {
        var (writer, stream) = NewWriter();
        UsnProtocol.WriteError(writer, "볼륨을 열 수 없습니다 (권한 없음)");
        writer.Flush();

        var message = UsnProtocol.Read(ReaderOver(stream));

        message!.Type.Should().Be(UsnMessageType.Error);
        message.Error.Should().Be("볼륨을 열 수 없습니다 (권한 없음)");
    }

    [Fact]
    public void Read_EmptyStream_ReturnsNull()
    {
        UsnProtocol.Read(ReaderOver(new MemoryStream())).Should().BeNull();
    }

    [Fact]
    public void MultipleMessages_StreamSequentially()
    {
        var (writer, stream) = NewWriter();
        UsnProtocol.WriteBatch(writer, [new IndexItem(@"C:\", "a.txt", false, 1, 2)]);
        UsnProtocol.WriteChange(writer, new UsnChange(FileChangeKind.Created, @"C:\b.txt", null, false));
        UsnProtocol.WriteEnumDone(writer, 42);
        writer.Flush();

        var reader = ReaderOver(stream);
        UsnProtocol.Read(reader)!.Type.Should().Be(UsnMessageType.Batch);
        UsnProtocol.Read(reader)!.Type.Should().Be(UsnMessageType.Change);
        var done = UsnProtocol.Read(reader);
        done!.Type.Should().Be(UsnMessageType.EnumDone);
        done.NextUsn.Should().Be(42);
        UsnProtocol.Read(reader).Should().BeNull("스트림 끝");
    }

    [Fact]
    public void EmptyBatch_Roundtrips()
    {
        var (writer, stream) = NewWriter();
        UsnProtocol.WriteBatch(writer, []);
        writer.Flush();

        var message = UsnProtocol.Read(ReaderOver(stream));

        message!.Type.Should().Be(UsnMessageType.Batch);
        message.Batch.Should().BeEmpty();
    }
}
