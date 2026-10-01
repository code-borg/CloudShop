using CloudShop.Application.Abstractions;
using CloudShop.Domain.Entities;

namespace CloudShop.Application.Products.GetProductById;

public class GetProductByIdHandler
{
    private readonly IProductRepository _productRepository;

    public GetProductByIdHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Product?> HandleAsync(
        GetProductByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await _productRepository.GetByIdAsync(
            query.Id,
            cancellationToken);
    }
}