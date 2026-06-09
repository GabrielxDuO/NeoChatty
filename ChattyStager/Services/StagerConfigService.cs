namespace ChattyStager.Services;

using ChattyStager.Model;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;

public class StagerConfigService
{
    private readonly IWebHostEnvironment _environment;

    public StagerConfigService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public string ConfigPath => Path.Combine(_environment.ContentRootPath, "ChattyStager.json");

    public async Task<StagerConfig> LoadAsync()
    {
        var config = await StagerConfig.TryLoad(ConfigPath) ?? new StagerConfig();
        ApplyDefaults(config);
        return config;
    }

    public async Task SaveAsync(StagerConfig config)
    {
        ApplyDefaults(config);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var jsonText = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        await File.WriteAllTextAsync(ConfigPath, jsonText);
    }

    public void ApplyDefaults(StagerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.DeployRoot))
            config.DeployRoot = Path.Combine(_environment.ContentRootPath, "deploy");

        config.BackendWorkingDirectory = GetBackendDeployPath(config);
        config.ServerConfigPath = Path.Combine(config.BackendWorkingDirectory, "chatty.server.config.js");

        if (string.IsNullOrWhiteSpace(config.LogDirectory))
            config.LogDirectory = Path.Combine(config.DeployRoot, "logs");

        if (string.IsNullOrWhiteSpace(config.MySqlAddr))
            config.MySqlAddr = "localhost";

        if (string.IsNullOrWhiteSpace(config.MySqlDatabase))
            config.MySqlDatabase = "chatty";

        if (string.IsNullOrWhiteSpace(config.MySqlUser))
            config.MySqlUser = "root";

        if (config.MySqlPort == 0)
            config.MySqlPort = 3306;

        if (config.ServerPort <= 0)
            config.ServerPort = 5637;

        if (string.IsNullOrWhiteSpace(config.HealthUrl))
            config.HealthUrl = $"http://127.0.0.1:{config.ServerPort}/api/health";

        if (string.IsNullOrWhiteSpace(config.BackendExecutable))
            config.BackendExecutable = "node";

        if (string.IsNullOrWhiteSpace(config.BackendArguments))
            config.BackendArguments = "dist/index.js";

    }

    public string GetBackendDeployPath(StagerConfig config)
    {
        return Path.Combine(_environment.ContentRootPath, "chatty-server");
    }

    public string GetWebDeployPath(StagerConfig config)
    {
        return _environment.WebRootPath;
    }

    public string GetArtifactCachePath(StagerConfig config)
    {
        return Path.Combine(config.DeployRoot, "artifacts");
    }

    public string GetTempPath(StagerConfig config)
    {
        return Path.Combine(config.DeployRoot, "tmp");
    }

    public string GetPidPath(StagerConfig config)
    {
        return Path.Combine(config.LogDirectory, "backend.pid");
    }

    public string GetStatusPath(StagerConfig config)
    {
        return Path.Combine(config.LogDirectory, "backend.status.json");
    }

    public string GetBackendLogPath(StagerConfig config)
    {
        return Path.Combine(config.LogDirectory, "backend.log");
    }

    public string BuildServerConfig(StagerConfig config)
    {
        ApplyDefaults(config);
        var payload = new
        {
            USE_HTTPS = config.UseHttps,
            PORT = config.ServerPort,
            DB = new
            {
                NAME = config.MySqlDatabase,
                USER = config.MySqlUser,
                PASSWORD = config.MySqlPassword,
                HOST = config.MySqlAddr,
                PORT = config.MySqlPort,
            },
            MOTD = config.Motd,
            INFO = config.Info,
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        return $"module.exports = {json};{Environment.NewLine}";
    }

    public async Task WriteServerConfigAsync(StagerConfig config)
    {
        ApplyDefaults(config);
        Directory.CreateDirectory(Path.GetDirectoryName(config.ServerConfigPath)!);
        await File.WriteAllTextAsync(config.ServerConfigPath, BuildServerConfig(config));
    }

}
