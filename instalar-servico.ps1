# Instala e inicia o servico "Server Front End" (inicia automaticamente com o Windows).
# Precisa de administrador: se nao estiver, pede elevacao (UAC) automaticamente.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($MyInvocation.MyCommand.Path)`"")
    exit
}

try {
    $serviceName = 'ServerFrontEnd'
    $exe = Join-Path $here 'bin\ServerFrontEnd.exe'

    & (Join-Path $here 'build.ps1')

    $existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host 'Servico ja existe; reiniciando com a versao atual...'
        if ($existing.Status -ne 'Stopped') { Stop-Service $serviceName -Force }
    } else {
        New-Service -Name $serviceName `
            -BinaryPathName "`"$exe`"" `
            -DisplayName 'Server Front End' `
            -Description 'Servidor HTTP que publica os arquivos do frontend (pasta www).' `
            -StartupType Automatic | Out-Null

        # Reinicia sozinho se o processo cair.
        sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
    }

    # Libera a porta no firewall para acesso pela rede local.
    $port = ([xml](Get-Content (Join-Path $here 'bin\ServerFrontEnd.exe.config'))).configuration.appSettings.add |
        Where-Object { $_.key -eq 'Port' } | ForEach-Object { $_.value }
    if (-not (Get-NetFirewallRule -DisplayName 'Server Front End' -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName 'Server Front End' -Direction Inbound -Protocol TCP `
            -LocalPort $port -Action Allow | Out-Null
    }

    Start-Service $serviceName
    Get-Service $serviceName | Format-Table Name, DisplayName, Status, StartType -AutoSize
    Write-Host "Pronto! Acesse http://localhost:$port/"
} catch {
    Write-Host "ERRO: $_" -ForegroundColor Red
}
Read-Host 'Pressione Enter para fechar'
