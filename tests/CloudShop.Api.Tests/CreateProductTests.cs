using System.Net;
using System.Text;
using System.Text.Json;
using CloudShop.Application.Abstractions;
using CloudShop.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CloudShop.Api.Tests;

public class CreateProductTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Missing_description_returns_400_in_deployment_environments(string environment)
    {
        using var factory = new ProductApiFactory(environment);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsync("/products", new StringContent("{\"name\":\"Laptop\",\"price\":49999}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Product description is required.", body.RootElement.GetProperty("error").GetString());
        Assert.Empty(factory.Repository.Products);
    }

    [Theory]
    [InlineData("{\"price\":49999,\"description\":\"Laptop\"}", "Product name is required.")]
    [InlineData("{\"name\":null,\"description\":\"Laptop\"}", "Product name is required.")]
    [InlineData("{\"name\":\"\",\"description\":\"Laptop\"}", "Product name is required.")]
    [InlineData("{\"name\":\"  \",\"description\":\"Laptop\"}", "Product name is required.")]
    [InlineData("{\"name\":\"Test Laptop\",\"price\":49999}", "Product description is required.")]
    [InlineData("{\"name\":\"Laptop\",\"description\":null}", "Product description is required.")]
    [InlineData("{\"name\":\"Laptop\",\"description\":\"\"}", "Product description is required.")]
    [InlineData("{\"name\":\"Laptop\",\"description\":\"  \"}", "Product description is required.")]
    [InlineData("{\"name\":\"Laptop\",\"description\":\"Testing\",\"price\":-1}", "Price cannot be negative.")]
    [InlineData("{\"name\":\"Laptop\",\"description\":\"Testing\",\"stockQuantity\":-1}", "Stock quantity cannot be negative.")]
    public async Task Invalid_input_returns_400_without_persisting(string json, string message)
    {
        using var factory = new ProductApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsync("/products", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(message, body.RootElement.GetProperty("error").GetString());
        Assert.Empty(factory.Repository.Products);
    }

    [Theory]
    [InlineData("{\"name\":\"Test Laptop\",\"description\":\"Laptop for development and testing\",\"price\":49999}", 49999, 0)]
    [InlineData("{\"name\":\"Test Laptop\",\"description\":\"Laptop for development and testing\",\"price\":0,\"stockQuantity\":0}", 0, 0)]
    [InlineData("{\"name\":\"Test Laptop\",\"description\":\"Laptop for development and testing\",\"price\":49999,\"stockQuantity\":5}", 49999, 5)]
    public async Task Valid_input_returns_201_and_persists(string json, int price, int stock)
    {
        using var factory = new ProductApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsync("/products", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = Assert.Single(factory.Repository.Products);
        Assert.Equal("Test Laptop", product.Name);
        Assert.Equal("Laptop for development and testing", product.Description);
        Assert.Equal((decimal)price, product.Price);
        Assert.Equal(stock, product.StockQuantity);
        Assert.Equal($"/products/{product.Id}", response.Headers.Location?.OriginalString);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(product.Id, body.RootElement.GetProperty("id").GetGuid());
    }

    private sealed class ProductApiFactory(string environment = "Testing") : WebApplicationFactory<Program>
    {
        public FakeProductRepository Repository { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("KeyVault:Enabled", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProductRepository>();
                services.AddSingleton<IProductRepository>(Repository);
            });
        }
    }

    private sealed class FakeProductRepository : IProductRepository
    {
        public List<Product> Products { get; } = [];
        public Task AddAsync(Product product, CancellationToken cancellationToken = default)
        {
            Products.Add(product);
            return Task.CompletedTask;
        }
        public Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(Products);
        public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Products.Find(p => p.Id == id));
        public Task<Product?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) => GetByIdAsync(id, cancellationToken);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
