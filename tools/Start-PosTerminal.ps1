param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,39}$')]
    [string]$TerminalProfile,
    [switch]$Provision
)

$ErrorActionPreference = 'Stop'
$executable = [IO.Path]::GetFullPath($ExecutablePath)
$configuration = [IO.Path]::GetFullPath($ConfigPath)
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "No se encontró Electron: $executable"
}
if (-not (Test-Path -LiteralPath $configuration -PathType Leaf)) {
    throw "No se encontró la configuración: $configuration"
}
if ($Provision) {
    $protected = Read-Host 'Credencial de esta caja (solo primera vinculación)' -AsSecureString
    $credential = [System.Net.NetworkCredential]::new('', $protected).Password
    if (-not $credential) { throw 'La credencial de caja no puede estar vacía.' }
    $env:CARNICERIAS_POS_TERMINAL_TOKEN = $credential
}

try {
    & $executable "--pos-profile=$TerminalProfile" "--pos-config=$configuration"
}
finally {
    if ($Provision) {
        Remove-Item Env:\CARNICERIAS_POS_TERMINAL_TOKEN -ErrorAction SilentlyContinue
        $credential = $null
    }
}
