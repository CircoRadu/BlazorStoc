using MySqlConnector;

namespace BlazorStoc.Services;

internal static class DatabaseConnections
{
    public static MySqlConnection Create(IConfiguration configuration)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = configuration["Database:Host"] ?? "127.0.0.1",
            // Implicit 3307: portul instantei MariaDB locale reale (docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md),
            // nu portul implicit 3306 al MariaDB/MySQL.
            Port = configuration.GetValue<uint>("Database:Port", 3307),
            Database = configuration["Database:Name"] ?? "BlazorStoc",
            // Implicit blazorstoc_dev: contul de dezvoltare cu SELECT/INSERT/UPDATE/DELETE pe instanta locala
            // reala (fara drepturi DDL); parola vine exclusiv din configuratia privata locala (Subtask 2.2), nu
            // din acest fisier.
            UserID = configuration["Database:User"] ?? "blazorstoc_dev",
            Password = configuration["Database:Password"],
            SslMode = Enum.Parse<MySqlSslMode>(configuration["Database:SslMode"] ?? "Required", true),
            CharacterSet = configuration["Database:CharSet"] ?? "utf8mb4",
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 20,
            MaximumPoolSize = 10,
            MinimumPoolSize = 0,
            ConnectionIdleTimeout = 60,
            AllowLoadLocalInfile = false
        };
        return new MySqlConnection(builder.ConnectionString);
    }

    // The dedicated schema-migration account (blazorstoc_migrator): DDL on the BlazorStoc schema only, no data rights.
    // Credentials come from Database:MigratorUser / Database:MigratorPassword (private file, never appsettings.json).
    public static MySqlConnection CreateMigrator(IConfiguration configuration)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = configuration["Database:Host"] ?? "127.0.0.1",
            Port = configuration.GetValue<uint>("Database:Port", 3307),
            Database = configuration["Database:Name"] ?? "BlazorStoc",
            UserID = configuration["Database:MigratorUser"] ?? string.Empty,
            Password = configuration["Database:MigratorPassword"],
            SslMode = Enum.Parse<MySqlSslMode>(configuration["Database:SslMode"] ?? "Required", true),
            CharacterSet = configuration["Database:CharSet"] ?? "utf8mb4",
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 120,
            Pooling = false,
            AllowLoadLocalInfile = false
        };
        return new MySqlConnection(builder.ConnectionString);
    }
}
