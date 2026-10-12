using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace CloudShop.Api.Configuration;

public static class KeyVaultConfigurationExtensions
{
    // The client factory permits offline provider tests; production uses DefaultAzureCredential.
    public static void AddCloudShopKeyVault(
        this ConfigurationManager configuration,
        Func<Uri, SecretClient>? clientFactory = null)
    {
        var enabledSetting = configuration["KeyVault:Enabled"];
        if (enabledSetting is null)
            return;
        if (!bool.TryParse(enabledSetting, out var enabled))
            throw new InvalidOperationException("KeyVault:Enabled must be true or false.");
        if (!enabled)
            return;

        if (!Uri.TryCreate(configuration["KeyVault:VaultUri"], UriKind.Absolute, out var vaultUri)
            || vaultUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(vaultUri.Host)
            || !vaultUri.IsDefaultPort
            || vaultUri.AbsolutePath != "/"
            || vaultUri.UserInfo.Length != 0
            || vaultUri.Query.Length != 0
            || vaultUri.Fragment.Length != 0)
        {
            throw new InvalidOperationException(
                "When KeyVault:Enabled is true, KeyVault:VaultUri must be an absolute HTTPS vault URL without credentials, a path, query, or fragment.");
        }

        IConfigurationRoot vaultConfiguration;
        try
        {
            var client = clientFactory?.Invoke(vaultUri)
                ?? new SecretClient(vaultUri, new DefaultAzureCredential());
            vaultConfiguration = new ConfigurationBuilder()
                .AddAzureKeyVault(client, new WelcomeMessageSecretManager())
                .Build();
        }
        catch (Exception exception) when (IsVaultAccessFailure(exception))
        {
            throw new InvalidOperationException(
                "Unable to load CloudShop Key Vault configuration. Check the vault URL, identity, secret read/list permissions, and network access.", exception);
        }

        // Validate the vault's own data so a local fallback cannot conceal a missing secret.
        if (string.IsNullOrWhiteSpace(vaultConfiguration[WelcomeMessageSecretManager.ConfigurationKey]))
        {
            ((IDisposable)vaultConfiguration).Dispose();
            throw new InvalidOperationException(
                "When KeyVault:Enabled is true, the vault must provide a non-empty CloudShop--WelcomeMessage secret.");
        }

        configuration.AddConfiguration(vaultConfiguration, shouldDisposeConfiguration: true);
    }

    private static bool IsVaultAccessFailure(Exception exception)
        => exception is RequestFailedException or AuthenticationFailedException or CredentialUnavailableException
            || exception is AggregateException aggregate && aggregate.InnerExceptions.All(IsVaultAccessFailure);
}
