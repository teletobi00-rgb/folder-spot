using System.Text;
using Explorer.Indexing.Index;

namespace Explorer.Indexing.Usn;

public enum FileChangeKind : byte
{
    Created,
    Deleted,
    Renamed,
    Modified,
}

/// <summary>USN tailing이 보고하는 변경 한 건.</summary>
public readonly record struct UsnChange(
    FileChangeKind Kind,
    string FullPath,
    string? OldFullPath,
    bool IsDirectory);

public enum UsnMessageType : byte
{
    /// <summary>MFT 열거 항목 배치.</summary>
    Batch = 1,

    /// <summary>열거 완료 — 이어질 tailing 시작 USN 포함.</summary>
    EnumDone = 2,

    /// <summary>tailing 변경 한 건.</summary>
    Change = 3,

    /// <summary>오류 보고.</summary>
    Error = 4,

    /// <summary>긴 MFT 열거 중에도 메인 프로세스의 시작 감시 타이머를 갱신하기 위한 신호.</summary>
    Heartbeat = 5,
}

/// <summary>헬퍼→메인 메시지 (수신 측 표현).</summary>
public sealed record UsnMessage
{
    public required UsnMessageType Type { get; init; }

    public IReadOnlyList<IndexItem> Batch { get; init; } = [];

    public ulong NextUsn { get; init; }

    public UsnChange Change { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// 헬퍼(권한 프로세스)와 메인 앱 사이의 named pipe 메시지 프레이밍.
/// 프레임 = [1바이트 타입][payload]. 문자열은 [2바이트 길이][UTF-8]. BinaryWriter는 항상 little-endian.
/// </summary>
public static class UsnProtocol
{
    public static void WriteBatch(BinaryWriter writer, IReadOnlyList<IndexItem> items)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(items);

        writer.Write((byte)UsnMessageType.Batch);
        writer.Write(items.Count);
        foreach (var item in items)
        {
            WriteString(writer, item.ParentPath);
            WriteString(writer, item.Name);
            writer.Write(item.IsDirectory);
            writer.Write(item.Size);
            writer.Write(item.ModifiedTicks);
        }
    }

    public static void WriteEnumDone(BinaryWriter writer, ulong nextUsn)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write((byte)UsnMessageType.EnumDone);
        writer.Write(nextUsn);
    }

    public static void WriteChange(BinaryWriter writer, in UsnChange change)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write((byte)UsnMessageType.Change);
        writer.Write((byte)change.Kind);
        WriteString(writer, change.FullPath);
        WriteString(writer, change.OldFullPath ?? string.Empty);
        writer.Write(change.IsDirectory);
    }

    public static void WriteError(BinaryWriter writer, string message)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write((byte)UsnMessageType.Error);
        WriteString(writer, message);
    }

    public static void WriteHeartbeat(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write((byte)UsnMessageType.Heartbeat);
    }

    /// <summary>다음 메시지를 읽는다. 스트림 끝이면 null.</summary>
    public static UsnMessage? Read(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // BinaryReader.PeekChar는 인코딩으로 디코드를 시도해 바이너리 스트림을 망가뜨린다 — 직접 읽고 EOF를 잡는다.
        byte typeByte;
        try
        {
            typeByte = reader.ReadByte();
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        return (UsnMessageType)typeByte switch
        {
            UsnMessageType.Batch => ReadBatch(reader),
            UsnMessageType.EnumDone => new UsnMessage { Type = UsnMessageType.EnumDone, NextUsn = reader.ReadUInt64() },
            UsnMessageType.Change => ReadChange(reader),
            UsnMessageType.Error => new UsnMessage { Type = UsnMessageType.Error, Error = ReadString(reader) },
            UsnMessageType.Heartbeat => new UsnMessage { Type = UsnMessageType.Heartbeat },
            _ => throw new InvalidDataException($"알 수 없는 USN 메시지 타입: {typeByte}"),
        };
    }

    private static UsnMessage ReadBatch(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > 5_000_000)
        {
            throw new InvalidDataException($"비정상 배치 크기: {count}");
        }

        var items = new List<IndexItem>(Math.Min(count, 8192));
        for (var i = 0; i < count; i++)
        {
            var parentPath = ReadString(reader);
            var name = ReadString(reader);
            var isDir = reader.ReadBoolean();
            var size = reader.ReadInt64();
            var ticks = reader.ReadInt64();
            items.Add(new IndexItem(parentPath, name, isDir, size, ticks));
        }

        return new UsnMessage { Type = UsnMessageType.Batch, Batch = items };
    }

    private static UsnMessage ReadChange(BinaryReader reader)
    {
        var kind = (FileChangeKind)reader.ReadByte();
        var fullPath = ReadString(reader);
        var oldPath = ReadString(reader);
        var isDir = reader.ReadBoolean();
        return new UsnMessage
        {
            Type = UsnMessageType.Change,
            Change = new UsnChange(kind, fullPath, string.IsNullOrEmpty(oldPath) ? null : oldPath, isDir),
        };
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            // 경로는 사실상 이 길이를 넘지 않지만, 넘더라도 UTF-8 멀티바이트 시퀀스를
            // 쪼개 U+FFFD로 깨뜨리지 않도록 코드유닛 경계(선두 바이트)까지 물러나 자른다.
            var cut = ushort.MaxValue;
            while (cut > 0 && (bytes[cut] & 0xC0) == 0x80)
            {
                cut--;
            }

            bytes = bytes[..cut];
        }

        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        var bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }
}
