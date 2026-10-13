param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    $existing = Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1
    if ($existing) { throw "La carpeta de salida ya contiene archivos: $output" }
}

$webProject = Join-Path $repository 'src\Carnicerias.Web'
$posProject = Join-Path $repository 'src\Carnicerias.Pos'
$apiProject = Join-Path $repository 'src\Carnicerias.Api\Carnicerias.Api.csproj'
$bootstrapProject = Join-Path $repository 'tools\Carnicerias.Bootstrap\Carnicerias.Bootstrap.csproj'
$webBuild = Join-Path $webProject 'dist\Carnicerias.Web\browser'
$serverOutput = Join-Path $output 'servidor'
$bootstrapOutput = Join-Path $output 'inicializacion'
$posOutput = Join-Path $output 'cajas'
$buildArtifacts = Join-Path $output 'compilacion'

& npm run build --prefix $webProject
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación Angular.' }
& npm run build:electron --prefix $posProject
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación Electron.' }
& npm run stage:web --prefix $posProject
if ($LASTEXITCODE -ne 0) { throw 'Falló la preparación del bundle Angular para Electron.' }
if (-not (Test-Path -LiteralPath (Join-Path $webBuild 'index.html'))) {
    throw 'No se encontró el index.html compilado de Angular.'
}

& dotnet publish $apiProject --configuration Release --artifacts-path $buildArtifacts --output $serverOutput
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación de la API.' }
& dotnet publish $bootstrapProject --configuration Release --artifacts-path $buildArtifacts --output $bootstrapOutput
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación de la herramienta de inicialización.' }
$serverWebRoot = Join-Path $serverOutput 'wwwroot'
New-Item -ItemType Directory -Path $serverWebRoot -Force | Out-Null
Copy-Item -Path (Join-Path $webBuild '*') -Destination $serverWebRoot -Recurse

$previousForgeOutput = $env:CARNICERIAS_FORGE_OUT
try {
    $env:CARNICERIAS_FORGE_OUT = $posOutput
    Push-Location $posProject
    try {
        & .\node_modules\.bin\electron-forge.cmd package --platform win32 --arch x64
        if ($LASTEXITCODE -ne 0) { throw 'Falló el paquete Windows de Electron.' }
    }
    finally { Pop-Location }
}
finally { $env:CARNICERIAS_FORGE_OUT = $previousForgeOutput }
Copy-Item -LiteralPath (Join-Path $repository 'tools\Start-PosTerminal.ps1') -Destination $posOutput
Copy-Item -LiteralPath (Join-Path $repository 'documentacion\despliegue\pos-config.ejemplo.json') -Destination $posOutput

Write-Output "Artefactos del laboratorio: $output"
Write-Output "Servidor: $serverOutput"
Write-Output "Inicialización: $bootstrapOutput"
Write-Output "Cajas: $posOutput"
