$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '..\..\tools\Start-RusticaManagedLab.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'El script del laboratorio La Rústica no se puede analizar.' }

$waitFunction = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Wait-HealthyUrls'
}, $true)
if ($null -eq $waitFunction) { throw 'Verify debe esperar a que los servicios acepten conexiones.' }
$waitCalls = @($ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'Wait-HealthyUrls'
}, $true))
if ($waitCalls.Count -ne 1) { throw 'La acción Verify debe usar la espera de salud.' }
. ([scriptblock]::Create($waitFunction.Extent.Text))

$script:attempts = 0
function Invoke-RestMethod {
    param([string]$Uri, [int]$TimeoutSec)
    $script:attempts++
    if ($script:attempts -lt 3) { throw 'Todavía no acepta conexiones.' }
    return [pscustomobject]@{ status = 'healthy' }
}
function Start-Sleep { param([int]$Milliseconds) }

Wait-HealthyUrls -Urls @('http://localhost:4302/api/health') -TimeoutSeconds 5 -RetryDelayMilliseconds 0
if ($script:attempts -ne 3) {
    throw 'Verify debe reintentar cuando Angular todavía está iniciando.'
}

$script:attempts = 0
try {
    Wait-HealthyUrls -Urls @('http://localhost:4302/api/health') -TimeoutSeconds 0 -RetryDelayMilliseconds 0
    throw 'Se aceptó un servicio inaccesible.'
}
catch {
    if ($_.Exception.Message -notlike '*http://localhost:4302/api/health*') { throw }
}

Write-Output 'PASS: Verify espera el arranque y muestra la URL que no responde.'
