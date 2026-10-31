# Compila o ServerFrontEnd.exe usando o compilador C# que vem com o Windows (.NET Framework 4).
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin  = Join-Path $here 'bin'

New-Item -ItemType Directory -Force $bin | Out-Null

& $csc /nologo /codepage:65001 /optimize+ /target:exe `
    "/out:$bin\ServerFrontEnd.exe" `
    /reference:System.ServiceProcess.dll /reference:System.Configuration.dll `
    "$here\src\ServerFrontEnd.cs"
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao." }

# Copia a configuracao apenas se ainda nao existir, para nao apagar ajustes feitos em bin\.
$cfg = Join-Path $bin 'ServerFrontEnd.exe.config'
if (-not (Test-Path $cfg)) { Copy-Item "$here\src\App.config" $cfg }

Write-Host "Compilado: $bin\ServerFrontEnd.exe"
