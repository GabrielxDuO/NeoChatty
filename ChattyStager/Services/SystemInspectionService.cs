namespace ChattyStager.Services;

using ChattyStager.Model;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

public class SystemInspectionService
{
    public async Task<RuntimeCheckResult> CheckRuntimeAsync(StagerConfig config)
    {
        var node = await CheckNodeAsync();
        var databaseClient = await CheckDatabaseClientAsync();
        return new RuntimeCheckResult(node, databaseClient);
    }

    private static async Task<RuntimeCheckItem> CheckNodeAsync()
    {
        var candidates = new List<(string FileName, string Arguments)>
        {
            ("node", "--version"),
            ("mise", "exec -- node --version"),
            ("nvm", "exec 22 node --version"),
        };

        foreach (var candidate in candidates)
        {
            var result = await TryRunAsync(candidate.FileName, candidate.Arguments);
            if (!result.Success)
                continue;

            var version = result.Output.Trim();
            var supported = IsNode22OrNewer(version);
            if (supported)
            {
                return new RuntimeCheckItem("Node.js", "22+", version, result.Path, true, true, "Node.js 22 or newer is available.");
            }
        }

        var fallback = await TryRunAsync("node", "--version");
        return new RuntimeCheckItem(
            "Node.js",
            "22+",
            fallback.Success ? fallback.Output.Trim() : fallback.Message,
            fallback.Path,
            fallback.Success,
            false,
            "Node.js 22 or newer is required.");
    }

    private static async Task<RuntimeCheckItem> CheckDatabaseClientAsync()
    {
        var candidates = new[]
        {
            ("mysql", "--version"),
            ("mariadb", "--version"),
            ("/opt/homebrew/opt/mariadb@11.8/bin/mariadb", "--version"),
            ("/opt/homebrew/opt/mysql@8.4/bin/mysql", "--version"),
        };

        foreach (var candidate in candidates)
        {
            var result = await TryRunAsync(candidate.Item1, candidate.Item2);
            if (!result.Success)
                continue;

            var detected = result.Output.Trim();
            var lower = detected.ToLowerInvariant();
            var isMaria = lower.Contains("mariadb");
            var match = Regex.Match(detected, @"(?<major>\d+)\.(?<minor>\d+)\.");
            var supported = match.Success && int.Parse(match.Groups["major"].Value) >= (isMaria ? 11 : 8);
            return new RuntimeCheckItem(
                "Database CLI",
                "MySQL 8+ or MariaDB 11+",
                detected,
                result.Path,
                true,
                supported,
                supported ? "Supported database client detected." : "Install MySQL 8 or newer, or MariaDB 11 or newer.");
        }

        return new RuntimeCheckItem("Database CLI", "MySQL 8+ or MariaDB 11+", "Not found", "", false, false, "mysql or mariadb command is unavailable.");
    }

    private static bool IsNode22OrNewer(string version)
    {
        var match = Regex.Match(version.Trim(), @"v(?<major>\d+)\.");
        return match.Success && int.Parse(match.Groups["major"].Value) >= 22;
    }

    private static async Task<(bool Success, string Output, string Message, string Path)> TryRunAsync(string fileName, string arguments)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(fileName, arguments, timeout: TimeSpan.FromSeconds(10));
            var path = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? fileName
                : (await ResolvePathAsync(fileName)) ?? fileName;
            return result.ExitCode == 0
                ? (true, string.IsNullOrWhiteSpace(result.Output) ? result.Error : result.Output, "", path)
                : (false, result.Output, string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error, path);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message, fileName);
        }
    }

    private static async Task<string?> ResolvePathAsync(string fileName)
    {
        if (fileName.Contains('/'))
            return File.Exists(fileName) ? fileName : null;

        try
        {
            var result = await ProcessRunner.RunAsync("/usr/bin/which", fileName, timeout: TimeSpan.FromSeconds(5));
            return result.ExitCode == 0 ? result.Output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
