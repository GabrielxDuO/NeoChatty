namespace ChattyStager.Services;

using ChattyStager.Model;
using System.Diagnostics;
using System.Text;

public record ProcessRunResult(
    int ExitCode,
    string Output,
    string Error,
    string Executable,
    string Arguments,
    string? WorkingDirectory);

public static class ProcessRunner
{
    public static async Task<ProcessRunResult> RunAsync(
        string executable,
        string arguments,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        List<OperationLogEntry>? logs = null,
        Func<string, string>? sanitize = null,
        CancellationToken cancellationToken = default)
    {
        sanitize ??= static value => value;
        logs?.Add(new OperationLogEntry(DateTimeOffset.Now, "info", sanitize($"{executable} {arguments}")));

        var output = new StringBuilder();
        var error = new StringBuilder();
        using var timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts?.Token ?? CancellationToken.None);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (environment != null)
        {
            foreach (var pair in environment)
                startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data == null)
                return;
            output.AppendLine(args.Data);
            logs?.Add(new OperationLogEntry(DateTimeOffset.Now, "stdout", sanitize(args.Data)));
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data == null)
                return;
            error.AppendLine(args.Data);
            logs?.Add(new OperationLogEntry(DateTimeOffset.Now, "stderr", sanitize(args.Data)));
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start {executable}.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            throw new TimeoutException($"{executable} timed out after {timeout!.Value}.");
        }

        return new ProcessRunResult(
            process.ExitCode,
            output.ToString(),
            error.ToString(),
            executable,
            arguments,
            workingDirectory);
    }
}
