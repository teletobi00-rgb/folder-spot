using System.IO;

namespace Explorer.Preview.Tests;

/// <summary>테스트용 임시 디렉터리 — Dispose 시 정리한다.</summary>
internal sealed class TempFiles : IDisposable
{
    public TempFiles()
    {
        Root = Path.Combine(Path.GetTempPath(), "ExplorerPreviewTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string WriteText(string name, string content, System.Text.Encoding? encoding = null)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path, content, encoding ?? new System.Text.UTF8Encoding(false));
        return path;
    }

    public string WriteBytes(string name, byte[] bytes)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public string Combine(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
