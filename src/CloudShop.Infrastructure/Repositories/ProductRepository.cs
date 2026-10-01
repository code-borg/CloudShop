using CloudShop.Application.Abstractions;
using CloudShop.Domain.Entities;
using CloudShop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace CloudShop.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly CloudShopDbContext _dbContext;

    public ProductRepository(CloudShopDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        Product product,
        CancellationToken cancellationToken = default)
    {
        _dbContext.Products.Add(product);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<Product>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetByIdAsync(
    Guid id,
    CancellationToken cancellationToken = default)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(
                product => product.Id == id,
                cancellationToken);
    }
}
