namespace CloudShop.Api.Contracts;

public record CreateProductRequest(
    string Name,
    string Description,
    decimal Price,
    int StockQuantity);