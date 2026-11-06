param(
    [ValidateSet('Backup', 'Apply', 'Verify')]
    [string]$Action,
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$container = 'carnicerias-pos-test-20261007'
$database = 'carnicerias_test_visual'
$containerId = '9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0'
$volumeId = '1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d'
$beforeMigration = '20261106125046_AddPhysicalCountI21'
$targetMigration = '20261106151328_AddProductionOrdersI23'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Read-Sql([string]$sql) {
    $rows = @($sql | docker exec -i $container psql -U postgres -d $database `
        -v ON_ERROR_STOP=1 -At)
    if ($LASTEXITCODE -ne 0) { throw 'Falló una verificación SQL.' }
    return $rows
}

function Confirm-Container {
    $inspection = @(docker inspect $container | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0 -or $inspection.Count -ne 1) {
        throw 'No existe el contenedor autorizado.'
    }
    $instance = $inspection[0]
    if ($instance.Id -ne $containerId -or -not $instance.State.Running -or
        $instance.NetworkSettings.Ports.'5432/tcp'[0].HostIp -ne '127.0.0.1' -or
        $instance.NetworkSettings.Ports.'5432/tcp'[0].HostPort -ne '55433' -or
        @($instance.Mounts | Where-Object { $_.Name -eq $volumeId }).Count -ne 1) {
        throw 'Identidad, volumen o puerto del contenedor no coinciden.'
    }
    $identity = @(Read-Sql 'SELECT current_database();')
    if ($identity.Count -ne 1 -or $identity[0] -ne $database) {
        throw 'La base autorizada no responde.'
    }
    return $instance
}

function Confirm-NoWriters {
    $connections = @(Read-Sql 'SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid();')
    if ($connections.Count -ne 1 -or $connections[0] -ne '0') {
        throw "Hay conexiones activas a la aplicación: $($connections -join ', ')"
    }
}

function Read-History {
    $history = @(Read-Sql 'SELECT count(*), max("MigrationId") FROM "__EFMigrationsHistory";')
    if ($history.Count -ne 1) { throw 'No se pudo leer el historial de migraciones.' }
    return $history[0]
}

function Read-SaldoFingerprint {
    $sql = 'SELECT count(*), coalesce(sum("OnHand"),0), coalesce(sum("Reserved"),0), coalesce(sum("ProtectedOnHand"),0), coalesce(sum("ProtectedReserved"),0), coalesce(sum("Quarantined"),0) FROM inventory.branch_inventory;'
    return @(Read-Sql $sql)[0]
}

$instance = Confirm-Container

if ($Action -eq 'Backup') {
    Confirm-NoWriters
    if ((Read-History) -ne "72|$beforeMigration") {
        throw 'El historial no coincide con el estado anterior a I23.'
    }
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmssZ')
    $name = "i23-before-$stamp.dump"
    $inside = "/tmp/$name"
    $directory = Join-Path $root '.local-backups'
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $local = Join-Path $directory $name
    docker exec $container pg_dump -U postgres -d $database -Fc -f $inside
    if ($LASTEXITCODE -ne 0) { throw 'Falló pg_dump.' }
    docker cp "${container}:${inside}" $local
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo copiar el respaldo.' }
    $toc = @(docker exec $container pg_restore -l $inside)
    if ($LASTEXITCODE -ne 0 -or $toc.Count -lt 100 -or
        -not ($toc | Select-String -Quiet 'physical_count_documents') -or
        -not ($toc | Select-String -Quiet 'receipt_package_reads')) {
        throw 'El respaldo no contiene los objetos esperados.'
    }
    $hash = (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash.ToLowerInvariant()
    $insideHash = ((docker exec $container sha256sum $inside) -split ' ')[0].ToLowerInvariant()
    if ($LASTEXITCODE -ne 0 -or $hash -ne $insideHash) {
        throw 'El checksum del respaldo no coincide.'
    }
    $file = Get-Item -LiteralPath $local
    $manifest = [pscustomobject]@{ Database=$database; ContainerId=$instance.Id;
        Volume=$volumeId; Path=$file.FullName; SizeBytes=$file.Length;
        CreatedUtc=$file.CreationTimeUtc; Sha256=$hash; TocEntries=$toc.Count;
        MigrationCount=72; PreviousMigration=$beforeMigration;
        StockFingerprint=(Read-SaldoFingerprint) }
    $manifestPath = "$local.json"
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Output "Respaldo verificado: $manifestPath"
    Write-Output "SHA-256: $hash"
    return
}

if ($Action -eq 'Apply') {
    if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
        throw 'Indicá el manifiesto del respaldo con -ManifestPath.'
    }
    $resolved = [IO.Path]::GetFullPath($ManifestPath)
    $backupDirectory = [IO.Path]::GetFullPath((Join-Path $root '.local-backups'))
    if (-not $resolved.StartsWith($backupDirectory + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'El manifiesto debe estar dentro de .local-backups.'
    }
    $manifest = Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json
    if ($manifest.Database -ne $database -or $manifest.ContainerId -ne $instance.Id -or
        $manifest.Volume -ne $volumeId -or $manifest.MigrationCount -ne 72 -or
        $manifest.PreviousMigration -ne $beforeMigration -or
        -not (Test-Path -LiteralPath $manifest.Path) -or
        (Get-FileHash -LiteralPath $manifest.Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne
            $manifest.Sha256) {
        throw 'El manifiesto o checksum del respaldo no coincide.'
    }
    Confirm-NoWriters
    if ((Read-History) -ne "72|$beforeMigration") {
        throw 'Hay una migración nueva o un cambio inesperado.'
    }
    if ((Read-SaldoFingerprint) -ne $manifest.StockFingerprint) {
        throw 'Los saldos cambiaron desde el respaldo.'
    }
    $migrationFiles = @(Get-ChildItem -LiteralPath (Join-Path $root `
        'src\Carnicerias.Infrastructure\Migrations') -Filter '*_*.cs' |
        Where-Object { $_.Name -notlike '*.Designer.cs' } | Sort-Object Name)
    if ($migrationFiles.Count -ne 73 -or
        $migrationFiles[-1].Name -ne "$targetMigration.cs") {
        throw 'No hay exactamente una migración nueva en código.'
    }
    $passwords = @($instance.Config.Env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' })
    if ($passwords.Count -ne 1) { throw 'Falta la credencial de la API local.' }
    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    $connection['Host'] = '127.0.0.1'
    $connection['Port'] = 55433
    $connection['Database'] = $database
    $connection['Username'] = 'postgres'
    $connection['Password'] = $passwords[0].Substring('POSTGRES_PASSWORD='.Length)
    $previous = [Environment]::GetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING',
            $connection.ConnectionString, 'Process')
        & dotnet build (Join-Path $root 'src\Carnicerias.Infrastructure') --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación previa a la migración.' }
        & dotnet ef database update $targetMigration `
            --project (Join-Path $root 'src\Carnicerias.Infrastructure') `
            --startup-project (Join-Path $root 'src\Carnicerias.Infrastructure') `
            --context PlatformAccessDbContext --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Falló la migración; no se intentó restaurar.' }
    }
    finally {
        [Environment]::SetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING', $previous, 'Process')
        $connection['Password'] = ''
    }
}

if ($Action -eq 'Apply' -or $Action -eq 'Verify') {
    if ((Read-History) -ne "73|$targetMigration") {
        throw 'La migración no quedó como última aplicada.'
    }
    $roles = @(Read-Sql "SELECT count(*) FROM platform_access.roles WHERE `"Code`"='production';")
    $orders = @(Read-Sql "SELECT to_regclass('inventory.production_orders') IS NOT NULL;")
    $pieces = @(Read-Sql 'SELECT count(*) FROM inventory.pieces WHERE "ReservedByProductionOrderId" IS NOT NULL OR "ProductionOrderId" IS NOT NULL;')
    if ($roles[0] -ne '1' -or $orders[0] -ne 't' -or $pieces[0] -ne '0') {
        throw 'La estructura o los roles de Producción no coinciden.'
    }
    if ($Action -eq 'Apply' -and (Read-SaldoFingerprint) -ne $manifest.StockFingerprint) {
        throw 'Los saldos cambiaron durante la migración.'
    }
    Write-Output "I23 verificado en $database; historial 73, rol Producción y saldos consistentes."
}
