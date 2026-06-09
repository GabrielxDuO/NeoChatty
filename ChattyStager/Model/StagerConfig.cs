namespace ChattyStager.Model;

using System.Text.Json;
using System.IO;

public class StagerConfig
{
    public bool IsSetUp { get; set; } = false;
    public bool UseHttps { get; set; } = false;
    public string PassKey { get; set; } = "";
    public string ChattyWebEndpoint { get; set; } = "";
    public string ChattyHostName { get; set; } = "";
    public string MySqlAddr { get; set; } = "localhost";
    public int MySqlPort { get; set; } = 3306;
    public string MySqlDatabase { get; set; } = "chatty";
    public string MySqlUser { get; set; } = "root";
    public string MySqlPassword { get; set; } = "";
    public string DeployRoot { get; set; } = "";
    public string BackendWorkingDirectory { get; set; } = "";
    public string BackendExecutable { get; set; } = "node";
    public string BackendArguments { get; set; } = "dist/index.js";
    public string BackendEnvironment { get; set; } = "";
    public bool InstallBackendProductionDependencies { get; set; } = true;
    public string HealthUrl { get; set; } = "http://127.0.0.1:5637/api/health";
    public string LogDirectory { get; set; } = "";
    public string ServerConfigPath { get; set; } = "";
    public int ServerPort { get; set; } = 5637;
    public string Motd { get; set; } = "Chatty managed by ChattyStager";
    public string Info { get; set; } = "Chatty backend service.";

    public static async Task Flush(StagerConfig safeNewVersionConfig)
    {
        try
        {
            var jsonText = JsonSerializer.Serialize(safeNewVersionConfig, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(Path.Combine(new DirectoryInfo(Directory.GetCurrentDirectory()).FullName, "ChattyStager.json"), jsonText);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            throw new InvalidOperationException("Unable to create ChattyStager.json");
        }
    }

    public static async Task<StagerConfig?> TryLoad(string filePath)
    {
        StagerConfig? fromJson;
        try
        {
            var read = await File.ReadAllTextAsync(filePath);
            fromJson =  JsonSerializer.Deserialize<StagerConfig>(read);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
        return fromJson;
    }

    public static async Task<bool> HasValidConfig(bool overrideExisting = false, bool mustBeSetUp = false)
    {
        try
        {
            var di = new DirectoryInfo(Directory.GetCurrentDirectory());
        
            foreach (var scan in di.GetFiles())
            {
                if (scan.Name != "ChattyStager.json") continue;
                var load = await TryLoad(scan.FullName);
                if (load != null)
                {
                    return !mustBeSetUp || load.IsSetUp;
                }
                if (!overrideExisting)
                    scan.CopyTo(Path.Combine(di.FullName,
                        $"ChattyStager.invalid.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.json"));
                scan.Delete();
            }
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine(e);
            return false;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        return false;
    }

    public static async Task<bool> TryCreateConfig(bool force = false)
    {
        var di = new DirectoryInfo(Directory.GetCurrentDirectory());
        
        // absolute need to create new
        try
        {
            var isValidConfigCreated = await HasValidConfig(force);
            if (isValidConfigCreated)
                return true;
            
            var jsonText = JsonSerializer.Serialize(new StagerConfig());
            await File.WriteAllTextAsync(Path.Combine(di.FullName, "ChattyStager.json"), jsonText);
            return true;
            
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            throw new InvalidOperationException("Unable to create ChattyStager.json");
        }
    } 
}
