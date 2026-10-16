$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '..\..\tools\Stop-LocalServices.ps1'
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw 'Falta el comando para detener los servicios locales.'
}

$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'El comando Stop no se puede analizar.' }
foreach ($name in @('Assert-OwnedLocalService', 'Get-ListeningConnections', 'Stop-OwnedLocalServices')) {
    $definition = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $name
    }, $true)
    if ($null -eq $definition) { throw "Falta $name." }
    . ([scriptblock]::Create($definition.Extent.Text))
}

$apiTarget = [pscustomobject]@{
    Port = 5249
    Kind = 'Api'
    Addresses = @('127.0.0.1')
    Executable = 'C:\Lab\Carnicerias.Api.exe'
    Url = 'http://127.0.0.1:5249'
}
$apiListener = [pscustomobject]@{ LocalPort = 5249; LocalAddress = '127.0.0.1'; OwningProcess = 42 }
$apiProcess = [pscustomobject]@{
    ProcessId = 42
    Name = 'Carnicerias.Api.exe'
    ExecutablePath = 'C:\Lab\Carnicerias.Api.exe'
    CommandLine = '"C:\Lab\Carnicerias.Api.exe" --urls http://127.0.0.1:5249'
    CreationDate = [datetime]'2026-10-16T10:00:00'
}
Assert-OwnedLocalService $apiTarget @($apiListener) $apiProcess

$foreignApi = $apiProcess.PSObject.Copy()
$foreignApi.ExecutablePath = 'C:\Other\Carnicerias.Api.exe'
try {
    Assert-OwnedLocalService $apiTarget @($apiListener) $foreignApi
    throw 'Se aceptó una API de otra ruta.'
}
catch {
    if ($_.Exception.Message -eq 'Se aceptó una API de otra ruta.') { throw }
}

$webTarget = [pscustomobject]@{
    Port = 4302
    Kind = 'Angular'
    Addresses = @('::1', '127.0.0.1')
    Project = 'C:\Lab\Central.Web'
    Proxy = 'C:\Lab\rustica-proxy.json'
    PortArgument = '--port 4302'
}
$webListener = [pscustomobject]@{ LocalPort = 4302; LocalAddress = '::1'; OwningProcess = 77 }
$webProcess = [pscustomobject]@{
    ProcessId = 77
    Name = 'node.exe'
    ExecutablePath = 'C:\Program Files\nodejs\node.exe'
    CommandLine = '"node" "C:\Lab\Central.Web\node_modules\.bin\\..\@angular\cli\bin\ng.js" serve --host localhost --port 4302 --proxy-config C:\Lab\rustica-proxy.json'
    CreationDate = [datetime]'2026-10-16T10:00:00'
}
Assert-OwnedLocalService $webTarget @($webListener) $webProcess

$foreignWeb = $webProcess.PSObject.Copy()
$foreignWeb.CommandLine = $foreignWeb.CommandLine.Replace('C:\Lab\Central.Web', 'C:\Other\Central.Web')
try {
    Assert-OwnedLocalService $webTarget @($webListener) $foreignWeb
    throw 'Se aceptó Angular de otro proyecto.'
}
catch {
    if ($_.Exception.Message -eq 'Se aceptó Angular de otro proyecto.') { throw }
}

$otherTarget = [pscustomobject]@{
    Port = 5246
    Kind = 'Api'
    Addresses = @('127.0.0.1')
    Executable = 'C:\Lab\Carnicerias.Api.exe'
    Url = 'http://127.0.0.1:5246'
}
$otherListener = [pscustomobject]@{ LocalPort = 5246; LocalAddress = '127.0.0.1'; OwningProcess = 43 }
$otherProcess = [pscustomobject]@{
    ProcessId = 43
    Name = 'Carnicerias.Api.exe'
    ExecutablePath = 'C:\Other\Carnicerias.Api.exe'
    CommandLine = '"C:\Other\Carnicerias.Api.exe" --urls http://127.0.0.1:5246'
    CreationDate = [datetime]'2026-10-16T10:00:00'
}
$script:stopped = @()
function Get-NetTCPConnection {
    [CmdletBinding()]
    param([int]$LocalPort, [string]$State)
    if ($LocalPort -eq 5249) { return $apiListener }
    if ($LocalPort -eq 5246) { return $otherListener }
}
function Get-CimInstance {
    param([string]$ClassName, [string]$Filter)
    if ($Filter -eq 'ProcessId=42') { return $apiProcess }
    if ($Filter -eq 'ProcessId=43') { return $otherProcess }
}
function Stop-Process {
    [CmdletBinding()]
    param([int]$Id)
    $script:stopped += $Id
}
try {
    Stop-OwnedLocalServices @($apiTarget, $otherTarget)
    throw 'Se detuvo un grupo que contenía un proceso ajeno.'
}
catch {
    if ($_.Exception.Message -eq 'Se detuvo un grupo que contenía un proceso ajeno.') { throw }
}
if ($script:stopped.Count -ne 0) {
    throw 'Stop debe validar todo el grupo antes de terminar el primer proceso.'
}

function Get-NetTCPConnection {
    [CmdletBinding()]
    param([int]$LocalPort, [string]$State)
    throw 'No se pudo consultar el puerto.'
}
try {
    $null = @(Get-ListeningConnections 5249)
    throw 'Se interpretó un fallo de consulta como puerto libre.'
}
catch {
    if ($_.Exception.Message -eq 'Se interpretó un fallo de consulta como puerto libre.') { throw }
}

Write-Output 'PASS: Stop reconoce solo APIs y Angular del laboratorio.'
