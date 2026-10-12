using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CloudShop.Infrastructure.Persistence;

public class CloudShopDbContextFactory : IDesignTimeDbContextFactory<CloudShopDbContext>
{
    public CloudShopDbContext CreateDbContext(string[] args)
    {
        // SQL generation needs the provider, but does not need a database connection.
        var options = new DbContextOptionsBuilder<CloudShopDbContext>()
            .UseSqlServer(SqlServerRetryPolicy.Configure)
            .Options;

        return new CloudShopDbContext(options);
    }
}
