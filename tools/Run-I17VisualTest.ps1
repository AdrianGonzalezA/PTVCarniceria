$ErrorActionPreference = 'Stop'
$container = 'carnicerias-pos-test-20261007'
$backup = Join-Path $PSScriptRoot '../.local-backups/i17-before-20261105-150437Z.dump'
$expectedHash = '716F31F6B4CBF839B6790AF00C4BA58397F9A8CBB97D7BA7127C25889C951154'
$inspect = @(docker inspect $container | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $inspect.Count -ne 1 -or
    $inspect[0].Id -ne '9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0' -or
    -not $inspect[0].State.Running -or
    $inspect[0].NetworkSettings.Ports.'5432/tcp'[0].HostIp -ne '127.0.0.1' -or
    $inspect[0].NetworkSettings.Ports.'5432/tcp'[0].HostPort -ne '55433' -or
    @($inspect[0].Mounts | Where-Object {
        $_.Name -eq '1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d'
    }).Count -ne 1) { throw 'Contenedor, puerto o volumen no autorizados.' }
if (-not (Test-Path -LiteralPath $backup) -or
    (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'El respaldo previo a I17 no coincide con el checksum registrado.'
}
$check = @'
BEGIN TRANSACTION READ ONLY;
SELECT current_database(), count(*), max("MigrationId") FROM "__EFMigrationsHistory";
SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND state<>'idle';
COMMIT;
'@ | docker exec -i $container psql -U postgres -d carnicerias_test_visual -v ON_ERROR_STOP=1 -At -f -
if ($LASTEXITCODE -ne 0 -or
    $check -notcontains 'carnicerias_test_visual|65|20261105144324_AddShiftClosingReportsI17' -or
    $check -notcontains '0') { throw "Prevuelo fallido: $($check -join ', ')" }
$passwords = @($inspect[0].Config.Env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' })
if ($passwords.Count -ne 1) { throw 'Falta la credencial del contenedor autorizado.' }
$connection = New-Object System.Data.Common.DbConnectionStringBuilder
$connection['Host'] = '127.0.0.1'
$connection['Port'] = 55433
$connection['Database'] = 'carnicerias_test_visual'
$connection['Username'] = 'postgres'
$connection['Password'] = $passwords[0].Substring('POSTGRES_PASSWORD='.Length)
$previousConnection = [Environment]::GetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING', 'Process')
$previousApproval = [Environment]::GetEnvironmentVariable('CARNICERIAS_I17_WRITES_APPROVED', 'Process')
try {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING',
        $connection.ConnectionString, 'Process')
    [Environment]::SetEnvironmentVariable('CARNICERIAS_I17_WRITES_APPROVED', 'I17_APPROVED', 'Process')
    dotnet test (Join-Path $PSScriptRoot '../tests/Carnicerias.IntegrationTests/Carnicerias.IntegrationTests.csproj') `
        --filter FullyQualifiedName~ShiftClosingReportVisualEndpointTests -v:q
    if ($LASTEXITCODE -ne 0) { throw "La prueba HTTP I17 falló ($LASTEXITCODE)." }
}
finally {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING', $previousConnection, 'Process')
    [Environment]::SetEnvironmentVariable('CARNICERIAS_I17_WRITES_APPROVED', $previousApproval, 'Process')
    $connection['Password'] = ''
}
$post = @'
BEGIN TRANSACTION READ ONLY;
SELECT 'fixtures', count(*) FROM platform_access.companies WHERE "Name" LIKE 'I17 fixture %';
SELECT 'reports', count(*) FROM payments_cash.shift_closing_reports;
SELECT 'shifts', count(*) FROM payments_cash.cashier_shifts;
COMMIT;
'@ | docker exec -i $container psql -U postgres -d carnicerias_test_visual -v ON_ERROR_STOP=1 -At -f -
if ($LASTEXITCODE -ne 0 -or $post -notcontains 'fixtures|0' -or
    $post -notcontains 'reports|0' -or $post -notcontains 'shifts|33') {
    throw "La limpieza del fixture no coincide con el estado previo: $($post -join ', ')"
}
$post
