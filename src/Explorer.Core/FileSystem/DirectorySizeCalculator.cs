using System.IO;

namespace Explorer.Core.FileSystem;

/// <summary>폴더의 전체 크기(하위 포함)를 합산한다. 접근 불가 폴더는 건너뛰고 예외 없이 진행한다.</summary>
public static class DirectorySizeCalculator
{
    /// <summary>하위 디렉터리를 모두 순회하며 파일 길이를 합산한다(반복적 — 깊은 트리에서도 안전).</summary>
    public static long Compute(string directoryPath, CancellationToken cancellationToken = default)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(directoryPath);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // 개별 파일 접근 실패는 무시.
                    }
                }

                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    stack.Push(sub);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                // 접근 불가/사라진 디렉터리는 건너뛴다.
            }
        }

        return total;
    }
}
