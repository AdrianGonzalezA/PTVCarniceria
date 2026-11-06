$ErrorActionPreference = 'Stop'
$container = 'carnicerias-pos-test-20261007'
$backup = Join-Path $PSScriptRoot '../.local-backups/i20-package-before-20261106-113429Z.dump'
$expectedHash = 'C92E66506DB23375001CFB9022E98DD1553BF200298D9AFF20C0AB983312ACF8'
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
    (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne $expectedHash)
    { throw 'El respaldo previo a I20 no coincide con el checksum registrado.' }
$sql = @'
BEGIN TRANSACTION READ ONLY;
SELECT current_database(), count(*), max("MigrationId") FROM "__EFMigrationsHistory"
  GROUP BY current_database();
SELECT 'writers', count(*) FROM pg_stat_activity WHERE datname=current_database()
  AND pid<>pg_backend_pid() AND state<>'idle';
SELECT 'fixtures', count(*) FROM platform_access.companies
  WHERE "Name" LIKE 'I20 package fixture %' OR "Name" LIKE 'I06 fixture %';
SELECT 'companies', count(*) FROM platform_access.companies;
SELECT 'users', count(*) FROM platform_access.users;
SELECT 'codes', count(*) FROM catalog_pricing.product_codes;
SELECT 'reads', count(*) FROM inventory.receipt_package_reads;
SELECT 'receipts', count(*) FROM inventory.receipt_documents;
SELECT 'movements', count(*) FROM inventory.inventory_movements;
SELECT 'onhand', coalesce(sum("OnHand"), 0) FROM inventory.branch_inventory;
COMMIT;
'@
$pre = $sql | docker exec -i $container psql -U postgres -d carnicerias_test_visual -v ON_ERROR_STOP=1 -At -f -
if ($LASTEXITCODE -ne 0 -or
    $pre -notcontains 'carnicerias_test_visual|70|20261106111744_AddReceiptPackageReadsI20' -or
    $pre -notcontains 'writers|0' -or $pre -notcontains 'fixtures|0')
    { throw "Prevuelo I20 fallido: $($pre -join ', ')" }
$baseline = @($pre | Where-Object {
    $_ -match '^(companies|users|codes|reads|receipts|movements|onhand)\|'
})
$passwords = @($inspect[0].Config.Env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' })
if ($passwords.Count -ne 1) { throw 'Falta la credencial del contenedor autorizado.' }
$connection = New-Object System.Data.Common.DbConnectionStringBuilder
$connection['Host'] = '127.0.0.1'
$connection['Port'] = 55433
$connection['Database'] = 'carnicerias_test_visual'
$connection['Username'] = 'postgres'
$connection['Password'] = $passwords[0].Substring('POSTGRES_PASSWORD='.Length)
$previousConnection = [Environment]::GetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING', 'Process')
$previousI20 = [Environment]::GetEnvironmentVariable('CARNICERIAS_I20_WRITES_APPROVED', 'Process')
$previousI06 = [Environment]::GetEnvironmentVariable('CARNICERIAS_I06_WRITES_APPROVED', 'Process')
try {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING',
        $connection.ConnectionString, 'Process')
    [Environment]::SetEnvironmentVariable('CARNICERIAS_I20_WRITES_APPROVED', 'I20_APPROVED', 'Process')
    [Environment]::SetEnvironmentVariable('CARNICERIAS_I06_WRITES_APPROVED', 'I06_APPROVED', 'Process')
    dotnet test (Join-Path $PSScriptRoot '../tests/Carnicerias.IntegrationTests/Carnicerias.IntegrationTests.csproj') `
        --no-restore --filter 'FullyQualifiedName~InventoryPackageVisualEndpointTests|FullyQualifiedName~CatalogCodeSearchVisualEndpointTests' `
        --logger 'console;verbosity=normal' -v:q
    if ($LASTEXITCODE -ne 0) { throw "Las pruebas HTTP I20 fallaron ($LASTEXITCODE)." }
}
finally {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_VISUAL_CONNECTION_STRING', $previousConnection, 'Process')
    [Environment]::SetEnvironmentVariable('CARNICERIAS_I20_WRITES_APPROVED', $previousI20, 'Process')
    [Environment]::SetEnvironmentVariable('CARNICERIAS_I06_WRITES_APPROVED', $previousI06, 'Process')
    $connection['Password'] = ''
}
$post = $sql | docker exec -i $container psql -U postgres -d carnicerias_test_visual -v ON_ERROR_STOP=1 -At -f -
if ($LASTEXITCODE -ne 0 -or $post -notcontains 'fixtures|0' -or
    @($baseline | Where-Object { $post -notcontains $_ }).Count -ne 0)
    { throw "La limpieza del fixture no coincide con el estado previo: $($post -join ', ')" }
$post
