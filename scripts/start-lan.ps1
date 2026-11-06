# Hosts the app for other devices on this network. Run from anywhere: .\scripts\start-lan.ps1
# See "Host on Your Local Network" in README.md.

$port = 5105
$mailpitPort = 8025

# The address of the adapter that holds the default route, so VPN and virtual adapters are skipped.
$ip = Get-NetIPConfiguration |
    Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } |
    Select-Object -First 1 -ExpandProperty IPv4Address |
    Select-Object -First 1 -ExpandProperty IPAddress

if (-not $ip) {
    Write-Error 'No network connection found. Connect to a network and try again.'
    exit 1
}

# Links built outside a request to this address: table QR codes and links in emails.
$env:TableQr__BaseUrl = "http://${ip}:$port"
$env:Email__SiteUrl = "http://${ip}:$port"

Write-Host ''
Write-Host "  Customer site : http://${ip}:$port"
Write-Host "  Admin portal  : http://admin.$ip.nip.io:$port"
Write-Host "  Mailpit inbox : http://${ip}:$mailpitPort"
Write-Host ''

if (-not (Get-NetFirewallRule -DisplayName 'RROMS LAN' -ErrorAction SilentlyContinue)) {
    Write-Warning 'Other devices are blocked until the firewall allows them. In an Administrator PowerShell, run:'
    Write-Host "  New-NetFirewallRule -DisplayName 'RROMS LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $port,$mailpitPort -RemoteAddress LocalSubnet -Profile Any"
    Write-Host ''
}

if (-not (Get-NetTCPConnection -State Listen -LocalPort $mailpitPort -ErrorAction SilentlyContinue)) {
    Write-Warning 'Mailpit is not running, so login codes cannot be read. Start it in another terminal with: mailpit'
    Write-Host ''
}

dotnet run --project (Split-Path $PSScriptRoot -Parent) --launch-profile lan
