param(
    [ValidateSet('All', 'Pos', 'Portals', 'Visual', 'VisualCentral', 'Rustica', 'RusticaBranch')]
    [string]$Group = 'All'
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Assert-OwnedLocalService($target, [object[]]$listeners, $process) {
    if ($listeners.Count -eq 0 -or $null -eq $process) {
        throw "No se pudo identificar el proceso del puerto $($target.Port)."
    }
    $processIds = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($processIds.Count -ne 1 -or [int]$process.ProcessId -ne [int]$processIds[0]) {
        throw "El puerto $($target.Port) no pertenece a un único proceso conocido."
    }
    foreach ($listener in $listeners) {
        if ([int]$listener.LocalPort -ne [int]$target.Port -or
            $target.Addresses -notcontains $listener.LocalAddress) {
            throw "El puerto $($target.Port) escucha en una dirección no autorizada."
        }
    }

    $commandLine = [string]$process.CommandLine
    if ($target.Kind -eq 'Api') {
        $expectedPath = [IO.Path]::GetFullPath($target.Executable)
        if ($process.Name -ine [IO.Path]::GetFileName($expectedPath) -or
            $process.ExecutablePath -ine $expectedPath -or
            $commandLine -notmatch '(?i)--urls\s+' -or
            $commandLine -notmatch ([regex]::Escape($target.Url) + '(?=\s|"|$)')) {
            throw "El proceso del puerto $($target.Port) no es la API esperada; no se detuvo."
        }
        return
    }
    if ($target.Kind -eq 'Angular') {
        $projectPrefix = [IO.Path]::GetFullPath($target.Project).TrimEnd('\') + '\node_modules'
        if ($process.Name -ine 'node.exe' -or
            $commandLine.IndexOf($projectPrefix, [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
            $commandLine.IndexOf('@angular\cli\bin\ng.js', [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
            $commandLine -notmatch '(?i)\bserve\b' -or
            ($target.PortArgument -and $commandLine.IndexOf($target.PortArgument, [StringComparison]::OrdinalIgnoreCase) -lt 0) -or
            ($target.Proxy -and $commandLine.IndexOf($target.Proxy, [StringComparison]::OrdinalIgnoreCase) -lt 0)) {
            throw "El proceso del puerto $($target.Port) no es el Angular esperado; no se detuvo."
        }
        return
    }
    throw "Tipo de servicio no reconocido en el puerto $($target.Port)."
}

function Get-ListeningConnections([int]$port) {
    try {
        Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop
    }
    catch {
        if ($_.FullyQualifiedErrorId -eq 'CmdletizationQuery_NotFound,Get-NetTCPConnection') { return }
        throw
    }
}

function Stop-OwnedLocalServices([object[]]$targets) {
    $verified = @()
    Write-Verbose "Objetivos: $($targets.Count)."
    foreach ($target in $targets) {
        Write-Verbose "Buscando escucha en $($target.Port)."
        $listeners = @(Get-ListeningConnections $target.Port)
        Write-Verbose "Escuchas encontradas: $($listeners.Count)."
        if ($listeners.Count -eq 0) { continue }
        $processIds = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
        if ($processIds.Count -ne 1) {
            throw "El puerto $($target.Port) no pertenece a un único proceso; no se detuvo ningún servicio."
        }
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($processIds[0])"
        Assert-OwnedLocalService $target $listeners $process
        $verified += [pscustomobject]@{
            Target = $target
            ProcessId = [int]$process.ProcessId
            CreationDate = $process.CreationDate
        }
    }

    foreach ($entry in $verified) {
        $listeners = @(Get-ListeningConnections $entry.Target.Port)
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($entry.ProcessId)"
        Assert-OwnedLocalService $entry.Target $listeners $process
        if ($process.CreationDate -ne $entry.CreationDate) {
            throw "Cambió el proceso del puerto $($entry.Target.Port); se interrumpió la parada."
        }
        Stop-Process -Id $entry.ProcessId -ErrorAction Stop
        Write-Output "Detenido $($entry.Target.Kind) del puerto $($entry.Target.Port) (PID $($entry.ProcessId))."
    }

    foreach ($entry in $verified) {
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        while (Get-ListeningConnections $entry.Target.Port) {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "El puerto $($entry.Target.Port) sigue ocupado después de detener el proceso."
            }
            Start-Sleep -Milliseconds 100
        }
    }
    if ($verified.Count -eq 0) { Write-Output 'Los puertos del grupo ya estaban libres.' }
}

function New-ApiTarget($port, $executable, $hostName, [string[]]$addresses) {
    return [pscustomobject]@{
        Port = $port; Kind = 'Api'; Addresses = $addresses
        Executable = $executable; Url = "http://$($hostName):$port"
    }
}

function New-AngularTarget($port, $project, $proxy = '', $portArgument = '') {
    return [pscustomobject]@{
        Port = $port; Kind = 'Angular'; Addresses = @('127.0.0.1', '::1')
        Project = $project; Proxy = $proxy; PortArgument = $portArgument
    }
}

$centralExecutable = Join-Path $root '.local-backups\runtime\central\Carnicerias.Central.Api.exe'
$branchExecutable = Join-Path $root '.local-backups\runtime\branch\Carnicerias.Api.exe'
$managedExecutable = Join-Path $root '.local-backups\runtime\branch-managed-alta-v2\Carnicerias.Api.exe'
$centralWeb = Join-Path $root 'src\Carnicerias.Central.Web'
$posTargets = @(
    (New-ApiTarget 5197 (Join-Path $root 'src\Carnicerias.Api\bin\Release\net10.0\Carnicerias.Api.exe') 'localhost' @('127.0.0.1', '::1')),
    (New-AngularTarget 4200 (Join-Path $root 'src\Carnicerias.Web'))
)
$portalTargets = @(
    (New-AngularTarget 4300 $centralWeb '' '--port 4300'),
    (New-AngularTarget 4301 $centralWeb 'proxy.superadmin.local.json' '--port 4301')
)
$visualTargets = @(
    (New-ApiTarget 5245 $centralExecutable '127.0.0.1' @('127.0.0.1')),
    (New-ApiTarget 5246 $branchExecutable '127.0.0.1' @('127.0.0.1')),
    (New-ApiTarget 5247 $branchExecutable '127.0.0.1' @('127.0.0.1')),
    (New-ApiTarget 5248 $branchExecutable '127.0.0.1' @('127.0.0.1')),
    (New-ApiTarget 5250 $centralExecutable '127.0.0.1' @('127.0.0.1'))
)
$rusticaTargets = @(
    (New-ApiTarget 5249 $managedExecutable '127.0.0.1' @('127.0.0.1')),
    (New-ApiTarget 5251 $centralExecutable '127.0.0.1' @('127.0.0.1')),
    (New-AngularTarget 4302 $centralWeb (Join-Path $root 'tools\rustica-managed-proxy.conf.json') '--port 4302')
)

$targets = switch ($Group) {
    'All' { @($posTargets) + @($portalTargets) + @($visualTargets) + @($rusticaTargets) }
    'Pos' { $posTargets }
    'Portals' { $portalTargets }
    'Visual' { $visualTargets }
    'VisualCentral' { @($visualTargets | Where-Object { $_.Port -in @(5245, 5250) }) }
    'Rustica' { $rusticaTargets }
    'RusticaBranch' { @($rusticaTargets | Where-Object { $_.Port -eq 5249 }) }
}
Write-Verbose "Grupo $($Group)."
Stop-OwnedLocalServices $targets
