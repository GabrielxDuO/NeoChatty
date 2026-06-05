namespace ChattyStager.Services;

using ChattyStager.Model;
using System.Diagnostics;
using System.Runtime.InteropServices;

public class BackendProcessService
{
    private readonly StagerConfigService _configService;
    private Process? _process;
    private DateTimeOffset? _startedAt;

    public BackendProcessService(StagerConfigService configService)
    {
        _configService = configService;
    }

    public BackendStatus GetStatus()
    {
        var isRunning = _process is { HasExited: false };
        return new BackendStatus(
            isRunning,
            isRunning ? _process!.Id : null,
            isRunning ? _startedAt : null,
            isRunning ? "Backend is running." : "Backend is stopped.");
    }

    public Task<BackendStatus> StartAsync(StagerConfig config)
    {
        if (_process is { HasExited: false })
            return Task.FromResult(GetStatus());

        var workingDirectory = _configService.GetBackendDeployPath(config);
        if (!Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"Backend deploy directory was not found: {workingDirectory}");

        var startInfo = CreateShellStartInfo(config.BackendStartCommand, workingDirectory);
        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start backend process.");
        _startedAt = DateTimeOffset.Now;
        return Task.FromResult(GetStatus());
    }

    public async Task<BackendStatus> StopAsync()
    {
        if (_process is not { HasExited: false })
            return GetStatus();

        _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync();
        return GetStatus();
    }

    private static ProcessStartInfo CreateShellStartInfo(string command, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/c \"{command}\"";
        }
        else
        {
            startInfo.FileName = "/bin/bash";
            startInfo.Arguments = $"-lc \"{command}\"";
        }

        return startInfo;
    }
}
