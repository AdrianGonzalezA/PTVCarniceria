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
$previousMigration = '20261106160757_AddAnatomicalGroupI22'
$targetMigration = '20261106165321_AddAnatomicalGroupCountSnapshotI22'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Read-Sql([string]$sql) {
    $rows = @($sql | docker exec -i $container psql -U postgres -d $database `
        -v ON_ERROR_STOP=1 -At)
    if ($LASTEXITCODE -ne 0) { throw 'Falló una consulta de verificación.' }
    return $rows
}
function Read-History { return @(Read-Sql 'SELECT count(*), max("MigrationId") FROM "__EFMigrationsHistory";')[0] }
function Read-SaldoFingerprint {
    return @(Read-Sql 'SELECT count(*), coalesce(sum("OnHand"),0), coalesce(sum("Reserved"),0), coalesce(sum("ProtectedOnHand"),0), coalesce(sum("ProtectedReserved"),0), coalesce(sum("Quarantined"),0) FROM inventory.branch_inventory;')[0]
}
function Confirm-NoWriters {
    $connections = @(Read-Sql 'SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid();')
    if ($connections.Count -ne 1 -or $connections[0] -ne '0') {
        throw "Hay conexiones a la base: $($connections -join ', ')"
    }
}

$inspection = @(docker inspect $container | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $inspection.Count -ne 1 -or
    $inspection[0].Id -ne $containerId -or -not $inspection[0].State.Running -or
    $inspection[0].NetworkSettings.Ports.'5432/tcp'[0].HostIp -ne '127.0.0.1' -or
    $inspection[0].NetworkSettings.Ports.'5432/tcp'[0].HostPort -ne '55433' -or
    @($inspection[0].Mounts | Where-Object { $_.Name -eq $volumeId }).Count -ne 1 -or
    @(Read-Sql 'SELECT current_database();')[0] -ne $database) {
    throw 'Contenedor, volumen, puerto o base no autorizados.'
}

if ($Action -eq 'Backup') {
    Confirm-NoWriters
    if ((Read-History) -ne "74|$previousMigration") {
        throw 'El historial no coincide con el estado previo al snapshot I22.'
    }
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmssZ')
    $name = "i22-count-before-$stamp.dump"
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
        -not ($toc | Select-String -Quiet 'physical_count_lines') -or
        -not ($toc | Select-String -Quiet 'products')) {
        throw 'El respaldo no contiene los objetos esperados.'
    }
    $hash = (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash.ToLowerInvariant()
    $insideHash = ((docker exec $container sha256sum $inside) -split ' ')[0].ToLowerInvariant()
    if ($LASTEXITCODE -ne 0 -or $hash -ne $insideHash) {
        throw 'El checksum del respaldo no coincide.'
    }
    $file = Get-Item -LiteralPath $local
    $manifest = [pscustomobject]@{ Database=$database; ContainerId=$containerId;
        Volume=$volumeId; Path=$file.FullName; SizeBytes=$file.Length;
        CreatedUtc=$file.CreationTimeUtc; Sha256=$hash; TocEntries=$toc.Count;
        MigrationCount=74; PreviousMigration=$previousMigration;
        StockFingerprint=(Read-SaldoFingerprint);
        CountLineCount=@(Read-Sql 'SELECT count(*) FROM inventory.physical_count_lines;')[0] }
    $manifestPath = "$local.json"
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Output "Respaldo verificado: $manifestPath"
    Write-Output "SHA-256: $hash"
    return
}

if ($Action -eq 'Apply') {
    if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
        throw 'Indicá -ManifestPath con el manifiesto del respaldo.'
    }
    $resolved = [IO.Path]::GetFullPath($ManifestPath)
    $backupDirectory = [IO.Path]::GetFullPath((Join-Path $root '.local-backups'))
    if (-not $resolved.StartsWith($backupDirectory + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'El manifiesto debe estar dentro de .local-backups.'
    }
    $manifest = Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json
    if ($manifest.Database -ne $database -or $manifest.ContainerId -ne $containerId -or
        $manifest.Volume -ne $volumeId -or $manifest.MigrationCount -ne 74 -or
        $manifest.PreviousMigration -ne $previousMigration -or
        -not (Test-Path -LiteralPath $manifest.Path) -or
        (Get-FileHash -LiteralPath $manifest.Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne
            $manifest.Sha256) {
        throw 'El manifiesto o checksum del respaldo no coincide.'
    }
    Confirm-NoWriters
    if ((Read-History) -ne "74|$previousMigration" -or
        (Read-SaldoFingerprint) -ne $manifest.StockFingerprint -or
        @(Read-Sql 'SELECT count(*) FROM inventory.physical_count_lines;')[0] -ne
            $manifest.CountLineCount) {
        throw 'El historial o los datos cambiaron desde el respaldo.'
    }
    $migrationFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\Carnicerias.Infrastructure\Migrations') -Filter '*_*.cs' |
        Where-Object { $_.Name -notlike '*.Designer.cs' } | Sort-Object Name)
    if ($migrationFiles.Count -ne 75 -or
        $migrationFiles[-1].Name -ne "$targetMigration.cs") {
        throw 'No hay exactamente una migración nueva en código.'
    }
    $passwords = @($inspection[0].Config.Env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' })
    if ($passwords.Count -ne 1) { throw 'Falta la credencial del contenedor.' }
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
        if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación previa.' }
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
    if ((Read-History) -ne "75|$targetMigration") {
        throw 'La migración no quedó como última aplicada.'
    }
    $columns = @(Read-Sql "SELECT count(*) FROM information_schema.columns WHERE table_schema='inventory' AND table_name='physical_count_lines' AND column_name='AnatomicalGroup';")
    $classified = @(Read-Sql 'SELECT count(*) FROM inventory.physical_count_lines WHERE "AnatomicalGroup" IS NOT NULL;')
    if ($columns[0] -ne '1' -or $classified[0] -ne '0') {
        throw 'La estructura o los datos históricos no coinciden.'
    }
    if ($Action -eq 'Apply' -and (Read-SaldoFingerprint) -ne $manifest.StockFingerprint) {
        throw 'Los saldos cambiaron durante la migración.'
    }
    Write-Output "Snapshot I22 verificado en $database; historial 75, conteos históricos sin clasificación inventada y saldos consistentes."
}
