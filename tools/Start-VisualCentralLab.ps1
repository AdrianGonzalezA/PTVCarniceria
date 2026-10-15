param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Provision', 'Check', 'Start', 'Verify')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$containerName = 'carnicerias-pos-test-20261007'
$databaseName = 'carnicerias_test_visual'
$centralCompanyId = '802dcfdf-c653-443e-bb73-7d34cdb42d46'
$remoteCompanyId = 'a07b1e5e-9ef3-4e7c-9540-177ebed5084c'
$branches = @(
    @{ Port = 5246; InstallationId = 'bc852f7f-2eb4-4416-b3a2-71ed00b7de91'; BranchId = '0fa444b7-de6a-4477-968a-03eb068add00'; SecretReference = 'VISUAL_BRANCH' },
    @{ Port = 5247; InstallationId = '170da9e8-0c31-403d-ac3a-78439e8411b7'; BranchId = 'b108c7d4-9805-432a-92e7-246e4418c83d'; SecretReference = 'VISUAL_NORTH' },
    @{ Port = 5248; InstallationId = '822e4898-c7ea-4d7c-a8cb-33f500942bb1'; BranchId = 'd7f87213-287a-4b91-b4fc-726d74c717db'; SecretReference = 'VISUAL_SOUTH' }
)
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$privateDirectory = Join-Path $root '.local-backups'
$vaultPath = Join-Path $privateDirectory 'visual-central-lab.dpapi'
$branchExecutable = Join-Path $privateDirectory 'runtime\branch\Carnicerias.Api.exe'
$centralExecutable = Join-Path $privateDirectory 'runtime\central\Carnicerias.Central.Api.exe'
$entropy = [Text.Encoding]::UTF8.GetBytes('Carnicerias.VisualCentralLab.v1')

function New-RandomHex {
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
}

function Read-Vault {
    if (-not (Test-Path -LiteralPath $vaultPath -PathType Leaf)) {
        throw 'Falta aprovisionar el almacén protegido del laboratorio Visual.'
    }
    $ciphertext = [IO.File]::ReadAllBytes($vaultPath)
    $plaintext = [Security.Cryptography.ProtectedData]::Unprotect(
        $ciphertext, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try {
        $data = [Text.Encoding]::UTF8.GetString($plaintext) | ConvertFrom-Json
        $connection = New-Object System.Data.Common.DbConnectionStringBuilder
        $connection.set_ConnectionString($data.ConnectionString)
        if ($data.Version -ne 1 -or @($data.Tokens.PSObject.Properties).Count -ne 3 -or
            $data.Key -notmatch '^[0-9a-f]{64}$' -or
            $connection['Host'] -ne '127.0.0.1' -or $connection['Port'] -ne '55433' -or
            $connection['Database'] -ne $databaseName -or $connection['Username'] -ne 'postgres') {
            throw 'El almacén protegido no corresponde a este laboratorio.'
        }
        foreach ($branch in $branches) {
            $token = $data.Tokens.($branch.SecretReference)
            if ($token -notmatch '^[0-9a-f]{64}$') {
                throw 'Falta una credencial protegida de sucursal.'
            }
        }
        return $data
    }
    finally {
        [Array]::Clear($plaintext, 0, $plaintext.Length)
    }
}

function Assert-LabDatabase {
    $inspection = docker inspect $containerName | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $inspection.Count -ne 1 -or
        $inspection[0].Name -ne "/$containerName" -or
        $inspection[0].NetworkSettings.Ports.'5432/tcp'[0].HostIp -ne '127.0.0.1' -or
        $inspection[0].NetworkSettings.Ports.'5432/tcp'[0].HostPort -ne '55433') {
        throw 'El contenedor o el puerto PostgreSQL no corresponde al laboratorio autorizado.'
    }
    $actualDatabase = docker exec $containerName psql -U postgres -d $databaseName -At -c 'select current_database();'
    if ($LASTEXITCODE -ne 0 -or $actualDatabase.Trim() -ne $databaseName) {
        throw 'La única base autorizada no está disponible.'
    }
    $lines = @(docker exec $containerName psql -U postgres -d $databaseName -At -c 'select to_jsonb(i) from central_access.installations i;')
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo comprobar el registro central de sucursales.' }
    $installations = @($lines | ForEach-Object { $_ | ConvertFrom-Json })
    foreach ($branch in $branches) {
        $matching = @($installations | Where-Object { $_.InstallationId -eq $branch.InstallationId })
        if ($matching.Count -ne 1 -or $matching[0].CentralCompanyId -ne $centralCompanyId -or
            $matching[0].RemoteCompanyId -ne $remoteCompanyId -or
            $matching[0].RemoteBranchId -ne $branch.BranchId -or
            $matching[0].DestinationUrl -ne "http://127.0.0.1:$($branch.Port)/" -or
            $matching[0].SecretReference -ne $branch.SecretReference -or
            -not $matching[0].IsActive) {
            throw "La instalación Visual del puerto $($branch.Port) no coincide con la base; no se reinicia."
        }
    }
    return $inspection[0]
}

function Start-WithEnvironment($executable, $port, $settings) {
    $before = @{}
    foreach ($name in $settings.Keys) {
        $before[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
    }
    try {
        return Start-Process -FilePath $executable -ArgumentList '--urls', "http://127.0.0.1:$port" `
            -WorkingDirectory (Split-Path $executable) -WindowStyle Hidden -PassThru
    }
    finally {
        foreach ($name in $settings.Keys) {
            [Environment]::SetEnvironmentVariable($name, $before[$name], 'Process')
        }
    }
}

$inspection = Assert-LabDatabase
if ($Action -eq 'Provision') {
    if (Test-Path -LiteralPath $vaultPath) {
        throw 'El almacén ya existe. No se regeneran claves ni tokens automáticamente.'
    }
    $passwordEntries = @($inspection.Config.Env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' })
    if ($passwordEntries.Count -ne 1) { throw 'No se encontró una contraseña PostgreSQL única en el contenedor.' }
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $builder['Host'] = '127.0.0.1'
    $builder['Port'] = 55433
    $builder['Database'] = $databaseName
    $builder['Username'] = 'postgres'
    $builder['Password'] = $passwordEntries[0].Substring('POSTGRES_PASSWORD='.Length)
    $tokens = @{}
    foreach ($branch in $branches) { $tokens[$branch.SecretReference] = New-RandomHex }
    $data = @{ Version = 1; ConnectionString = $builder.ConnectionString; Key = (New-RandomHex); Tokens = $tokens }
    $plaintext = [Text.Encoding]::UTF8.GetBytes(($data | ConvertTo-Json -Compress -Depth 4))
    try {
        $ciphertext = [Security.Cryptography.ProtectedData]::Protect(
            $plaintext, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
        $file = [IO.File]::Open($vaultPath, [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $file.Write($ciphertext, 0, $ciphertext.Length) }
        finally { $file.Dispose() }
    }
    finally {
        [Array]::Clear($plaintext, 0, $plaintext.Length)
    }
    $null = Read-Vault
    Write-Output 'Almacén DPAPI aprovisionado para la cuenta Windows actual; no se muestran credenciales.'
    return
}

$vault = Read-Vault
if ($Action -eq 'Check') {
    foreach ($path in @($branchExecutable, $centralExecutable)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Falta publicar: $path" }
    }
    Write-Output 'Base única, instalaciones Visual, credenciales protegidas y binarios: verificados.'
    return
}

if ($Action -eq 'Start') {
    foreach ($port in @(5245, 5246, 5247, 5248, 5250)) {
        if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
            throw "El puerto $port ya está ocupado; no se inicia un proceso duplicado."
        }
    }
    foreach ($path in @($branchExecutable, $centralExecutable)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Falta publicar: $path" }
    }
    $approved = @()
    foreach ($branch in $branches) {
        $endpoint = @{ url = "http://127.0.0.1:$($branch.Port)/"; addresses = @('127.0.0.1') }
        $approved += $endpoint
        $approved += @{ url = $endpoint.url; addresses = @('127.0.0.1'); centralCompanyId = $centralCompanyId }
    }
    $connection = $vault.ConnectionString
    $started = @()
    try {
        foreach ($branch in $branches) {
            $token = $vault.Tokens.($branch.SecretReference)
            $tokenBytes = New-Object byte[] 32
            for ($index = 0; $index -lt 32; $index++) {
                $tokenBytes[$index] = [Convert]::ToByte($token.Substring($index * 2, 2), 16)
            }
            $hash = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($tokenBytes))).Replace('-', '').ToLowerInvariant()
            $settings = @{
                CARNICERIAS_CONNECTION_STRING = $connection
                ConnectionStrings__PlatformAccess = $connection
                AllowedHosts = '127.0.0.1;localhost'
                Federation__Enabled = 'true'
                Federation__InstallationId = $branch.InstallationId
                Federation__CompanyId = $remoteCompanyId
                Federation__BranchId = $branch.BranchId
                Federation__CredentialSha256 = $hash
                Federation__NextCredentialSha256 = ''
            }
            $started += Start-WithEnvironment $branchExecutable $branch.Port $settings
            Write-Output "Sucursal Visual $($branch.Port): PID $($started[-1].Id)"
        }
        $centralSettings = @{
            CARNICERIAS_CENTRAL_CONNECTION_STRING = $connection
            ConnectionStrings__CentralAccess = $connection
            CARNICERIAS_CENTRAL_APPROVED_BRANCHES = ($approved | ConvertTo-Json -Compress -Depth 4)
            CARNICERIAS_CENTRAL_ALLOW_LOOPBACK_HTTP = 'true'
            CARNICERIAS_CENTRAL_CREDENTIAL_ACTIVE_KEY_VERSION = '1'
            CARNICERIAS_CENTRAL_CREDENTIAL_KEY_V1 = $vault.Key
            AllowedHosts = '127.0.0.1;localhost'
        }
        foreach ($branch in $branches) {
            $centralSettings["CARNICERIAS_CENTRAL_BRANCH_SECRET_$($branch.SecretReference)"] = $vault.Tokens.($branch.SecretReference)
        }
        foreach ($port in @(5245, 5250)) {
            $started += Start-WithEnvironment $centralExecutable $port $centralSettings
            Write-Output "Central $port`: PID $($started[-1].Id)"
        }
    }
    catch {
        foreach ($process in $started) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
        throw
    }
    Write-Output 'Procesos iniciados. Ejecutá -Action Verify para comprobar puertos y credenciales.'
    return
}

foreach ($branch in $branches) {
    $url = "http://127.0.0.1:$($branch.Port)/api/central/v1/installation"
    $response = Invoke-RestMethod -Uri $url -Headers @{ Authorization = "Bearer $($vault.Tokens.($branch.SecretReference))" } -TimeoutSec 10
    if ($response.installationId -ne $branch.InstallationId -or
        $response.companyId -ne $remoteCompanyId -or $response.branchId -ne $branch.BranchId) {
        throw "Identidad incorrecta en el puerto $($branch.Port)."
    }
    Write-Output "Sucursal $($branch.Port): identidad y credencial verificadas."
}
foreach ($port in @(5245, 5250)) {
    $health = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/health" -TimeoutSec 10
    if ($health.status -ne 'healthy') { throw "Central $port no está saludable." }
    try {
        $null = Invoke-WebRequest -Uri "http://127.0.0.1:$port/api/v1/branch-connections" -UseBasicParsing -TimeoutSec 10
        throw "La ruta de conexiones en $port aceptó una solicitud sin sesión."
    }
    catch [System.Net.WebException] {
        if ([int]$_.Exception.Response.StatusCode -ne 401) { throw }
    }
    Write-Output "Central $port`: saludable y ruta nueva protegida por sesión."
}
