namespace ChattyStager.Model;

public record RuntimeCheckItem(
    string Name,
    string Required,
    string Detected,
    bool IsInstalled,
    bool IsSupported,
    string Message);

public record RuntimeCheckResult(
    RuntimeCheckItem Node,
    RuntimeCheckItem Database)
{
    public bool IsReady => Node.IsSupported && Database.IsSupported;
}

public record DashboardSnapshot(
    double CpuPercent,
    double MemoryUsedMb,
    double MemoryTotalMb,
    double DiskUsedGb,
    double DiskTotalGb,
    long MessagesLast24Hours,
    long RegisteredUsers,
    bool BackendRunning,
    DateTimeOffset UpdatedAt);

public record DeploymentResult(
    bool Success,
    string Message,
    string TargetPath);

public record BackendStatus(
    bool IsRunning,
    int? ProcessId,
    DateTimeOffset? StartedAt,
    string Message);
