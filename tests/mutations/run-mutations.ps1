param([string]$Project = (Join-Path $PSScriptRoot '..\..\AutomationPractice.csproj'))
$ErrorActionPreference = 'Continue'
$rows = @()
$previousState = $env:STATE
$env:STATE = 'California' # The synthetic fixtures use a state option; the live page lists countries instead.
try {
    Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.html' | Sort-Object Name | ForEach-Object {
        $url = ([System.Uri]$_.FullName).AbsoluteUri
        Write-Host "`n=== $($_.Name) ==="
        dotnet run --project $Project -- $url
        $rows += [pscustomobject]@{ Mutation = $_.Name; Result = if ($LASTEXITCODE -eq 0) { 'PASS' } else { "FAIL ($LASTEXITCODE)" } }
    }
}
finally { $env:STATE = $previousState }
Write-Host "`nMutation summary"
$rows | Format-Table -AutoSize
