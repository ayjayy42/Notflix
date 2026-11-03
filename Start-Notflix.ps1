$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$localFolder = Join-Path $repoRoot '.local'
$settingsPath = Join-Path $localFolder 'development.json'
if (-not (Test-Path -LiteralPath $settingsPath)) { & (Join-Path $repoRoot 'Setup-Local.ps1') }
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if (Get-NetTCPConnection -State Listen -LocalPort 4200,5246 -ErrorAction SilentlyContinue) { throw 'Ports 4200 or 5246 are already in use. Stop the existing instance first.' }
$apiDll = Join-Path $repoRoot 'Notflix.API\bin\Release\net10.0\Notflix.API.dll'
if (-not (Test-Path -LiteralPath $apiDll)) { throw 'Run Setup-Local.ps1 first.' }
$priorConnection = $env:ConnectionStrings__NotflixConnectionString
$priorEnvironment = $env:ASPNETCORE_ENVIRONMENT
$priorUrls = $env:ASPNETCORE_URLS
$api = $null
try {
    $env:ConnectionStrings__NotflixConnectionString = $settings.ConnectionString
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5246'
    $api = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList $apiDll -WorkingDirectory (Join-Path $repoRoot 'Notflix.API') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $localFolder 'api.log') -RedirectStandardError (Join-Path $localFolder 'api-error.log')
    $webRoot = Join-Path $repoRoot 'Notflix.Web'
    $frontend = Start-Process -FilePath (Get-Command node).Source -ArgumentList @((Join-Path $webRoot 'node_modules\@angular\cli\bin\ng.js'),'serve','--host','127.0.0.1','--port','4200') -WorkingDirectory $webRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $localFolder 'web.log') -RedirectStandardError (Join-Path $localFolder 'web-error.log')
    [pscustomobject]@{ Api = $api.Id; Web = $frontend.Id } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $localFolder 'processes.json')
} catch {
    if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id }
    throw
} finally {
    $env:ConnectionStrings__NotflixConnectionString = $priorConnection
    $env:ASPNETCORE_ENVIRONMENT = $priorEnvironment
    $env:ASPNETCORE_URLS = $priorUrls
}
Write-Output "Notflix is starting at http://127.0.0.1:4200 . Login details: $settingsPath"
