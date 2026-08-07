using TrameEditor.Core.Ai;

namespace TrameEditor.Core.Tests.Ai;

public class AiRequirementsTests
{
    [Fact]
    public void Evaluate_GoodMachine_Meets()
    {
        var report = AiRequirements.Evaluate(32, 100, 8, hasNvidiaGpu: true);
        Assert.True(report.MeetsMinimum);
        Assert.Contains(report.Notes, n => n.Contains("GPU NVIDIA rilevata"));
    }

    [Fact]
    public void Evaluate_LowRam_DoesNotMeet()
    {
        var report = AiRequirements.Evaluate(4, 100, 8, false);
        Assert.False(report.MeetsMinimum);
        Assert.Contains(report.Notes, n => n.Contains("RAM insufficiente"));
    }

    [Fact]
    public void Evaluate_MinimumRam_MeetsWithWarning()
    {
        var report = AiRequirements.Evaluate(8, 100, 8, false);
        Assert.True(report.MeetsMinimum);
        Assert.Contains(report.Notes, n => n.Contains("RAM al minimo"));
        Assert.Contains(report.Notes, n => n.Contains("Nessuna GPU"));
    }

    [Fact]
    public void Evaluate_LowDiskOrCores_DoesNotMeet()
    {
        Assert.False(AiRequirements.Evaluate(16, 2, 8, false).MeetsMinimum);
        Assert.False(AiRequirements.Evaluate(16, 100, 2, false).MeetsMinimum);
    }

    [Fact]
    public void Collect_ReturnsPlausibleValues()
    {
        var report = AiRequirements.Collect();
        Assert.True(report.TotalRamGb > 1);
        Assert.True(report.CpuCores >= 1);
    }
}
