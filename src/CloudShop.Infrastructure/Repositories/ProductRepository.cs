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
        return await SqlServerRetryPolicy.CreateReadStrategy(_dbContext).ExecuteAsync(
            token => _dbContext.Products
                .AsNoTracking()
                .ToListAsync(token),
            cancellationToken);
    }

    public async Task<Product?> GetByIdAsync(
    Guid id,
    CancellationToken cancellationToken = default)
    {
        return await SqlServerRetryPolicy.CreateReadStrategy(_dbContext).ExecuteAsync(
            token => _dbContext.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(product => product.Id == id, token),
            cancellationToken);
    }

    public async Task<Product?> GetByIdForUpdateAsync(
    Guid id,
    CancellationToken cancellationToken = default)
    {
        return await _dbContext.Products
            .FirstOrDefaultAsync(
                product => product.Id == id,
                cancellationToken);
    }

    public async Task SaveChangesAsync(
    CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
