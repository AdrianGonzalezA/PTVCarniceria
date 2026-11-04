$ErrorActionPreference = 'Stop'
$container = 'carnicerias-pos-test-20261007'
$database = 'carnicerias_test_visual'
$volume = '1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d'
$inspection = @(docker inspect $container | ConvertFrom-Json)[0]
if ($inspection.Name -ne "/$container" -or -not $inspection.State.Running -or
    $inspection.NetworkSettings.Ports.'5432/tcp'[0].HostIp -ne '127.0.0.1' -or
    $inspection.NetworkSettings.Ports.'5432/tcp'[0].HostPort -ne '55433' -or
    -not ($inspection.Mounts | Where-Object {
        $_.Destination -eq '/var/lib/postgresql/data' -and $_.Name -eq $volume
    })) {
    throw 'El contenedor, volumen o puerto no coincide con la base autorizada.'
}
$actual = docker exec $container psql -U postgres -d $database -At -c 'select current_database()'
if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ne $database) {
    throw 'La base autorizada no responde.'
}
$passwords = @($inspection.Config.Env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' })
if ($passwords.Count -ne 1) { throw 'Falta la credencial del contenedor autorizado.' }
$connection = New-Object System.Data.Common.DbConnectionStringBuilder
$connection['Host'] = '127.0.0.1'
$connection['Port'] = 55433
$connection['Database'] = $database
$connection['Username'] = 'postgres'
$connection['Password'] = $passwords[0].Substring('POSTGRES_PASSWORD='.Length)
try {
    $env:CARNICERIAS_VISUAL_CONNECTION_STRING = $connection.ConnectionString
    $env:CARNICERIAS_I16_WRITES_APPROVED = 'I16_APPROVED'
    & dotnet test (Join-Path $PSScriptRoot '..\tests\Carnicerias.IntegrationTests\Carnicerias.IntegrationTests.csproj') `
        --no-restore --filter 'FullyQualifiedName~CashWithdrawalVisualEndpointTests' `
        --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw "Falló la prueba HTTP I16: $LASTEXITCODE" }
}
finally {
    Remove-Item Env:CARNICERIAS_VISUAL_CONNECTION_STRING -ErrorAction SilentlyContinue
    Remove-Item Env:CARNICERIAS_I16_WRITES_APPROVED -ErrorAction SilentlyContinue
    $connection['Password'] = ''
    $passwords = @()
}
