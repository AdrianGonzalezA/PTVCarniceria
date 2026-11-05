$ErrorActionPreference = 'Stop'
$container = 'carnicerias-pos-test-20261007'
$backup = Join-Path $PSScriptRoot '../.local-backups/i18a-pieces-before-20261105-153658Z.dump'
$expectedHash = '84C6C99CFE48314BF0AE237E9F0F664229D03D6D024E16B5CF7BEE815394FDB0'
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
    throw 'El respaldo previo a I18a piezas no coincide con el checksum registrado.'
}
$pre = @'
BEGIN TRANSACTION READ ONLY;
SELECT current_database(), count(*), max("MigrationId") FROM "__EFMigrationsHistory";
SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND state<>'idle';
SELECT 'fixtures', count(*) FROM platform_access.companies WHERE "Name" LIKE 'I18a receipt fixture %';
SELECT 'reads', count(*) FROM inventory.receipt_piece_reads;
COMMIT;
'@ | docker exec -i $container psql -U postgres -d carnicerias_test_visual -v ON_ERROR_STOP=1 -At -f -
if ($LASTEXITCODE -ne 0 -or
    $pre -notcontains 'carnicerias_test_visual|66|20261105152647_AddReceiptPieceReadsI18a' -or
    $pre -notcontains '0' -or
    $pre -notcontains 'fixtures|0' -or
    $pre -notcontains 'reads|0') { throw "Prevuelo fallido: $($pre -join ', ')" }
$passwords = @($inspect[0].Config.Env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' })
if ($passwords.Count -ne 1) { throw 'Falta la credencial del contenedor autorizado.' }
$connection = New-Object System.Data.Common.DbConnectionStringBuilder
$connection['Host'] = '127.0.0.1'
$connection['Port'] = 55433
$connection['Database'] = 'carnicerias_test_visual'
$connection['Username'] = 'postgres'
$connection['Password'] = $passwords[0].Substring('POSTGRES_PASSWORD='.Length)
$previousConnection = [Environment]::GetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING', 'Process')
$previousApproval = [Environment]::GetEnvironmentVariable('CARNICERIAS_I18A_WRITES_APPROVED', 'Process')
try {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING',
        $connection.ConnectionString, 'Process')
    [Environment]::SetEnvironmentVariable('CARNICERIAS_I18A_WRITES_APPROVED', 'I18A_APPROVED', 'Process')
    dotnet test (Join-Path $PSScriptRoot '../tests/Carnicerias.IntegrationTests/Carnicerias.IntegrationTests.csproj') `
        --filter FullyQualifiedName~InventoryReceiptVisualEndpointTests -v:q
    if ($LASTEXITCODE -ne 0) { throw "La prueba HTTP I18a piezas falló ($LASTEXITCODE)." }
}
finally {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING', $previousConnection, 'Process')
    [Environment]::SetEnvironmentVariable('CARNICERIAS_I18A_WRITES_APPROVED', $previousApproval, 'Process')
    $connection['Password'] = ''
}
$post = @'
BEGIN TRANSACTION READ ONLY;
SELECT 'fixtures', count(*) FROM platform_access.companies WHERE "Name" LIKE 'I18a receipt fixture %';
SELECT 'receipts', count(*) FROM inventory.receipt_documents;
SELECT 'lines', count(*) FROM inventory.receipt_lines;
SELECT 'reads', count(*) FROM inventory.receipt_piece_reads;
SELECT 'pieces', count(*) FROM inventory.pieces;
COMMIT;
'@ | docker exec -i $container psql -U postgres -d carnicerias_test_visual -v ON_ERROR_STOP=1 -At -f -
if ($LASTEXITCODE -ne 0 -or $post -notcontains 'fixtures|0' -or
    $post -notcontains 'receipts|0' -or $post -notcontains 'lines|0' -or
    $post -notcontains 'reads|0' -or $post -notcontains 'pieces|4') {
    throw "La limpieza del fixture no coincide con el estado previo: $($post -join ', ')"
}
$post
