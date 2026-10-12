using CloudShop.Api.Contracts;
using CloudShop.Api.Configuration;
using CloudShop.Api.ExceptionHandling;
using CloudShop.Application.Abstractions;
using CloudShop.Application.Products.CreateProduct;
using CloudShop.Application.Products.GetProductById;
using CloudShop.Application.Products.GetProducts;
using CloudShop.Application.Products.UpdateProductPrice;
using CloudShop.Domain.Entities;
using CloudShop.Infrastructure.Persistence;
using CloudShop.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddCloudShopKeyVault();

// Add services to the container.
builder.Services.AddOpenApi();

builder.Services.AddDbContext<CloudShopDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        SqlServerRetryPolicy.Configure));

builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<GetProductsHandler>();
builder.Services.AddScoped<GetProductByIdHandler>();
builder.Services.AddScoped<CreateProductHandler>();
builder.Services.AddScoped<UpdateProductPriceHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Build AFTER registering services
var app = builder.Build();

app.MapGet("/configuration/welcome", (IConfiguration configuration) =>
    Results.Ok(new { welcomeMessage = configuration["CloudShop:WelcomeMessage"] }));

app.MapGet("/products", async (GetProductsHandler handler) =>
    Results.Ok(await handler.HandleAsync(new GetProductsQuery())));

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

app.MapPut("/products/{id:guid}/price", async (
    Guid id,
    UpdateProductPriceRequest request,
    UpdateProductPriceHandler handler) =>
{
    var command = new UpdateProductPriceCommand(
        id,
        request.NewPrice);

    var product = await handler.HandleAsync(command);

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
app.UseExceptionHandler();
app.Run();

// Expose the entry point to the API integration tests.
public partial class Program { }
