using Npgsql;

namespace TelegramProjectManager.Configuration;

public static class DatabaseConfiguration
{
    public static string GetConnectionString(IConfiguration config)
    {
        var connection = config.GetConnectionString("Database");
        if (!string.IsNullOrWhiteSpace(connection)) return new NpgsqlConnectionStringBuilder(connection).ConnectionString;
        // Render supplies a postgres:// URL. Convert without ever logging it.
        var raw = config["DATABASE_URL"];
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme is not ("postgres" or "postgresql") ||
            string.IsNullOrEmpty(uri.Host) || string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath.Length < 2)
            throw new InvalidOperationException("Set ConnectionStrings__Database or DATABASE_URL securely.");
        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2) throw new InvalidOperationException("DATABASE_URL requires username and password.");
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host, Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath[1..]),
            Username = Uri.UnescapeDataString(credentials[0]), Password = Uri.UnescapeDataString(credentials[1]),
            SslMode = SslMode.VerifyFull
        };
        if (!string.IsNullOrEmpty(uri.Query))
        {
            // Accept only transport parameters that preserve certificate verification.
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2 || parts[0] != "sslmode" || parts[1] is not ("verify-full" or "verify-ca"))
                    throw new InvalidOperationException("DATABASE_URL supports only sslmode=verify-full or verify-ca; use an explicit Npgsql connection string for other settings.");
                builder.SslMode = parts[1] == "verify-ca" ? SslMode.VerifyCA : SslMode.VerifyFull;
            }
        }
        return builder.ConnectionString;
    }
}
