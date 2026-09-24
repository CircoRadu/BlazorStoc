using MySqlConnector;

namespace BlazorStoc.Services;

internal static class DatabaseConnections
{
    public static MySqlConnection Create(IConfiguration configuration)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = configuration["Database:Host"] ?? "127.0.0.1",
            Port = configuration.GetValue<uint>("Database:Port", 3306),
            Database = configuration["Database:Name"] ?? "BlazorStoc",
            UserID = configuration["Database:User"] ?? "blazorstoc_reader",
            Password = configuration["Database:Password"],
            SslMode = Enum.Parse<MySqlSslMode>(configuration["Database:SslMode"] ?? "Required", true),
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 20,
            MaximumPoolSize = 10,
            MinimumPoolSize = 0,
            ConnectionIdleTimeout = 60,
            AllowLoadLocalInfile = false
        };
        return new MySqlConnection(builder.ConnectionString);
    }
}
