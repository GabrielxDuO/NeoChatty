namespace ChattyStager.Services;

using ChattyStager.Model;
using MySql.Data.MySqlClient;

public class DatabaseAdminService
{
    private readonly StagerConfigService _configService;

    public DatabaseAdminService(StagerConfigService configService)
    {
        _configService = configService;
    }

    public async Task<bool> TestConnectionAsync(StagerConfig config)
    {
        using var connection = CreateConnection(config, includeDatabase: false);
        await connection.OpenAsync();
        return true;
    }

    public async Task InitializeDatabaseAsync(StagerConfig config, string sqlPath)
    {
        if (!File.Exists(sqlPath))
            throw new FileNotFoundException("SQL file was not found.", sqlPath);

        using var connection = CreateConnection(config, includeDatabase: false);
        await connection.OpenAsync();

        var scriptText = await File.ReadAllTextAsync(sqlPath);
        scriptText = NormalizeSql(scriptText, config.MySqlDatabase);

        var script = new MySqlScript(connection, scriptText);
        await Task.Run(() => script.Execute());
    }

    public async Task<long> CountUsersAsync(StagerConfig config)
    {
        return await ExecuteScalarLongAsync(config, "select count(*) from users;");
    }

    public async Task<long> CountMessagesLast24HoursAsync(StagerConfig config)
    {
        using var connection = CreateConnection(config, includeDatabase: true);
        await connection.OpenAsync();
        using var command = new MySqlCommand("select count(*) from messages where created_at >= @since;", connection);
        command.Parameters.AddWithValue("@since", DateTime.UtcNow.AddHours(-24));
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task<List<UserModel>> SearchUsersAsync(StagerConfig config, string query)
    {
        using var connection = CreateConnection(config, includeDatabase: true);
        await connection.OpenAsync();

        const string sql = """
                           select id, username, display_name, password_hash, avatar_locator, background_locator, token_version, created_at, updated_at, disabled
                           from users
                           where @query = '' or username like @like or display_name like @like or cast(id as char) = @query
                           order by id desc
                           limit 100;
                           """;

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@query", query.Trim());
        command.Parameters.AddWithValue("@like", $"%{query.Trim()}%");

        var users = new List<UserModel>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new UserModel
            {
                Id = Convert.ToUInt32(reader["id"]),
                Username = Convert.ToString(reader["username"]) ?? "",
                DisplayName = Convert.ToString(reader["display_name"]) ?? "",
                PasswordHash = Convert.ToString(reader["password_hash"]) ?? "",
                AvatarLocator = reader["avatar_locator"] == DBNull.Value ? null : Convert.ToString(reader["avatar_locator"]),
                BackgroundLocator = reader["background_locator"] == DBNull.Value ? null : Convert.ToString(reader["background_locator"]),
                TokenVersion = Convert.ToUInt32(reader["token_version"]),
                CreatedAt = Convert.ToDateTime(reader["created_at"]),
                UpdatedAt = Convert.ToDateTime(reader["updated_at"]),
                Disabled = reader["disabled"] != DBNull.Value && Convert.ToBoolean(reader["disabled"])
            });
        }

        return users;
    }

    public async Task SetUserBanAsync(StagerConfig config, uint userId, bool disabled)
    {
        using var connection = CreateConnection(config, includeDatabase: true);
        await connection.OpenAsync();
        var procedure = disabled ? "ban_user" : "unban_user";
        using var command = new MySqlCommand(procedure, connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("target", userId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ExecuteScalarLongAsync(StagerConfig config, string sql)
    {
        using var connection = CreateConnection(config, includeDatabase: true);
        await connection.OpenAsync();
        using var command = new MySqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private MySqlConnection CreateConnection(StagerConfig config, bool includeDatabase)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = config.MySqlAddr,
            Port = config.MySqlPort,
            UserID = config.MySqlUser,
            Password = config.MySqlPassword,
            CharacterSet = "utf8mb4",
            AllowUserVariables = true,
            SslMode = MySqlSslMode.Preferred
        };

        if (includeDatabase)
            builder.Database = config.MySqlDatabase;

        return new MySqlConnection(builder.ConnectionString);
    }

    private static string NormalizeSql(string sql, string database)
    {
        return sql
            .Replace("collate utf8mb4_unicode\nuse chatty;", $"collate utf8mb4_unicode_ci;{Environment.NewLine}use `{database}`;")
            .Replace("use chatty;", $"use `{database}`;");
    }
}
