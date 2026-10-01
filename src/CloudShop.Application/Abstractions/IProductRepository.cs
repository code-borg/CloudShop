using CloudShop.Domain.Entities;

namespace CloudShop.Application.Abstractions;

public interface IProductRepository
{
    Task AddAsync(
        Product product,
        CancellationToken cancellationToken = default);

    Task<List<Product>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<Product?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}