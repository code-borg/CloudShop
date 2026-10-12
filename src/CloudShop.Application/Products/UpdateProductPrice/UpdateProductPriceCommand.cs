using System;
using System.Collections.Generic;
using System.Text;

namespace CloudShop.Application.Products.UpdateProductPrice
{

    public record UpdateProductPriceCommand(
        Guid ProductId,
        decimal NewPrice);
}
