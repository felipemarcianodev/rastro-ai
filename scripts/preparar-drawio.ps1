# Prepara o draw.io que vai dentro do pacote (ADR-011): baixa o draw.war da versão fixa em scripts/drawio/drawio.json,
# confere o SHA-256 e extrai só a lista "include" para backend/src/Rastro.Api/drawio (fora do git). O download fica
# em artifacts/ e é reaproveitado enquanto o hash bater. Chamado pelo scripts/empacotar.ps1; quem roda a API pelo
# código-fonte roda este script uma vez para ter o desenho draw.io.
# Uso: powershell -File scripts/preparar-drawio.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content (Join-Path $PSScriptRoot 'drawio/drawio.json') -Raw | ConvertFrom-Json
$artifacts = Join-Path $root 'artifacts'
$war = Join-Path $artifacts "draw-$($manifest.version).war"
$target = Join-Path $root 'backend/src/Rastro.Api/drawio'

function Get-Sha256([string]$path) { (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() }

New-Item -ItemType Directory -Force $artifacts | Out-Null
if (-not (Test-Path $war) -or (Get-Sha256 $war) -ne $manifest.sha256) {
    Write-Host "== Baixando o draw.io $($manifest.version)"
    $partial = "$war.part"
    Invoke-WebRequest -Uri $manifest.url -OutFile $partial -UseBasicParsing
    $hash = Get-Sha256 $partial
    if ($hash -ne $manifest.sha256) {
        Remove-Item $partial -Force
        throw "O SHA-256 do draw.war não confere (esperado $($manifest.sha256), veio $hash). Nada foi extraído."
    }
    Move-Item $partial $war -Force
}

Write-Host "== Extraindo o draw.io $($manifest.version) para $target"
if (Test-Path $target) {
    # No Windows a pasta não diferencia maiúscula: "drawio" e uma pasta de código "Drawio" são a mesma. Com código
    # dentro, recusa em vez de apagar (já aconteceu: o script apagou um .cs).
    $code = Get-ChildItem $target -Recurse -Include *.cs, *.csproj, *.ts -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($code) { throw "A pasta $target tem código-fonte ($($code.Name)); nada foi apagado. Ela é só do draw.io." }
    Remove-Item $target -Recurse -Force
}
New-Item -ItemType Directory -Force $target | Out-Null
$targetFull = [System.IO.Path]::GetFullPath($target)

$zip = [System.IO.Compression.ZipFile]::OpenRead($war)
try {
    $count = 0
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        $name = $entry.FullName
        $wanted = $false
        foreach ($item in $manifest.include) {
            if (($item.EndsWith('/') -and $name.StartsWith($item)) -or $name -eq $item) { $wanted = $true; break }
        }
        if (-not $wanted) { continue }
        $destination = [System.IO.Path]::GetFullPath((Join-Path $target $name))
        # Entrada que sairia da pasta de destino (../) é recusada.
        if (-not $destination.StartsWith($targetFull, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Entrada fora da pasta do draw.io no draw.war: $name"
        }
        New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
        $count++
    }
} finally { $zip.Dispose() }

Copy-Item (Join-Path $PSScriptRoot 'drawio/LICENSE-drawio.txt') (Join-Path $target 'LICENSE-drawio.txt')
# PostConfig.js do Rastro: mantém o original e avisa a tela qual balão foi escolhido (ver o próprio arquivo).
Copy-Item (Join-Path $PSScriptRoot 'drawio/PostConfig.js') (Join-Path $target 'js/PostConfig.js') -Force
Set-Content -Path (Join-Path $target 'VERSION') -Value $manifest.version -NoNewline -Encoding ascii
Write-Host "draw.io pronto: $count arquivos em $target"
