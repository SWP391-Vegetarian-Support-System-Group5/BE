using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySqlConnector;

namespace API.Infrastructure;

public static class DatabaseConnectivity
{
    public static string ConfigureConnection(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        // Rotate pooled connections and detect idle transport failures on cloud MySQL.
        // Explicit deployment settings, TLS and credentials are preserved.
        if (!builder.ContainsKey("Connection Lifetime")) builder.ConnectionLifeTime = 300;
        if (!builder.ContainsKey("Connection Idle Timeout")) builder.ConnectionIdleTimeout = 60;
        if (!builder.ContainsKey("Keepalive")) builder.Keepalive = 30;
        return builder.ConnectionString;
    }

    public static bool IsTransientFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { IsTransient: true }) return true;
            // Connector 2.3.x doesn't classify command timeouts as transient.
            if (current is MySqlException mysql && (mysql.ErrorCode == MySqlErrorCode.CommandTimeoutExpired
                || mysql.Number is 1053 or 2006 or 2013
                || mysql.Number == 0 && HasTransportFailure(mysql))) return true;
        }
        return false;
    }

    private static bool HasTransportFailure(Exception error)
    {
        for (var inner = error.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is IOException or System.Net.Sockets.SocketException or TimeoutException) return true;
        return false;
    }
}

// Retry only opening a connection, before any SQL or transaction has executed.
// Global EnableRetryOnFailure would require replaying every manual transaction.
public sealed class DatabaseConnectionInterceptor(ILogger<DatabaseConnectionInterceptor> logger) : DbConnectionInterceptor
{
    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
        ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (result.IsSuppressed) return result;
        try { await connection.OpenAsync(cancellationToken); }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && DatabaseConnectivity.IsTransientFailure(error))
        {
            logger.LogWarning("Opening the database connection failed temporarily; retrying once before executing SQL.");
            await connection.CloseAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            await connection.OpenAsync(cancellationToken);
        }
        return InterceptionResult.Suppress();
    }

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (result.IsSuppressed) return result;
        try { connection.Open(); }
        catch (Exception error) when (DatabaseConnectivity.IsTransientFailure(error))
        {
            logger.LogWarning("Opening the database connection failed temporarily; retrying once before executing SQL.");
            connection.Close();
            Thread.Sleep(300);
            connection.Open();
        }
        return InterceptionResult.Suppress();
    }
}
