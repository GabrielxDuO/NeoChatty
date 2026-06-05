namespace ChattyStager.Services;

using ChattyStager.Model;
using Microsoft.AspNetCore.Hosting;
using System.IO.Compression;
using System.Net.Http.Headers;

public class DeploymentService
{
    private readonly HttpClient _httpClient;
    private readonly IWebHostEnvironment _environment;
    private readonly StagerConfigService _configService;

    public DeploymentService(HttpClient httpClient, IWebHostEnvironment environment, StagerConfigService configService)
    {
        _httpClient = httpClient;
        _environment = environment;
        _configService = configService;
    }

    public async Task<DeploymentResult> DeployBackendAsync(StagerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BackendArtifactUrl))
            return new DeploymentResult(false, "Backend artifact URL is empty.", "");

        var artifactPath = await DownloadArtifactAsync(config.BackendArtifactUrl, config.GitHubToken, _configService.GetArtifactCachePath(config), "backend.zip");
        var target = _configService.GetBackendDeployPath(config);
        ExtractArtifact(artifactPath, target);
        await _configService.WriteServerConfigAsync(config);

        return new DeploymentResult(true, "Backend artifact deployed.", target);
    }

    public async Task<DeploymentResult> DeployWebAsync(StagerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.WebArtifactUrl))
            return new DeploymentResult(false, "Web artifact URL is empty.", "");

        var artifactPath = await DownloadArtifactAsync(config.WebArtifactUrl, config.GitHubToken, _configService.GetArtifactCachePath(config), "web.zip");
        var tempTarget = Path.Combine(_configService.GetDeployRoot(config), "web-extracted");
        ExtractArtifact(artifactPath, tempTarget);

        var webRoot = FindWebRoot(tempTarget);
        CopyDirectory(webRoot, _environment.WebRootPath, overwrite: true);

        return new DeploymentResult(true, "Web artifact deployed to Stager wwwroot.", _environment.WebRootPath);
    }

    public async Task<DeploymentResult> InitializeDatabaseAsync(StagerConfig config, DatabaseAdminService databaseService, string sqlPath)
    {
        await databaseService.InitializeDatabaseAsync(config, sqlPath);
        return new DeploymentResult(true, "Database initialized from SQL file.", sqlPath);
    }

    private async Task<string> DownloadArtifactAsync(string source, string token, string cachePath, string fileName)
    {
        Directory.CreateDirectory(cachePath);
        var artifactPath = Path.Combine(cachePath, fileName);

        if (File.Exists(source))
        {
            File.Copy(source, artifactPath, overwrite: true);
            return artifactPath;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        request.Headers.UserAgent.ParseAdd("ChattyStager/1.0");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(artifactPath);
        await input.CopyToAsync(output);

        return artifactPath;
    }

    private static void ExtractArtifact(string zipPath, string targetPath)
    {
        if (Directory.Exists(targetPath))
            Directory.Delete(targetPath, recursive: true);

        Directory.CreateDirectory(targetPath);
        ZipFile.ExtractToDirectory(zipPath, targetPath, overwriteFiles: true);
    }

    private static string FindWebRoot(string extractedPath)
    {
        var indexFile = Directory
            .EnumerateFiles(extractedPath, "index.html", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();

        if (indexFile == null)
            throw new InvalidOperationException("Artifact does not contain index.html.");

        return Path.GetDirectoryName(indexFile)!;
    }

    private static void CopyDirectory(string source, string target, bool overwrite)
    {
        Directory.CreateDirectory(target);

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            File.Copy(file, Path.Combine(target, relative), overwrite);
        }
    }
}
