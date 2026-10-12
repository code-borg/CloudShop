using System.Net;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using CloudShop.Api.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CloudShop.Api.Tests;

public class KeyVaultConfigurationTests
{
    private const string VaultUri = "https://cloudshop-kv-borg-dev.vault.azure.net/";

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("FALSE")]
    public void Disabled_or_absent_flag_preserves_local_fallback_without_creating_a_client(string? enabled)
    {
        using var configuration = CreateConfiguration(enabled);
        configuration.AddCloudShopKeyVault(_ => throw new InvalidOperationException("Client must not be created."));
        Assert.Equal("Local welcome", configuration[WelcomeMessageSecretManager.ConfigurationKey]);
        Assert.Equal("Unchanged SQL configuration", configuration["ConnectionStrings:DefaultConnection"]);
    }

    [Fact]
    public void Invalid_enablement_flag_reports_a_clear_error()
    {
        using var configuration = CreateConfiguration("yes");
        var exception = Assert.Throws<InvalidOperationException>(() => configuration.AddCloudShopKeyVault());
        Assert.Contains("KeyVault:Enabled", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("http://example.vault.azure.net/")]
    [InlineData("https://example.vault.azure.net/secrets/welcome")]
    [InlineData("https://user:password@example.vault.azure.net/")]
    [InlineData("https://example.vault.azure.net/?query=value")]
    [InlineData("https://example.vault.azure.net/#fragment")]
    public void Enabled_provider_rejects_invalid_vault_URI_before_creating_a_client(string? uri)
    {
        using var configuration = CreateConfiguration("true", uri);
        var exception = Assert.Throws<InvalidOperationException>(() => configuration.AddCloudShopKeyVault(
            _ => throw new Exception("Client must not be created.")));
        Assert.Contains("KeyVault:VaultUri", exception.Message);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void Enabled_provider_loads_only_welcome_and_preserves_other_configuration(string enabled)
    {
        using var configuration = CreateConfiguration(enabled);
        var client = new OfflineSecretClient(
            Secret(WelcomeMessageSecretManager.SecretName, "Vault welcome"),
            Secret("ConnectionStrings--DefaultConnection", "Must never load"),
            Secret("Logging--LogLevel--Default", "Must never load"),
            Secret("CloudShop--OtherSecret", "Must never load"));
        Uri? requestedUri = null;

        configuration.AddCloudShopKeyVault(uri => { requestedUri = uri; return client; });

        Assert.Equal(new Uri(VaultUri), requestedUri);
        Assert.Equal("Vault welcome", configuration[WelcomeMessageSecretManager.ConfigurationKey]);
        Assert.Equal("Unchanged SQL configuration", configuration["ConnectionStrings:DefaultConnection"]);
        Assert.Equal("Information", configuration["Logging:LogLevel:Default"]);
        Assert.Null(configuration["CloudShop:OtherSecret"]);
        Assert.Equal(new[] { WelcomeMessageSecretManager.SecretName }, client.RetrievedNames);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Missing_or_blank_vault_secret_fails_even_when_local_fallback_exists(string? value)
    {
        using var configuration = CreateConfiguration("true");
        var client = value is null ? new OfflineSecretClient()
            : new OfflineSecretClient(Secret(WelcomeMessageSecretManager.SecretName, value));
        var exception = Assert.Throws<InvalidOperationException>(() => configuration.AddCloudShopKeyVault(_ => client));
        Assert.Contains("non-empty CloudShop--WelcomeMessage", exception.Message);
        Assert.Equal("Local welcome", configuration[WelcomeMessageSecretManager.ConfigurationKey]);
    }

    [Fact]
    public void Disabled_vault_secret_fails_without_retrieving_its_value()
    {
        using var configuration = CreateConfiguration("true");
        var secret = Secret(WelcomeMessageSecretManager.SecretName, "Hidden");
        secret.Properties.Enabled = false;
        var client = new OfflineSecretClient(secret);
        Assert.Throws<InvalidOperationException>(() => configuration.AddCloudShopKeyVault(_ => client));
        Assert.Empty(client.RetrievedNames);
    }

    [Theory]
    [InlineData("CloudShop--WelcomeMessage", true)]
    [InlineData("ConnectionStrings--DefaultConnection", false)]
    [InlineData("CloudShop--OtherSecret", false)]
    [InlineData("CloudShop--WelcomeMessage--Extra", false)]
    [InlineData("cloudshop--welcomemessage", false)]
    public void Manager_accepts_only_the_exact_welcome_secret(string name, bool accepted)
    {
        var manager = new WelcomeMessageSecretManager();
        var secret = Secret(name, "Synthetic value");
        Assert.Equal(accepted, manager.Load(secret.Properties));
        if (accepted)
            Assert.Equal("CloudShop:WelcomeMessage", manager.GetKey(secret));
        else
            Assert.Throws<InvalidOperationException>(() => manager.GetKey(secret));
    }

    [Fact]
    public void Vault_access_failure_reports_a_clear_error_without_silent_fallback()
    {
        using var configuration = CreateConfiguration("true");
        var denied = new RequestFailedException(403, "Synthetic access denial.");
        var client = new OfflineSecretClient { ListFailure = denied };
        var exception = Assert.Throws<InvalidOperationException>(() => configuration.AddCloudShopKeyVault(_ => client));
        Assert.Contains("Unable to load CloudShop Key Vault configuration", exception.Message);
        Assert.Same(denied, exception.InnerException);
    }

    [Fact]
    public void Secret_retrieval_failure_reports_a_clear_startup_error()
    {
        using var configuration = CreateConfiguration("true");
        var client = new OfflineSecretClient(Secret(WelcomeMessageSecretManager.SecretName, "Unused"))
        {
            GetFailure = new RequestFailedException(403, "Synthetic secret access denial.")
        };
        var exception = Assert.Throws<InvalidOperationException>(() => configuration.AddCloudShopKeyVault(_ => client));
        Assert.Contains("Unable to load CloudShop Key Vault configuration", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void Host_checks_enabled_configuration_before_startup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("KeyVault:Enabled", "true");
            builder.UseSetting("KeyVault:VaultUri", "not-a-url");
        });
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("KeyVault:VaultUri", exception.Message);
    }

    [Theory]
    [InlineData(null, "Welcome to CloudShop!")]
    [InlineData("Offline test welcome", "Offline test welcome")]
    public async Task Welcome_endpoint_returns_only_the_configured_message(string? configuredMessage, string expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("KeyVault:Enabled", "false");
            builder.UseSetting("KeyVault:VaultUri", "not-a-url");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = new Dictionary<string, string?> { ["KeyVault:Enabled"] = "false" };
                if (configuredMessage is not null)
                    settings[WelcomeMessageSecretManager.ConfigurationKey] = configuredMessage;
                configuration.AddInMemoryCollection(settings);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/configuration/welcome");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal(expected, body.RootElement.GetProperty("welcomeMessage").GetString());
    }

    private static ConfigurationManager CreateConfiguration(string? enabled, string? vaultUri = VaultUri)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KeyVault:Enabled"] = enabled,
            ["KeyVault:VaultUri"] = vaultUri,
            [WelcomeMessageSecretManager.ConfigurationKey] = "Local welcome",
            ["ConnectionStrings:DefaultConnection"] = "Unchanged SQL configuration",
            ["Logging:LogLevel:Default"] = "Information"
        });
        return configuration;
    }

    private static KeyVaultSecret Secret(string name, string value)
    {
        var secret = new KeyVaultSecret(name, value);
        secret.Properties.Enabled = true;
        return secret;
    }

    // Protected parameterless SDK constructor plus overrides: no credential or HTTP transport.
    private sealed class OfflineSecretClient(params KeyVaultSecret[] secrets) : SecretClient
    {
        public List<string> RetrievedNames { get; } = [];
        public Exception? ListFailure { get; init; }
        public Exception? GetFailure { get; init; }

        public override Pageable<SecretProperties> GetPropertiesOfSecrets(CancellationToken cancellationToken = default)
        {
            if (ListFailure is not null)
                throw ListFailure;
            return Pageable<SecretProperties>.FromPages(
                [Page<SecretProperties>.FromValues(secrets.Select(secret => secret.Properties).ToArray(), null, new OfflineResponse())]);
        }

        public override Task<Response<KeyVaultSecret>> GetSecretAsync(
            string name, string? version = null, SecretContentType? outContentType = null,
            CancellationToken cancellationToken = default)
        {
            RetrievedNames.Add(name);
            if (GetFailure is not null)
                return Task.FromException<Response<KeyVaultSecret>>(GetFailure);
            return Task.FromResult(Response.FromValue(secrets.Single(secret => secret.Name == name), new OfflineResponse()));
        }
    }

    private sealed class OfflineResponse : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = "offline";
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
        protected override bool TryGetHeader(string name, out string value) { value = ""; return false; }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) { values = []; return false; }
    }
}
