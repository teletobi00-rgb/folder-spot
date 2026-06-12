using Explorer.Core.FileOperations;
using FluentAssertions;

namespace Explorer.Core.Tests.FileOperations;

public sealed class ProgressEstimatorTests
{
    [Fact]
    public void SingleSample_GivesNoRate()
    {
        var estimator = new ProgressEstimator();
        estimator.AddSample(0, 0);

        estimator.GetPercentPerSecond().Should().BeNull();
        estimator.GetEtaSeconds(0).Should().BeNull();
    }

    [Fact]
    public void SteadyProgress_EstimatesRateAndEta()
    {
        var estimator = new ProgressEstimator();
        estimator.AddSample(0, 0);
        estimator.AddSample(1, 10);
        estimator.AddSample(2, 20);

        estimator.GetPercentPerSecond().Should().BeApproximately(10, 0.01);
        estimator.GetEtaSeconds(20).Should().BeApproximately(8, 0.01, "남은 80% / 10%/s");
    }

    [Fact]
    public void SlidingWindow_DropsOldSamples()
    {
        var estimator = new ProgressEstimator(TimeSpan.FromSeconds(5));
        estimator.AddSample(0, 0);     // 느렸던 초반
        estimator.AddSample(10, 10);
        estimator.AddSample(11, 30);   // 최근 빨라짐
        estimator.AddSample(12, 50);

        // 0초 표본은 윈도우(5s) 밖 — 최근 구간 기준 20%/s
        estimator.GetPercentPerSecond().Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void NoForwardProgress_GivesNullRate()
    {
        var estimator = new ProgressEstimator();
        estimator.AddSample(0, 50);
        estimator.AddSample(1, 50);

        estimator.GetPercentPerSecond().Should().BeNull();
    }

    [Fact]
    public void CompletedProgress_GivesNoEta()
    {
        var estimator = new ProgressEstimator();
        estimator.AddSample(0, 90);
        estimator.AddSample(1, 100);

        estimator.GetEtaSeconds(100).Should().BeNull();
    }
}
