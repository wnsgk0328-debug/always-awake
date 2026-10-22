param([switch]$Test)
$ErrorActionPreference = 'Stop'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'version.txt') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'version.txt must contain x.y.z' }
$buildDir = Join-Path $PSScriptRoot ('build\' + $version + '\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $PSScriptRoot 'AlwaysAwake.cs'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$metadata = Join-Path $buildDir 'Version.cs'
$exe = Join-Path $buildDir 'AlwaysAwake.exe'
@"
[assembly: System.Reflection.AssemblyVersion("$version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$version")]
"@ | Set-Content -LiteralPath $metadata -Encoding UTF8
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /debug- "/win32manifest:$manifest" "/out:$exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $source $metadata
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
if ((Get-Item -LiteralPath $exe).Length -gt 32768) { throw 'Executable exceeds the 32 KiB release budget' }
if ($Test) {
    $report = Join-Path $buildDir 'self-test.txt'
    $process = Start-Process -FilePath $exe -ArgumentList @('--self-test-core', ('"' + $report + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(15000)) { Stop-Process -Id $process.Id; throw 'Self-test timed out' }
    if ($process.ExitCode -ne 0) { if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report }; throw 'Self-test failed' }
    Get-Content -LiteralPath $report
}
Write-Output "Built Always Awake $version : $exe"
return $exe
