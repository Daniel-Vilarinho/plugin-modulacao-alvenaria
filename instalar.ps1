# Instala (ou reinstala) a ultima versao do AlvMod no AutoCAD deste computador.
# Rodar UMA vez no PowerShell, com o AutoCAD fechado:
#   irm https://raw.githubusercontent.com/Daniel-Vilarinho/plugin-modulacao-alvenaria/main/instalar.ps1 | iex
# Depois disso, as atualizacoes aparecem sozinhas ao abrir o AutoCAD.
$Repo = "Daniel-Vilarinho/plugin-modulacao-alvenaria"

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

if (Get-Process -Name "acad" -ErrorAction SilentlyContinue) {
    Write-Warning "Feche o AutoCAD antes de instalar."
    return
}

$destino = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$bundle = Join-Path $destino "AlvMod.bundle"
$temp = Join-Path $env:TEMP ("AlvMod-install-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $temp | Out-Null

try {
    Write-Host "Buscando a ultima versao em github.com/$Repo ..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ "User-Agent" = "AlvMod-Installer" }
    $asset = $release.assets | Where-Object { $_.name -like "AlvMod*.zip" } | Select-Object -First 1
    if (-not $asset) { throw "A release $($release.tag_name) nao tem o pacote AlvMod*.zip." }

    $zip = Join-Path $temp $asset.name
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $temp -Force

    New-Item -ItemType Directory -Force $destino | Out-Null
    if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
    Copy-Item (Join-Path $temp "AlvMod.bundle") $destino -Recurse

    # Tira a marca "baixado da internet" para o .NET aceitar carregar as DLLs.
    Get-ChildItem $bundle -Recurse | Unblock-File

    Write-Host ""
    Write-Host "AlvMod $($release.tag_name) instalado em $bundle" -ForegroundColor Green
    Write-Host "Abra o AutoCAD e digite ALVVERSAO para conferir."
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
