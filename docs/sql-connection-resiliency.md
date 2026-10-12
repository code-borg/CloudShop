# SQL connection resiliency

Runtime and design-time SQL Server options share `SqlServerRetryPolicy.Configure`:

- `EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)`.
- One initial attempt plus up to five retries per EF operation, using the provider's default transient-error classification and backoff. The SQL Server provider can add its own throttling delay to the configured base delay cap.
- Runtime passes the configured DefaultConnection string through unchanged. No managed identity, SQL Express, timeout, schema, migration, or Azure resource changes are required. The existing design-time factory remains connectionless for SQL generation.
- There are no HTTP request or manual POST retries. EF itself retries supported database operations, including SaveChanges.
- `ProductRepository.GetAllAsync` and `GetByIdAsync` wrap their side-effect-free `AsNoTracking` queries in an EF SQL Server execution strategy with the same retry settings and `errorNumbersToAdd: [-2]`. This enables SQL timeout replay for GET reads only. `GetByIdForUpdateAsync`, `AddAsync`, and `SaveChangesAsync` keep the shared default policy. The ambient EF execution strategy prevents nested query retries from multiplying the attempt count.

## Limits and follow-up

Retries improve availability while a paused serverless database resumes, but do not guarantee that the first request succeeds. Microsoft documents auto-resume latency as generally about one minute. Actual connection/command timeouts, retry delays, driver behavior, and upstream request timeouts determine whether a request lasts long enough to succeed. A 10-second retry delay is not a 10-second total request deadline.

Important: EF Core SQL Server 10.0.12's default transient detector deliberately excludes SqlException error -2 (SQL timeout), while recognizing System.TimeoutException and selected SQL/network errors. Inspect SqlException.Number and its Errors collection in Application Insights for the reported post-login timeout. Error number alone cannot distinguish a safe connection-open timeout from a command/commit timeout. The new read strategy retries -2, but the shared policy intentionally does not: a timed-out write can have an uncertain outcome. A persistent slow SELECT can also time out repeatedly; bounded retries improve availability but cannot repair a slow query or exhausted resources.

After recognized transient failures exhaust retries, EF throws RetryLimitExceededException with the original failure as its inner exception. The existing API exception handler only maps DomainException to 400, so exhaustion still results in HTTP 500 Problem Details if the response has not started. Non-transient failures are propagated immediately. Existing exception logging remains available; no telemetry integration or logging settings were changed.

EF retries can buffer query results, increasing memory use for large GET /products results. Write commit failures can leave an uncertain outcome. Product IDs are client-generated GUIDs, which reduces silent duplicate inserts when EF replays the same tracked insert, but does not make a separate client POST retry idempotent. This change does not introduce exactly-once semantics.

Configuration tests use synthetic SQL Express and managed identity strings and never open a SQL connection. They inspect runtime execution strategies, unchanged connection strings, and design-time options; behavioral tests check a recognized transient failure is retried and a domain exception is not. SqlTimeoutRetryTests constructs synthetic SqlException objects for the installed SqlClient 6.1.6 through test-only reflection. It checks -2 replay for reads and exclusion from the shared policy, rejection of errors 515/2627/18456, cancellation, six-attempt exhaustion, and the actual repository read/write paths. Connection interceptors throw before opening a database connection. They do not simulate Azure auto-resume or authenticate to Azure.

## Windows test execution blocker

Code Integrity events from 2026-10-09 09:31:47 UTC show testhost.exe was denied loading tests/CloudShop.Api.Tests/bin/Release/net10.0/CloudShop.Api.dll. Event 3077 names VerifiedAndReputableDesktop (Smart App Control), policy GUID {0283ac0f-fff1-49ae-ada1-8a933130cad6}. The matching 3089 event has TotalSignatureCount=0 and publisher Unknown. Get-AuthenticodeSignature also reports NotSigned; the API DLL has only its data stream, with no Zone.Identifier. Event 3118 explicitly reports Smart App Control block details. This is an enforced signing/reputation decision, not an EF failure, a database error, or a PowerShell execution-policy problem. Other unsigned project DLLs loaded successfully, so unsigned status alone does not explain which individual binary gains reputation. The event evidence proves the rejection but does not disclose the cloud reputation decision or explain why the older binary was allowed.

CiTool policy listing returned access denied. No policy changes, signing substitutions, unblocking, renaming or alternate load tricks were attempted. No existing CI workflow or Docker command was found; WSL inventory failed with REGDB_E_CLASSNOTREG. No approved alternate full-suite environment is currently available in this session. The full integration suite must run on an existing permitted CI/development runner (or through the organization's authorized signing process); do not weaken this host's protections.

Focused policy/repository tests can run here without loading the blocked API DLL. These do not substitute for full API integration validation. The previous ZIP remains unverified and predates the read-specific timeout change; it is not a deployable artifact for this update. Only rebuild and package normally after all required tests pass in a permitted environment.

Follow-up results: Release build succeeded with 0 warnings and 0 errors. All 15 selected policy/repository tests passed (including 12 new SQL timeout cases); results are in `artifacts/diagnostics/retry-followup/focused-retry-tests.trx`. No new Code Integrity block events were observed from this run. The rebuilt API DLL now has a different SHA256 from the previously denied binary; permission for the new hash has not been established, so the full suite was not attempted on this host. The 17 API-dependent integration cases remain unverified. No new deployment ZIP was generated or marked verified.

On a permitted runner, execute `dotnet test CloudShop.slnx --configuration Release`, then run `scripts/Package-Api.ps1` normally (without PrepareUnverifiedPackage). On Windows where running the local script is permitted by policy, invoke it directly with `& ./scripts/Package-Api.ps1`; do not override security policy to run it. The script builds, tests, publishes and verifies package contents, and does not deploy.

`scripts/Package-Api.ps1` normally stops if tests fail. The explicit `-PrepareUnverifiedPackage` flag records failed tests in release-tests.trx, writes UNVERIFIED.txt, and names the artifact cloudshop-api-unverified.zip. This is a preparation artifact, not a tested release. Run the normal script in an environment permitted to load the rebuilt assemblies before deployment. Neither mode deploys.

References:

- https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency
- https://learn.microsoft.com/en-us/azure/azure-sql/database/serverless-tier-auto-pause-resume?view=azuresql
- https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.SqlServer/Storage/Internal/SqlServerTransientExceptionDetector.cs
- https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.SqlServer/SqlServerRetryingExecutionStrategy.cs
- https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/test-your-app-with-smart-app-control
- https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/event-id-explanations
