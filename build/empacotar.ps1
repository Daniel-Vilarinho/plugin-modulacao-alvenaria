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
New-Item -ItemType Directory -Force (Join-Path $bundle "Contents") | Out-Null

$bin = Join-Path $raiz "src/AlvMod.Plugin/bin/Release/net48"
Copy-Item (Join-Path $bin "AlvMod.*.dll") (Join-Path $bundle "Contents")
Copy-Item (Join-Path $bin "AlvMod.*.pdb") (Join-Path $bundle "Contents") -ErrorAction SilentlyContinue

# O AutoCAD nunca pode receber copias das DLLs dele mesmo.
if (Get-ChildItem (Join-Path $bundle "Contents") -Filter "ac*mgd.dll") { throw "DLL do AutoCAD foi copiada para o pacote." }

$xml = Get-Content (Join-Path $raiz "packaging/AlvMod.bundle/PackageContents.xml") -Raw -Encoding UTF8
$xml.Replace("__VERSAO__", $Versao) | Set-Content (Join-Path $bundle "PackageContents.xml") -Encoding utf8

$zip = Join-Path $out "AlvMod-v$Versao.zip"
Compress-Archive -Path $bundle -DestinationPath $zip
Write-Host "Pacote gerado: $zip"
