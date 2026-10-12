using CloudShop.Domain.Exceptions;
using CloudShop.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudShop.Api.Tests;

public class SqlServerRetryConfigurationTests
{
    // Synthetic strings only: inspecting options never opens a database connection.
    [Theory]
    [InlineData("Development", @"Server=.\SQLEXPRESS;Database=CloudShop;Trusted_Connection=True;TrustServerCertificate=True")]
    [InlineData("Production", "Server=tcp:example.database.windows.net,1433;Database=CloudShop;Authentication=Active Directory Managed Identity;Encrypt=True")]
    public void Runtime_uses_bounded_retries_and_preserves_connection_string(string environment, string connectionString)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("KeyVault:Enabled", "false");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KeyVault:Enabled"] = "false",
                    ["ConnectionStrings:DefaultConnection"] = connectionString
                }));
        });
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CloudShopDbContext>();

        var actualConnectionString = context.Database.GetConnectionString();
        Assert.NotNull(actualConnectionString);
        var expected = new SqlConnectionStringBuilder(connectionString);
        var actual = new SqlConnectionStringBuilder(actualConnectionString);
        // Compare every supported property's parsed value, including defaults:
        // server, database, authentication, encryption, trust, and timeouts.
        foreach (string keyword in expected.Keys)
        {
            // EF Core supplies a diagnostic name when the caller leaves it unset.
            if (keyword == "Application Name" && !expected.ShouldSerialize(keyword))
            {
                Assert.StartsWith("EFCore/", actual.ApplicationName);
                continue;
            }
            Assert.True(Equals(expected[keyword], actual[keyword]),
                $"Connection property '{keyword}' changed.");
        }
        AssertRetryPolicy(context);
    }

    [Fact]
    public void Design_time_uses_the_same_retry_policy_without_a_connection_string()
    {
        using var context = new CloudShopDbContextFactory().CreateDbContext([]);
        Assert.Null(context.Database.GetConnectionString());
        AssertRetryPolicy(context);
    }

    [Fact]
    public void Retry_policy_retries_a_provider_recognized_transient_failure()
    {
        using var context = new CloudShopDbContextFactory().CreateDbContext([]);
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;

        var result = strategy.Execute(() =>
        {
            if (++attempts == 1)
                throw new TimeoutException("Synthetic transient failure.");
            return 42;
        });

        Assert.Equal(42, result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Retry_policy_does_not_retry_domain_validation_failures()
    {
        using var context = new CloudShopDbContextFactory().CreateDbContext([]);
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;

        Assert.Throws<DomainException>(() => strategy.Execute((Action)(() =>
        {
            attempts++;
            throw new DomainException("Product description is required.");
        })));
        Assert.Equal(1, attempts);
    }

    private static void AssertRetryPolicy(CloudShopDbContext context)
    {
        var strategy = Assert.IsType<SqlServerRetryingExecutionStrategy>(context.Database.CreateExecutionStrategy());
        Assert.True(strategy.RetriesOnFailure);
        Assert.Equal(5, strategy.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.MaxRetryDelay);
        Assert.True(strategy.AdditionalErrorNumbers is null || !strategy.AdditionalErrorNumbers.Any());
    }
}
