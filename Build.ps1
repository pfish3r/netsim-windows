$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$arguments = '/nologo /target:winexe /optimize+ /out:"{0}\NetSim.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "{0}\Engine.cs" "{0}\App.cs"' -f $PSScriptRoot
$build = Start-Process -FilePath $compiler -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($build.ExitCode -ne 0 -or -not (Test-Path -LiteralPath "$PSScriptRoot\NetSim.exe")) { throw 'Build failed.' }
Write-Host 'Built NetSim.exe. Double-click it to start.'

