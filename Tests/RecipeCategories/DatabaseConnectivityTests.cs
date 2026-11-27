using System.Data;
using System.Data.Common;
using System.Diagnostics;
using API.Infrastructure;
using DAL.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Hosting;
using MySqlConnector;

static class DatabaseConnectivityTests
{
    public static async Task RunAsync(bool live)
    {
        var interceptor = new DatabaseConnectionInterceptor(NullLogger<DatabaseConnectionInterceptor>.Instance);
        var connection = new TestConnection(1);
        var opened = await interceptor.ConnectionOpeningAsync(connection, null!, default);
        Require(opened.IsSuppressed && connection.Opens == 2 && connection.State == ConnectionState.Open, "Transient open recovers once");
        connection = new TestConnection(3);
        try { await interceptor.ConnectionOpeningAsync(connection, null!, default); throw new Exception("Expected bounded failure"); }
        catch (TestDatabaseException) { Require(connection.Opens == 2, "Retry bounded to one extra open"); }
        connection = new TestConnection(1, transient: false);
        try { await interceptor.ConnectionOpeningAsync(connection, null!, default); throw new Exception("Expected permanent failure"); }
        catch (TestDatabaseException) { Require(connection.Opens == 1, "Permanent errors are not retried"); }
        connection = new TestConnection(1);
        Require(interceptor.ConnectionOpening(connection, null!, default).IsSuppressed && connection.Opens == 2, "Synchronous connection recovers");
        connection = new TestConnection(0);
        await interceptor.ConnectionOpeningAsync(connection, null!, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult.Suppress());
        Require(connection.Opens == 0, "Earlier interceptor suppression respected");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await interceptor.ConnectionOpeningAsync(new TestConnection(1), null!, default, cancelled.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { Console.WriteLine("PASS cancellation respected"); }
        }
        var transportError = MySqlError((MySqlErrorCode)0, new IOException("Private transport details"));
        Require(DatabaseConnectivity.IsTransientFailure(new InvalidOperationException("EF wrapper", transportError)), "Exact wrapped transport failure classified");
        Require(DatabaseConnectivity.IsTransientFailure(MySqlError(MySqlErrorCode.CommandTimeoutExpired)), "Connector command timeout classified");
        Require(!DatabaseConnectivity.IsTransientFailure(MySqlError((MySqlErrorCode)1045)), "Invalid credentials not treated as transient");
        Require(!DatabaseConnectivity.IsTransientFailure(MySqlError((MySqlErrorCode)1064)), "SQL errors not hidden as outages");
        var configured = new MySqlConnectionStringBuilder(DatabaseConnectivity.ConfigureConnection("Server=localhost;User ID=test;Password=test;SslMode=VerifyFull"));
        Require(configured.Keepalive == 30 && configured.ConnectionLifeTime == 300 && configured.ConnectionIdleTimeout == 60 && configured.SslMode == MySqlSslMode.VerifyFull, "Cloud pool defaults preserve TLS");
        var explicitOptions = new MySqlConnectionStringBuilder(DatabaseConnectivity.ConfigureConnection("Server=localhost;Keep Alive=7;ConnectionLifeTime=9;ConnectionIdleTimeout=11"));
        Require(explicitOptions.Keepalive == 7 && explicitOptions.ConnectionLifeTime == 9 && explicitOptions.ConnectionIdleTimeout == 11, "Explicit pool options respected");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ApiExceptionMiddleware(_ => throw new InvalidOperationException("wrapper", transportError), NullLogger<ApiExceptionMiddleware>.Instance, new TestEnvironment());
        await middleware.Invoke(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Require(context.Response.StatusCode == 503 && context.Response.Headers.RetryAfter == "5" && !body.Contains("Private transport details"), "Outage returns 503 without connection details");
        Console.WriteLine("PASS database recovery and error response checks; no writes replayed.");
        if (live) await VerifyLiveAsync(interceptor);
    }

    private static async Task VerifyLiveAsync(DatabaseConnectionInterceptor interceptor)
    {
        var builder = new ConfigurationBuilder();
        DotEnvConfiguration.AddFile(builder, "API/.env");
        DotEnvConfiguration.AddFile(builder, "API/.aiven.env");
        builder.AddEnvironmentVariables();
        var config = builder.Build();
        var raw = config.GetConnectionString("VegetarianSupportDatabase") ?? throw new Exception("Database configuration missing");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var options = new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(DatabaseConnectivity.ConfigureConnection(raw), new MySqlServerVersion(new Version(8, 0, 0)))
            .AddInterceptors(interceptor).Options;
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 3; i++)
        {
            await using var db = new VegetarianDbContext(options);
            await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
            Require(await db.Database.SqlQueryRaw<int>("SELECT 1 AS `Value`").SingleAsync(timeout.Token) == 1, $"Live SELECT 1 and manual transaction round {i + 1}");
            await transaction.RollbackAsync(timeout.Token);
        }
        Console.WriteLine($"PASS real configured MySQL connection/pool reuse ({timer.ElapsedMilliseconds} ms); SELECT only, no schema/data changes.");
    }

    private static MySqlException MySqlError(MySqlErrorCode code, Exception? inner = null) =>
        (MySqlException)typeof(MySqlException).GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null, new[] { typeof(MySqlErrorCode), typeof(string), typeof(Exception) }, null)!
            .Invoke(new object?[] { code, "Private database details", inner });
    private static void Require(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine($"PASS {label}"); }

    sealed class TestDatabaseException(bool transient) : DbException("Test failure") { public override bool IsTransient => transient; }
    sealed class TestConnection(int failures, bool transient = true) : DbConnection
    {
        public int Opens { get; private set; }
        private ConnectionState state;
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "8.0";
        public override ConnectionState State => state;
        public override void Open() { Opens++; if (Opens <= failures) throw new TestDatabaseException(transient); state = ConnectionState.Open; }
        public override Task OpenAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Open(); return Task.CompletedTask; }
        public override void Close() => state = ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
    sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Test";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
