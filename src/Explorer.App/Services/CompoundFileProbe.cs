using System.IO;
using System.Text;

namespace Explorer.App.Services;

/// <summary>
/// CFBF(OLE 복합 문서) 안에 MS-OFFCRYPTO 암호화 표시 스트림/스토리지가 있는지 가볍게 탐지한다.
/// 암호화/IRM(AIP)로 보호된 Office 파일(OOXML·레거시 모두)과 AIP 래퍼는 "EncryptedPackage" 스트림 +
/// "DataSpaces" 스토리지(+ "EncryptionInfo")를 가진다. 일반 레거시 .doc/.xls/.ppt는 항상 CFBF이지만
/// 이 스트림이 없으므로 구분된다. 헤더/FAT/디렉터리만 상한을 두고 읽으며, 파싱 실패는 보호 아님(fail-open)으로 본다.
/// </summary>
internal static class CompoundFileProbe
{
    private const int HeaderSize = 512;
    private const int DirEntrySize = 128;
    private const uint MaxRegSect = 0xFFFFFFFA; // 이 값 이상은 특수(FREE/END/FAT 등)
    private const int MaxFatSectors = 32;       // FAT 적재 상한(IO 폭주 방지)
    private const int MaxDirSectors = 64;       // 디렉터리 체인 순회 상한

    private static readonly string[] EncryptionEntryNames = ["EncryptedPackage", "DataSpaces", "EncryptionInfo"];

    public static bool HasEncryptionStream(Stream stream)
    {
        try
        {
            return Probe(stream);
        }
        catch (Exception ex)
            when (ex is IOException or EndOfStreamException or ArgumentException or OverflowException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool Probe(Stream stream)
    {
        var header = new byte[HeaderSize];
        stream.Seek(0, SeekOrigin.Begin);
        if (stream.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false) < HeaderSize)
        {
            return false;
        }

        int sectorShift = BitConverter.ToUInt16(header, 0x1E);
        if (sectorShift is not (9 or 12))
        {
            return false;
        }

        int sectorSize = 1 << sectorShift;
        uint firstDirSector = BitConverter.ToUInt32(header, 0x30);

        // FAT(다음-섹터 포인터 배열) 적재: 헤더 DIFAT(0x4C, 109개)가 가리키는 FAT 섹터들을 상한까지 읽는다.
        var fat = LoadFat(stream, header, sectorSize);

        // 디렉터리 섹터 체인을 따라가며 엔트리 이름을 검사한다.
        var visited = new HashSet<uint>();
        uint dir = firstDirSector;
        for (var s = 0; s < MaxDirSectors && dir < MaxRegSect; s++)
        {
            if (!visited.Add(dir))
            {
                break; // 순환 방지
            }

            var sector = ReadSector(stream, dir, sectorSize);
            if (sector is null)
            {
                break;
            }

            for (var off = 0; off + DirEntrySize <= sector.Length; off += DirEntrySize)
            {
                if (MatchesEncryptionName(sector, off))
                {
                    return true;
                }
            }

            dir = dir < (uint)fat.Count ? fat[(int)dir] : MaxRegSect;
        }

        return false;
    }

    private static List<uint> LoadFat(Stream stream, byte[] header, int sectorSize)
    {
        var fat = new List<uint>();
        var read = 0;
        for (var i = 0; i < 109 && read < MaxFatSectors; i++)
        {
            uint fatSector = BitConverter.ToUInt32(header, 0x4C + (i * 4));
            if (fatSector >= MaxRegSect)
            {
                continue;
            }

            var sector = ReadSector(stream, fatSector, sectorSize);
            if (sector is null)
            {
                break;
            }

            for (var j = 0; j + 4 <= sector.Length; j += 4)
            {
                fat.Add(BitConverter.ToUInt32(sector, j));
            }

            read++;
        }

        return fat;
    }

    private static bool MatchesEncryptionName(byte[] sector, int entryOffset)
    {
        byte objectType = sector[entryOffset + 0x42];
        if (objectType is not (1 or 2 or 5)) // storage / stream / root 만
        {
            return false;
        }

        int nameLen = BitConverter.ToUInt16(sector, entryOffset + 0x40); // null 종단 포함 바이트 길이
        if (nameLen is < 4 or > 64)
        {
            return false;
        }

        // 이름은 64바이트 필드 안의 UTF-16LE null 종단 문자열. nameLen(종단 포함)을 상한으로 쓰되 실제 null
        // 종단 전까지를 이름으로 본다(생산자가 nameLen을 넉넉히 잡아 trailing null이 섞여도 안전).
        var raw = Encoding.Unicode.GetString(sector, entryOffset, nameLen - 2);
        var terminator = raw.IndexOf((char)0);
        var name = terminator >= 0 ? raw[..terminator] : raw;

        // MS-OFFCRYPTO의 DataSpaces 스토리지는 이름 앞에 제어문자(U+0006)가 붙는다 — 선행 제어문자를 떼고 비교.
        var start = 0;
        while (start < name.Length && name[start] < ' ')
        {
            start++;
        }

        var trimmed = start == 0 ? name : name[start..];
        foreach (var marker in EncryptionEntryNames)
        {
            if (string.Equals(trimmed, marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[]? ReadSector(Stream stream, uint sector, int sectorSize)
    {
        long offset = HeaderSize + ((long)sector * sectorSize);
        if (offset < HeaderSize || offset + sectorSize > stream.Length)
        {
            return null;
        }

        var buffer = new byte[sectorSize];
        stream.Seek(offset, SeekOrigin.Begin);
        return stream.ReadAtLeast(buffer, sectorSize, throwOnEndOfStream: false) >= sectorSize ? buffer : null;
    }
}
