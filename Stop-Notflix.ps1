$ErrorActionPreference = 'Stop'
$processFile = Join-Path $PSScriptRoot '.local\processes.json'
if (-not (Test-Path -LiteralPath $processFile)) { return }
$recorded = Get-Content -LiteralPath $processFile -Raw | ConvertFrom-Json
foreach ($processId in @($recorded.Api, $recorded.Web)) {
    $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId=$processId" -ErrorAction SilentlyContinue
    if ($processInfo -and $processInfo.CommandLine -like ('*' + $PSScriptRoot + '*') -and ($processInfo.CommandLine -match 'Notflix.API.dll|@angular\\cli\\bin\\ng.js')) {
        Stop-Process -Id $processId
    }
}
Remove-Item -LiteralPath $processFile
