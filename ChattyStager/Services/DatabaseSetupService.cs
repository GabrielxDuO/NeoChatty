namespace ChattyStager.Services;

using ChattyStager.Model;
using MySql.Data.MySqlClient;

public class DatabaseSetupService
{
    public async Task<RuntimeCheckItem> TestConnectionAsync(StagerConfig config)
    {
        try
        {
            using var connection = CreateConnection(config, includeDatabase: false);
            await connection.OpenAsync();
            var serverVersion = connection.ServerVersion;
            return new RuntimeCheckItem(
                "Database connection",
                $"{config.MySqlAddr}:{config.MySqlPort}",
                serverVersion,
                $"{config.MySqlUser}@{config.MySqlAddr}:{config.MySqlPort}",
                true,
                true,
                "Database connection succeeded.");
        }
        catch (Exception ex)
        {
            return new RuntimeCheckItem(
                "Database connection",
                $"{config.MySqlAddr}:{config.MySqlPort}",
                ex.Message,
                $"{config.MySqlUser}@{config.MySqlAddr}:{config.MySqlPort}",
                false,
                false,
                "Database connection failed.");
        }
    }

    public async Task<DeploymentResult> InitializeDatabaseAsync(StagerConfig config, string sqlPath)
    {
        var logs = new List<OperationLogEntry>();
        var steps = new List<DeploymentStepResult>();

        try
        {
            await RunStepAsync(steps, "Open database connection", logs, async () =>
            {
                using var connection = CreateConnection(config, includeDatabase: false);
                await connection.OpenAsync();
            });

            await RunStepAsync(steps, "Execute SQL file", logs, async () =>
            {
                if (!File.Exists(sqlPath))
                    throw new FileNotFoundException("SQL file was not found.", sqlPath);

                using var connection = CreateConnection(config, includeDatabase: false);
                await connection.OpenAsync();
                var scriptText = NormalizeSql(await File.ReadAllTextAsync(sqlPath), config.MySqlDatabase);
                var script = new MySqlScript(connection, scriptText);
                await Task.Run(() => script.Execute());
            });

            await RunStepAsync(steps, "Validate schema", logs, async () =>
            {
                await EnsureSchemaReadyAsync(config);
            });

            return new DeploymentResult(true, "Database initialized and validated.", sqlPath, steps, logs);
        }
        catch (Exception ex)
        {
            logs.Add(new OperationLogEntry(DateTimeOffset.Now, "error", ex.Message));
            return new DeploymentResult(false, ex.Message, sqlPath, steps, logs);
        }
    }

    public async Task EnsureSchemaReadyAsync(StagerConfig config)
    {
        var requiredTables = new[] { "users", "messages" };
        foreach (var table in requiredTables)
        {
            var count = await ExecuteScalarLongAsync(config, """
                                                             select count(*)
                                                             from information_schema.tables
                                                             where table_schema = @database and table_name = @name;
                                                             """, command =>
            {
                command.Parameters.AddWithValue("@database", config.MySqlDatabase);
                command.Parameters.AddWithValue("@name", table);
            });
            if (count != 1)
                throw new InvalidOperationException($"Required table `{table}` does not exist.");
        }

        foreach (var procedure in new[] { "ban_user", "unban_user" })
        {
            var count = await ExecuteScalarLongAsync(config, """
                                                             select count(*)
                                                             from information_schema.routines
                                                             where routine_schema = @database and routine_name = @name and routine_type = 'PROCEDURE';
                                                             """, command =>
            {
                command.Parameters.AddWithValue("@database", config.MySqlDatabase);
                command.Parameters.AddWithValue("@name", procedure);
            });
            if (count != 1)
                throw new InvalidOperationException($"Required procedure `{procedure}` does not exist.");
        }
    }

    public async Task<long> CountUsersAsync(StagerConfig config)
    {
        return await ExecuteScalarLongAsync(config, "select count(*) from users;");
    }

    public async Task<long> CountMessagesLast24HoursAsync(StagerConfig config)
    {
        return await ExecuteScalarLongAsync(config, "select count(*) from messages where created_at >= @since;", command =>
        {
            command.Parameters.AddWithValue("@since", DateTime.UtcNow.AddHours(-24));
        });
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
                Disabled = reader["disabled"] != DBNull.Value && Convert.ToBoolean(reader["disabled"]),
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
            CommandType = System.Data.CommandType.StoredProcedure,
        };
        command.Parameters.AddWithValue("target", userId);
        await command.ExecuteNonQueryAsync();

        var state = await ExecuteScalarLongAsync(config, "select disabled from users where id = @id;", cmd =>
        {
            cmd.Parameters.AddWithValue("@id", userId);
        });
        var expected = disabled ? 1 : 0;
        if (state != expected)
            throw new InvalidOperationException($"Procedure `{procedure}` did not update user {userId}.");
    }

    public MySqlConnection CreateConnection(StagerConfig config, bool includeDatabase)
    {
        if (config.MySqlPort is <= 0 or > 65535)
            throw new InvalidOperationException("Database port must be between 1 and 65535.");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = config.MySqlAddr,
            Port = (uint)config.MySqlPort,
            UserID = config.MySqlUser,
            Password = config.MySqlPassword,
            CharacterSet = "utf8mb4",
            AllowUserVariables = true,
            AllowLoadLocalInfile = true,
            SslMode = MySqlSslMode.Preferred,
        };

        if (includeDatabase)
            builder.Database = config.MySqlDatabase;

        return new MySqlConnection(builder.ConnectionString);
    }

    private async Task<long> ExecuteScalarLongAsync(StagerConfig config, string sql, Action<MySqlCommand>? configure = null)
    {
        using var connection = CreateConnection(config, includeDatabase: true);
        await connection.OpenAsync();
        using var command = new MySqlCommand(sql, connection);
        configure?.Invoke(command);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private static async Task RunStepAsync(
        List<DeploymentStepResult> steps,
        string name,
        List<OperationLogEntry> logs,
        Func<Task> action)
    {
        logs.Add(new OperationLogEntry(DateTimeOffset.Now, "info", name));
        try
        {
            await action();
            steps.Add(new DeploymentStepResult(name, OperationState.Success, "Done"));
        }
        catch (Exception ex)
        {
            steps.Add(new DeploymentStepResult(name, OperationState.Failed, ex.Message));
            throw;
        }
    }

    private static string NormalizeSql(string sql, string database)
    {
        return sql
            .Replace("collate utf8mb4_unicode\nuse chatty;", $"collate utf8mb4_unicode_ci;{Environment.NewLine}use `{database}`;")
            .Replace("use chatty;", $"use `{database}`;");
    }
}
