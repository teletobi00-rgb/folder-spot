namespace Explorer.Core.FileOperations;

/// <summary>진행 상태 스냅샷 (불변). Percent는 0~100, 알 수 없으면 null.</summary>
public sealed record OperationProgress
{
    public static OperationProgress Empty { get; } = new();

    public int? Percent { get; init; }

    public int ProcessedItems { get; init; }

    public string? CurrentItem { get; init; }

    /// <summary>남은 예상 시간(초). 추정 불가면 null.</summary>
    public double? EtaSeconds { get; init; }

    /// <summary>초당 진행률(%). 속도 표기용 — 바이트 속도는 총량을 알 때만 별도 계산.</summary>
    public double? PercentPerSecond { get; init; }
}

/// <summary>
/// 슬라이딩 윈도우 기반 진행 속도/ETA 추정기. 시계는 외부에서 경과 시간(초)으로 주입한다(테스트 가능).
/// </summary>
public sealed class ProgressEstimator
{
    private readonly TimeSpan _window;
    private readonly Queue<(double ElapsedSeconds, double Percent)> _samples = new();

    public ProgressEstimator(TimeSpan? window = null)
    {
        _window = window ?? TimeSpan.FromSeconds(5);
    }

    public void AddSample(double elapsedSeconds, double percent)
    {
        _samples.Enqueue((elapsedSeconds, percent));
        while (_samples.Count > 1 && elapsedSeconds - _samples.Peek().ElapsedSeconds > _window.TotalSeconds)
        {
            _samples.Dequeue();
        }
    }

    /// <summary>초당 진행률(%). 표본이 부족하면 null.</summary>
    public double? GetPercentPerSecond()
    {
        if (_samples.Count < 2)
        {
            return null;
        }

        var first = _samples.Peek();
        var last = _samples.Last();
        var seconds = last.ElapsedSeconds - first.ElapsedSeconds;
        if (seconds <= 0)
        {
            return null;
        }

        var rate = (last.Percent - first.Percent) / seconds;
        return rate > 0 ? rate : null;
    }

    public double? GetEtaSeconds(double currentPercent)
    {
        var rate = GetPercentPerSecond();
        if (rate is null or <= 0 || currentPercent >= 100)
        {
            return null;
        }

        return (100 - currentPercent) / rate.Value;
    }
}
