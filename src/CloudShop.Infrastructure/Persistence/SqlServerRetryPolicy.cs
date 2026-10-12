using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CloudShop.Infrastructure.Persistence;

public static class SqlServerRetryPolicy
{
    private const int MaxRetryCount = 5;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

    public static void Configure(SqlServerDbContextOptionsBuilder options)
    {
        // Keep SQL timeout (-2) out of the shared policy: writes may have committed.
        options.EnableRetryOnFailure(
            maxRetryCount: MaxRetryCount,
            maxRetryDelay: MaxRetryDelay,
            errorNumbersToAdd: null);
    }

    // Only use for side-effect-free reads. Never wrap SaveChanges or a write handler.
    public static SqlServerRetryingExecutionStrategy CreateReadStrategy(CloudShopDbContext context)
        => new(context, MaxRetryCount, MaxRetryDelay, errorNumbersToAdd: [-2]);
}
