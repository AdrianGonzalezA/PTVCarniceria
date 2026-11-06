$ErrorActionPreference = 'Stop'
$container = 'carnicerias-pos-test-20261007'
$database = 'carnicerias_test_visual'
$expectedId = '9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0'
$expectedVolume = '1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$backup = Join-Path $root '.local-backups/i18b-i21-before-20261106-130322Z.dump'
$expectedHash = '0f915b30591f788a9048f5e8068e5b71bc04b8d4237b26ff8927fdcf95947f12'
if (-not (Test-Path -LiteralPath $backup) -or
    (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
    throw 'Falta el respaldo verificado o cambió su checksum.'
}
$inspection = @(docker inspect $container | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $inspection.Count -ne 1) { throw 'No existe el contenedor autorizado.' }
$instance = $inspection[0]
if ($instance.Id -ne $expectedId -or -not $instance.State.Running -or
    $instance.NetworkSettings.Ports.'5432/tcp'[0].HostIp -ne '127.0.0.1' -or
    $instance.NetworkSettings.Ports.'5432/tcp'[0].HostPort -ne '55433' -or
    @($instance.Mounts | Where-Object { $_.Name -eq $expectedVolume }).Count -ne 1) {
    throw 'Identidad, volumen o puerto de PostgreSQL no coinciden.'
}
$sql = 'SELECT current_database(), count(*), max("MigrationId") FROM "__EFMigrationsHistory";'
$history = @($sql | docker exec -i $container psql -U postgres -d $database -v ON_ERROR_STOP=1 -At)
if ($LASTEXITCODE -ne 0 -or $history.Count -ne 1 -or
    $history[0] -ne 'carnicerias_test_visual|70|20261106111744_AddReceiptPackageReadsI20') {
    throw "Historial de migraciones inesperado: $($history -join ', ')"
}
$sql = 'SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid();'
$connections = @($sql | docker exec -i $container psql -U postgres -d $database -v ON_ERROR_STOP=1 -At)
if ($LASTEXITCODE -ne 0 -or $connections.Count -ne 1 -or $connections[0] -ne '0') {
    throw 'Hay conexiones de aplicación activas; no se migra.'
}
$passwords = @($instance.Config.Env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' })
if ($passwords.Count -ne 1) { throw 'Falta la credencial del usuario de la API.' }
$connection = New-Object System.Data.Common.DbConnectionStringBuilder
$connection['Host'] = '127.0.0.1'
$connection['Port'] = 55433
$connection['Database'] = $database
$connection['Username'] = 'postgres'
$connection['Password'] = $passwords[0].Substring('POSTGRES_PASSWORD='.Length)
$previous = [Environment]::GetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING', 'Process')
$started = Get-Date
try {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING',
        $connection.ConnectionString, 'Process')
    & dotnet ef database update 20261106125046_AddPhysicalCountI21 --no-build `
        --project (Join-Path $root 'src/Carnicerias.Infrastructure') `
        --startup-project (Join-Path $root 'src/Carnicerias.Infrastructure') `
        --context PlatformAccessDbContext
    if ($LASTEXITCODE -ne 0) { throw "Falló la migración: $LASTEXITCODE" }
}
finally {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING', $previous, 'Process')
    $connection['Password'] = ''
}
$sql = 'SELECT count(*), max("MigrationId") FROM "__EFMigrationsHistory";'
$after = @($sql | docker exec -i $container psql -U postgres -d $database -v ON_ERROR_STOP=1 -At)
if ($LASTEXITCODE -ne 0 -or $after.Count -ne 1 -or
    $after[0] -ne '72|20261106125046_AddPhysicalCountI21') {
    throw "La verificación posterior no coincide: $($after -join ', ')"
}
[pscustomobject]@{ Database=$database; MigrationsBefore=70;
    MigrationsAfter=72; DurationSeconds=[math]::Round(((Get-Date)-$started).TotalSeconds,1);
    Backup=$backup; BackupSha256=$expectedHash } | ConvertTo-Json -Compress
