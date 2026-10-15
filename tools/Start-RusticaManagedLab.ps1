param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Check', 'Start', 'StartBranch', 'Verify')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$container = 'carnicerias-pos-test-20261007'
$database = 'carnicerias_test_visual'
$branchCompanyId = '102ee262-53e1-48ed-9756-7c2c84bccb44'
$branchId = '0d5723bb-ac42-4f69-ab3a-4a23591abdf2'
$centralCompanyId = '11613dd2-0fb6-44cc-873d-804933aca921'
$vaultPath = Join-Path $root '.local-backups\visual-central-lab.dpapi'
$branchExecutable = Join-Path $root '.local-backups\runtime\branch-managed-alta-v2\Carnicerias.Api.exe'
$centralExecutable = Join-Path $root '.local-backups\runtime\central\Carnicerias.Central.Api.exe'
$webDirectory = Join-Path $root 'src\Carnicerias.Central.Web'
$proxyPath = Join-Path $root 'tools\rustica-managed-proxy.conf.json'
$logDirectory = Join-Path $root '.local-backups\runtime'

function Assert-Lab {
    $inspection = docker inspect $container | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $inspection.Count -ne 1 -or
        $inspection[0].Name -ne "/$container" -or
        $inspection[0].NetworkSettings.Ports.'5432/tcp'[0].HostIp -ne '127.0.0.1' -or
        $inspection[0].NetworkSettings.Ports.'5432/tcp'[0].HostPort -ne '55433') {
        throw 'El PostgreSQL del laboratorio no corresponde al contenedor autorizado.'
    }
    $actual = docker exec $container psql -U postgres -d $database -At -c 'select current_database();'
    if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ne $database) {
        throw 'La unica base de aplicacion autorizada no esta disponible.'
    }
    $sql = "select count(*) from platform_access.branches b join platform_access.companies c on to_jsonb(c)->>'Id'=to_jsonb(b)->>'CompanyId' where to_jsonb(b)->>'Id'='$branchId' and to_jsonb(c)->>'Id'='$branchCompanyId' and (to_jsonb(b)->>'IsActive')::boolean and (to_jsonb(c)->>'IsActive')::boolean;"
    $count = docker exec $container psql -U postgres -d $database -At -c $sql
    if ($LASTEXITCODE -ne 0 -or $count.Trim() -ne '1') {
        throw 'La instalacion local no corresponde a La Rustica / Alta Cordoba.'
    }
    $centralSql = "select count(*) from central_access.companies c where to_jsonb(c)->>'Id'='$centralCompanyId' and to_jsonb(c)->>'Code'='LARUSTICA' and (to_jsonb(c)->>'IsActive')::boolean;"
    $centralCount = docker exec $container psql -U postgres -d $database -At -c $centralSql
    if ($LASTEXITCODE -ne 0 -or $centralCount.Trim() -ne '1') {
        throw 'La empresa central La Rustica no corresponde a este laboratorio.'
    }
    foreach ($path in @($vaultPath, $branchExecutable, $centralExecutable, $proxyPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Falta $path" }
    }
}

function Read-Vault {
    $entropy = [Text.Encoding]::UTF8.GetBytes('Carnicerias.VisualCentralLab.v1')
    $plaintext = [Security.Cryptography.ProtectedData]::Unprotect(
        [IO.File]::ReadAllBytes($vaultPath), $entropy,
        [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { $vault = [Text.Encoding]::UTF8.GetString($plaintext) | ConvertFrom-Json }
    finally { [Array]::Clear($plaintext, 0, $plaintext.Length) }
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $builder.set_ConnectionString($vault.ConnectionString)
    if ($vault.Version -ne 1 -or $vault.Key -notmatch '^[0-9a-f]{64}$' -or
        $builder['Host'] -ne '127.0.0.1' -or $builder['Port'] -ne '55433' -or
        $builder['Database'] -ne $database -or $builder['Username'] -ne 'postgres') {
        throw 'El almacen protegido no corresponde a este laboratorio.'
    }
    return $vault
}

function Start-WithEnvironment($executable, $port, $settings, $name) {
    $previous = @{}
    foreach ($key in $settings.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process')
    }
    try {
        return Start-Process -FilePath $executable -ArgumentList '--urls', "http://127.0.0.1:$port" `
            -WorkingDirectory (Split-Path $executable) -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $logDirectory "$name-stdout.log") `
            -RedirectStandardError (Join-Path $logDirectory "$name-stderr.log")
    }
    finally {
        foreach ($key in $settings.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process')
        }
    }
}

function New-BranchSettings($vaultData) {
    return @{
        CARNICERIAS_CONNECTION_STRING = $vaultData.ConnectionString
        ConnectionStrings__PlatformAccess = $vaultData.ConnectionString
        AllowedHosts = '127.0.0.1;localhost'
        Federation__Enabled = 'true'
        Federation__Mode = 'managed'
        Federation__CompanyId = $branchCompanyId
        Federation__BranchId = $branchId
        Federation__AllowLabLoopback = 'true'
    }
}

Assert-Lab
$vault = Read-Vault
if ($Action -eq 'Check') {
    Write-Output 'Base unica, sucursal Alta Cordoba, binarios y almacen DPAPI verificados.'
    return
}
if ($Action -eq 'StartBranch') {
    if (Get-NetTCPConnection -LocalPort 5249 -State Listen -ErrorAction SilentlyContinue) {
        throw 'El puerto 5249 ya esta ocupado; no se reemplaza su proceso.'
    }
    $process = Start-WithEnvironment $branchExecutable 5249 (New-BranchSettings $vault) 'rustica-alta-branch'
    Write-Output "Sucursal Alta Cordoba 5249 PID $($process.Id)."
    return
}
if ($Action -eq 'Start') {
    foreach ($port in @(5249, 5251, 4302)) {
        if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
            throw "El puerto $port ya esta ocupado; no se reemplaza su proceso."
        }
    }
    $approved = ConvertTo-Json -InputObject @(@{
        url = 'http://127.0.0.1:5249/'
        addresses = @('127.0.0.1')
        centralCompanyId = $centralCompanyId
    }) -Compress -Depth 4
    $started = @()
    try {
        $started += Start-WithEnvironment $branchExecutable 5249 (New-BranchSettings $vault) 'rustica-alta-branch'
        $started += Start-WithEnvironment $centralExecutable 5251 @{
            CARNICERIAS_CENTRAL_CONNECTION_STRING = $vault.ConnectionString
            ConnectionStrings__CentralAccess = $vault.ConnectionString
            CARNICERIAS_CENTRAL_APPROVED_BRANCHES = $approved
            CARNICERIAS_CENTRAL_ALLOW_LOOPBACK_HTTP = 'true'
            CARNICERIAS_CENTRAL_CREDENTIAL_ACTIVE_KEY_VERSION = '1'
            CARNICERIAS_CENTRAL_CREDENTIAL_KEY_V1 = $vault.Key
            AllowedHosts = '127.0.0.1;localhost'
        } 'rustica-managed-central'
        $started += Start-Process -FilePath 'npm.cmd' -ArgumentList @(
            'run', 'start', '--', '--host', 'localhost', '--port', '4302',
            '--proxy-config', $proxyPath) -WorkingDirectory $webDirectory `
            -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $logDirectory 'rustica-managed-web-stdout.log') `
            -RedirectStandardError (Join-Path $logDirectory 'rustica-managed-web-stderr.log')
    }
    catch {
        foreach ($process in $started) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
        throw
    }
    Write-Output "Sucursal 5249 PID $($started[0].Id); central 5251 PID $($started[1].Id); sitio 4302 PID $($started[2].Id)."
    Write-Output 'Esperar a que Angular compile y ejecutar -Action Verify.'
    return
}

foreach ($url in @('http://127.0.0.1:5249/api/health',
    'http://127.0.0.1:5251/api/health', 'http://localhost:4302/api/health')) {
    $health = Invoke-RestMethod -Uri $url -TimeoutSec 10
    if ($health.status -ne 'healthy') { throw "Sin salud en $url" }
}
Write-Output 'Sucursal, central y proxy Angular saludables. El token no se lee ni se muestra.'
