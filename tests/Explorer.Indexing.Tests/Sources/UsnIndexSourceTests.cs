using System.IO.Pipes;
using System.Text;
using Explorer.Indexing.Index;
using Explorer.Indexing.Sources;
using Explorer.Indexing.Usn;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Indexing.Tests.Sources;

/// <summary>
/// 가짜 헬퍼(권한 불필요)를 파이프 클라이언트로 띄워, UsnIndexSource의 서버+프로토콜+디스패치 전체를 검증한다.
/// </summary>
public sealed class UsnIndexSourceTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue(because);
    }

    /// <summary>지정 메시지를 스트리밍한 뒤 hold 토큰까지 파이프를 열어두는 가짜 헬퍼 런처를 만든다.</summary>
    private static Func<string, string, bool> FakeHelper(
        Action<BinaryWriter> writeMessages,
        CancellationToken hold)
    {
        return (pipeName, volume) =>
        {
            _ = Task.Run(async () =>
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                await client.ConnectAsync(5000);
                using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);
                writeMessages(writer);
                writer.Flush();
                try
                {
                    await Task.Delay(Timeout.Infinite, hold);
                }
                catch (OperationCanceledException)
                {
                }
            });
            return true;
        };
    }

    [Fact]
    public async Task Enumerate_StreamsBatches_AndSignalsEnumerated()
    {
        using var hold = new CancellationTokenSource();
        var launcher = FakeHelper(
            writer =>
            {
                UsnProtocol.WriteBatch(writer, [
                    new IndexItem(@"C:\", "a.txt", false, 0, 0),
                    new IndexItem(@"C:\Work", "plan.xlsx", false, 0, 0),
                ]);
                UsnProtocol.WriteEnumDone(writer, 99);
            },
            hold.Token);

        var batches = new List<IndexItem>();
        using var source = new UsnIndexSource("fake.exe", NullLogger<UsnIndexSource>.Instance, launcher);

        var result = await source.StartAsync(@"C:\", batches.AddRange, _ => { }, CancellationToken.None);

        result.Should().Be(UsnStartResult.Enumerated);
        batches.Should().HaveCount(2);
        batches.Select(b => b.Name).Should().Contain("plan.xlsx");
        hold.Cancel();
    }

    [Fact]
    public async Task Tailing_DeliversChangesAfterEnumeration()
    {
        using var hold = new CancellationTokenSource();
        var launcher = FakeHelper(
            writer =>
            {
                UsnProtocol.WriteEnumDone(writer, 1);
                UsnProtocol.WriteChange(writer, new UsnChange(FileChangeKind.Created, @"C:\new.txt", null, false));
                UsnProtocol.WriteChange(writer, new UsnChange(FileChangeKind.Deleted, @"C:\old.txt", null, false));
            },
            hold.Token);

        var changes = new List<UsnChange>();
        using var source = new UsnIndexSource("fake.exe", NullLogger<UsnIndexSource>.Instance, launcher);

        var result = await source.StartAsync(@"C:\", _ => { }, c => { lock (changes) { changes.Add(c); } }, CancellationToken.None);

        result.Should().Be(UsnStartResult.Enumerated);
        await WaitUntilAsync(() => changes.Count == 2, "tailing 변경 2건 도착");
        changes.Should().Contain(c => c.Kind == FileChangeKind.Created && c.FullPath == @"C:\new.txt");
        changes.Should().Contain(c => c.Kind == FileChangeKind.Deleted);
        hold.Cancel();
    }

    [Fact]
    public async Task HelperReportsError_ResultsInFallback()
    {
        using var hold = new CancellationTokenSource();
        var launcher = FakeHelper(
            writer => UsnProtocol.WriteError(writer, "볼륨 열기 실패(권한 없음)"),
            hold.Token);

        using var source = new UsnIndexSource("fake.exe", NullLogger<UsnIndexSource>.Instance, launcher);

        var result = await source.StartAsync(@"C:\", _ => { }, _ => { }, CancellationToken.None);

        result.Should().Be(UsnStartResult.Fallback);
        hold.Cancel();
    }

    [Fact]
    public async Task LauncherFails_ResultsInFallback()
    {
        using var source = new UsnIndexSource(
            "fake.exe", NullLogger<UsnIndexSource>.Instance, launcher: (_, _) => false);

        var result = await source.StartAsync(@"C:\", _ => { }, _ => { }, CancellationToken.None);

        result.Should().Be(UsnStartResult.Fallback);
    }

    [Fact]
    public async Task PipeClosesWithoutEnumDone_ResultsInFallback()
    {
        // 헬퍼가 EnumDone 없이 즉시 종료(파이프 닫힘) → 폴백
        var launcher = new Func<string, string, bool>((pipeName, volume) =>
        {
            _ = Task.Run(async () =>
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                await client.ConnectAsync(5000);
                // 아무것도 안 쓰고 즉시 닫음
            });
            return true;
        });

        using var source = new UsnIndexSource("fake.exe", NullLogger<UsnIndexSource>.Instance, launcher);

        var result = await source.StartAsync(@"C:\", _ => { }, _ => { }, CancellationToken.None);

        result.Should().Be(UsnStartResult.Fallback);
    }

    [Fact]
    public async Task ConnectedHelperWithoutMessages_TimesOutToFallback()
    {
        using var hold = new CancellationTokenSource();
        var launcher = new Func<string, string, bool>((pipeName, volume) =>
        {
            _ = Task.Run(async () =>
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                await client.ConnectAsync(5000);
                try
                {
                    await Task.Delay(Timeout.Infinite, hold.Token);
                }
                catch (OperationCanceledException)
                {
                }
            });
            return true;
        });

        using var source = new UsnIndexSource(
            "fake.exe",
            NullLogger<UsnIndexSource>.Instance,
            launcher,
            TimeSpan.FromMilliseconds(100));

        var result = await source.StartAsync(@"C:\", _ => { }, _ => { }, CancellationToken.None);

        result.Should().Be(UsnStartResult.Fallback);
        hold.Cancel();
    }

    [Fact]
    public async Task Heartbeat_KeepsEnumerationWaitAliveUntilEnumDone()
    {
        var launcher = new Func<string, string, bool>((pipeName, volume) =>
        {
            _ = Task.Run(async () =>
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                await client.ConnectAsync(5000);
                using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);
                UsnProtocol.WriteHeartbeat(writer);
                writer.Flush();
                await Task.Delay(80);
                UsnProtocol.WriteEnumDone(writer, 1);
                writer.Flush();
            });
            return true;
        });

        using var source = new UsnIndexSource(
            "fake.exe",
            NullLogger<UsnIndexSource>.Instance,
            launcher,
            TimeSpan.FromMilliseconds(120));

        var result = await source.StartAsync(@"C:\", _ => { }, _ => { }, CancellationToken.None);

        result.Should().Be(UsnStartResult.Enumerated);
    }
}
