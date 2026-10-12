# CloudShop Key Vault configuration

Key Vault is disabled by default (`KeyVault:Enabled=false` in appsettings.json). Local development uses the non-sensitive `CloudShop:WelcomeMessage` fallback, "Welcome to CloudShop!", which can be overridden through ordinary configuration. No Azure credentials are needed while disabled.

The API calls AddCloudShopKeyVault immediately after WebApplication.CreateBuilder and before service registration. The provider is enabled only by a boolean true value. An invalid enable flag reports a configuration error. Enabled startup requires a root HTTPS vault URI and a non-empty, enabled CloudShop--WelcomeMessage secret. Missing/disabled/blank vault values cannot be hidden by the local fallback because validation occurs against the vault provider's own configuration before it is added to the application.

WelcomeMessageSecretManager loads exactly CloudShop--WelcomeMessage and maps it to CloudShop:WelcomeMessage. Other secrets' values are not retrieved or mapped, including ConnectionStrings--DefaultConnection. The provider still lists secret metadata as part of discovery; the allowlist is not an Azure authorization boundary. SQL connection strings, SQL retry policies, and existing database authentication settings are unchanged.

GET /configuration/welcome returns only:

```json
{"welcomeMessage":"the configured message"}
```

This endpoint is public under the existing API setup. The welcome-message value must be suitable for public display; do not put credentials or confidential information in this particular secret. No configuration dump endpoint or secret-value logging was added.

## Azure settings to apply separately

| App Service setting | Value |
|---|---|
| KeyVault__Enabled | true |
| KeyVault__VaultUri | https://cloudshop-kv-borg-dev.vault.azure.net/ |

Authentication uses DefaultAzureCredential. With the existing system-assigned managed identity, no client secret or user-assigned identity ID is needed. To restrict the credential chain to managed identity, optionally configure AZURE_TOKEN_CREDENTIALS=ManagedIdentityCredential, supported by the installed Azure.Identity version. Verify that no user-assigned identity selector is configured if the system-assigned identity is intended. No Azure settings were applied by this implementation.

Before enabling, verify the existing identity has the necessary secret list/read access and the App Service can reach the vault. Azure authorization and networking were not tested or changed. Enabled startup intentionally fails with a clear error on configuration, authentication, or vault access failure. Loading configuration adds an external dependency and cold-start latency. SDK retries can extend startup time. The provider caches the secret for the application lifetime; no polling interval is enabled. Restart/reload is needed to pick up a changed secret.

## Dependencies and verification

Explicit API references:

- Azure.Extensions.AspNetCore.Configuration.Secrets 1.5.2
- Azure.Identity 1.21.0

Both existing test factories explicitly disable Key Vault, including Production-environment tests. Offline provider tests override SecretClient operations without credentials or an HTTP transport. They verify enablement, URI validation, mapping, secret exclusion, fallback, missing/disabled values, access failures, and endpoint output. They also verify enabled validation runs during actual API startup. None contacts the live vault.

Complete Release test suite: 60 passed, 0 failed, 0 skipped. Results: artifacts/diagnostics/key-vault-integration/release-tests.trx. No deployment ZIP was created and no deployment was performed.
