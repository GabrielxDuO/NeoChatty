namespace ChattyStager.Model;

public enum OperationState
{
    Pending,
    Running,
    Success,
    Failed,
}

public record OperationLogEntry(
    DateTimeOffset At,
    string Level,
    string Message);

public record DeploymentStepResult(
    string Name,
    OperationState State,
    string Message,
    string? Detail = null);

public record ArtifactInfo(
    long Id,
    string Name,
    string ArchiveDownloadUrl,
    long SizeInBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public record RuntimeCheckItem(
    string Name,
    string Required,
    string Detected,
    string Path,
    bool IsInstalled,
    bool IsSupported,
    string Message);

public record RuntimeCheckResult(
    RuntimeCheckItem Node,
    RuntimeCheckItem DatabaseClient)
{
    public bool IsReady => Node.IsSupported && DatabaseClient.IsSupported;
}

public record BackendRuntimeStatus(
    bool IsRunning,
    int? ProcessId,
    int? RecordedProcessId,
    DateTimeOffset? StartedAt,
    string HealthUrl,
    bool HealthOk,
    string Message,
    string LogPath);

public record DashboardSnapshot(
    double CpuPercent,
    double MemoryUsedMb,
    double MemoryTotalMb,
    double DiskUsedGb,
    double DiskTotalGb,
    long MessagesLast24Hours,
    long RegisteredUsers,
    bool DatabaseOk,
    string DatabaseMessage,
    BackendRuntimeStatus Backend,
    DateTimeOffset UpdatedAt);

public record DeploymentResult(
    bool Success,
    string Message,
    string TargetPath,
    IReadOnlyList<DeploymentStepResult> Steps,
    IReadOnlyList<OperationLogEntry> Logs);
