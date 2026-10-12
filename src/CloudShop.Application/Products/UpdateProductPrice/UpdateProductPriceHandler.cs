using System;
using System.Collections.Generic;
using System.Text;
using CloudShop.Application.Abstractions;
using CloudShop.Domain.Entities;


namespace CloudShop.Application.Products.UpdateProductPrice
{
    public class UpdateProductPriceHandler
    {
        private readonly IProductRepository _productRepository;

        public UpdateProductPriceHandler(
            IProductRepository productRepository)
        {
            _productRepository = productRepository;
        }

        public async Task<Product?> HandleAsync(
            UpdateProductPriceCommand command,
            CancellationToken cancellationToken = default)
        {
            var product = await _productRepository.GetByIdForUpdateAsync(
                command.ProductId,
                cancellationToken);

            if (product is null)
            {
                return null;
            }

            product.ChangePrice(command.NewPrice);

            await _productRepository.SaveChangesAsync(
                cancellationToken);

            return product;
        }
    }
}
