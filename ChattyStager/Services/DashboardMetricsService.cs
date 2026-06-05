namespace ChattyStager.Services;

using ChattyStager.Model;
using System.Diagnostics;

public class DashboardMetricsService
{
    private readonly DatabaseSetupService _databaseService;
    private readonly BackendSupervisorService _backendSupervisor;

    public DashboardMetricsService(DatabaseSetupService databaseService, BackendSupervisorService backendSupervisor)
    {
        _databaseService = databaseService;
        _backendSupervisor = backendSupervisor;
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(StagerConfig config)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory)!);
        var totalDisk = drive.TotalSize / 1024d / 1024d / 1024d;
        var freeDisk = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
        var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d;
        var usedMemory = Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d;
        var backend = await _backendSupervisor.GetStatusAsync(config);

        var databaseOk = true;
        var databaseMessage = "Database metrics loaded.";
        long messages = 0;
        long users = 0;
        try
        {
            messages = await _databaseService.CountMessagesLast24HoursAsync(config);
            users = await _databaseService.CountUsersAsync(config);
        }
        catch (Exception ex)
        {
            databaseOk = false;
            databaseMessage = ex.Message;
        }

        return new DashboardSnapshot(
            CpuPercent: await SampleCpuPercentAsync(),
            MemoryUsedMb: usedMemory,
            MemoryTotalMb: totalMemory,
            DiskUsedGb: totalDisk - freeDisk,
            DiskTotalGb: totalDisk,
            MessagesLast24Hours: messages,
            RegisteredUsers: users,
            DatabaseOk: databaseOk,
            DatabaseMessage: databaseMessage,
            Backend: backend,
            UpdatedAt: DateTimeOffset.Now);
    }

    private static async Task<double> SampleCpuPercentAsync()
    {
        var process = Process.GetCurrentProcess();
        var startCpu = process.TotalProcessorTime;
        var start = DateTime.UtcNow;
        await Task.Delay(250);
        process.Refresh();
        var cpuUsed = (process.TotalProcessorTime - startCpu).TotalMilliseconds;
        var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
        if (elapsed <= 0)
            return 0;

        return Math.Clamp(cpuUsed / (elapsed * Environment.ProcessorCount) * 100, 0, 100);
    }
}
