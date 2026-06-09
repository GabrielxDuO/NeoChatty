namespace ChattyStager.Services;

using ChattyStager.Model;
using System.Net.Http.Headers;
using System.Text.Json;

public class GitHubActionsArtifactService
{
    private readonly HttpClient _httpClient;

    public GitHubActionsArtifactService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ArtifactInfo> FindLatestSuccessfulArtifactAsync(
        StagerConfig config,
        string artifactName,
        List<OperationLogEntry> logs,
        CancellationToken cancellationToken = default)
    {
        var artifacts = await FindLatestBuildArtifactsAsync(logs, cancellationToken);
        if (string.Equals(artifactName, GitHubArtifactConstants.ServerArtifactName, StringComparison.OrdinalIgnoreCase))
            return artifacts.Server;
        if (string.Equals(artifactName, GitHubArtifactConstants.WebappArtifactName, StringComparison.OrdinalIgnoreCase))
            return artifacts.Webapp;
        throw new InvalidOperationException($"Unsupported artifact `{artifactName}`.");
    }

    public async Task<(ArtifactInfo Server, ArtifactInfo Webapp)> FindLatestBuildArtifactsAsync(
        List<OperationLogEntry> logs,
        CancellationToken cancellationToken = default)
    {
        logs.Add(new OperationLogEntry(DateTimeOffset.Now, "info", $"Finding latest `{GitHubArtifactConstants.ServerArtifactName}` and `{GitHubArtifactConstants.WebappArtifactName}` artifacts from public repository artifacts."));

        using var artifactsDoc = await GetJsonAsync(GitHubArtifactConstants.RepositoryArtifactsUrl, cancellationToken);
        ArtifactInfo? server = null;
        ArtifactInfo? webapp = null;

        foreach (var artifact in artifactsDoc.RootElement.GetProperty("artifacts").EnumerateArray())
        {
            var name = artifact.GetProperty("name").GetString() ?? "";
            if (server == null && string.Equals(name, GitHubArtifactConstants.ServerArtifactName, StringComparison.OrdinalIgnoreCase))
            {
                server = CreateArtifactInfo(artifact);
                logs.Add(new OperationLogEntry(DateTimeOffset.Now, "info", $"Matched artifact `{server.Name}` ({server.SizeInBytes} bytes)."));
            }
            else if (webapp == null && string.Equals(name, GitHubArtifactConstants.WebappArtifactName, StringComparison.OrdinalIgnoreCase))
            {
                webapp = CreateArtifactInfo(artifact);
                logs.Add(new OperationLogEntry(DateTimeOffset.Now, "info", $"Matched artifact `{webapp.Name}` ({webapp.SizeInBytes} bytes)."));
            }

            if (server != null && webapp != null)
                return (server, webapp);
        }

        var missing = string.Join(", ", new[]
        {
            server == null ? GitHubArtifactConstants.ServerArtifactName : "",
            webapp == null ? GitHubArtifactConstants.WebappArtifactName : "",
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        throw new InvalidOperationException($"Required artifact(s) not found in repository artifacts: {missing}.");
    }

    public async Task<string> DownloadArtifactAsync(
        StagerConfig config,
        ArtifactInfo artifact,
        string targetDirectory,
        List<OperationLogEntry> logs,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, $"{artifact.Name}.{artifact.Id}.zip");
        logs.Add(new OperationLogEntry(DateTimeOffset.Now, "info", $"Downloading artifact `{artifact.Name}`."));

        using var request = CreateRequest(artifact.ArchiveDownloadUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(targetPath);
        await input.CopyToAsync(output, cancellationToken);

        logs.Add(new OperationLogEntry(DateTimeOffset.Now, "info", $"Saved artifact to {targetPath}."));
        return targetPath;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ChattyStager/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return request;
    }

    private static ArtifactInfo CreateArtifactInfo(JsonElement artifact)
    {
        return new ArtifactInfo(
            artifact.GetProperty("id").GetInt64(),
            artifact.GetProperty("name").GetString() ?? "",
            artifact.GetProperty("archive_download_url").GetString() ?? "",
            artifact.TryGetProperty("size_in_bytes", out var size) ? size.GetInt64() : 0,
            artifact.GetProperty("created_at").GetDateTimeOffset(),
            artifact.GetProperty("expires_at").GetDateTimeOffset());
    }
}
