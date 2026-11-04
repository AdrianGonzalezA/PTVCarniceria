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
    throw 'El contenedor, volumen o puerto no coincide con la base de desarrollo autorizada.'
}
$migrationState = Get-Content -Raw (Join-Path $PSScriptRoot 'sql/20261104_i02_http_identity_readonly.sql') |
    docker exec -i $container psql -v ON_ERROR_STOP=1 -U postgres -d $database -At
if ($LASTEXITCODE -ne 0 -or $migrationState.Trim() -ne "$database|59|1") {
    throw 'La base autorizada no tiene exactamente las 59 migraciones previstas.'
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
    $env:CARNICERIAS_I02_HTTP_WRITES_APPROVED = 'I02_HTTP_APPROVED'
    & dotnet test (Join-Path $PSScriptRoot '..\tests\Carnicerias.IntegrationTests\Carnicerias.IntegrationTests.csproj') `
        --no-restore --filter 'FullyQualifiedName~TraceablePieceHttpVisualEndpointTests' `
        --logger 'console;verbosity=normal'
    $testExitCode = $LASTEXITCODE
    $postcheck = Get-Content -Raw (Join-Path $PSScriptRoot 'sql/20261104_i02_http_postcheck_readonly.sql') |
        docker exec -i $container psql -v ON_ERROR_STOP=1 -U postgres -d $database -At
    if ($LASTEXITCODE -ne 0 -or $postcheck.Trim() -ne "$database|0|3") {
        throw "Postcheck I02 HTTP inesperado: $postcheck"
    }
    Write-Output "Postcheck I02 HTTP: $postcheck"
    if ($testExitCode -ne 0) { throw "Fallo la prueba HTTP I02: $testExitCode" }
}
finally {
    Remove-Item Env:CARNICERIAS_VISUAL_CONNECTION_STRING -ErrorAction SilentlyContinue
    Remove-Item Env:CARNICERIAS_I02_HTTP_WRITES_APPROVED -ErrorAction SilentlyContinue
    $connection['Password'] = ''
    $passwords = @()
}
