using System.Data.Common;
using System.Reflection;
using CloudShop.Domain.Entities;
using CloudShop.Infrastructure.Persistence;
using CloudShop.Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace CloudShop.Api.Tests;

public class SqlTimeoutRetryTests
{
    [Theory]
    [InlineData("GetAllAsync", true)]
    [InlineData("GetByIdAsync", true)]
    [InlineData("GetByIdForUpdateAsync", false)]
    [InlineData("AddAsync", false)]
    [InlineData("SaveChangesAsync", false)]
    public async Task Only_side_effect_free_repository_reads_replay_SQL_timeout(string operation, bool shouldRetry)
    {
        var interceptor = new TimeoutBeforeOpenInterceptor();
        var options = new DbContextOptionsBuilder<CloudShopDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=SyntheticOnly;Integrated Security=True", SqlServerRetryPolicy.Configure)
            .AddInterceptors(interceptor)
            .Options;
        using var context = new CloudShopDbContext(options);
        var repository = new ProductRepository(context);
        var product = new Product("Test", "Test description", 1, 1);

        // The interceptor throws before SqlClient opens a connection on every attempt.
        async Task InvokeRepository()
        {
            switch (operation)
            {
                case "GetAllAsync": await repository.GetAllAsync(); break;
                case "GetByIdAsync": await repository.GetByIdAsync(Guid.NewGuid()); break;
                case "GetByIdForUpdateAsync": await repository.GetByIdForUpdateAsync(Guid.NewGuid()); break;
                case "AddAsync": await repository.AddAsync(product); break;
                case "SaveChangesAsync":
                    context.Products.Add(product);
                    await repository.SaveChangesAsync();
                    break;
                default: throw new ArgumentException("Unknown test operation.");
            }
        }

        var thrown = await Record.ExceptionAsync(InvokeRepository);
        if (shouldRetry)
        {
            Assert.Same(interceptor.StopException, thrown);
            Assert.Equal(2, interceptor.Attempts);
        }
        else
        {
            // SaveChanges wraps provider exceptions in DbUpdateException.
            Assert.True(ReferenceEquals(interceptor.Timeout, thrown)
                || ReferenceEquals(interceptor.Timeout, thrown?.InnerException));
            Assert.Equal(1, interceptor.Attempts);
        }
    }

    [Fact]
    public async Task Shared_strategy_does_not_replay_a_SQL_timeout()
    {
        using var context = new CloudShopDbContextFactory().CreateDbContext([]);
        var strategy = context.Database.CreateExecutionStrategy();
        var exception = CreateSqlException(-2);
        var attempts = 0;

        var thrown = await Assert.ThrowsAsync<SqlException>(() => strategy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromException<int>(exception);
        }));

        Assert.Same(exception, thrown);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Read_strategy_retries_SQL_timeout_and_succeeds()
    {
        using var context = new CloudShopDbContextFactory().CreateDbContext([]);
        var strategy = SqlServerRetryPolicy.CreateReadStrategy(context);
        Assert.Equal(5, strategy.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.MaxRetryDelay);
        Assert.Equal(new[] { -2 }, strategy.AdditionalErrorNumbers);
        var attempts = 0;

        var result = await strategy.ExecuteAsync(() => ++attempts == 1
            ? Task.FromException<int>(CreateSqlException(-2))
            : Task.FromResult(42));

        Assert.Equal(42, result);
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(515)] // NOT NULL violation
    [InlineData(2627)] // Duplicate key: could indicate an uncertain earlier write
    [InlineData(18456)] // Authentication failure
    public async Task Read_strategy_does_not_retry_non_transient_SQL_errors(int errorNumber)
    {
        using var context = new CloudShopDbContextFactory().CreateDbContext([]);
        var strategy = SqlServerRetryPolicy.CreateReadStrategy(context);
        var exception = CreateSqlException(errorNumber);
        var attempts = 0;

        var thrown = await Assert.ThrowsAsync<SqlException>(() => strategy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromException<int>(exception);
        }));

        Assert.Same(exception, thrown);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Read_strategy_honors_cancellation_before_replay()
    {
        using var context = new CloudShopDbContextFactory().CreateDbContext([]);
        var strategy = SqlServerRetryPolicy.CreateReadStrategy(context);
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => strategy.ExecuteAsync(token =>
        {
            attempts++;
            cancellation.Cancel();
            return Task.FromException<int>(CreateSqlException(-2));
        }, cancellation.Token));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Read_strategy_stops_after_five_retries_with_original_SQL_timeout()
    {
        using var context = new CloudShopDbContextFactory().CreateDbContext([]);
        // Use the production strategy's classification/count with delay suppressed for this test.
        var strategy = new ImmediateReadStrategy(context);
        var exception = CreateSqlException(-2);
        var attempts = 0;

        var thrown = await Assert.ThrowsAsync<RetryLimitExceededException>(() => strategy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromException<int>(exception);
        }));

        Assert.Equal(6, attempts);
        Assert.Same(exception, thrown.InnerException);
    }

    private sealed class ImmediateReadStrategy(CloudShopDbContext context)
        : SqlServerRetryingExecutionStrategy(
            context,
            SqlServerRetryPolicy.CreateReadStrategy(context).MaxRetryCount,
            SqlServerRetryPolicy.CreateReadStrategy(context).MaxRetryDelay,
            SqlServerRetryPolicy.CreateReadStrategy(context).AdditionalErrorNumbers)
    {
        protected override TimeSpan? GetNextDelay(Exception lastException)
            => base.GetNextDelay(lastException) is null ? null : TimeSpan.Zero;
    }

    private sealed class TimeoutBeforeOpenInterceptor : DbConnectionInterceptor
    {
        public SqlException Timeout { get; } = CreateSqlException(-2);
        public InvalidOperationException StopException { get; } = new("Synthetic stop: no database connection allowed.");
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (++Attempts == 1)
                throw Timeout;
            throw StopException;
        }
    }

    // SqlClient has no public SqlException constructor. Reflection is confined to
    // synthetic test data for the installed client version; no connection is opened.
    private static SqlException CreateSqlException(int errorNumber)
    {
        var errorConstructor = typeof(SqlError).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)], null)!;
        var error = (SqlError)errorConstructor.Invoke(
            [errorNumber, (byte)0, (byte)20, "SyntheticServer", "Synthetic SQL failure", "", 0, null]);
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(errors, [error]);
        var exceptionConstructor = typeof(SqlException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(string), typeof(SqlErrorCollection), typeof(Exception), typeof(Guid)], null)!;
        var exception = (SqlException)exceptionConstructor.Invoke(["Synthetic SQL failure", errors, null, Guid.Empty]);
        Assert.Equal(errorNumber, exception.Number);
        return exception;
    }
}
