# Gera o pacote do Rastro (ADR-002): artifacts/rastro-ai.<versão>.nupkg, pronto para "dotnet tool install".
#   1. build do front (Angular) para o wwwroot da API;
#   2. dotnet pack em Release.
# Código aberto (ADR-004): sem licença no programa e sem ofuscação.
# Uso: powershell -File scripts/empacotar.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root 'backend/src/Rastro.Api'
$artifacts = Join-Path $root 'artifacts'
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()

function Invoke-Step([string]$description, [scriptblock]$command) {
    Write-Host "== $description"
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$description falhou (código $LASTEXITCODE)." }
}

# 1. Front
Push-Location (Join-Path $root 'frontend')
try {
    if (-not (Test-Path 'node_modules')) { Invoke-Step 'npm ci' { npm ci } }
    Invoke-Step 'Build do front' { npm run build }
} finally { Pop-Location }

$wwwroot = Join-Path $api 'wwwroot'
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
Copy-Item (Join-Path $root 'frontend/dist/rastro-web/browser') $wwwroot -Recurse

# 2. Pacote
$package = Join-Path $artifacts "rastro-ai.$version.nupkg"
if (Test-Path $package) { Remove-Item $package -Force }
Invoke-Step 'dotnet pack (Release)' { dotnet pack $api -c Release -o $artifacts }
if (-not (Test-Path $package)) { throw "O pack não gerou $package." }

Write-Host "Pacote pronto: $package"
