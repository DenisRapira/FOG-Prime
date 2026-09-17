param([ValidateRange(1, 10)][int]$TimeoutSeconds = 4)

$ErrorActionPreference = 'Stop'
$curl = (Get-Command curl.exe -ErrorAction Stop).Source
# Candidates, not permanent DNS overrides. TLS hostname validation stays enabled.
# Source: Flowseal/zapret-discord-youtube tag 1.10.2, .service/hosts.
$candidates = @('162.159.138.232', '162.159.137.232', '162.159.128.233', '162.159.135.232')
foreach ($domain in @('discord.com', 'updates.discord.com')) {
    $dns = @(Resolve-DnsName $domain -Type A -ErrorAction SilentlyContinue |
        Where-Object IPAddress | Select-Object -ExpandProperty IPAddress)
    $addresses = @($dns + $candidates | Select-Object -Unique)
    $url = if ($domain -eq 'discord.com') { 'https://discord.com/api/v10/gateway' } else { 'https://updates.discord.com/' }
    foreach ($address in $addresses) {
        $parsed = $null
        if (-not [Net.IPAddress]::TryParse($address, [ref]$parsed)) { continue }
        $result = & $curl --noproxy '*' --resolve "${domain}:443:$address" --connect-timeout 2 --max-time $TimeoutSeconds --silent --output NUL --write-out '%{http_code} %{time_total}' $url
        [pscustomobject]@{
            Host = $domain
            Address = $address
            InCurrentDns = $dns -contains $address
            CurlExit = $LASTEXITCODE
            HttpStatusAndSeconds = $result
        }
    }
}
Write-Host 'Read-only test. Results describe the current route, including any active VPN. HTTP alone does not verify Discord startup.'
