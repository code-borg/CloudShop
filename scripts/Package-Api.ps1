param(
    # Prepare an explicitly unverified artifact when local tests cannot complete.
    # Normal packaging still requires every test to pass.
    [switch]$PrepareUnverifiedPackage
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$packageRoot = Join-Path $projectRoot ('artifacts/packages/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$publishPath = Join-Path $packageRoot 'publish'
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

# Always build from source into a new directory; never reuse the top-level publish folder.
dotnet build (Join-Path $projectRoot 'CloudShop.slnx') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
dotnet test (Join-Path $projectRoot 'CloudShop.slnx') --configuration Release --no-build --logger "trx;LogFileName=release-tests.trx" --results-directory $packageRoot
$testsPassed = $LASTEXITCODE -eq 0
if (-not $testsPassed) {
    if (-not $PrepareUnverifiedPackage) { throw 'Release tests failed.' }
    Write-Warning 'Tests failed. Preparing an UNVERIFIED package; see release-tests.trx. Do not treat this as a tested release.'
    'Release tests failed. This artifact is not a tested release. Review release-tests.trx and rerun tests in an environment permitted to load the assemblies before deployment.' |
        Set-Content -LiteralPath (Join-Path $packageRoot 'UNVERIFIED.txt')
}
dotnet publish (Join-Path $projectRoot 'src/CloudShop.Api/CloudShop.Api.csproj') --configuration Release --no-restore --output $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipName = if ($testsPassed) { 'cloudshop-api.zip' } else { 'cloudshop-api-unverified.zip' }
$zipPath = Join-Path $packageRoot $zipName
[IO.Compression.ZipFile]::CreateFromDirectory($publishPath, $zipPath)
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if (-not $archive.GetEntry('CloudShop.Api.dll')) { throw 'API DLL must be at ZIP root.' }
    $manifest = foreach ($entry in $archive.Entries) {
        $sourcePath = Join-Path $publishPath $entry.FullName
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $zipHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($zipHash -ne (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash) {
            throw "ZIP content differs from publish output: $($entry.FullName)"
        }
        [pscustomobject]@{ File = $entry.FullName; SHA256 = $zipHash }
    }
} finally { $archive.Dispose() }
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot 'manifest.json')
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256 | Format-List
Write-Output "Content-verified deployment ZIP (Release tests passed: $testsPassed): $zipPath"
