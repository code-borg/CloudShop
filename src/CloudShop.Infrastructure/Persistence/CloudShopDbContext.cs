using CloudShop.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CloudShop.Infrastructure.Persistence;

public class CloudShopDbContext : DbContext
{
    public CloudShopDbContext(
        DbContextOptions<CloudShopDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);
    }
}