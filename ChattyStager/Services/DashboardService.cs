namespace ChattyStager.Services;

using ChattyStager.Model;
using System.Diagnostics;

public class DashboardService
{
    private readonly DatabaseAdminService _databaseService;
    private readonly BackendProcessService _backendProcessService;

    public DashboardService(DatabaseAdminService databaseService, BackendProcessService backendProcessService)
    {
        _databaseService = databaseService;
        _backendProcessService = backendProcessService;
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(StagerConfig config)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory)!);
        var totalDisk = drive.TotalSize / 1024d / 1024d / 1024d;
        var freeDisk = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
        var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d;
        var usedMemory = Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d;

        var messages = 0L;
        var users = 0L;
        try
        {
            messages = await _databaseService.CountMessagesLast24HoursAsync(config);
            users = await _databaseService.CountUsersAsync(config);
        }
        catch
        {
        }

        return new DashboardSnapshot(
            CpuPercent: await SampleCpuPercentAsync(),
            MemoryUsedMb: usedMemory,
            MemoryTotalMb: totalMemory,
            DiskUsedGb: totalDisk - freeDisk,
            DiskTotalGb: totalDisk,
            MessagesLast24Hours: messages,
            RegisteredUsers: users,
            BackendRunning: _backendProcessService.GetStatus().IsRunning,
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
