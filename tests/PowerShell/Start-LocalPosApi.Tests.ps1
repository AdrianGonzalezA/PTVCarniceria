$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '..\..\tools\Start-LocalPosApi.ps1'
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw 'Falta el comando de arranque local de la API POS.'
}

$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'El comando de arranque local no se puede analizar.' }

$connectionFunction = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'New-LocalConnectionString'
}, $true)
if ($null -eq $connectionFunction) { throw 'Falta la validación del PostgreSQL local.' }
. ([scriptblock]::Create($connectionFunction.Extent.Text))

$container = [pscustomobject]@{
    Name = '/carnicerias-pos-test-20261007'
    State = [pscustomobject]@{ Running = $true }
    NetworkSettings = [pscustomobject]@{
        Ports = [pscustomobject]@{
            '5432/tcp' = @([pscustomobject]@{ HostIp = '127.0.0.1'; HostPort = '55433' })
        }
    }
    Config = [pscustomobject]@{
        Env = @('POSTGRES_USER=postgres', 'POSTGRES_PASSWORD=ejemplo;sin-valor-real')
    }
}
$connection = New-LocalConnectionString $container 'carnicerias-pos-test-20261007' 'carnicerias_test_visual'
$parsed = New-Object System.Data.Common.DbConnectionStringBuilder
$parsed.set_ConnectionString($connection)
if ($parsed['Host'] -ne '127.0.0.1' -or $parsed['Port'] -ne '55433' -or
    $parsed['Database'] -ne 'carnicerias_test_visual' -or
    $parsed['Username'] -ne 'postgres' -or
    $parsed['Password'] -ne 'ejemplo;sin-valor-real') {
    throw 'La cadena local no apunta a la única base autorizada o no conserva la contraseña.'
}

$container.NetworkSettings.Ports.'5432/tcp'[0].HostPort = '55434'
try {
    $null = New-LocalConnectionString $container 'carnicerias-pos-test-20261007' 'carnicerias_test_visual'
    throw 'Se aceptó un puerto PostgreSQL no autorizado.'
}
catch {
    if ($_.Exception.Message -ne 'El contenedor PostgreSQL no corresponde al laboratorio autorizado.') { throw }
}

$container.NetworkSettings.Ports.'5432/tcp'[0].HostPort = '55433'
$container.Config.Env = @('POSTGRES_PASSWORD=ejemplo;sin-valor-real')
$connection = New-LocalConnectionString $container 'carnicerias-pos-test-20261007' 'carnicerias_test_visual'
$parsed.set_ConnectionString($connection)
if ($parsed['Username'] -ne 'postgres') {
    throw 'Sin POSTGRES_USER explícito debe utilizarse el usuario predeterminado postgres.'
}

Write-Output 'PASS: arranque local restringido a la base y puerto autorizados.'
