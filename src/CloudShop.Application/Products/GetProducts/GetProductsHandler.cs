using CloudShop.Application.Abstractions;
using CloudShop.Domain.Entities;

namespace CloudShop.Application.Products.GetProducts;

public class GetProductsHandler
{
    private readonly IProductRepository _productRepository;

    public GetProductsHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<List<Product>> HandleAsync(
        GetProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await _productRepository.GetAllAsync(cancellationToken);
    }
}