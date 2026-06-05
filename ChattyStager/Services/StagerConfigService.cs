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

    public string ConfigPath => Path.Combine(Directory.GetCurrentDirectory(), "ChattyStager.json");

    public async Task<StagerConfig> LoadAsync()
    {
        var config = await StagerConfig.TryLoad(ConfigPath) ?? new StagerConfig();
        ApplyDefaults(config);
        return config;
    }

    public async Task SaveAsync(StagerConfig config)
    {
        ApplyDefaults(config);
        await StagerConfig.Flush(config);
    }

    public string GetDeployRoot(StagerConfig config)
    {
        ApplyDefaults(config);
        return config.DeployRoot;
    }

    public string GetBackendDeployPath(StagerConfig config)
    {
        return Path.Combine(GetDeployRoot(config), "backend");
    }

    public string GetArtifactCachePath(StagerConfig config)
    {
        return Path.Combine(GetDeployRoot(config), "artifacts");
    }

    public string GetServerConfigPath(StagerConfig config)
    {
        ApplyDefaults(config);
        return config.ServerConfigPath;
    }

    public string BuildServerConfig(StagerConfig config)
    {
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
                PORT = config.MySqlPort
            },
            MOTD = config.Motd,
            INFO = config.Info
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return $"module.exports = {json};{Environment.NewLine}";
    }

    public async Task WriteServerConfigAsync(StagerConfig config)
    {
        var path = GetServerConfigPath(config);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, BuildServerConfig(config));
    }

    private void ApplyDefaults(StagerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.DeployRoot))
            config.DeployRoot = Path.Combine(_environment.ContentRootPath, "deploy");

        if (string.IsNullOrWhiteSpace(config.ServerConfigPath))
            config.ServerConfigPath = Path.Combine(GetBackendDeployPath(config), "chatty.server.config.js");

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

        if (string.IsNullOrWhiteSpace(config.BackendStartCommand))
            config.BackendStartCommand = "node dist/index.js";
    }
}
