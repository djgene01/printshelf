using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace PrintShelf.Web.Data;

/// <summary>
/// Used by `dotnet ef` only. The app itself registers <see cref="IDbContextFactory{AppDbContext}"/>.
/// Resolution order: PRINTSHELF_CONN, the project's appsettings, then the local dev container.
/// </summary>
public sealed class PrintShelfDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public const string DevConnection =
        "Host=127.0.0.1;Port=5434;Database=printshelf;Username=printshelf;Password=printshelf_dev_pw";

    public AppDbContext CreateDbContext(string[] args)
    {
        string? connectionString = Environment.GetEnvironmentVariable("PRINTSHELF_CONN");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            foreach (string basePath in CandidatePaths())
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(basePath)
                    .AddJsonFile("appsettings.json", optional: true)
                    .AddJsonFile("appsettings.Development.json", optional: true)
                    .Build();
                connectionString = config.GetConnectionString("PrintShelf");
                if (!string.IsNullOrWhiteSpace(connectionString))
                    break;
            }
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString ?? DevConnection)
            .Options;
        return new AppDbContext(options);
    }

    private static IEnumerable<string> CandidatePaths()
    {
        yield return Directory.GetCurrentDirectory();
        // bin/Debug/net10.0 -> project folder
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
