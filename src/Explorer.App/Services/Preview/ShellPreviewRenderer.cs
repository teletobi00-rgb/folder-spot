using System.IO;
using Explorer.Preview;
using Explorer.Preview.Renderers;
using Explorer.Shell.FileOperations;

namespace Explorer.App.Services.Preview;

/// <summary>
/// 등록된 IPreviewHandler(실제 OLE 미리보기)가 있는 확장자를 <see cref="PreviewKind.Native"/>로 보낸다.
/// 이미지/텍스트/미디어/압축은 앞선 전용 렌더러가 먼저 처리하므로, 여기서는 Office/PDF 등
/// 핸들러가 등록된 나머지 형식만 잡는다. 실제 HWND 호스팅은 PreviewView가 담당한다.
///
/// 단, <b>네트워크/매핑 드라이브 파일은 OLE 호스팅이 불가</b>하다 — Office 핸들러는 out-of-proc 전용으로
/// 등록돼 있는데 대리 프로세스(prevhost)가 네트워크 세션/자격증명을 못 가져 DoPreview가 E_FAIL 난다.
/// 그래서 네트워크 경로는 파일 정보(InfoPreviewRenderer)로 폴백해 흰 화면 대신 메타데이터를 보여준다.
/// </summary>
public sealed class ShellPreviewRenderer : IPreviewRenderer
{
    private readonly InfoPreviewRenderer _networkFallback;

    public ShellPreviewRenderer(InfoPreviewRenderer networkFallback)
    {
        ArgumentNullException.ThrowIfNull(networkFallback);
        _networkFallback = networkFallback;
    }

    public bool CanRender(string extension) =>
        !string.IsNullOrEmpty(extension) && ShellPreviewHandlerResolver.ResolveClsid(extension) is not null;

    public Task<PreviewResult> RenderAsync(string filePath, CancellationToken cancellationToken)
    {
        if (IsNetworkPath(filePath))
        {
            return _networkFallback.RenderAsync(filePath, cancellationToken);
        }

        return Task.FromResult(new PreviewResult
        {
            Kind = PreviewKind.Native,
            FilePath = filePath,
            DisplayName = Path.GetFileName(filePath),
        });
    }

    /// <summary>UNC(\\..)이거나 매핑된 네트워크 드라이브(변환 시 경로가 바뀜)면 true.</summary>
    private static bool IsNetworkPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
        || !string.Equals(MappedDrivePathResolver.ToUncIfMappedDrive(path), path, StringComparison.OrdinalIgnoreCase);
}
