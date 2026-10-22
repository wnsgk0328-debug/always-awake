param([switch]$Test)
$ErrorActionPreference = 'Stop'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'version.txt') -Raw).Trim()
$buildOutput = & (Join-Path $PSScriptRoot 'Build.ps1') -Test:$Test
$exe = [string]($buildOutput | Select-Object -Last 1)
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build did not return an executable' }
$dist = Join-Path $PSScriptRoot ('dist\v' + $version)
if (Test-Path -LiteralPath $dist) { throw 'Release folder exists. Preserve it and use a new version.' }
New-Item -ItemType Directory -Path $dist | Out-Null
$portable = Join-Path $dist 'portable'
New-Item -ItemType Directory -Path $portable | Out-Null
Copy-Item -LiteralPath $exe -Destination (Join-Path $portable 'AlwaysAwake.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $portable
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination $portable
$zip = Join-Path $dist 'AlwaysAwake-win-portable.zip'
Compress-Archive -LiteralPath (Join-Path $portable 'AlwaysAwake.exe'),(Join-Path $portable 'README.md'),(Join-Path $portable 'CHANGELOG.md') -DestinationPath $zip -CompressionLevel Optimal
Copy-Item -LiteralPath $exe -Destination (Join-Path $dist 'AlwaysAwake.exe')
$lines = foreach ($name in @('AlwaysAwake.exe','AlwaysAwake-win-portable.zip')) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $dist $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), [string[]]$lines, (New-Object Text.UTF8Encoding($false)))
Write-Output "Release ready: $dist"
Get-ChildItem -LiteralPath $dist -File | Select-Object Name,Length
