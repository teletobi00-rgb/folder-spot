namespace Explorer.Shell.Clipboard;

/// <summary>CFSTR_PREFERREDDROPEFFECT 직렬화 — 탐색기와 복사/잘라내기 의도를 주고받는 4바이트 형식.</summary>
public static class DropEffectFormat
{
    public const string FormatName = "Preferred DropEffect";

    private const int EffectCopy = 1;
    private const int EffectMove = 2;
    private const int EffectLink = 4;

    /// <summary>잘라내기=MOVE(2), 복사=COPY|LINK(5, 탐색기 관례).</summary>
    public static byte[] Encode(bool cut) => BitConverter.GetBytes(cut ? EffectMove : EffectCopy | EffectLink);

    public static bool DecodeIsCut(byte[]? bytes) =>
        bytes is { Length: >= 4 } && (BitConverter.ToInt32(bytes, 0) & EffectMove) != 0;
}
