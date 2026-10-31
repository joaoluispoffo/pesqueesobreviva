# Publica a versao atual do jogo (www\pesqueesobreviva.html) no Cloudflare (projeto "pesque-e-sobreviva").
# Endereco: https://pesque-e-sobreviva.serverfrontend.workers.dev
# Configuracao em wrangler.jsonc. Na primeira vez em outro computador, rode antes:  npx wrangler login
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:Path = "C:\Program Files\nodejs;$env:APPDATA\npm;$env:Path"

$deploy = Join-Path $here 'cloudflare'
New-Item -ItemType Directory -Force $deploy | Out-Null
Copy-Item (Join-Path $here 'www\pesqueesobreviva.html') (Join-Path $deploy 'index.html') -Force

Push-Location $here
try { npx --yes wrangler@latest deploy } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar." }
Write-Host "Publicado: https://pesque-e-sobreviva.serverfrontend.workers.dev"
