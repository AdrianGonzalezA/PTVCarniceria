$ErrorActionPreference = 'Stop'
$container = 'carnicerias-pos-test-20261007'
$database = 'carnicerias_test_visual'
$expectedId = '9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0'
$expectedVolume = '1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d'
$inspect = @(docker inspect $container | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $inspect.Count -ne 1) { throw 'No se encontró el contenedor autorizado.' }
$instance = $inspect[0]
if ($instance.Id -ne $expectedId -or -not $instance.State.Running -or
    $instance.NetworkSettings.Ports.'5432/tcp'[0].HostIp -ne '127.0.0.1' -or
    $instance.NetworkSettings.Ports.'5432/tcp'[0].HostPort -ne '55433' -or
    @($instance.Mounts | Where-Object { $_.Name -eq $expectedVolume }).Count -ne 1) {
    throw 'Identidad, volumen o puerto de PostgreSQL no coinciden con el laboratorio autorizado.'
}
$preflight = Get-Content -Raw (Join-Path $PSScriptRoot 'sql/20261105_i17_preflight_readonly.sql') |
    docker exec -i $container psql -U postgres -d $database -v ON_ERROR_STOP=1 -At -f -
if ($LASTEXITCODE -ne 0 -or $preflight -notcontains "database|$database|postgres" -or
    $preflight -notcontains 'migrations|64|20261105135907_AddProtectedStockI10' -or
    $preflight -notcontains 'other_connections|0') {
    throw "Prevuelo I17 fallido: $($preflight -join ', ')"
}
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmssZ')
$name = "i17-before-$stamp.dump"
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
    -not ($toc | Select-String -Quiet 'cashier_shifts')) {
    throw 'El respaldo no tiene un TOC válido o faltan objetos esperados.'
}
$localHash = (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash.ToLowerInvariant()
$insideHash = ((docker exec $container sha256sum $inside) -split ' ')[0].ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $localHash -ne $insideHash) {
    throw 'El checksum del respaldo local difiere del original.'
}
$file = Get-Item -LiteralPath $local
[pscustomobject]@{ Path=$file.FullName; SizeBytes=$file.Length; CreatedUtc=$file.CreationTimeUtc;
    Sha256=$localHash; TocEntries=$toc.Count; ContainerId=$instance.Id; Volume=$expectedVolume } |
    ConvertTo-Json -Compress
