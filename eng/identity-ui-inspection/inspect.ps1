#Requires -Version 7.0
<#
.SYNOPSIS
    Interactive page inspector for the local Tellma identity server.

.DESCRIPTION
    Presents a menu of every renderable page in the identity server and drives whatever
    multi-step process is needed to reach the selected one. Pages that are a plain URL are
    simply opened; pages that sit behind a sign-in, an OIDC request, a device grant or an
    emailed link get a scripted flow that does the machine parts (device authorization,
    token redemption, the invitation and Temporary Access Pass APIs) and prints precise
    instructions for the parts only a human can do (typing a code, clicking Allow,
    completing a passkey ceremony).

    Sign-in codes and emailed links are written by the server to its console. When this
    script starts the server itself ("managed" mode) it captures that console to a log
    under the repository's gitignored .tmp directory and reads the codes out automatically.
    When a server is already running that this script did not start ("external" mode)
    everything still works, but the script asks you to paste the code or link from your own
    terminal.

.PARAMETER BaseUrl
    Origin of the running server. Defaults to the launch profile's HTTPS URL.

.PARAMETER AdminEmail
    The seeded dev administrator's email, used as the default in every flow.

.PARAMETER AdminSub
    The seeded dev administrator's subject id, used by the Temporary Access Pass flow.

.PARAMETER NoBrowser
    Print URLs instead of launching a browser.

.EXAMPLE
    pwsh eng/identity-ui-inspection/inspect.ps1

.NOTES
    Prerequisites are the ones in ui-inspection-guide.md beside this script: LocalDB
    running, the dev certificate trusted, and user secrets carrying EnablePasswordSignIn
    plus the local-browser / local-control-plane clients.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'https://localhost:7051',
    [string] $AdminEmail = 'admin@localhost',
    [string] $AdminSub = '00000000-0000-0000-0000-000000000001',
    [switch] $NoBrowser
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------
# Paths and constants
# ---------------------------------------------------------------------------------------

$script:Base = $BaseUrl.TrimEnd('/')
$script:Port = ([uri]$script:Base).Port
# Found by walking up to the solution file rather than counting directories, so moving this
# script does not silently point the server launch at the wrong working directory.
$script:RepoRoot = $PSScriptRoot
while ($script:RepoRoot -and -not (Test-Path -LiteralPath (Join-Path $script:RepoRoot 'Tellma.slnx'))) {
    $script:RepoRoot = Split-Path -Parent $script:RepoRoot
}
if (-not $script:RepoRoot) {
    throw 'Could not locate the repository root (no Tellma.slnx found above this script).'
}

$script:ProjectDir = Join-Path $script:RepoRoot 'src/apps/Tellma.Identity.Web'

# Everything this script generates goes to the repository's .tmp directory, which is
# gitignored — the script and its guide are tracked, their by-products are not. Created on
# demand, because a fresh clone has no .tmp at all.
$script:WorkDir = Join-Path $script:RepoRoot '.tmp'
if (-not (Test-Path -LiteralPath $script:WorkDir)) {
    New-Item -ItemType Directory -Path $script:WorkDir | Out-Null
}

$script:OutLog = Join-Path $script:WorkDir 'server.log'
$script:ErrLog = Join-Path $script:WorkDir 'server.err.log'
$script:PidFile = Join-Path $script:WorkDir 'server.pid'

# Set only while this script owns the server process; it is what makes automatic code and
# link extraction possible. Null means "we cannot see the server's console".
$script:LogPath = $null
$script:ServerMode = 'unknown'   # managed | external | down | unknown
$script:Culture = 'en'           # toggled from the menu; 'ar' appends culture=ar to URLs

# Splat for every call to the local server: the dev certificate is self-signed.
$script:Http = @{ SkipCertificateCheck = $true }

# The authorize request every OIDC-driven page hangs off. The PKCE pair is fixed and
# local-only (verifier: tellma-local-inspection-verifier-0123456789abcdefghijklmnop) and is
# never redeemed here, so pasting it around is harmless.
$script:AuthorizeTemplate = '{0}/connect/authorize?client_id={1}&response_type=code' +
    '&redirect_uri=http%3A%2F%2F127.0.0.1%2Fcallback&scope=openid%20profile%20email' +
    '&code_challenge=OTAUWGLBzoyXFd-eBWRw54MfcImOuK-5HoHKhWWIlc0&code_challenge_method=S256&state=abc123'

$script:DeviceGrantType = 'urn:ietf:params:oauth:grant-type:device_code'

# Cached management-scope token, so a failed invitation does not cost another device grant.
$script:IdentityToken = $null

# ---------------------------------------------------------------------------------------
# Presentation helpers
# ---------------------------------------------------------------------------------------

function Write-Banner {
    param([string] $Text)
    Write-Host ''
    Write-Host "  == $Text " -ForegroundColor White -NoNewline
    Write-Host ('=' * [Math]::Max(0, 66 - $Text.Length)) -ForegroundColor DarkGray
}

function Write-Step {
    param([string] $Text)
    Write-Host "  -> $Text" -ForegroundColor Green
}

function Write-Note {
    param([string] $Text)
    Write-Host "     $Text" -ForegroundColor Gray
}

function Write-Ask {
    param([string] $Text)
    Write-Host "  !! $Text" -ForegroundColor Yellow
}

function Write-Bad {
    param([string] $Text)
    Write-Host "  xx $Text" -ForegroundColor Red
}

function Write-Value {
    param([string] $Label, [string] $Value)
    Write-Host ''
    Write-Host "     $Label " -ForegroundColor Gray -NoNewline
    Write-Host $Value -ForegroundColor Cyan
    Write-Host ''
}

function Wait-Enter {
    param([string] $Text = 'Press Enter to return to the menu')
    Write-Host ''
    Read-Host "  $Text" | Out-Null
}

<#
    True once the user has pressed a key. Waiting loops poll this instead of relying on
    Ctrl+C: a managed server shares this console, so Ctrl+C would take the server down with
    the loop.
#>
function Test-AbortKey {
    try {
        if ([Console]::KeyAvailable) {
            [Console]::ReadKey($true) | Out-Null
            return $true
        }
    }
    catch {
        # Input is redirected (non-interactive host); there is no key to poll.
    }
    return $false
}

# ---------------------------------------------------------------------------------------
# URLs and the browser
# ---------------------------------------------------------------------------------------

<#
    Builds an absolute URL from a server-relative path, appending the culture override when
    the Arabic toggle is on. Note the query string sets no cookie: it applies to that one
    request, which is why the flows below offer the cookie snippet instead.
#>
function Get-Url {
    param([Parameter(Mandatory)][string] $Path)

    $url = if ($Path -match '^https?://') { $Path } else { $script:Base + $Path }
    if ($script:Culture -eq 'ar') {
        $url += $(if ($url.Contains('?')) { '&' } else { '?' }) + 'culture=ar'
    }
    return $url
}

function Open-Url {
    param([Parameter(Mandatory)][string] $Url)

    Write-Host ''
    Write-Host "     $Url" -ForegroundColor Cyan
    Write-Host ''

    if ($NoBrowser) { return }

    # Each platform has its own opener; a failure here is not fatal because the URL has
    # already been printed above.
    try {
        if ($IsWindows) { Start-Process $Url | Out-Null }
        elseif ($IsMacOS) { & open $Url }
        else { & xdg-open $Url 2>$null }
    }
    catch {
        Write-Note 'Could not launch a browser automatically - open the URL above.'
    }
}

<#
    Prints the browser-console snippet that sets the ASP.NET Core culture cookie. Only the
    cookie survives redirects, so any flow that hops through authorize -> login -> code
    needs it rather than the query string.
#>
function Show-CultureCookieHint {
    if ($script:Culture -ne 'ar') { return }
    Write-Ask 'This flow redirects, so the culture query string will be dropped at the first hop.'
    Write-Note 'Run this once in the browser console on the site origin, then restart the flow:'
    Write-Host ''
    Write-Host '     document.cookie = ".AspNetCore.Culture=c%3Dar%7Cuic%3Dar; path=/"' -ForegroundColor Cyan
    Write-Host ''
}

# ---------------------------------------------------------------------------------------
# Server lifecycle
# ---------------------------------------------------------------------------------------

function Test-ServerUp {
    try {
        $response = Invoke-WebRequest -Uri "$script:Base/Identity/Account/Login" `
            -TimeoutSec 5 -SkipHttpErrorCheck @script:Http
        return $null -ne $response
    }
    catch { return $false }
}

<#
    Returns the process id recorded by a previous managed start, but only when that process
    is still alive; a stale pid file from an earlier run is treated as absent.
#>
function Get-ManagedPid {
    if (-not (Test-Path -LiteralPath $script:PidFile)) { return $null }
    $recorded = (Get-Content -LiteralPath $script:PidFile -Raw).Trim()
    if (-not ($recorded -match '^\d+$')) { return $null }
    $process = Get-Process -Id ([int]$recorded) -ErrorAction SilentlyContinue
    if (-not $process) { return $null }
    return [int]$recorded
}

<#
    Classifies the server as managed (started here, console readable), external (someone
    else's, console not readable) or down, and points $script:LogPath at the captured log
    only in the managed case.
#>
function Update-ServerState {
    $up = Test-ServerUp
    $managedPid = Get-ManagedPid

    if ($up -and $managedPid) {
        $script:ServerMode = 'managed'
        $script:LogPath = $script:OutLog
    }
    elseif ($up) {
        $script:ServerMode = 'external'
        $script:LogPath = $null
    }
    else {
        $script:ServerMode = 'down'
        $script:LogPath = $null
    }
}

function Start-ManagedServer {
    if (Test-Path -LiteralPath $script:OutLog) { Remove-Item -LiteralPath $script:OutLog -Force }
    if (Test-Path -LiteralPath $script:ErrLog) { Remove-Item -LiteralPath $script:ErrLog -Force }

    Write-Step "Starting the server under this script (log: $script:OutLog)"
    Write-Note 'The first start builds the project and applies migrations; allow a minute or two.'

    # No launch-profile override: the profile is what sets ASPNETCORE_ENVIRONMENT=Development,
    # and without Development the host fails config validation and never listens.
    $start = @{
        FilePath               = 'dotnet'
        ArgumentList           = @('run', '--project', $script:ProjectDir)
        WorkingDirectory       = $script:RepoRoot
        RedirectStandardOutput = $script:OutLog
        RedirectStandardError  = $script:ErrLog
        PassThru               = $true
    }
    # Hidden window on Windows rather than -NoNewWindow: sharing this console would mean a
    # stray Ctrl+C here also kills the server.
    if ($IsWindows) { $start.WindowStyle = 'Hidden' } else { $start.NoNewWindow = $true }
    $process = Start-Process @start

    Set-Content -LiteralPath $script:PidFile -Value $process.Id

    $deadline = (Get-Date).AddMinutes(4)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            Write-Bad "The server exited with code $($process.ExitCode). Last lines of the log:"
            Show-LogTail -Lines 25 | Out-Host
            Show-ErrorTail | Out-Host
            Update-ServerState
            return $false
        }
        if (Test-ServerUp) {
            Write-Step 'Server is listening.'
            Update-ServerState
            return $true
        }
        Start-Sleep -Milliseconds 750
        Write-Host '.' -NoNewline -ForegroundColor DarkGray
    }

    Write-Bad 'The server did not come up in time.'
    Update-ServerState
    return $false
}

function Stop-ManagedServer {
    $managedPid = Get-ManagedPid
    if (-not $managedPid) {
        Write-Note 'No server started by this script is running.'
        return
    }
    Stop-ProcessTree -ProcessId $managedPid
    Remove-Item -LiteralPath $script:PidFile -Force -ErrorAction SilentlyContinue
    Write-Step "Stopped the managed server (pid $managedPid)."
    Update-ServerState
}

<#
    Kills a process and its children. `dotnet run` launches the app as a child process, so
    killing only the parent would leave the port bound.
#>
function Stop-ProcessTree {
    param([Parameter(Mandatory)][int] $ProcessId)

    if ($IsWindows) {
        & taskkill.exe /PID $ProcessId /T /F 2>&1 | Out-Null
    }
    else {
        # Unix: kill the process group `dotnet run` leads, falling back to the process alone.
        & kill -TERM -- "-$ProcessId" 2>$null
        Start-Sleep -Milliseconds 500
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    }
}

<#
    Finds whichever process is listening on the server port, so an externally started server
    can be taken over on request.
#>
function Get-ListenerPid {
    try {
        if ($IsWindows) {
            $connection = Get-NetTCPConnection -LocalPort $script:Port -State Listen -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($connection) { return [int]$connection.OwningProcess }
        }
        else {
            $found = & lsof -ti "tcp:$script:Port" -sTCP:LISTEN 2>$null | Select-Object -First 1
            if ($found) { return [int]$found }
        }
    }
    catch { }
    return $null
}

<#
    Brings the server into a usable state, preferring managed mode because it is the only
    mode in which sign-in codes and emailed links can be read automatically.
#>
function Confirm-Server {
    param([switch] $RequireManaged)

    Update-ServerState

    if ($script:ServerMode -eq 'down') {
        Write-Ask 'No server is listening.'
        if ((Read-Host '  Start one under this script now? [Y/n]') -notmatch '^[nN]') {
            return (Start-ManagedServer)
        }
        Write-Note "Start it yourself with: dotnet run --project $script:ProjectDir"
        return $false
    }

    if ($script:ServerMode -eq 'external' -and $RequireManaged) {
        Write-Ask 'A server is running that this script did not start, so its console output cannot be read here.'
        Write-Note 'Either paste the code/link manually when asked, or let the script restart the server and capture it.'
        if ((Read-Host '  Restart it under this script? [y/N]') -match '^[yY]') {
            $listener = Get-ListenerPid
            if (-not $listener) {
                Write-Bad "Could not find the process on port $script:Port. Stop it yourself, then choose [s] Server."
                return $true
            }
            Stop-ProcessTree -ProcessId $listener
            Start-Sleep -Seconds 2
            return (Start-ManagedServer)
        }
    }

    return $true
}

function Show-LogTail {
    param([int] $Lines = 40, [switch] $Follow)

    if (-not (Test-Path -LiteralPath $script:OutLog)) {
        Write-Note 'No captured log - the server was not started by this script.'
        return
    }

    try { Get-Content -LiteralPath $script:OutLog -Tail $Lines } catch { }
    if (-not $Follow) { return }

    # Hand-rolled follow rather than -Wait, which can only be stopped with Ctrl+C - and a
    # managed server shares this console, so Ctrl+C would kill it too.
    Write-Note 'Following the log; press any key to stop.'
    $seen = Get-LogLineCount -Path $script:OutLog
    while (-not (Test-AbortKey)) {
        $total = Get-LogLineCount -Path $script:OutLog
        if ($total -gt $seen) {
            try { Get-Content -LiteralPath $script:OutLog | Select-Object -Skip $seen } catch { }
            $seen = $total
        }
        Start-Sleep -Milliseconds 400
    }
}

function Show-ErrorTail {
    if (-not (Test-Path -LiteralPath $script:ErrLog)) { return }
    $lines = @(Get-Content -LiteralPath $script:ErrLog -Tail 15)
    if ($lines.Count -eq 0) { return }
    Write-Bad 'stderr:'
    $lines
}

# ---------------------------------------------------------------------------------------
# Reading emails out of the captured log
# ---------------------------------------------------------------------------------------

<#
    Counts the lines currently in a log file, or 0 when it is absent or momentarily locked.
#>
function Get-LogLineCount {
    param([string] $Path)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return 0 }
    try { return @(Get-Content -LiteralPath $Path).Count }
    catch { return 0 }
}

<#
    Records how far the log has been consumed. Line counts rather than byte offsets, so the
    mark stays valid whatever encoding the console writes.
#>
function Get-LogMark {
    return (Get-LogLineCount -Path $script:LogPath)
}

<#
    Waits for the next emailed value matching a pattern and returns its first capture
    group. In external mode - where the server's console belongs to another terminal -
    falls straight through to asking for the value.

    Matches against the new log text as a whole rather than line by line: a message body
    now spans several lines, so the code or link sits below the EMAIL SINK line rather than
    on it. Patterns should therefore anchor on 'EMAIL SINK' and reach forward, which also
    keeps them from matching a digit run or a URL in some unrelated log line.

    .PARAMETER Pattern    Regex with exactly one capture group, matched against new log text.
    .PARAMETER Mark       Line count captured with Get-LogMark before triggering the email.
    .PARAMETER What       Human name of the value, used in prompts.
    .PARAMETER Hint       Where to look for it in the server's console.
#>
function Wait-Email {
    param(
        [Parameter(Mandatory)][string] $Pattern,
        [Parameter(Mandatory)][int] $Mark,
        [string] $What = 'value',
        [string] $Hint = 'Look for the EMAIL SINK line in the server terminal.',
        [int] $TimeoutSec = 120
    )

    if (-not $script:LogPath) {
        Write-Ask "This script cannot read the server's console."
        Write-Note $Hint
        return (Read-Host "  Paste the $What here").Trim()
    }

    Write-Step "Waiting for the $What in the server log (up to $TimeoutSec s; press any key to give up)"
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $readFailures = 0

    while ((Get-Date) -lt $deadline) {
        $lines = $null
        try { $lines = @(Get-Content -LiteralPath $script:LogPath) | Select-Object -Skip $Mark }
        catch {
            # The server may hold the file open mid-write; retry on the next tick. Persistent
            # failure means the log is not readable at all, so stop pretending it is.
            $readFailures++
            if ($readFailures -ge 25) {
                Write-Host ''
                Write-Bad 'The captured log cannot be read while the server holds it open.'
                break
            }
            $lines = @()
        }

        if (($lines -join "`n") -match $Pattern) {
            Write-Host ''
            return $Matches[1]
        }

        if (Test-AbortKey) {
            Write-Host ''
            Write-Note 'Stopped waiting.'
            break
        }

        Start-Sleep -Milliseconds 400
        Write-Host '.' -NoNewline -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-Note $Hint
    return (Read-Host "  Paste the $What here, or press Enter to abort").Trim()
}

function Wait-SignInCode {
    param([Parameter(Mandatory)][int] $Mark)
    # The code is the first eight-digit run after the sink line, on a line of its own.
    return Wait-Email -Mark $Mark -What 'sign-in code' `
        -Pattern 'EMAIL SINK[\s\S]*?\b(\d{8})\b' `
        -Hint 'EMAIL SINK ... body=Your sign-in code ... then the code on its own line.'
}

function Wait-EmailedLink {
    param(
        [Parameter(Mandatory)][int] $Mark,
        [Parameter(Mandatory)][string] $What,
        [int] $TimeoutSec = 120
    )
    # The body names the action, then puts the link on the next line. It is the only link in
    # the message - the footer's legal links appear only where a deployment has published
    # them, and this one does not - so the first address after the sink line is the one.
    return Wait-Email -Mark $Mark -What $What -TimeoutSec $TimeoutSec `
        -Pattern 'EMAIL SINK[\s\S]*?(https?://\S+)' `
        -Hint 'EMAIL SINK ... body=... then the action and its link on the next line.'
}

# ---------------------------------------------------------------------------------------
# OAuth helpers
# ---------------------------------------------------------------------------------------

<#
    Prints what an API actually objected to. ASP.NET model validation answers with
    ProblemDetails, whose `errors` map names the offending field - far more useful than the
    bare status line the exception carries.
#>
function Show-ApiError {
    param([int] $Status, $Response)

    Write-Bad "The request was refused (HTTP $Status)."
    if ($Response.title) { Write-Note $Response.title }
    if ($Response.detail) { Write-Note $Response.detail }

    if ($Response.errors) {
        foreach ($field in $Response.errors.PSObject.Properties) {
            Write-Note ("{0}: {1}" -f $field.Name, ($field.Value -join '; '))
        }
    }
}

<#
    Reads an email address, re-prompting until it looks like one. The invitation API rejects
    the whole batch on a malformed address, and that rejection is worth catching here rather
    than after a round trip.
#>
function Read-EmailAddress {
    param([Parameter(Mandatory)][string] $Prompt, [Parameter(Mandatory)][string] $Default)

    while ($true) {
        $value = (Read-Host "  $Prompt [$Default]").Trim()
        if (-not $value) { return $Default }
        if ($value -match '^[^@\s]+@[^@\s]+$') { return $value }
        Write-Bad "'$value' is not an email address - the display name is asked for separately."
    }
}

function Invoke-TokenEndpoint {
    param([Parameter(Mandatory)][hashtable] $Body)
    return Invoke-RestMethod -Method Post -Uri "$script:Base/connect/token" `
        -Body $Body -SkipHttpErrorCheck @script:Http
}

<#
    Runs a device authorization from start to finish: requests a device code, sends you to
    the verification page to approve it, then polls the token endpoint until the approval
    lands. Returns the token response, or $null if it never completed.
#>
function Invoke-DeviceFlow {
    param(
        [string] $ClientId = 'local-browser',
        [string] $Scope = 'openid profile',
        [switch] $ApprovalOnly   # stop after the browser step; do not redeem the code
    )

    $device = Invoke-RestMethod -Method Post -Uri "$script:Base/connect/device" `
        -Body @{ client_id = $ClientId; scope = $Scope } -SkipHttpErrorCheck @script:Http

    if ($device.error) {
        Write-Bad "Device authorization refused: $($device.error) - $($device.error_description)"
        return $null
    }

    Write-Value 'User code:' $device.user_code
    Write-Step 'Opening the verification page - click Allow'
    Open-Url $device.verification_uri_complete

    if ($ApprovalOnly) { return $device }

    $interval = if ($device.interval) { [int]$device.interval } else { 5 }
    $deadline = (Get-Date).AddSeconds([int]$device.expires_in)

    Write-Step 'Polling for approval (press any key to give up)'
    while ((Get-Date) -lt $deadline) {
        # Poll the key in small slices so giving up does not wait out the whole interval.
        for ($slice = 0; $slice -lt ($interval * 4); $slice++) {
            if (Test-AbortKey) {
                Write-Host ''
                Write-Note 'Stopped waiting for approval.'
                return $null
            }
            Start-Sleep -Milliseconds 250
        }

        $token = Invoke-TokenEndpoint -Body @{
            grant_type  = $script:DeviceGrantType
            client_id   = $ClientId
            device_code = $device.device_code
        }

        if ($token.access_token) {
            Write-Host ''
            Write-Step 'Access token issued.'
            return $token
        }

        switch ($token.error) {
            'authorization_pending' { Write-Host '.' -NoNewline -ForegroundColor DarkGray }
            'slow_down' { $interval += 5 }
            default {
                Write-Host ''
                Write-Bad "Device grant failed: $($token.error) - $($token.error_description)"
                return $null
            }
        }
    }

    Write-Host ''
    Write-Bad 'The device code expired before it was approved.'
    return $null
}

<#
    Gets an access token carrying the management scope, running a device grant only when
    there is no live one cached. Nothing but the device grant can produce this token for the
    local-browser client, and it costs a browser round trip, so it is worth keeping.
#>
function Get-IdentityScopeToken {
    if ($script:IdentityToken -and $script:IdentityToken.ExpiresAt -gt (Get-Date).AddSeconds(60)) {
        $remaining = [int]($script:IdentityToken.ExpiresAt - (Get-Date)).TotalSeconds
        Write-Step "Reusing the access token from an earlier device grant (${remaining}s left)."
        return $script:IdentityToken.Token
    }

    Write-Step 'Requesting a device code for the tellma_identity scope'
    $token = Invoke-DeviceFlow -Scope 'openid tellma_identity'
    if (-not $token) { return $null }

    $script:IdentityToken = @{
        Token     = $token.access_token
        ExpiresAt = (Get-Date).AddSeconds([int]$token.expires_in)
    }
    return $token.access_token
}

<#
    Gets a control-plane access token with the client-credentials grant. The seeded
    local-control-plane client is confidential, so this needs no browser at all.
#>
function Get-ControlPlaneToken {
    $token = Invoke-TokenEndpoint -Body @{
        grant_type    = 'client_credentials'
        client_id     = 'local-control-plane'
        client_secret = 'local-dev-secret'
        scope         = 'tellma_control_plane'
    }

    if (-not $token.access_token) {
        Write-Bad "Client credentials refused: $($token.error) - $($token.error_description)"
        Write-Note 'Check that the local-control-plane client is in user secrets with the matching secret.'
        return $null
    }
    return $token.access_token
}

# ---------------------------------------------------------------------------------------
# User secrets
# ---------------------------------------------------------------------------------------

function Get-UserSecrets {
    try {
        $raw = & dotnet user-secrets --project $script:ProjectDir list 2>&1
        if ($LASTEXITCODE -ne 0) { return $null }
        $map = @{}
        foreach ($line in $raw) {
            if ($line -match '^(?<key>[^=]+?)\s*=\s*(?<value>.*)$') {
                $map[$Matches['key'].Trim()] = $Matches['value'].Trim()
            }
        }
        return $map
    }
    catch { return $null }
}

<#
    Reports the two settings and two clients without which four pages 404 and every
    OIDC-driven page is unreachable. Advisory only - it never edits the store.
#>
function Test-Prerequisites {
    Write-Banner 'Prerequisites'
    $secrets = Get-UserSecrets
    if ($null -eq $secrets) {
        Write-Bad 'Could not read user secrets (is the dotnet SDK on PATH?).'
        return
    }

    $passwordSignIn = $secrets['TellmaIdentity:EnablePasswordSignIn']
    if ($passwordSignIn -and $passwordSignIn -match '^(true|True)$') {
        Write-Step 'EnablePasswordSignIn = true (ForgotPassword and ResetPassword are reachable)'
    }
    else {
        Write-Bad 'EnablePasswordSignIn is not true - ForgotPassword and ResetPassword return 404.'
    }

    $clientIds = $secrets.Keys |
        Where-Object { $_ -match '^TellmaIdentity:Seed:Clients:\d+:ClientId$' } |
        ForEach-Object { $secrets[$_] }

    foreach ($required in @('local-browser', 'local-control-plane')) {
        if ($clientIds -contains $required) { Write-Step "Client '$required' is seeded" }
        else { Write-Bad "Client '$required' is missing from user secrets" }
    }

    if ($clientIds -contains 'third-party') { Write-Step "Client 'third-party' is seeded (Consent is reachable)" }
    else { Write-Note "Client 'third-party' is not seeded - the Consent page flow will offer to add it." }
}

<#
    Adds the consent-requiring client to user secrets at the next free array index. Seeded
    clients are re-applied from configuration on every start, so this is the only durable
    place to put it - a SQL edit is reverted at the next boot and invisible to a running
    server anyway (OpenIddict caches applications in memory).
#>
function Add-ThirdPartyClient {
    $secrets = Get-UserSecrets
    if ($null -eq $secrets) { Write-Bad 'Could not read user secrets.'; return $false }

    $indices = $secrets.Keys |
        Where-Object { $_ -match '^TellmaIdentity:Seed:Clients:(\d+):' } |
        ForEach-Object { [int]($_ -replace '^TellmaIdentity:Seed:Clients:(\d+):.*$', '$1') }

    $next = if ($indices) { (($indices | Measure-Object -Maximum).Maximum + 1) } else { 0 }
    $prefix = "TellmaIdentity:Seed:Clients:$next"

    # ${prefix} braces are required: a bare $prefix: would parse as a scope qualifier.
    $settings = [ordered]@{
        "${prefix}:ClientId"       = 'third-party'
        "${prefix}:DisplayName"    = 'Third Party App'
        "${prefix}:Kind"           = 'Native'
        "${prefix}:RequireConsent" = 'true'
        "${prefix}:RedirectUris:0" = 'http://127.0.0.1/callback'
    }

    foreach ($key in $settings.Keys) {
        & dotnet user-secrets --project $script:ProjectDir set $key $settings[$key] | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Bad "Failed to set $key"; return $false }
    }

    Write-Step "Added the third-party client at index $next."
    return $true
}

# ---------------------------------------------------------------------------------------
# Flows
# ---------------------------------------------------------------------------------------

<#
    Phase B: sign in as the dev admin. The admin has no password and no passkey by design,
    so an emailed code is the only way in.
#>
function Invoke-SignInFlow {
    if (-not (Confirm-Server -RequireManaged)) { return }
    Show-CultureCookieHint

    $mark = Get-LogMark
    Write-Step "Opening Login - enter $AdminEmail, tick 'Remember me', and press 'Email me a sign-in code'"
    Open-Url (Get-Url '/Identity/Account/Login')

    $code = Wait-SignInCode -Mark $mark
    if (-not $code) { Write-Bad 'No code captured.'; return }

    Write-Value 'Sign-in code:' $code
    Write-Note 'Enter it on the EmailCode page the browser is showing. That page in its real'
    Write-Note 'context is step 16 of the guide; the standalone version is menu item 7.'
}

<#
    Phase D: every OIDC-driven page is the same authorize request with different parameters.
#>
function Invoke-AuthorizeFlow {
    param(
        [string] $ClientId = 'local-browser',
        [string] $Extra = '',
        [string[]] $Expect = @()
    )

    if (-not (Confirm-Server)) { return }
    Show-CultureCookieHint

    foreach ($line in $Expect) { Write-Note $line }
    Open-Url ((Get-Url ($script:AuthorizeTemplate -f $script:Base, $ClientId)) + $Extra)
}

<#
    Phase D, step 27: the Consent page. Seeded clients are first-party and default to
    implicit consent, so a client that requires it has to exist first.
#>
function Invoke-ConsentFlow {
    $secrets = Get-UserSecrets
    $clientIds = if ($secrets) {
        $secrets.Keys | Where-Object { $_ -match '^TellmaIdentity:Seed:Clients:\d+:ClientId$' } |
            ForEach-Object { $secrets[$_] }
    }
    else { @() }

    if ($clientIds -notcontains 'third-party') {
        Write-Ask "No consent-requiring client is seeded."
        if ((Read-Host '  Add the third-party client to user secrets now? [Y/n]') -match '^[nN]') { return }
        if (-not (Add-ThirdPartyClient)) { return }

        Write-Ask 'The server must restart to pick up the new client.'
        if ($script:ServerMode -eq 'managed') {
            if ((Read-Host '  Restart the managed server now? [Y/n]') -notmatch '^[nN]') {
                Stop-ManagedServer
                if (-not (Start-ManagedServer)) { return }
            }
        }
        else {
            Write-Note 'Restart your server terminal, then choose this item again.'
            return
        }
    }

    Invoke-AuthorizeFlow -ClientId 'third-party' -Expect @(
        "Expect: 'Authorize application - Third Party App wants to access your account', with Allow / Deny.",
        'Sign in first if you are anonymous; consent is asked after authentication.'
    )
}

<#
    Phase D, steps 28-29: the device verification page and the DeviceApproved page it leads
    to. Also offers the empty code-entry form, which is a distinct state worth reviewing.
#>
function Invoke-DeviceVerifyFlow {
    if (-not (Confirm-Server)) { return }
    Show-CultureCookieHint

    Write-Note "Expect: 'Authorize your device' naming Local Browser Client, with Allow / Deny."
    Write-Note "Clicking Allow lands on DeviceApproved - 'All set.' (step 29)."
    $device = Invoke-DeviceFlow -Scope 'openid profile' -ApprovalOnly
    if (-not $device) { return }

    Write-Host ''
    if ((Read-Host '  Also open /connect/verify with no code (the empty entry form)? [y/N]') -match '^[yY]') {
        Write-Note "Enter the user code above by hand to reach the same approval page."
        Open-Url (Get-Url '/connect/verify')
    }
}

<#
    Phase E: a valid invitation link. Needs a token carrying the management scope, which
    only the device grant can produce for the local-browser client, then the bulk API, then
    the link that arrives by email a moment later (delivery is queued, not inline).
#>
function Invoke-InvitationFlow {
    if (-not (Confirm-Server -RequireManaged)) { return }
    Show-CultureCookieHint

    $accessToken = Get-IdentityScopeToken
    if (-not $accessToken) { return }

    # The link is single-use, so a fresh address each time avoids re-inviting a consumed
    # invitation and gives a clean 'Invited' rather than 'Reinvited'.
    $suffix = (Get-Random -Minimum 1000 -Maximum 9999)
    $email = Read-EmailAddress -Prompt 'Email address to invite' -Default "newbie-$suffix@localhost"
    $displayName = (Read-Host '  Display name [New Bie]').Trim()
    if (-not $displayName) { $displayName = 'New Bie' }

    $locale = if ($script:Culture -eq 'ar') { 'ar' } else { 'en' }
    $body = @{ users = @(@{ email = $email; displayName = $displayName; locale = $locale }) } |
        ConvertTo-Json -Depth 5

    $mark = Get-LogMark
    $status = 0
    $response = Invoke-RestMethod -Method Post -Uri "$script:Base/api/identity/invitations" `
        -Headers @{ Authorization = "Bearer $accessToken" } `
        -ContentType 'application/json' -Body $body `
        -SkipHttpErrorCheck -StatusCodeVariable 'status' @script:Http

    if ($status -ge 400) {
        Show-ApiError -Status $status -Response $response
        return
    }

    foreach ($result in $response.results) {
        if ($result.error) { Write-Bad "$($result.email): $($result.error)" }
        else { Write-Step "$($result.email): $($result.status) (sub $($result.sub))" }
    }

    # A batch can succeed as a request while refusing every user in it, and then no email is
    # ever sent - waiting for a link that will not arrive would just burn the timeout.
    if (-not (@($response.results) | Where-Object { -not $_.error })) {
        Write-Bad 'No user was invited, so no link will arrive.'
        return
    }

    $link = Wait-EmailedLink -Mark $mark -What 'invitation link'
    if (-not $link) { return }

    Write-Note "Expect: 'Welcome' - the valid state. Opening it consumes the link; re-invite for a second look."
    Open-Url $link
}

<#
    Phase E: a Temporary Access Pass, the admin-assisted recovery path. The operator
    surface exists only in standalone deployments, which is what the dev host runs.
#>
function Invoke-TapFlow {
    if (-not (Confirm-Server)) { return }
    Show-CultureCookieHint

    $accessToken = Get-ControlPlaneToken
    if (-not $accessToken) { return }

    $sub = (Read-Host "  Subject to recover [$AdminSub]").Trim()
    if (-not $sub) { $sub = $AdminSub }

    $status = 0
    $pass = Invoke-RestMethod -Method Post `
        -Uri "$script:Base/api/identity/users/$sub/temporary-access-passes" `
        -Headers @{ Authorization = "Bearer $accessToken" } `
        -SkipHttpErrorCheck -StatusCodeVariable 'status' @script:Http

    if ($status -ge 400) {
        Show-ApiError -Status $status -Response $pass
        Write-Note '404 means the subject does not exist; 403 means the token lacks the control-plane scope.'
        return
    }

    Write-Value 'Temporary Access Pass:' $pass.pass
    Write-Note "Expires: $($pass.expiresUtc)"
    Write-Note "On the page that opens, enter $AdminEmail and the pass above."
    Write-Note 'It redirects into RegisterPasskey in recovery mode.'
    Open-Url (Get-Url '/Identity/Account/Tap')
}

<#
    Phase E: the real ResetPassword form, plus its invalid twin for comparison. Both pages
    404 unless EnablePasswordSignIn is on.
#>
function Invoke-ResetPasswordFlow {
    if (-not (Confirm-Server -RequireManaged)) { return }
    Show-CultureCookieHint

    $mark = Get-LogMark
    Write-Step "Opening ForgotPassword - submit $AdminEmail"
    Open-Url (Get-Url '/Identity/Account/ForgotPassword')

    $link = Wait-EmailedLink -Mark $mark -What 'password reset link'
    if (-not $link) { return }

    Open-Url $link

    Write-Host ''
    if ((Read-Host '  Also open the invalid state for comparison? [y/N]') -match '^[yY]') {
        Open-Url (Get-Url '/Identity/Account/ResetPassword?code=bad')
    }
}

<#
    Phase C pages all require a session. Rather than guess whether one exists, offer the
    sign-in flow when the page bounces back to Login.
#>
function Open-SignedInPage {
    param([Parameter(Mandatory)][string] $Path, [string] $Expect)

    if (-not (Confirm-Server)) { return }
    if ($Expect) { Write-Note $Expect }
    Write-Note 'Requires a session - if this lands on Sign in, run menu item 15 first.'
    Open-Url (Get-Url $Path)
}

# ---------------------------------------------------------------------------------------
# The page catalogue
# ---------------------------------------------------------------------------------------

$script:Entries = [System.Collections.Generic.List[object]]::new()

function Register-Page {
    param(
        [Parameter(Mandatory)][string] $Id,
        [Parameter(Mandatory)][string] $Group,
        [Parameter(Mandatory)][string] $Title,
        [string] $Path,
        [string] $Expect,
        [scriptblock] $Flow
    )
    $script:Entries.Add([pscustomobject]@{
        Id     = $Id
        Group  = $Group
        Title  = $Title
        Path   = $Path
        Expect = $Expect
        Flow   = $Flow
    })
}

# Ids deliberately match the step numbers in ui-inspection-guide.md beside this script, so the gaps
# (16, 17) are the sub-steps of the sign-in flow rather than missing pages.

# --- Phase A: anonymous pages, reachable by pasting a URL --------------------------------
Register-Page -Id '1'  -Group 'A' -Title 'Login' -Path '/Identity/Account/Login' `
    -Expect "Expect: 'Sign in', email field, 'Email me a sign-in code', 'Sign in with a passkey'."
Register-Page -Id '2'  -Group 'A' -Title 'Login - no method offered' -Path '/Identity/Account/Login?methods=password' `
    -Expect "Expect: 'No sign-in method is available for this request.' - the allow-list names only methods this deployment does not offer."
Register-Page -Id '3'  -Group 'A' -Title 'AccessDenied' -Path '/Identity/Account/AccessDenied' `
    -Expect "Expect: 'You do not have access to this resource.'"
Register-Page -Id '4'  -Group 'A' -Title 'LoggedOut' -Path '/Identity/Account/LoggedOut' `
    -Expect "Expect: 'You are signed out.'"
Register-Page -Id '5'  -Group 'A' -Title 'Logout' -Path '/Identity/Account/Logout' `
    -Expect "Expect: 'Sign out of your account?' with the confirm button."
Register-Page -Id '6'  -Group 'A' -Title 'DeviceApproved' -Path '/Identity/Account/DeviceApproved' `
    -Expect "Expect: 'All set - you can return to your device.'"
Register-Page -Id '7'  -Group 'A' -Title 'EmailCode (standalone)' -Path "/Identity/Account/EmailCode?email=$AdminEmail" `
    -Expect "Expect: the code entry form, 'Verify' and 'Send a new code'."
Register-Page -Id '8'  -Group 'A' -Title 'Setup' -Path '/Identity/Account/Setup' `
    -Expect "Expect: 'Set up the administrator' with the one-time setup token field."
Register-Page -Id '9'  -Group 'A' -Title 'Tap (form only)' -Path '/Identity/Account/Tap' `
    -Expect "Expect: 'Recover your account', email + access pass fields. Item 31 issues a real pass."
Register-Page -Id '10' -Group 'A' -Title 'Invitation - invalid' -Path '/Identity/Account/Invitation?code=bad' `
    -Expect "Expect: 'This invitation link is invalid or has expired.' Expired, used and forged links all look identical."
Register-Page -Id '11' -Group 'A' -Title 'ForgotPassword' -Path '/Identity/Account/ForgotPassword' `
    -Expect 'Expect: "Forgot your password?" - 404 without EnablePasswordSignIn.'
Register-Page -Id '12' -Group 'A' -Title 'ExternalLogin - error' -Path '/Identity/Account/ExternalLogin?handler=Callback&remoteError=access_denied' `
    -Expect 'Expect: the error state with a link back to sign-in. This page has no success state of its own.'
Register-Page -Id '13' -Group 'A' -Title 'Error' -Path '/error' `
    -Expect "Expect: 'Something went wrong'."
Register-Page -Id '14' -Group 'A' -Title 'Error - with protocol detail' `
    -Path '/connect/authorize?client_id=nope&response_type=code&redirect_uri=http%3A%2F%2F127.0.0.1%2Fcallback&scope=openid' `
    -Expect "Expect: the same page carrying invalid_client / 'The specified client is unknown.' Confirm it never echoes raw request data."
Register-Page -Id '33' -Group 'A' -Title 'ResetPassword - invalid' -Path '/Identity/Account/ResetPassword?code=bad' `
    -Expect 'Expect: the reset form in its invalid state. Item 32 reaches the valid one.'
Register-Page -Id '34' -Group 'A' -Title 'Device Verify - empty form' -Path '/connect/verify' `
    -Expect 'Expect: the empty code-entry form. Item 28 reaches it with a code attached.'

# --- Phase B: sign in --------------------------------------------------------------------
Register-Page -Id '15' -Group 'B' -Title 'Sign in as the dev admin (email code)' -Flow { Invoke-SignInFlow }

# --- Phase C: signed-in pages ------------------------------------------------------------
Register-Page -Id '18' -Group 'C' -Title 'Manage / Profile' `
    -Flow { Open-SignedInPage -Path '/Identity/Manage/Index' }
Register-Page -Id '19' -Group 'C' -Title 'Manage / Passkeys' `
    -Flow { Open-SignedInPage -Path '/Identity/Manage/Passkeys' -Expect 'Expect: any credential registered on item 22.' }
Register-Page -Id '20' -Group 'C' -Title 'Manage / Sessions' `
    -Flow { Open-SignedInPage -Path '/Identity/Manage/Sessions' -Expect 'Expect: your current session listed.' }
Register-Page -Id '21' -Group 'C' -Title 'Manage / Authenticator app' `
    -Flow { Open-SignedInPage -Path '/Identity/Manage/EnableAuthenticator' -Expect 'Expect: the QR code and the manual key.' }
Register-Page -Id '22' -Group 'C' -Title 'RegisterPasskey' `
    -Flow {
        Open-SignedInPage -Path '/Identity/Account/RegisterPasskey' `
            -Expect 'Complete a real ceremony with Windows Hello or a security key - do this before items 25/26 so the step-up screens have something to satisfy them.'
    }

# --- Phase D: the OIDC-driven pages ------------------------------------------------------
Register-Page -Id '23' -Group 'D' -Title 'Authorize - plain' -Flow {
    Invoke-AuthorizeFlow -Expect @(
        'With a session this redirects straight to http://127.0.0.1/callback?code=... which fails to load.',
        'That failure IS the success: a code was issued.')
}
Register-Page -Id '24' -Group 'D' -Title 'Authorize - forced re-authentication' -Flow {
    Invoke-AuthorizeFlow -Extra '&prompt=login' -Expect @(
        "Expect the heading to change from 'Sign in' to 'Confirm it's you'.",
        '&max_age=0 reaches the same page.')
}
Register-Page -Id '25' -Group 'D' -Title 'Authorize - step up to aal2' -Flow {
    Invoke-AuthorizeFlow -Extra '&acr_values=urn%3Atellma%3Aacr%3Aaal2' -Expect @(
        'Expect only the passkey button: an email code cannot satisfy aal2.')
}
Register-Page -Id '26' -Group 'D' -Title 'Authorize - step up to aal3' -Flow {
    Invoke-AuthorizeFlow -Extra '&acr_values=urn%3Atellma%3Aacr%3Aaal3' -Expect @(
        'Expect the device-bound passkey notice. A synced passkey is refused here even though it signs you in elsewhere.')
}
Register-Page -Id '27' -Group 'D' -Title 'Consent' -Flow { Invoke-ConsentFlow }
Register-Page -Id '28' -Group 'D' -Title 'Device Verify (+ DeviceApproved)' -Flow { Invoke-DeviceVerifyFlow }

# --- Phase E: the token-gated pages ------------------------------------------------------
Register-Page -Id '30' -Group 'E' -Title 'Invitation - valid' -Flow { Invoke-InvitationFlow }
Register-Page -Id '31' -Group 'E' -Title 'Temporary Access Pass -> RegisterPasskey (recovery)' -Flow { Invoke-TapFlow }
Register-Page -Id '32' -Group 'E' -Title 'ResetPassword - valid' -Flow { Invoke-ResetPasswordFlow }

$script:GroupTitles = [ordered]@{
    'A' = 'PHASE A - anonymous pages (paste a URL)'
    'B' = 'PHASE B - sign in'
    'C' = 'PHASE C - signed-in pages'
    'D' = 'PHASE D - the OIDC-driven pages'
    'E' = 'PHASE E - the token-gated pages'
}

# ---------------------------------------------------------------------------------------
# Menu
# ---------------------------------------------------------------------------------------

function Show-Menu {
    Clear-Host
    Write-Host ''
    Write-Host '  TELLMA IDENTITY - PAGE INSPECTOR' -ForegroundColor White

    $modeColor = switch ($script:ServerMode) {
        'managed' { 'Green' }
        'external' { 'Yellow' }
        default { 'Red' }
    }
    $modeText = switch ($script:ServerMode) {
        'managed' { 'managed by this script (codes read automatically)' }
        'external' { 'started elsewhere (codes must be pasted)' }
        'down' { 'not running' }
        default { 'unknown' }
    }
    Write-Host '  server: ' -ForegroundColor DarkGray -NoNewline
    Write-Host $modeText -ForegroundColor $modeColor -NoNewline
    Write-Host "   language: " -ForegroundColor DarkGray -NoNewline
    Write-Host $script:Culture -ForegroundColor Cyan -NoNewline
    Write-Host "   $script:Base" -ForegroundColor DarkGray
    Write-Host ''

    foreach ($group in $script:GroupTitles.Keys) {
        $items = @($script:Entries | Where-Object { $_.Group -eq $group })
        if (-not $items) { continue }

        Write-Host "  $($script:GroupTitles[$group])" -ForegroundColor White

        # Phase A is a long list of one-liners, so it reads better in two columns.
        if ($group -eq 'A') {
            $half = [Math]::Ceiling($items.Count / 2)
            for ($i = 0; $i -lt $half; $i++) {
                $left = Format-MenuItem $items[$i]
                $right = if (($i + $half) -lt $items.Count) { Format-MenuItem $items[$i + $half] } else { '' }
                Write-Host ('   ' + $left.PadRight(36) + $right)
            }
        }
        else {
            foreach ($item in $items) { Write-Host ('   ' + (Format-MenuItem $item)) }
        }
        Write-Host ''
    }

    Write-Host '  COMMANDS' -ForegroundColor White
    Write-Host '   s  server: start / stop / restart      l  tail the captured log'
    Write-Host '   a  toggle language (en / ar)           p  re-check prerequisites'
    Write-Host '   d  drop the database (full reset)      q  quit'
    Write-Host ''
}

function Format-MenuItem {
    param([Parameter(Mandatory)][object] $Item)
    return ('{0,3}  {1}' -f $Item.Id, $Item.Title)
}

function Invoke-Entry {
    param([Parameter(Mandatory)][object] $Entry)

    Write-Banner $Entry.Title

    if ($Entry.Flow) {
        & $Entry.Flow
    }
    else {
        if (-not (Confirm-Server)) { Wait-Enter; return }
        if ($Entry.Expect) { Write-Note $Entry.Expect }
        Open-Url (Get-Url $Entry.Path)
    }

    Wait-Enter
}

function Invoke-ServerCommand {
    Write-Banner 'Server'
    Update-ServerState
    Write-Note "Current state: $script:ServerMode"
    Write-Host ''
    Write-Host '   1  start under this script (captures the console)'
    Write-Host '   2  stop the managed server'
    Write-Host '   3  restart the managed server'
    Write-Host '   4  take over an externally started server'
    Write-Host '   0  back'
    Write-Host ''

    switch ((Read-Host '  Choice')) {
        '1' {
            if ($script:ServerMode -ne 'down') { Write-Bad 'Something is already listening on the port.' }
            else { Start-ManagedServer | Out-Null }
        }
        '2' { Stop-ManagedServer }
        '3' {
            Stop-ManagedServer
            Start-Sleep -Seconds 2
            Start-ManagedServer | Out-Null
        }
        '4' {
            $listener = Get-ListenerPid
            if (-not $listener) { Write-Bad "Nothing found listening on port $script:Port." }
            else {
                Write-Ask "This kills pid $listener."
                if ((Read-Host '  Proceed? [y/N]') -match '^[yY]') {
                    Stop-ProcessTree -ProcessId $listener
                    Start-Sleep -Seconds 2
                    Start-ManagedServer | Out-Null
                }
            }
        }
        default { }
    }
    Wait-Enter
}

<#
    Drops the identity database so the next start re-migrates and re-seeds it. Destructive
    and therefore double-confirmed; the connection string in user secrets wins over the one
    in appsettings.Development.json, so the target is read from there when present.
#>
function Invoke-DropDatabase {
    Write-Banner 'Drop the database'

    $secrets = Get-UserSecrets
    $connection = if ($secrets) { $secrets['TellmaIdentity:ConnectionString'] } else { $null }
    if (-not $connection) {
        $connection = 'Server=(localdb)\MSSQLLocalDB;Database=TellmaIdentity;Trusted_Connection=True;TrustServerCertificate=True'
    }

    $server = if ($connection -match 'Server=([^;]+)') { $Matches[1] } else { '(localdb)\MSSQLLocalDB' }
    $database = if ($connection -match 'Database=([^;]+)') { $Matches[1] } else { 'TellmaIdentity' }

    Write-Ask "This drops [$database] on $server. Every user, session, passkey and audit row goes with it."
    if ((Read-Host "  Type the database name to confirm") -ne $database) {
        Write-Note 'Cancelled.'
        Wait-Enter
        return
    }

    if ($script:ServerMode -ne 'down') {
        Write-Note 'Stopping the server first - it holds an open connection.'
        if ($script:ServerMode -eq 'managed') { Stop-ManagedServer }
        else {
            Write-Bad 'Stop your externally started server, then try again.'
            Wait-Enter
            return
        }
    }

    try {
        & sqlcmd -S $server -Q "IF DB_ID('$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END"
        Write-Step "Dropped [$database]. The next start re-migrates and re-seeds it."
    }
    catch {
        Write-Bad "sqlcmd failed: $($_.Exception.Message)"
    }
    Wait-Enter
}

# ---------------------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------------------

Write-Banner 'Tellma identity page inspector'
Update-ServerState
Test-Prerequisites
Write-Host ''
Write-Note "Server is currently: $script:ServerMode"
if ($script:ServerMode -eq 'external') {
    Write-Note 'Codes and links will have to be pasted from your server terminal.'
    Write-Note 'Choose [s] then [4] to hand the server over to this script and read them automatically.'
}
Wait-Enter -Text 'Press Enter for the menu'

$emptySelections = 0
while ($true) {
    Update-ServerState
    Show-Menu
    $choice = (Read-Host '  Select').Trim()

    if (-not $choice) {
        # A redirected stdin returns empty forever; bail out rather than spin.
        if (++$emptySelections -ge 3) {
            Write-Note 'No input - exiting. This script needs an interactive terminal.'
            return
        }
        continue
    }
    $emptySelections = 0

    switch -Regex ($choice) {
        '^[qQ]$' { Write-Host ''; return }
        '^[sS]$' { Invoke-ServerCommand; continue }
        '^[lL]$' { Write-Banner 'Server log'; Show-LogTail -Lines 40 -Follow; Wait-Enter; continue }
        '^[pP]$' { Test-Prerequisites; Wait-Enter; continue }
        '^[dD]$' { Invoke-DropDatabase; continue }
        '^[aA]$' {
            $script:Culture = if ($script:Culture -eq 'en') { 'ar' } else { 'en' }
            continue
        }
        default {
            $entry = $script:Entries | Where-Object { $_.Id -eq $choice } | Select-Object -First 1
            if ($entry) { Invoke-Entry -Entry $entry }
            else {
                Write-Bad "No such item: $choice"
                Start-Sleep -Seconds 1
            }
        }
    }
}
