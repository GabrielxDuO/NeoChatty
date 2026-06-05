namespace ChattyStager.Services;

using ChattyStager.Model;
using System.IO.Compression;

public class ArtifactDeploymentService
{
    private readonly StagerConfigService _configService;
    private readonly GitHubActionsArtifactService _githubArtifacts;

    public ArtifactDeploymentService(StagerConfigService configService, GitHubActionsArtifactService githubArtifacts)
    {
        _configService = configService;
        _githubArtifacts = githubArtifacts;
    }

    public async Task<DeploymentResult> DeployBackendFromGitHubAsync(StagerConfig config, CancellationToken cancellationToken = default)
    {
        _configService.ApplyDefaults(config);
        var logs = new List<OperationLogEntry>();
        var steps = new List<DeploymentStepResult>();

        try
        {
            var artifact = await RunStepAsync(steps, "Find backend artifact", logs, async () =>
                await _githubArtifacts.FindLatestSuccessfulArtifactAsync(config, config.BackendArtifactName, logs, cancellationToken));

            var zipPath = await RunStepAsync(steps, "Download backend artifact", logs, async () =>
                await _githubArtifacts.DownloadArtifactAsync(config, artifact, _configService.GetArtifactCachePath(config), logs, cancellationToken));

            var targetPath = await DeployBackendZipCoreAsync(config, zipPath, "backend", steps, logs, cancellationToken);

            return new DeploymentResult(true, "Backend artifact deployed.", targetPath, steps, logs);
        }
        catch (Exception ex)
        {
            logs.Add(new OperationLogEntry(DateTimeOffset.Now, "error", Sanitize(ex.Message, config)));
            return new DeploymentResult(false, ex.Message, _configService.GetBackendDeployPath(config), steps, logs);
        }
    }

    public async Task<DeploymentResult> DeployWebFromGitHubAsync(StagerConfig config, CancellationToken cancellationToken = default)
    {
        _configService.ApplyDefaults(config);
        var logs = new List<OperationLogEntry>();
        var steps = new List<DeploymentStepResult>();

        try
        {
            var artifact = await RunStepAsync(steps, "Find web artifact", logs, async () =>
                await _githubArtifacts.FindLatestSuccessfulArtifactAsync(config, config.WebArtifactName, logs, cancellationToken));

            var zipPath = await RunStepAsync(steps, "Download web artifact", logs, async () =>
                await _githubArtifacts.DownloadArtifactAsync(config, artifact, _configService.GetArtifactCachePath(config), logs, cancellationToken));

            var extractedPath = await RunStepAsync(steps, "Extract web artifact", logs, () =>
                Task.FromResult(ExtractZip(config, zipPath, "web")));

            var webRoot = await RunStepAsync(steps, "Validate web artifact", logs, () =>
                Task.FromResult(FindWebRoot(extractedPath)));

            var targetPath = await RunStepAsync(steps, "Publish web artifact", logs, () =>
            {
                var target = _configService.GetWebDeployPath(config);
                ReplaceDirectory(webRoot, target);
                return Task.FromResult(target);
            });

            return new DeploymentResult(true, "Web artifact deployed.", targetPath, steps, logs);
        }
        catch (Exception ex)
        {
            logs.Add(new OperationLogEntry(DateTimeOffset.Now, "error", Sanitize(ex.Message, config)));
            return new DeploymentResult(false, ex.Message, _configService.GetWebDeployPath(config), steps, logs);
        }
    }

    public DeploymentResult DeployBackendFromZip(StagerConfig config, string zipPath)
    {
        _configService.ApplyDefaults(config);
        var logs = new List<OperationLogEntry>();
        var steps = new List<DeploymentStepResult>();
        try
        {
            var target = DeployBackendZipCoreAsync(config, zipPath, "backend-local", steps, logs, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return new DeploymentResult(true, "Backend zip deployed.", target, steps, logs);
        }
        catch (Exception ex)
        {
            logs.Add(new OperationLogEntry(DateTimeOffset.Now, "error", Sanitize(ex.Message, config)));
            return new DeploymentResult(false, ex.Message, _configService.GetBackendDeployPath(config), steps, logs);
        }
    }

    public DeploymentResult DeployWebFromZip(StagerConfig config, string zipPath)
    {
        _configService.ApplyDefaults(config);
        var logs = new List<OperationLogEntry>();
        var steps = new List<DeploymentStepResult>();
        try
        {
            var extractedPath = RunStep(steps, "Extract web artifact", logs, () => ExtractZip(config, zipPath, "web-local"));
            var webRoot = RunStep(steps, "Validate web artifact", logs, () => FindWebRoot(extractedPath));
            var target = RunStep(steps, "Publish web artifact", logs, () =>
            {
                var publishTarget = _configService.GetWebDeployPath(config);
                ReplaceDirectory(webRoot, publishTarget);
                return publishTarget;
            });
            return new DeploymentResult(true, "Web zip deployed.", target, steps, logs);
        }
        catch (Exception ex)
        {
            logs.Add(new OperationLogEntry(DateTimeOffset.Now, "error", ex.Message));
            return new DeploymentResult(false, ex.Message, _configService.GetWebDeployPath(config), steps, logs);
        }
    }

    private string ExtractZip(StagerConfig config, string zipPath, string prefix)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Artifact zip was not found.", zipPath);

        var tempPath = Path.Combine(_configService.GetTempPath(config), $"{prefix}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        Directory.CreateDirectory(tempPath);
        ZipFile.ExtractToDirectory(zipPath, tempPath, overwriteFiles: true);
        return tempPath;
    }

    private async Task<string> DeployBackendZipCoreAsync(
        StagerConfig config,
        string zipPath,
        string prefix,
        List<DeploymentStepResult> steps,
        List<OperationLogEntry> logs,
        CancellationToken cancellationToken)
    {
        var extractedPath = await RunStepAsync(steps, "Extract backend artifact", logs, () =>
            Task.FromResult(ExtractZip(config, zipPath, prefix)));

        var backendRoot = await RunStepAsync(steps, "Validate backend artifact", logs, () =>
            Task.FromResult(FindBackendRoot(extractedPath)));

        if (config.InstallBackendProductionDependencies || !Directory.Exists(Path.Combine(backendRoot, "node_modules")))
        {
            await RunStepAsync(steps, "Install backend production dependencies", logs, async () =>
            {
                await InstallProductionDependenciesAsync(config, backendRoot, logs, cancellationToken);
                return true;
            });
        }

        var targetPath = await RunStepAsync(steps, "Publish backend artifact", logs, () =>
        {
            ReplaceDirectory(backendRoot, _configService.GetBackendDeployPath(config));
            config.BackendWorkingDirectory = _configService.GetBackendDeployPath(config);
            config.ServerConfigPath = Path.Combine(config.BackendWorkingDirectory, "chatty.server.config.js");
            return Task.FromResult(config.BackendWorkingDirectory);
        });

        await RunStepAsync(steps, "Write backend config", logs, async () =>
        {
            await _configService.WriteServerConfigAsync(config);
            return true;
        });

        return targetPath;
    }

    private static async Task InstallProductionDependenciesAsync(
        StagerConfig config,
        string backendRoot,
        List<OperationLogEntry> logs,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var executable in GetCorepackCandidates(config))
        {
            try
            {
                var result = await ProcessRunner.RunAsync(
                    executable,
                    "pnpm install --prod --frozen-lockfile",
                    backendRoot,
                    timeout: TimeSpan.FromMinutes(8),
                    logs: logs,
                    sanitize: message => Sanitize(message, config),
                    cancellationToken: cancellationToken);

                if (result.ExitCode == 0)
                    return;

                errors.Add($"{executable}: {BuildProcessFailure(result)}");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                errors.Add($"{executable}: {ex.Message}");
            }
        }

        throw new InvalidOperationException($"Unable to install backend production dependencies. {string.Join(" | ", errors)}");
    }

    private static IEnumerable<string> GetCorepackCandidates(StagerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.BackendExecutable))
        {
            var nodeDirectory = Path.GetDirectoryName(config.BackendExecutable);
            if (!string.IsNullOrWhiteSpace(nodeDirectory))
            {
                var adjacentCorepack = Path.Combine(nodeDirectory, OperatingSystem.IsWindows() ? "corepack.cmd" : "corepack");
                if (File.Exists(adjacentCorepack))
                    yield return adjacentCorepack;
            }
        }

        yield return "corepack";
    }

    private static string BuildProcessFailure(ProcessRunResult result)
    {
        var message = string.Join(Environment.NewLine, new[] { result.Error, result.Output }
            .Where(value => !string.IsNullOrWhiteSpace(value)))
            .Trim();

        return string.IsNullOrWhiteSpace(message)
            ? $"exit code {result.ExitCode}"
            : message;
    }

    private static string FindBackendRoot(string extractedPath)
    {
        var packageJson = Directory
            .EnumerateFiles(extractedPath, "package.json", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();

        if (packageJson == null)
            throw new InvalidOperationException("Backend artifact must contain package.json.");

        var root = Path.GetDirectoryName(packageJson)!;
        if (!File.Exists(Path.Combine(root, "pnpm-lock.yaml")))
            throw new InvalidOperationException("Backend artifact must contain pnpm-lock.yaml.");
        if (!Directory.Exists(Path.Combine(root, "dist")))
            throw new InvalidOperationException("Backend artifact must contain dist/.");

        return root;
    }

    private static string FindWebRoot(string extractedPath)
    {
        var indexFile = Directory
            .EnumerateFiles(extractedPath, "index.html", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();

        if (indexFile == null)
            throw new InvalidOperationException("Web artifact must contain index.html.");

        return Path.GetDirectoryName(indexFile)!;
    }

    private static void ReplaceDirectory(string source, string target)
    {
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        var staging = $"{target}.next-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        MoveOrCopyDirectory(source, staging);

        var backup = $"{target}.bak-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        if (Directory.Exists(target))
            Directory.Move(target, backup);

        Directory.Move(staging, target);
        if (Directory.Exists(backup))
            Directory.Delete(backup, recursive: true);
    }

    private static void MoveOrCopyDirectory(string source, string target)
    {
        try
        {
            Directory.Move(source, target);
        }
        catch (IOException)
        {
            CopyDirectory(source, target);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var targetFile = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static async Task<T> RunStepAsync<T>(
        List<DeploymentStepResult> steps,
        string name,
        List<OperationLogEntry> logs,
        Func<Task<T>> action)
    {
        logs.Add(new OperationLogEntry(DateTimeOffset.Now, "info", name));
        try
        {
            var result = await action();
            steps.Add(new DeploymentStepResult(name, OperationState.Success, "Done"));
            return result;
        }
        catch (Exception ex)
        {
            steps.Add(new DeploymentStepResult(name, OperationState.Failed, ex.Message));
            throw;
        }
    }

    private static T RunStep<T>(
        List<DeploymentStepResult> steps,
        string name,
        List<OperationLogEntry> logs,
        Func<T> action)
    {
        logs.Add(new OperationLogEntry(DateTimeOffset.Now, "info", name));
        try
        {
            var result = action();
            steps.Add(new DeploymentStepResult(name, OperationState.Success, "Done"));
            return result;
        }
        catch (Exception ex)
        {
            steps.Add(new DeploymentStepResult(name, OperationState.Failed, ex.Message));
            throw;
        }
    }

    private static string Sanitize(string message, StagerConfig config)
    {
        return string.IsNullOrWhiteSpace(config.GitHubToken)
            ? message
            : message.Replace(config.GitHubToken, "***");
    }
}
