# Para e remove o servico "Server Front End" e a regra de firewall.
$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($MyInvocation.MyCommand.Path)`"")
    exit
}

try {
    $serviceName = 'ServerFrontEnd'
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $serviceName | Out-Null
        Write-Host 'Servico removido.'
    } else {
        Write-Host 'Servico nao estava instalado.'
    }
    Get-NetFirewallRule -DisplayName 'Server Front End' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
} catch {
    Write-Host "ERRO: $_" -ForegroundColor Red
}
Read-Host 'Pressione Enter para fechar'
