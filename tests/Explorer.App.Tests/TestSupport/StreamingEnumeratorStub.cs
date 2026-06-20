using System.Runtime.CompilerServices;
using Explorer.Core.FileSystem;
using NSubstitute;

namespace Explorer.App.Tests.TestSupport;

/// <summary>
/// 테스트 mock의 <see cref="IFileSystemEnumerator.StreamAsync"/>가 기존 ListAsync 스텁 결과를 한 배치로
/// 흘려보내도록 연결한다 — 테스트들이 ListAsync만 스텁해 두면 스트리밍 경로(ReloadAsync)도 그대로 동작한다.
/// </summary>
internal static class StreamingEnumeratorStub
{
    public static void StreamFromList(IFileSystemEnumerator enumerator)
    {
        enumerator.StreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Yield(enumerator, ci.ArgAt<string>(0), ci.ArgAt<CancellationToken>(1)));
    }

    private static async IAsyncEnumerable<IReadOnlyList<FileEntry>> Yield(
        IFileSystemEnumerator enumerator, string path, [EnumeratorCancellation] CancellationToken ct)
    {
        var list = await enumerator.ListAsync(path, ct).ConfigureAwait(false);
        if (list.Count > 0)
        {
            yield return list;
        }
    }
}
