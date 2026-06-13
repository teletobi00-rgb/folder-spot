using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Explorer.Indexing.Usn;

/// <summary>USN/MFT 열거가 내놓는 골격 레코드 (MFT 열거는 Reason=0).</summary>
public readonly record struct RawUsnRecord(
    ulong FileReferenceNumber,
    ulong ParentFileReferenceNumber,
    string Name,
    bool IsDirectory,
    uint Reason,
    long Usn);

/// <summary>
/// NTFS 볼륨의 MFT 직접 열거 + USN 저널 읽기 (Win32 FSCTL). 볼륨 핸들은 백업 권한이 필요해
/// 관리자 프로세스에서만 열린다 — 실패 시 예외 없이 false/빈 열거로 graceful degrade.
/// </summary>
public sealed class VolumeUsnReader : IDisposable
{
    private const uint FsctlQueryUsnJournal = 0x000900f4;
    private const uint FsctlEnumUsnData = 0x000900b3;
    private const uint FsctlReadUsnJournal = 0x000900bb;
    private const uint FileAttributeDirectory = 0x10;

    // tailing에서 관심 있는 변경 사유 (생성/삭제/이름변경/데이터/기본정보).
    private const uint ReasonMask =
        0x00000100 | // USN_REASON_FILE_CREATE
        0x00000200 | // USN_REASON_FILE_DELETE
        0x00001000 | // USN_REASON_RENAME_OLD_NAME
        0x00002000 | // USN_REASON_RENAME_NEW_NAME
        0x00000001 | // USN_REASON_DATA_OVERWRITE
        0x00000002 | // USN_REASON_DATA_EXTEND
        0x00000004;  // USN_REASON_DATA_TRUNCATION

    private readonly string _volumeRoot;
    private SafeFileHandle? _handle;

    public VolumeUsnReader(string volumeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeRoot);
        _volumeRoot = volumeRoot;
    }

    public ulong UsnJournalId { get; private set; }

    public long NextUsn { get; private set; }

    /// <summary>볼륨을 열고 USN 저널을 질의한다. 권한/저널 없음 등으로 실패하면 false.</summary>
    public bool TryOpen()
    {
        try
        {
            var devicePath = $@"\\.\{_volumeRoot.TrimEnd('\\', '/')}"; // "C:" → \\.\C:
            _handle = CreateFile(
                devicePath,
                0x80000000, // GENERIC_READ
                0x00000003, // FILE_SHARE_READ | FILE_SHARE_WRITE
                IntPtr.Zero,
                3, // OPEN_EXISTING
                0x02000000, // FILE_FLAG_BACKUP_SEMANTICS
                IntPtr.Zero);

            if (_handle.IsInvalid)
            {
                _handle = null;
                return false;
            }

            return QueryJournal();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>MFT 전체를 열거해 골격 레코드를 스트리밍한다.</summary>
    public IEnumerable<RawUsnRecord> EnumerateMft(CancellationToken cancellationToken)
    {
        if (_handle is null)
        {
            yield break;
        }

        const int bufferSize = 64 * 1024;
        var buffer = new byte[bufferSize];
        var input = new MftEnumData { StartFileReferenceNumber = 0, LowUsn = 0, HighUsn = NextUsn };

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!DeviceIoControl(FsctlEnumUsnData, ref input, buffer, out var bytesReturned) || bytesReturned <= 8)
            {
                yield break;
            }

            // 출력 버퍼: 앞 8바이트 = 다음 StartFileReferenceNumber, 이후 USN_RECORD_V2 묶음.
            input.StartFileReferenceNumber = BitConverter.ToUInt64(buffer, 0);
            foreach (var record in ParseRecords(buffer, 8, bytesReturned))
            {
                yield return record;
            }
        }
    }

    /// <summary>지정 USN부터 저널 변경을 한 번 읽어 레코드를 돌려준다(블로킹 없음). 다음 USN을 out으로.</summary>
    public IReadOnlyList<RawUsnRecord> ReadChanges(long startUsn, out long nextUsn, CancellationToken cancellationToken)
    {
        nextUsn = startUsn;
        if (_handle is null)
        {
            return [];
        }

        const int bufferSize = 64 * 1024;
        var buffer = new byte[bufferSize];
        var input = new ReadUsnJournalData
        {
            StartUsn = startUsn,
            ReasonMask = ReasonMask,
            ReturnOnlyOnClose = 0,
            Timeout = 0,
            BytesToWaitFor = 0, // 블로킹하지 않음 — 호출자가 폴링 주기를 제어
            UsnJournalId = UsnJournalId,
        };

        if (cancellationToken.IsCancellationRequested
            || !DeviceIoControlRead(FsctlReadUsnJournal, ref input, buffer, out var bytesReturned)
            || bytesReturned <= 8)
        {
            return [];
        }

        nextUsn = BitConverter.ToInt64(buffer, 0);
        return [.. ParseRecords(buffer, 8, bytesReturned)];
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }

    private bool QueryJournal()
    {
        var output = new byte[Marshal.SizeOf<UsnJournalDataV0>()];
        if (!DeviceIoControlQuery(FsctlQueryUsnJournal, output, out var bytesReturned) || bytesReturned == 0)
        {
            return false;
        }

        var data = MemoryMarshal.Read<UsnJournalDataV0>(output);
        UsnJournalId = data.UsnJournalID;
        NextUsn = data.NextUsn;
        return true;
    }

    private static IEnumerable<RawUsnRecord> ParseRecords(byte[] buffer, int offset, int length)
    {
        var position = offset;
        while (position + 4 <= length)
        {
            var recordLength = BitConverter.ToInt32(buffer, position);
            if (recordLength <= 0 || position + recordLength > length)
            {
                yield break;
            }

            // USN_RECORD_V2 고정 헤더 오프셋
            var frn = BitConverter.ToUInt64(buffer, position + 8);
            var parentFrn = BitConverter.ToUInt64(buffer, position + 16);
            var usn = BitConverter.ToInt64(buffer, position + 24);
            var reason = BitConverter.ToUInt32(buffer, position + 40);
            var attributes = BitConverter.ToUInt32(buffer, position + 52);
            var nameLength = BitConverter.ToUInt16(buffer, position + 56);
            var nameOffset = BitConverter.ToUInt16(buffer, position + 58);

            var name = nameLength > 0 && position + nameOffset + nameLength <= length
                ? Encoding.Unicode.GetString(buffer, position + nameOffset, nameLength)
                : string.Empty;

            yield return new RawUsnRecord(
                FileReferenceNumber: frn,
                ParentFileReferenceNumber: parentFrn,
                Name: name,
                IsDirectory: (attributes & FileAttributeDirectory) != 0,
                Reason: reason,
                Usn: usn);

            position += recordLength;
        }
    }

    private bool DeviceIoControl(uint code, ref MftEnumData input, byte[] output, out int bytesReturned)
    {
        var inputSize = Marshal.SizeOf<MftEnumData>();
        var inputPtr = Marshal.AllocHGlobal(inputSize);
        try
        {
            Marshal.StructureToPtr(input, inputPtr, fDeleteOld: false);
            return DeviceIoControlNative(_handle!, code, inputPtr, inputSize, output, output.Length, out bytesReturned, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(inputPtr);
        }
    }

    private bool DeviceIoControlRead(uint code, ref ReadUsnJournalData input, byte[] output, out int bytesReturned)
    {
        var inputSize = Marshal.SizeOf<ReadUsnJournalData>();
        var inputPtr = Marshal.AllocHGlobal(inputSize);
        try
        {
            Marshal.StructureToPtr(input, inputPtr, fDeleteOld: false);
            return DeviceIoControlNative(_handle!, code, inputPtr, inputSize, output, output.Length, out bytesReturned, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(inputPtr);
        }
    }

    private bool DeviceIoControlQuery(uint code, byte[] output, out int bytesReturned) =>
        DeviceIoControlNative(_handle!, code, IntPtr.Zero, 0, output, output.Length, out bytesReturned, IntPtr.Zero);

    [StructLayout(LayoutKind.Sequential)]
    private struct UsnJournalDataV0
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MftEnumData
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReadUsnJournalData
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, int inBufferSize,
        byte[] outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    private static bool DeviceIoControlNative(
        SafeFileHandle device, uint code, IntPtr inBuffer, int inSize,
        byte[] outBuffer, int outSize, out int bytesReturned, IntPtr overlapped)
    {
        if (DeviceIoControl(device, code, inBuffer, inSize, outBuffer, outSize, out bytesReturned, overlapped))
        {
            return true;
        }

        // ERROR_HANDLE_EOF = 정상적인 열거 종료. 그 외 실패도 호출자는 false를 "중단"으로 다루므로
        // 어느 쪽이든 bytesReturned를 명시적으로 0으로 둬 미초기화 의존을 없앤다.
        bytesReturned = 0;
        return false;
    }
}
