param([string]$UserName = 'demo')
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$localFolder = Join-Path $repoRoot '.local'
New-Item -ItemType Directory -Path $localFolder -Force | Out-Null
$settingsPath = Join-Path $localFolder 'development.json'
if (Test-Path -LiteralPath $settingsPath) {
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
} else {
    $randomBytes = New-Object byte[] 24
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($randomBytes) } finally { $rng.Dispose() }
    $settings = [pscustomobject]@{
        UserName = $UserName
        Password = [Convert]::ToBase64String($randomBytes) + '!aA7'
        ConnectionString = 'Server=(localdb)\MSSQLLocalDB;Database=NotflixDevelopment;Integrated Security=true;TrustServerCertificate=true'
    }
    $settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath
}
$localDb = Get-ChildItem -Path "$env:ProgramFiles\Microsoft SQL Server\*\Tools\Binn\SqlLocalDB.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $localDb) { throw 'SQL Server LocalDB is required for this local launcher.' }
& $localDb.FullName start MSSQLLocalDB
if ($LASTEXITCODE -ne 0) { & $localDb.FullName create MSSQLLocalDB -s; if ($LASTEXITCODE -ne 0) { throw 'Could not start LocalDB.' } }
dotnet build (Join-Path $repoRoot 'Notflix.API\Notflix.API.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'API build failed.' }
$priorConnection = $env:ConnectionStrings__NotflixConnectionString
$priorUser = $env:Seed__UserName
$priorPassword = $env:Seed__Password
try {
    $env:ConnectionStrings__NotflixConnectionString = $settings.ConnectionString
    $env:Seed__UserName = $settings.UserName
    $env:Seed__Password = $settings.Password
    dotnet (Join-Path $repoRoot 'Notflix.API\bin\Release\net10.0\Notflix.API.dll') --setup --seed
    if ($LASTEXITCODE -ne 0) { throw 'Local database setup failed.' }
} finally {
    $env:ConnectionStrings__NotflixConnectionString = $priorConnection
    $env:Seed__UserName = $priorUser
    $env:Seed__Password = $priorPassword
}
Push-Location (Join-Path $repoRoot 'Notflix.Web')
try { npm.cmd ci --ignore-scripts --no-fund; if ($LASTEXITCODE -ne 0) { throw 'Frontend install failed.' } } finally { Pop-Location }
Write-Output "Local setup ready. Login details are in $settingsPath (ignored by Git)."
