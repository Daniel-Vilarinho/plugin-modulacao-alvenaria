# Compila o plugin e monta out\AlvMod-v<versao>.zip com a pasta AlvMod.bundle.
# Uso: pwsh build/empacotar.ps1 -Versao 0.2.0 [-Repo dono/repositorio]
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [string]$Repo = ""
)
$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $PSScriptRoot
$out = Join-Path $raiz "out"
$bundle = Join-Path $out "AlvMod.bundle"

$props = @("-c", "Release", "-p:Version=$Versao")
if ($Repo) { $props += "-p:GitHubRepo=$Repo" }
dotnet build (Join-Path $raiz "src/AlvMod.Plugin/AlvMod.Plugin.csproj") @props
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao." }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

# Uma pasta por runtime: net48 (AutoCAD 2018-2024) e net8 (AutoCAD 2025+).
$alvos = @{ "net48" = "net48"; "net8.0-windows" = "net8" }
foreach ($tfm in $alvos.Keys) {
    $destino = Join-Path $bundle ("Contents/" + $alvos[$tfm])
    New-Item -ItemType Directory -Force $destino | Out-Null
    $bin = Join-Path $raiz "src/AlvMod.Plugin/bin/Release/$tfm"
    Copy-Item (Join-Path $bin "AlvMod.*.dll") $destino
    Copy-Item (Join-Path $bin "AlvMod.*.pdb") $destino -ErrorAction SilentlyContinue
}

# O AutoCAD nunca pode receber copias das DLLs dele mesmo.
if (Get-ChildItem $bundle -Recurse -Filter "ac*mgd.dll") { throw "DLL do AutoCAD foi copiada para o pacote." }

$xml = Get-Content (Join-Path $raiz "packaging/AlvMod.bundle/PackageContents.xml") -Raw -Encoding UTF8
$xml.Replace("__VERSAO__", $Versao) | Set-Content (Join-Path $bundle "PackageContents.xml") -Encoding utf8

$zip = Join-Path $out "AlvMod-v$Versao.zip"
Compress-Archive -Path $bundle -DestinationPath $zip
Write-Host "Pacote gerado: $zip"
