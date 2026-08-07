using System.Diagnostics;

namespace TrameEditor.Core.Ai;

/// <summary>Fotografia del PC rispetto ai requisiti dell'AI locale.</summary>
public sealed record AiRequirementsReport(
    double TotalRamGb,
    double FreeDiskGb,
    int CpuCores,
    bool HasNvidiaGpu,
    bool MeetsMinimum,
    IReadOnlyList<string> Notes);

public static class AiRequirements
{
    public const double MinRamGb = 8;
    public const double RecommendedRamGb = 16;
    public const double MinFreeDiskGb = 6;
    public const int MinCpuCores = 4;

    /// <summary>Valutazione pura (testabile) a partire dai valori misurati.</summary>
    public static AiRequirementsReport Evaluate(double totalRamGb, double freeDiskGb,
        int cpuCores, bool hasNvidiaGpu)
    {
        var notes = new List<string>();
        var meets = true;

        if (totalRamGb < MinRamGb)
        {
            meets = false;
            notes.Add($"RAM insufficiente: {totalRamGb:F0} GB (minimo {MinRamGb:F0}, consigliati {RecommendedRamGb:F0}).");
        }
        else if (totalRamGb < RecommendedRamGb)
        {
            notes.Add($"RAM al minimo ({totalRamGb:F0} GB): l'AI funzionerà ma conviene chiudere le altre applicazioni.");
        }

        if (freeDiskGb < MinFreeDiskGb)
        {
            meets = false;
            notes.Add($"Spazio su disco insufficiente: {freeDiskGb:F1} GB liberi (servono ~{MinFreeDiskGb:F0} GB per Ollama e il modello).");
        }

        if (cpuCores < MinCpuCores)
        {
            meets = false;
            notes.Add($"Processore con soli {cpuCores} core: le risposte sarebbero troppo lente.");
        }

        notes.Add(hasNvidiaGpu
            ? "GPU NVIDIA rilevata: le risposte saranno rapide."
            : "Nessuna GPU NVIDIA rilevata: l'AI userà il processore (risposte più lente, anche decine di secondi).");

        return new AiRequirementsReport(totalRamGb, freeDiskGb, cpuCores, hasNvidiaGpu, meets, notes);
    }

    /// <summary>Misura i valori reali di questo PC e li valuta.</summary>
    public static AiRequirementsReport Collect()
    {
        var ramGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024 / 1024;
        double freeDiskGb;
        try
        {
            var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData))!;
            freeDiskGb = new DriveInfo(systemDrive).AvailableFreeSpace / 1024.0 / 1024 / 1024;
        }
        catch
        {
            freeDiskGb = MinFreeDiskGb; // non misurabile: non bloccare per questo
        }
        return Evaluate(ramGb, freeDiskGb, Environment.ProcessorCount, DetectNvidiaGpu());
    }

    private static bool DetectNvidiaGpu()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("nvidia-smi", "-L")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (process is null)
                return false;
            process.WaitForExit(3000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
