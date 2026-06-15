using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Explorer.App.Services.Preview;

/// <summary>
/// 확장자에 등록된 IPreviewHandler(실제 OLE 미리보기) 셸 확장의 CLSID를 찾는다.
/// AssocQueryString이 .ext → ProgID → SystemFileAssociations 순의 레지스트리 해석을 대신 처리한다.
/// 확장자당 결과를 캐시한다(미리보기는 자주 호출되므로).
/// </summary>
internal static class ShellPreviewHandlerResolver
{
    // IPreviewHandler IID — "이 인터페이스를 구현한 셸 확장의 CLSID"를 질의하는 키.
    private const string PreviewHandlerIid = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";
    private const int SOk = 0;
    private const uint AssocfInitDefaultToStar = 0x4;
    private const uint AssocStrShellExtension = 16;

    private static readonly ConcurrentDictionary<string, Guid?> Cache = new(StringComparer.OrdinalIgnoreCase);

    // 매크로 포함 Office 형식은 별도 미리보기 핸들러가 없는 경우가 많다 → 동일 포맷인 기본 확장자의
    // 핸들러로 폴백(예: xlsm은 xlsx 핸들러가 동일 OOXML 콘텐츠를 렌더).
    private static readonly Dictionary<string, string> MacroFallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["xlsm"] = "xlsx",
        ["xlsb"] = "xlsx",
        ["xltm"] = "xlsx",
        ["docm"] = "docx",
        ["dotm"] = "docx",
        ["pptm"] = "pptx",
        ["potm"] = "pptx",
    };

    /// <summary>점 없는 소문자 확장자(예: "docx")의 미리보기 핸들러 CLSID. 없으면 null.</summary>
    public static Guid? ResolveClsid(string extensionWithoutDot)
    {
        if (string.IsNullOrWhiteSpace(extensionWithoutDot))
        {
            return null;
        }

        return Cache.GetOrAdd(extensionWithoutDot, ext =>
        {
            var direct = Query("." + ext);
            if (direct is not null)
            {
                return direct;
            }

            return MacroFallback.TryGetValue(ext, out var baseExt) ? Query("." + baseExt) : null;
        });
    }

    private static Guid? Query(string dottedExtension)
    {
        try
        {
            uint size = 0;
            // 1차: 버퍼 null → 필요한 크기를 받는다(S_FALSE 또는 크기 채움). 핸들러 없으면 실패.
            _ = AssocQueryString(AssocfInitDefaultToStar, AssocStrShellExtension,
                dottedExtension, PreviewHandlerIid, null, ref size);
            if (size == 0)
            {
                return null;
            }

            var buffer = new char[size];
            var hr = AssocQueryString(AssocfInitDefaultToStar, AssocStrShellExtension,
                dottedExtension, PreviewHandlerIid, buffer, ref size);
            if (hr != SOk)
            {
                return null;
            }

            var end = Array.IndexOf(buffer, '\0');
            var clsidText = new string(buffer, 0, end >= 0 ? end : buffer.Length).Trim();
            return Guid.TryParse(clsidText, out var clsid) ? clsid : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int AssocQueryString(
        uint flags, uint str, string pszAssoc, string? pszExtra, char[]? pszOut, ref uint pcchOut);
}
