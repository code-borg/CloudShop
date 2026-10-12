using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace CloudShop.Api.Configuration;

public sealed class WelcomeMessageSecretManager : KeyVaultSecretManager
{
    public const string SecretName = "CloudShop--WelcomeMessage";
    public const string ConfigurationKey = "CloudShop:WelcomeMessage";

    public override bool Load(SecretProperties secret)
        => string.Equals(secret.Name, SecretName, StringComparison.Ordinal);

    public override string GetKey(KeyVaultSecret secret)
        => string.Equals(secret.Name, SecretName, StringComparison.Ordinal)
            ? ConfigurationKey
            : throw new InvalidOperationException("Only the CloudShop welcome-message secret can be mapped.");
}
