using CloudShop.Api.Contracts;
using CloudShop.Application.Abstractions;
using CloudShop.Application.Products.CreateProduct;
using CloudShop.Application.Products.GetProductById;
using CloudShop.Application.Products.GetProducts;
using CloudShop.Domain.Entities;
using CloudShop.Infrastructure.Persistence;
using CloudShop.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

builder.Services.AddDbContext<CloudShopDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<GetProductsHandler>();
builder.Services.AddScoped<GetProductByIdHandler>();
builder.Services.AddScoped<CreateProductHandler>();

// Build AFTER registering services
var app = builder.Build();

app.MapPost("/products", async (
    CreateProductRequest request,
    CreateProductHandler handler) =>
{
    var command = new CreateProductCommand(
        request.Name,
        request.Description,
        request.Price,
        request.StockQuantity);

    var product = await handler.HandleAsync(command);

    return Results.Created(
        $"/products/{product.Id}",
        product);
});

app.MapGet("/products/{id:guid}", async (
    Guid id,
    GetProductByIdHandler handler) =>
{
    var query = new GetProductByIdQuery(id);

    var product = await handler.HandleAsync(query);

    if (product is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(product);
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF =>
        32 + (int)(TemperatureC / 0.5556);
}