using CloudShop.Application.Abstractions;
using CloudShop.Domain.Entities;

namespace CloudShop.Application.Products.CreateProduct;

public class CreateProductHandler
{
    private readonly IProductRepository _productRepository;

    public CreateProductHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Product> HandleAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var product = new Product(
            command.Name,
            command.Description,
            command.Price,
            command.StockQuantity);

        await _productRepository.AddAsync(
            product,
            cancellationToken);

        return product;
    }
}