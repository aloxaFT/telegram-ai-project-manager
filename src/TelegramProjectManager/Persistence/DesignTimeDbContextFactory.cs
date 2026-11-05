using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TelegramProjectManager.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProjectDbContext>
{
    public ProjectDbContext CreateDbContext(string[] args)
    {
        // Design-time generation requires no credentials or running database.
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Database") ?? "Host=localhost;Database=telegram_pm_design";
        return new ProjectDbContext(new DbContextOptionsBuilder<ProjectDbContext>().UseNpgsql(connection).Options);
    }
}
