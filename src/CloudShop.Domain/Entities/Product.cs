namespace CloudShop.Domain.Entities;

public class Product
{
    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public decimal Price { get; private set; }

    public int StockQuantity { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public Product(
        string name,
        string description,
        decimal price,
        int stockQuantity)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name is required.");

        if (price < 0)
            throw new ArgumentException("Price cannot be negative.");

        if (stockQuantity < 0)
            throw new ArgumentException("Stock quantity cannot be negative.");

        Id = Guid.NewGuid();
        Name = name;
        Description = description;
        Price = price;
        StockQuantity = stockQuantity;
        CreatedAt = DateTime.UtcNow;
    }
}