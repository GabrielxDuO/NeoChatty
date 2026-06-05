namespace ChattyStager.Services;

using ChattyStager.Model;
using System.Diagnostics;
using System.Text.RegularExpressions;

public class SystemInspectionService
{
    public async Task<RuntimeCheckResult> CheckRuntimeAsync()
    {
        var node = await CheckNodeAsync();
        var database = await CheckDatabaseAsync();
        return new RuntimeCheckResult(node, database);
    }

    private static async Task<RuntimeCheckItem> CheckNodeAsync()
    {
        var output = await RunCommandAsync("node", "--version");
        if (output.Success && !IsNode22(output.Output))
        {
            var miseOutput = await RunCommandAsync("mise", "exec -- node --version");
            if (miseOutput.Success)
                output = miseOutput;
        }

        if (!output.Success)
        {
            return new RuntimeCheckItem("Node.js", "22.x", output.Message, false, false, "node command is unavailable.");
        }

        var version = output.Output.Trim();
        var supported = IsNode22(version);
        return new RuntimeCheckItem("Node.js", "22.x", version, true, supported, supported ? "Node.js 22 is installed." : "Install Node.js 22 for the backend runtime.");
    }

    private static async Task<RuntimeCheckItem> CheckDatabaseAsync()
    {
        var mysql = await RunCommandAsync("mysql", "--version");
        if (!mysql.Success)
            mysql = await RunCommandAsync("mariadb", "--version");
        if (!mysql.Success)
            mysql = await RunCommandAsync("/opt/homebrew/opt/mariadb@11.8/bin/mariadb", "--version");
        if (!mysql.Success)
            mysql = await RunCommandAsync("/opt/homebrew/opt/mysql@8.4/bin/mysql", "--version");

        if (!mysql.Success)
        {
            return new RuntimeCheckItem("Database CLI", "MySQL 8.x or MariaDB 11.x", mysql.Message, false, false, "mysql or mariadb command is unavailable.");
        }

        var detected = mysql.Output.Trim();
        var lower = detected.ToLowerInvariant();
        var isMaria = lower.Contains("mariadb");
        var match = Regex.Match(detected, @"(?<major>\d+)\.(?<minor>\d+)\.");
        var supported = match.Success && int.Parse(match.Groups["major"].Value) == (isMaria ? 11 : 8);
        var required = isMaria ? "MariaDB 11.x" : "MySQL 8.x";

        return new RuntimeCheckItem("Database CLI", required, detected, true, supported, supported ? "Supported database client detected." : "Install MySQL 8 or MariaDB 11.");
    }

    private static bool IsNode22(string version)
    {
        var match = Regex.Match(version.Trim(), @"v(?<major>\d+)\.");
        return match.Success && int.Parse(match.Groups["major"].Value) == 22;
    }

    private static async Task<(bool Success, string Output, string Message)> RunCommandAsync(string fileName, string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return (false, "", "Failed to start process.");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            return process.ExitCode == 0
                ? (true, string.IsNullOrWhiteSpace(output) ? error : output, "")
                : (false, output, string.IsNullOrWhiteSpace(error) ? output : error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }
}
