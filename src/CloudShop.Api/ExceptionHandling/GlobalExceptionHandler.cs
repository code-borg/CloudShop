using CloudShop.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace CloudShop.Api.ExceptionHandling
{
    public class GlobalExceptionHandler : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            if (exception is DomainException)
            {
                httpContext.Response.StatusCode =
                    StatusCodes.Status400BadRequest;

                await httpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        error = exception.Message
                    },
                    cancellationToken);

                return true;
            }

            return false;
        }
    }
}
