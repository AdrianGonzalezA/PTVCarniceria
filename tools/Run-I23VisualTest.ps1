$ErrorActionPreference = 'Stop'
$container = 'carnicerias-pos-test-20261007'
$database = 'carnicerias_test_visual'
$expectedContainerId = '9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0'
$expectedVolumeId = '1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d'
$manifestPath = Join-Path $PSScriptRoot '../.local-backups/i23-before-20261106-153539Z.dump.json'
$inspection = @(docker inspect $container | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $inspection.Count -ne 1 -or
    $inspection[0].Id -ne $expectedContainerId -or
    -not $inspection[0].State.Running -or
    $inspection[0].NetworkSettings.Ports.'5432/tcp'[0].HostIp -ne '127.0.0.1' -or
    $inspection[0].NetworkSettings.Ports.'5432/tcp'[0].HostPort -ne '55433' -or
    @($inspection[0].Mounts | Where-Object { $_.Name -eq $expectedVolumeId }).Count -ne 1) {
    throw 'Contenedor, volumen o puerto no autorizados.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Database -ne $database -or $manifest.ContainerId -ne $expectedContainerId -or
    $manifest.Volume -ne $expectedVolumeId -or
    -not (Test-Path -LiteralPath $manifest.Path) -or
    (Get-FileHash -LiteralPath $manifest.Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne
        $manifest.Sha256) {
    throw 'El respaldo verificado previo a I23 no coincide.'
}
$sql = @'
BEGIN TRANSACTION READ ONLY;
SELECT current_database(), count(*), max("MigrationId") FROM "__EFMigrationsHistory"
  GROUP BY current_database();
SELECT 'writers', count(*) FROM pg_stat_activity WHERE datname=current_database()
  AND pid<>pg_backend_pid() AND state<>'idle';
SELECT 'fixtures', count(*) FROM platform_access.companies WHERE "Name" LIKE 'I23 production fixture %';
SELECT 'companies', count(*) FROM platform_access.companies;
SELECT 'users', count(*) FROM platform_access.users;
SELECT 'pieces', count(*) FROM inventory.pieces;
SELECT 'orders', count(*) FROM inventory.production_orders;
SELECT 'movements', count(*) FROM inventory.inventory_movements;
SELECT 'onhand', coalesce(sum("OnHand"), 0) FROM inventory.branch_inventory;
SELECT 'protected', coalesce(sum("ProtectedOnHand"), 0) FROM inventory.branch_inventory;
COMMIT;
'@
$pre = @($sql | docker exec -i $container psql -U postgres -d $database -v ON_ERROR_STOP=1 -At -f -)
if ($LASTEXITCODE -ne 0 -or
    $pre -notcontains 'carnicerias_test_visual|75|20261106165321_AddAnatomicalGroupCountSnapshotI22' -or
    $pre -notcontains 'writers|0' -or $pre -notcontains 'fixtures|0') {
    throw "Prevuelo I23 fallido: $($pre -join ', ')"
}
$baseline = @($pre | Where-Object {
    $_ -match '^(companies|users|pieces|orders|movements|onhand|protected)\|'
})
$passwords = @($inspection[0].Config.Env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' })
if ($passwords.Count -ne 1) { throw 'Falta la credencial del contenedor autorizado.' }
$connection = New-Object System.Data.Common.DbConnectionStringBuilder
$connection['Host'] = '127.0.0.1'
$connection['Port'] = 55433
$connection['Database'] = $database
$connection['Username'] = 'postgres'
$connection['Password'] = $passwords[0].Substring('POSTGRES_PASSWORD='.Length)
$previousConnection = [Environment]::GetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING', 'Process')
$previousApproval = [Environment]::GetEnvironmentVariable('CARNICERIAS_I23_WRITES_APPROVED', 'Process')
try {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING',
        $connection.ConnectionString, 'Process')
    [Environment]::SetEnvironmentVariable('CARNICERIAS_I23_WRITES_APPROVED', 'I23_APPROVED', 'Process')
    dotnet test (Join-Path $PSScriptRoot '../tests/Carnicerias.IntegrationTests/Carnicerias.IntegrationTests.csproj') `
        --filter 'FullyQualifiedName~ProductionOrderVisualEndpointTests' `
        --logger 'console;verbosity=normal' -v:q
    if ($LASTEXITCODE -ne 0) { throw "La prueba HTTP I23 falló ($LASTEXITCODE)." }
}
finally {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING', $previousConnection, 'Process')
    [Environment]::SetEnvironmentVariable('CARNICERIAS_I23_WRITES_APPROVED', $previousApproval, 'Process')
    $connection['Password'] = ''
}
$post = @($sql | docker exec -i $container psql -U postgres -d $database -v ON_ERROR_STOP=1 -At -f -)
if ($LASTEXITCODE -ne 0 -or $post -notcontains 'fixtures|0' -or
    @($baseline | Where-Object { $post -notcontains $_ }).Count -ne 0) {
    throw "La limpieza I23 no coincide con el estado previo: $($post -join ', ')"
}
$post
