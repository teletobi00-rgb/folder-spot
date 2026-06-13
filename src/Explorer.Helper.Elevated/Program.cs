// Explorer 인덱싱 권한 헬퍼 (관리자 권한).
// 사용법: Explorer.Helper.Elevated.exe <pipeName> <volumeRoot>
//   - 메인 앱이 띄운 named pipe 서버에 클라이언트로 접속한다.
//   - 해당 볼륨의 MFT를 열거해 배치로 스트리밍(UsnProtocol) → EnumDone(nextUsn)
//   - 이후 USN 저널을 폴링하며 변경 델타를 스트리밍한다 (파이프가 끊기면 종료).
using System.IO.Pipes;
using Explorer.Indexing.Index;
using Explorer.Indexing.Usn;

const ulong NtfsRootFrn = 5; // NTFS 루트 디렉터리 FRN

if (args.Length < 2)
{
    return 2;
}

var pipeName = args[0];
var volumeRoot = args[1];

try
{
    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
    pipe.Connect(10_000);
    using var writer = new BinaryWriter(pipe, System.Text.Encoding.UTF8, leaveOpen: false);

    using var reader = new VolumeUsnReader(volumeRoot);
    if (!reader.TryOpen())
    {
        UsnProtocol.WriteError(writer, $"볼륨을 열 수 없습니다(권한/저널 없음): {volumeRoot}");
        writer.Flush();
        return 3;
    }

    var tracker = new UsnChangeTracker(NtfsRootFrn, volumeRoot);
    using var shutdown = new CancellationTokenSource();

    // 1) MFT 전체 열거 → 배치 스트리밍 + 변경 추적기 시드.
    //    경로 해석기(resolver)는 큰 볼륨(수백만 파일)에서 수백 MB를 점유하므로 지역 함수로
    //    수명을 가둔다 — 스트리밍이 끝나면 즉시 수거되어 tailing 루프로 끌고 가지 않는다.
    EnumerateAndStreamMft(reader, tracker, writer, NtfsRootFrn, volumeRoot, shutdown.Token);

    UsnProtocol.WriteEnumDone(writer, (ulong)reader.NextUsn);
    writer.Flush();

    // 2) USN 저널 tailing — 변경 델타 스트리밍.
    //    종료의 권위 있는 신호는 파이프가 끊겼을 때 Flush가 던지는 IOException(아래 catch)이다.
    //    IsConnected는 첫 진입을 막는 값싼 가드일 뿐 닫힘을 능동 감지하지는 않는다.
    var currentUsn = reader.NextUsn;
    while (pipe.IsConnected)
    {
        var records = reader.ReadChanges(currentUsn, out var nextUsn, shutdown.Token);
        if (records.Count == 0)
        {
            Thread.Sleep(1000); // 폴링 주기
            continue;
        }

        currentUsn = nextUsn;
        foreach (var record in records)
        {
            foreach (var change in tracker.Process(record))
            {
                UsnProtocol.WriteChange(writer, change);
            }
        }

        writer.Flush();
    }

    return 0;
}
catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException)
{
    // 메인 앱이 파이프를 닫으면 정상 종료로 취급한다.
    return 0;
}

// MFT를 열거해 추적기를 시드하고 배치로 스트리밍한다. resolver를 이 함수 안에 가둬,
// 반환 직후 GC가 수거할 수 있게 해 tailing 단계의 상주 메모리를 줄인다.
static void EnumerateAndStreamMft(
    VolumeUsnReader reader,
    UsnChangeTracker tracker,
    BinaryWriter writer,
    ulong rootFrn,
    string volumeRoot,
    CancellationToken ct)
{
    var resolver = new MftPathResolver(rootFrn, volumeRoot);
    foreach (var record in reader.EnumerateMft(ct))
    {
        tracker.Seed(record);
        resolver.Add(new MftRecord(
            record.FileReferenceNumber, record.ParentFileReferenceNumber, record.Name, record.IsDirectory));
    }

    var batch = new List<IndexItem>(8192);
    foreach (var item in resolver.ToIndexItems())
    {
        batch.Add(item);
        if (batch.Count >= 8192)
        {
            UsnProtocol.WriteBatch(writer, batch);
            writer.Flush();
            batch.Clear();
        }
    }

    if (batch.Count > 0)
    {
        UsnProtocol.WriteBatch(writer, batch);
    }
}
