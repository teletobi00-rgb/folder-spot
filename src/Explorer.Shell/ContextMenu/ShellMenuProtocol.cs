using System.IO;

namespace Explorer.Shell.ContextMenu;

/// <summary>
/// 메인 앱 ↔ 상주 셸 메뉴 헬퍼 사이의 요청/응답 인코딩(파이프 위 텍스트).
/// 한 요청 = 헤더 한 줄 "<x> <y> <count>" + 경로 <count>줄. 응답 = <see cref="Ack"/> 한 줄.
/// 경로에는 개행이 들어갈 수 없으므로(Windows 파일명 제약) 줄 단위로 안전하게 주고받는다.
/// </summary>
public static class ShellMenuProtocol
{
    /// <summary>메뉴 표시 완료(헬퍼 생존) 신호.</summary>
    public const string Ack = "ok";

    /// <summary>한 요청에 허용하는 경로 최대 개수(손상된 헤더로 과도한 할당을 막는 상한).</summary>
    private const int MaxPaths = 100_000;

    /// <summary>요청 한 건을 스트림에 쓴다(쓰고 즉시 flush).</summary>
    public static void WriteRequest(TextWriter writer, int screenX, int screenY, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(paths);

        writer.WriteLine($"{screenX} {screenY} {paths.Count}");
        foreach (var path in paths)
        {
            writer.WriteLine(path);
        }

        writer.Flush();
    }

    /// <summary>요청 한 건을 읽는다. 스트림이 닫혔거나(EOF) 헤더/본문이 잘못/잘리면 null(= 연결 종료로 간주).</summary>
    public static (int X, int Y, string[] Paths)? ReadRequest(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var header = reader.ReadLine();
        if (header is null)
        {
            return null; // EOF — 앱이 파이프를 닫음.
        }

        var parts = header.Split(' ', 3);
        if (parts.Length < 3
            || !int.TryParse(parts[0], out var x)
            || !int.TryParse(parts[1], out var y)
            || !int.TryParse(parts[2], out var count)
            || count < 0 || count > MaxPaths)
        {
            return null; // 손상된 헤더 — 연결 종료로 간주.
        }

        var paths = new string[count];
        for (var i = 0; i < count; i++)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                return null; // 본문이 잘림.
            }

            paths[i] = line;
        }

        return (x, y, paths);
    }
}
