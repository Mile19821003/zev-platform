using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Zev.Api.Data;

public sealed class ZevDbContextFactory : IDesignTimeDbContextFactory<ZevDbContext>
{
    public ZevDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost;Database=ZevDesign;Integrated Security=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(connectionString).Options;
        return new ZevDbContext(options, new HttpContextAccessor());
    }
}
