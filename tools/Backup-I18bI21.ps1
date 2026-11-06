$ErrorActionPreference = 'Stop'
$container = 'carnicerias-pos-test-20261007'
$database = 'carnicerias_test_visual'
$expectedId = '9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0'
$expectedVolume = '1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d'
$inspection = @(docker inspect $container | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $inspection.Count -ne 1) { throw 'No existe el contenedor autorizado.' }
$instance = $inspection[0]
if ($instance.Id -ne $expectedId -or -not $instance.State.Running -or
    $instance.NetworkSettings.Ports.'5432/tcp'[0].HostIp -ne '127.0.0.1' -or
    $instance.NetworkSettings.Ports.'5432/tcp'[0].HostPort -ne '55433' -or
    @($instance.Mounts | Where-Object { $_.Name -eq $expectedVolume }).Count -ne 1) {
    throw 'Identidad, volumen o puerto del contenedor no coinciden.'
}
$sql = 'SELECT current_database(), count(*), max("MigrationId") FROM "__EFMigrationsHistory";'
$history = @($sql | docker exec -i $container psql -U postgres -d $database -v ON_ERROR_STOP=1 -At)
if ($LASTEXITCODE -ne 0 -or $history.Count -ne 1 -or
    $history[0] -ne 'carnicerias_test_visual|70|20261106111744_AddReceiptPackageReadsI20') {
    throw "Historial inesperado: $($history -join ', ')"
}
$sql = 'SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid();'
$writers = @($sql | docker exec -i $container psql -U postgres -d $database -v ON_ERROR_STOP=1 -At)
if ($LASTEXITCODE -ne 0 -or $writers.Count -ne 1 -or $writers[0] -ne '0') {
    throw "Hay conexiones de aplicación activas: $($writers -join ', ')"
}
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmssZ')
$name = "i18b-i21-before-$stamp.dump"
$inside = "/tmp/$name"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$directory = Join-Path $root '.local-backups'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$local = Join-Path $directory $name
docker exec $container pg_dump -U postgres -d $database -Fc -f $inside
if ($LASTEXITCODE -ne 0) { throw 'pg_dump falló.' }
docker cp "${container}:${inside}" $local
if ($LASTEXITCODE -ne 0) { throw 'No se pudo copiar el respaldo.' }
$toc = @(docker exec $container pg_restore -l $inside)
if ($LASTEXITCODE -ne 0 -or $toc.Count -lt 100 -or
    -not ($toc | Select-String -Quiet 'transfer_documents') -or
    -not ($toc | Select-String -Quiet 'receipt_package_reads')) {
    throw 'El respaldo no tiene los objetos esperados.'
}
$localHash = (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash.ToLowerInvariant()
$insideHash = ((docker exec $container sha256sum $inside) -split ' ')[0].ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $localHash -ne $insideHash) {
    throw 'Checksum local y original difieren.'
}
$file = Get-Item -LiteralPath $local
[pscustomobject]@{ Path=$file.FullName; SizeBytes=$file.Length;
    CreatedUtc=$file.CreationTimeUtc; Sha256=$localHash;
    TocEntries=$toc.Count; MigrationCount=70; ContainerId=$instance.Id;
    Volume=$expectedVolume } | ConvertTo-Json -Compress
