$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '..\..\tools\Start-VisualCentralLab.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'El script del laboratorio central no se puede analizar.' }
$actionParameter = @($ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Action' })[0]
$actionValues = @($actionParameter.Attributes |
    Where-Object { $_.TypeName.FullName -eq 'ValidateSet' } |
    ForEach-Object { $_.PositionalArguments } |
    ForEach-Object { $_.Value })
if ('Stop' -notin $actionValues -or 'StopCentral' -notin $actionValues) {
    throw 'El laboratorio Visual necesita Stop y StopCentral.'
}

$settingsFunction = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'New-CentralSettings'
}, $true)
if ($null -eq $settingsFunction) { throw 'Falta New-CentralSettings.' }

foreach ($name in @('centralCompanyId', 'rusticaCentralCompanyId')) {
    $assignment = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -eq $name
    }, $true)
    if ($null -eq $assignment) { throw "Falta la identidad $name." }
    . ([scriptblock]::Create($assignment.Extent.Text))
}
if ($centralCompanyId -ne '802dcfdf-c653-443e-bb73-7d34cdb42d46' -or
    $rusticaCentralCompanyId -ne '11613dd2-0fb6-44cc-873d-804933aca921') {
    throw 'Las identidades centrales del laboratorio no coinciden.'
}
$branches = @(
    @{ Port = 5246; SecretReference = 'VISUAL_BRANCH' },
    @{ Port = 5247; SecretReference = 'VISUAL_NORTH' },
    @{ Port = 5248; SecretReference = 'VISUAL_SOUTH' }
)
. ([scriptblock]::Create($settingsFunction.Extent.Text))
$vault = [pscustomobject]@{
    ConnectionString = 'test-connection'
    Key = 'test-key'
    Tokens = [pscustomobject]@{
        VISUAL_BRANCH = 'test-branch'
        VISUAL_NORTH = 'test-north'
        VISUAL_SOUTH = 'test-south'
    }
}
$settings = New-CentralSettings $vault
$approved = $settings.CARNICERIAS_CENTRAL_APPROVED_BRANCHES | ConvertFrom-Json
if ($approved.Count -ne 7) { throw "Se esperaban siete destinos aprobados; hay $($approved.Count)." }

foreach ($port in @(5246, 5247, 5248)) {
    $destination = "http://127.0.0.1:$port/"
    $scoped = @($approved | Where-Object {
        $_.url -eq $destination -and $_.centralCompanyId -eq $centralCompanyId
    })
    if ($scoped.Count -ne 1) { throw "Falta el destino Visual $destination." }
}

$rustica = @($approved | Where-Object { $_.url -eq 'http://127.0.0.1:5249/' })
if ($rustica.Count -ne 1 -or
    $rustica[0].centralCompanyId -ne $rusticaCentralCompanyId -or
    @($rustica[0].addresses).Count -ne 1 -or
    $rustica[0].addresses[0] -ne '127.0.0.1') {
    throw 'Alta Cordoba debe estar aprobada solo para la empresa central La Rustica en loopback.'
}
Write-Output 'PASS: destinos Visual y Alta Cordoba aislados por empresa central.'
