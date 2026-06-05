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
        EnsureGitHubConfig(config);
        logs.Add(new OperationLogEntry(DateTimeOffset.Now, "info", $"Finding artifact `{artifactName}` in latest successful workflow run."));

        var workflow = Uri.EscapeDataString(config.GitHubWorkflow);
        var branchQuery = string.IsNullOrWhiteSpace(config.GitHubBranch)
            ? ""
            : $"&branch={Uri.EscapeDataString(config.GitHubBranch)}";
        var runsUrl = $"https://api.github.com/repos/{config.GitHubOwner}/{config.GitHubRepo}/actions/workflows/{workflow}/runs?status=success&per_page=10{branchQuery}";
        using var runsDoc = await GetJsonAsync(config, runsUrl, cancellationToken);
        var runs = runsDoc.RootElement.GetProperty("workflow_runs").EnumerateArray().ToList();
        if (runs.Count == 0)
            throw new InvalidOperationException("No successful workflow runs were found.");

        foreach (var run in runs)
        {
            var artifactsUrl = run.GetProperty("artifacts_url").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(artifactsUrl))
                continue;

            using var artifactsDoc = await GetJsonAsync(config, artifactsUrl, cancellationToken);
            foreach (var artifact in artifactsDoc.RootElement.GetProperty("artifacts").EnumerateArray())
            {
                var name = artifact.GetProperty("name").GetString() ?? "";
                if (!string.Equals(name, artifactName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var info = new ArtifactInfo(
                    artifact.GetProperty("id").GetInt64(),
                    name,
                    artifact.GetProperty("archive_download_url").GetString() ?? "",
                    artifact.TryGetProperty("size_in_bytes", out var size) ? size.GetInt64() : 0,
                    artifact.GetProperty("created_at").GetDateTimeOffset(),
                    artifact.GetProperty("expires_at").GetDateTimeOffset());

                logs.Add(new OperationLogEntry(DateTimeOffset.Now, "info", $"Matched artifact `{info.Name}` ({info.SizeInBytes} bytes)."));
                return info;
            }
        }

        throw new InvalidOperationException($"Artifact `{artifactName}` was not found in recent successful workflow runs.");
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

        using var request = CreateRequest(config, artifact.ArchiveDownloadUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(targetPath);
        await input.CopyToAsync(output, cancellationToken);

        logs.Add(new OperationLogEntry(DateTimeOffset.Now, "info", $"Saved artifact to {targetPath}."));
        return targetPath;
    }

    private async Task<JsonDocument> GetJsonAsync(StagerConfig config, string url, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(config, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(StagerConfig config, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ChattyStager/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(config.GitHubToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.GitHubToken);
        return request;
    }

    private static void EnsureGitHubConfig(StagerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.GitHubOwner) ||
            string.IsNullOrWhiteSpace(config.GitHubRepo) ||
            string.IsNullOrWhiteSpace(config.GitHubWorkflow))
        {
            throw new InvalidOperationException("GitHub owner, repo, and workflow are required.");
        }
    }
}
