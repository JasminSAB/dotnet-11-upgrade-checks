using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotnetUpgradeChecks;

// One documented .NET 11 breaking change: what the docs say .NET 10 does, what .NET 11 does,
// and a function that observes it on the runtime this process runs on.
public sealed record Check(string Name, string Net10, string Net11, string Docs, Func<Task<string>> Observe);

public static class Checks
{
    const string Compat = "https://learn.microsoft.com/en-us/dotnet/core/compatibility/";

    public static IReadOnlyList<Check> All { get; } =
    [
        new("Math.Round(655.925, 2, AwayFromZero)", "655.93", "655.92",
            Compat + "core-libraries/11/math-round-digits",
            () => Task.FromResult(Math.Round(655.925, 2, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture))),

        new("Math.Round(1.5, 16)", "ArgumentOutOfRangeException", "1.5",
            Compat + "core-libraries/11/math-round-digits",
            () => Task.FromResult(Try(() => Math.Round(1.5, 16).ToString(CultureInfo.InvariantCulture)))),

        new("unchecked((short)65578.0)", "42", "32767",
            Compat + "jit/11/fp-to-small-integer",
            () => Task.FromResult(ToShort(65578.0).ToString(CultureInfo.InvariantCulture))),

        new("A BackgroundService throws: await host.RunAsync()", "completes, exit code 0", "throws InvalidOperationException",
            Compat + "extensions/11/ihost-runasync-stopasync-throw-backgroundservice-failure",
            HostWithFailingWorker),

        new("Two scoped services throw in Dispose()", "InvalidOperationException, disposed: Second", "AggregateException (2), disposed: Second, First",
            Compat + "extensions/11/dependency-injection-disposal-exceptions",
            () => Task.FromResult(DisposeScope())),

        new("Chained configuration sets Endpoint to \"\"", "'https://example.com'", "''",
            Compat + "extensions/11/chained-configuration-empty-string",
            () => Task.FromResult(ChainedEmptyString())),

        new("Read a ZIP entry with one changed byte", "reads 'Total: 900.00 EUR'", "InvalidDataException",
            Compat + "core-libraries/11/ziparchive-entry-crc32-validation",
            () => Task.FromResult(ReadTamperedZip())),

        new("DateOnly.TryParseExact with an invalid DateTimeStyles", "False", "ArgumentException",
            Compat + "core-libraries/11/dateonly-timeonly-tryparse-argumentexception",
            () => Task.FromResult(Try(() => DateOnly.TryParseExact("2023-10-15", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, (DateTimeStyles)999, out _).ToString()))),

        new("EF Core SQL for Math.Max on SQL Server", "SELECT both columns, Math.Max runs in .NET", "SELECT GREATEST(...), needs SQL Server 2022",
            "https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/breaking-changes#sqlserver-compatibility-level-160",
            () => Task.FromResult(MathMaxSql())),
    ];

    static string Try(Func<string> action)
    {
        try { return action(); }
        catch (Exception e) { return e.GetType().Name; }
    }

    // NoInlining keeps the value a runtime value, so the JIT performs the conversion
    // instead of the compiler folding a constant.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static short ToShort(double value) => unchecked((short)value);

    sealed class FailingWorker : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("the payment queue is unreachable");
        }
    }

    public static async Task RunHostWithFailingWorker()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddHostedService<FailingWorker>();
        using var host = builder.Build();
        await host.RunAsync();
    }

    static async Task<string> HostWithFailingWorker()
    {
        try
        {
            await RunHostWithFailingWorker();
            return "completes, exit code 0";
        }
        catch (Exception e)
        {
            return $"throws {e.GetType().Name}";
        }
    }

    sealed class Disposals
    {
        public List<string> Order { get; } = [];
    }

    sealed class First(Disposals log) : IDisposable
    {
        public void Dispose() { log.Order.Add("First"); throw new InvalidOperationException("First"); }
    }

    sealed class Second(Disposals log) : IDisposable
    {
        public void Dispose() { log.Order.Add("Second"); throw new InvalidOperationException("Second"); }
    }

    static string DisposeScope()
    {
        var log = new Disposals();
        var services = new ServiceCollection()
            .AddSingleton(log)
            .AddScoped<First>()
            .AddScoped<Second>()
            .BuildServiceProvider();

        var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<First>();
        scope.ServiceProvider.GetRequiredService<Second>();
        try
        {
            scope.Dispose();
            return "no exception";
        }
        catch (AggregateException e)
        {
            return $"AggregateException ({e.InnerExceptions.Count}), disposed: {string.Join(", ", log.Order)}";
        }
        catch (Exception e)
        {
            return $"{e.GetType().Name}, disposed: {string.Join(", ", log.Order)}";
        }
    }

    // The example from the Microsoft Learn page.
    static string ChainedEmptyString()
    {
        IConfiguration inner = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Endpoint"] = "" })
            .Build();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Endpoint"] = "https://example.com" })
            .AddConfiguration(inner)
            .Build();

        return $"'{configuration["Endpoint"]}'";
    }

    static string ReadTamperedZip()
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("invoice.txt", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("Total: 100.00 EUR");
        }

        // Change one byte of the stored content: 100.00 becomes 900.00. The CRC32 in the
        // archive still describes the original content.
        var bytes = zip.ToArray();
        int at = IndexOf(bytes, Encoding.ASCII.GetBytes("Total: 100.00"));
        bytes[at + "Total: ".Length] = (byte)'9';

        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            using var reader = new StreamReader(archive.GetEntry("invoice.txt")!.Open());
            return $"reads '{reader.ReadToEnd()}'";
        }
        catch (InvalidDataException)
        {
            return "InvalidDataException";
        }
    }

    static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        throw new InvalidOperationException("content not found in the archive");
    }

    public sealed class Product
    {
        public int Id { get; set; }
        public decimal Price { get; set; }
        public decimal MinimumPrice { get; set; }
    }

    sealed class ShopContext : DbContext
    {
        public DbSet<Product> Products => Set<Product>();

        // No database is needed: ToQueryString only generates the SQL.
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseSqlServer("Server=.;Database=Shop;Trusted_Connection=True");
    }

    public static string LastSql { get; private set; } = "";

    static string MathMaxSql()
    {
        using var db = new ShopContext();
        LastSql = db.Products.Select(p => Math.Max(p.Price, p.MinimumPrice)).ToQueryString();
        return LastSql.Contains("GREATEST(") ? "SELECT GREATEST(...), needs SQL Server 2022"
            : LastSql.StartsWith("SELECT [p].[Price], [p].[MinimumPrice]") ? "SELECT both columns, Math.Max runs in .NET"
            : LastSql;
    }
}
