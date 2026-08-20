# Local UI inspection guide — every page the identity server renders

Twenty renderable pages: thirteen under `Areas/Identity/Pages/Account`, four under
`Areas/Identity/Pages/Manage`, and three MVC views (`Consent`, device `Verify`, `Error`). This walks
all twenty, in an order where each step leaves behind the state the next one needs.

Every URL and every screenshot-worthy state below was executed against a live local server while
writing this, so the routes, the flags, and the emitted links are observed rather than inferred.
Keep it that way: this is a living document, and a page whose flow changes should be re-walked here
rather than described from the code.

---

## 0. Prerequisites

- SQL Server LocalDB running (the app creates and migrates `TellmaIdentity` itself).
- The ASP.NET dev certificate trusted: `dotnet dev-certs https --trust`.
- Nothing else on ports 7051 / 5051.

## 1. Configure once, in user secrets

Two settings are off by default and hide four pages, and one client has to exist before any
OIDC-driven page can be reached. Put them in the host's user-secrets store so every terminal picks
them up and no tracked file changes. `Tellma.Identity.Web` already declares a `UserSecretsId`, and
`WebApplication.CreateBuilder` loads user secrets automatically in Development, so nothing else is
needed:

```bash
dotnet user-secrets --project src/apps/Tellma.Identity.Web list
```

Edit that store's `secrets.json` to:

```json
{
  "TellmaIdentity": {
    "EnablePasswordSignIn": true,
    "Seed": {
      "Clients": [
        {
          "ClientId": "local-browser",
          "DisplayName": "Local Browser Client",
          "Kind": "Cli",
          "RedirectUris": [ "http://127.0.0.1/callback" ]
        },
        {
          "ClientId": "local-control-plane",
          "DisplayName": "Local Control Plane",
          "Kind": "ControlPlane",
          "ClientSecret": "local-dev-secret"
        }
      ]
    }
  }
}
```

Then every launch is just:

```bash
cd src/apps/Tellma.Identity.Web
dotnet run
```

Environment variables work too if you prefer them per-shell — `TellmaIdentity__Seed__Clients__0__ClientId=local-browser`
and so on, with `__0__` for the array index.

Why each piece:

| Setting | Unlocks |
|---|---|
| `EnablePasswordSignIn=true` | `ForgotPassword` and `ResetPassword` — both return **404** otherwise |
| `local-browser` (Cli archetype) | everything reached through `/connect/authorize` and the device grant |
| `local-control-plane` | the Temporary Access Pass API behind the `Tap` page |

`dotnet run` picks up `Properties/launchSettings.json`, which sets `ASPNETCORE_ENVIRONMENT=Development`
— that is what loads `appsettings.Development.json` (LocalDB, self-signed dev keys, the seeded dev
admin) and what makes the email pipeline choose its log sink. Without the Development environment
the host **fails to start**: the dev certificate sources are refused, and so is the log sink, which
would otherwise discard real mail silently. Do not pass `--no-launch-profile`.

Wait for `Applying pending identity store migrations`, then open <https://localhost:7051>.

## 2. Where the emails go

There is no SMTP server locally, and none is configured. The identity server now sends through the
platform's shared email pipeline, which picks its transport from `Email:Provider` — and in
Development, with that key absent, picks the log sink. So there is nothing to configure: every
message is written to the console.

```
EMAIL SINK channel=live to=New Bie <newbie@localhost> cc=- bcc=- audience=internal correlation=none attachments=wordmark.png (5864 bytes) htmlLength=4972 subject=Your Tellma sign-in code body=Your sign-in code

Enter this code to finish signing in to Tellma as newbie@localhost.

91095144

The code expires in 10 minutes and can be used once.
...
```

The envelope carries more than it used to — the recipient now shows a display name, and `channel`,
`audience` and `correlation` come from the shared contract — and **the body no longer fits on the
line**. It is a full message now, laid out in paragraphs, so the code sits on a line of its own a
few lines below `EMAIL SINK` rather than inside it. Sign-in codes, invitation links and
password-reset links all arrive this way. Keep the terminal visible — several steps below need a
value from it, and `inspect.ps1` reads them from the same place by looking forward from the
`EMAIL SINK` line rather than at it.

Every message also carries an HTML alternative and the brand mark it references, which is what
`htmlLength` and `attachments` report. Neither is printed — the sink logs the text body only — but
the two now say the same thing, so what scrolls past is what the recipient reads.

Each message also carries a correlation naming the single-use code it belongs to, and the row
records how far its mail got. That is only visible in the database, but it is what makes the
invitation sweep possible: kill the server between inviting someone and the queue draining, restart
it, and within a couple of minutes the sweep delivers a fresh link — a different one, since the
original secret was never stored. The log sink reports no delivery events, so an invitation sent
here rests at `Sent` for good, which is exactly what an on-premise SMTP relay does too.

To *look* at the HTML rather than trust its length, point the pipeline at a local SMTP catcher —
anything that accepts mail on a port and shows it in a browser — and set the whole group, not just
the host. The transport's defaults are written for a real relay (port 587, STARTTLS, and a sender
address it insists on), so a catcher needs all four overridden or the host refuses to start:

```bash
dotnet user-secrets set "Email:Provider" "Smtp" && dotnet user-secrets set "Email:Smtp:Host" "localhost" && dotnet user-secrets set "Email:Smtp:Port" "1025" && dotnet user-secrets set "Email:Smtp:SecureSocket" "None" && dotnet user-secrets set "Email:Smtp:From:Address" "no-reply@localhost"
```

Remove `Email:Provider` again to go back to the console sink — that sink is the only transport that
never reaches the network, which is why it is the default here.

You will also see one line at startup naming the transport in force, which is the quickest way to
confirm nothing is about to try real delivery:

```
Email transport log-sink is active for deployment identity-development (delivery webhook configured: False, sandbox channel: True).
```

## 3. Seeding is idempotent — with one edge worth knowing

Restarting as often as you like is safe. Measured across five boots against the same database:

| Seeded thing | Behaviour on every start |
|---|---|
| Migrations | `MigrateAsync` — no-op once current |
| Scopes | create if absent, else **union** new resources into the existing scope |
| Clients | create if absent, else **overwrite** the stored client with the seeded definition |
| Dev admin | create if absent, else left completely alone — never reset |

No duplicates accumulate; two clients and two users after five boots.

The edge is the client row: re-seeding is not "leave it alone", it re-applies the descriptor. So any
hand edit to a seeded client is reverted at the next start — which is why client settings belong in
configuration and not in SQL (step 27). Resource permissions are the exception: those accumulate for
CLI and native clients, so a distribution audience granted at provisioning time is not lost.

## 4. Two more things worth knowing

- The seeded dev admin is `admin@localhost` and has **no credential at all** — email code is how you
  get in. That is deliberate; passwords are off and no passkey is seeded.
- Your session normally survives a server restart (data-protection keys persist to the per-user key
  ring). Tick **Remember me** at sign-in and it reliably does; if you do land back on Sign in after a
  restart, just sign in again.
- Identity re-issues the session cookie every five minutes to pick up security-stamp changes. A
  session that had been open longer than that used to lose its evidence and fail the next
  authorization with "the session must be re-established" — fixed, but it is what an intermittent
  failure a few minutes into a review would have been.

---

## Phase A — anonymous pages (no state needed)

Paste each URL directly.

| # | Page | URL | What you should see |
|---|---|---|---|
| 1 | **Login** | `/Identity/Account/Login` | "Sign in", email field, "Email me a sign-in code", "Sign in with a passkey" |
| 2 | **Login — no method** | `/Identity/Account/Login?methods=password` | "No sign-in method is available for this request." — the allow-list names only methods this deployment does not offer |
| 3 | **AccessDenied** | `/Identity/Account/AccessDenied` | "You do not have access to this resource." |
| 4 | **LoggedOut** | `/Identity/Account/LoggedOut` | "You are signed out." |
| 5 | **Logout** | `/Identity/Account/Logout` | "Sign out of your account?" with the confirm button |
| 6 | **DeviceApproved** | `/Identity/Account/DeviceApproved` | "All set — you can return to your device." |
| 7 | **EmailCode** | `/Identity/Account/EmailCode?email=admin@localhost` | The code entry form, "Verify" and "Send a new code". Click "Send a new code" with the field empty — it must submit, since asking for a code is what you do when you have none |
| 8 | **Setup** | `/Identity/Account/Setup` | "Set up the administrator", one-time setup token field |
| 9 | **Tap** | `/Identity/Account/Tap` | "Recover your account", email + access pass fields |
| 10 | **Invitation — invalid** | `/Identity/Account/Invitation?code=bad` | "This invitation link is invalid or has expired." The enumeration-safe state: expired, used, and forged links all look identical |
| 11 | **ForgotPassword** | `/Identity/Account/ForgotPassword` | "Forgot your password?" (404 without the flag from step 1) |
| 12 | **ExternalLogin** | `/Identity/Account/ExternalLogin?handler=Callback&remoteError=access_denied` | The error state with a link back to sign-in. This page has no success state of its own — it only ever renders an error, so this is all there is to review without real Google/Microsoft credentials. With credentials configured, signing in through a provider the account has not connected renders the other message: an instruction to sign in another way and connect it from Account & security |
| 13 | **Error** | `/error` | "Something went wrong", plus a reference — the request's trace id, which is what a user quotes and an operator searches for |
| 14 | **Error with protocol detail** | `/connect/authorize?client_id=nope&response_type=code&redirect_uri=http%3A%2F%2F127.0.0.1%2Fcallback&scope=openid` | Same page carrying `invalid_client` / "The specified client is unknown." — confirm it never echoes raw request data |

## Phase B — sign in

15. Go to `/Identity/Account/Login`, enter `admin@localhost`, submit.
16. You land on **EmailCode** in its real context. Copy the 8-digit code from the terminal and submit.
17. You are now signed in as the dev admin.

## Phase C — signed-in pages

| # | Page | URL |
|---|---|---|
| 18 | **Manage / Profile** | `/Identity/Manage/Index` |
| 19 | **Manage / Passkeys** | `/Identity/Manage/Passkeys` |
| 20 | **Manage / Sessions** | `/Identity/Manage/Sessions` — your current session should be listed |
| 21 | **Manage / Authenticator app** | `/Identity/Manage/EnableAuthenticator` — QR code and manual key |
| 22 | **RegisterPasskey** | `/Identity/Account/RegisterPasskey` — redirects to Login when anonymous |

On **RegisterPasskey** you can complete a real ceremony with Windows Hello or a security key; the
credential then shows up on the Passkeys page, which is worth doing before Phase D so the step-up
screens have something to satisfy them.

## Phase D — the OIDC-driven pages

All of these use one authorize URL. The PKCE pair is fixed and local-only, so it is safe to paste
(verifier `tellma-local-inspection-verifier-0123456789abcdefghijklmnop`, never redeemed here):

```
https://localhost:7051/connect/authorize?client_id=local-browser&response_type=code&redirect_uri=http%3A%2F%2F127.0.0.1%2Fcallback&scope=openid%20profile%20email&code_challenge=OTAUWGLBzoyXFd-eBWRw54MfcImOuK-5HoHKhWWIlc0&code_challenge_method=S256&state=abc123
```

23. **Plain** — with a session it redirects straight to `http://127.0.0.1/callback?code=…`, which
    fails to load. That failure *is* the success: a code was issued.
24. **Forced re-authentication** — append `&prompt=login`, or `&max_age=0`. The heading changes from
    "Sign in" to **"Confirm it's you"**. Both were checked; both reach the same page.
25. **Step-up to aal2** — append `&acr_values=urn%3Atellma%3Aacr%3Aaal2`. Only the passkey button is
    offered: an email code cannot satisfy aal2.
26. **Step-up to aal3** — append `&acr_values=urn%3Atellma%3Aacr%3Aaal3`. Adds the device-bound
    passkey notice. A synced passkey is refused here even though it signs you in elsewhere.
27. **Consent** — configure a client to require it. Seeded clients default to implicit because
    they are first-party; add `RequireConsent` to a seed entry in user secrets:

    ```json
    { "ClientId": "third-party", "DisplayName": "Third Party App", "Kind": "Native",
      "RequireConsent": true, "RedirectUris": [ "http://127.0.0.1/callback" ] }
    ```

    Restart, then authorize with `client_id=third-party`: "Authorize application — Third Party App
    wants to access your account…", Allow / Deny.

    > Do **not** try to flip `ConsentType` with SQL. It fails twice over: OpenIddict caches
    > applications in memory, so a running server never sees the change; and the seeder re-applies
    > the client descriptor on every start, so a restart reverts the row. An earlier version of
    > this guide suggested the SQL — that was wrong.

28. **Device Verify** — start a device authorization:

    ```bash
    curl -sk -X POST https://localhost:7051/connect/device -d "client_id=local-browser&scope=openid profile"
    ```

    Open the `verification_uri_complete` it returns. You get **"Authorize your device"** naming the
    client, with Allow / Deny. Visiting `/connect/verify` with no code shows the empty code-entry
    form instead — both states are worth a look.
29. **DeviceApproved in context** — click Allow on the previous step: "All set."

## Phase E — the token-gated pages

**Invitation (valid).** Get a token with the management scope through the device grant, then invite
someone:

```bash
curl -sk -X POST https://localhost:7051/connect/device -d "client_id=local-browser&scope=openid tellma_identity"
```

Approve the returned `user_code` in the browser, then redeem it:

```bash
curl -sk -X POST https://localhost:7051/connect/token -d "grant_type=urn:ietf:params:oauth:grant-type:device_code&client_id=local-browser&device_code=PASTE_DEVICE_CODE"
```

Write the body to a file and post that — inline JSON quoting differs between cmd, PowerShell
and bash, and the escaped-double-quote form reaches PowerShell with the backslashes intact
(`'\' is an invalid start of a property name`):

```bash
echo '{"users":[{"email":"newbie@localhost","displayName":"New Bie","locale":"en"}]}' > invite.json
```

```bash
curl -sk -X POST https://localhost:7051/api/identity/invitations -H "Authorization: Bearer PASTE_ACCESS_TOKEN" -H "Content-Type: application/json" --data-binary @invite.json
```

30. The invitation link appears in the terminal a moment later (delivery is queued, not inline).
    Open it: **"Welcome"** — the valid state. Note the link is single-use; opening it consumes it, so
    re-invite if you want a second look.

**Temporary Access Pass.** Using the control-plane client:

```bash
curl -sk -X POST https://localhost:7051/connect/token -d "grant_type=client_credentials&client_id=local-control-plane&client_secret=local-dev-secret&scope=tellma_control_plane"
```

```bash
curl -sk -X POST "https://localhost:7051/api/identity/users/00000000-0000-0000-0000-000000000001/temporary-access-passes" -H "Authorization: Bearer PASTE_ACCESS_TOKEN"
```

31. Enter the returned pass plus `admin@localhost` on `/Identity/Account/Tap`. It redirects into
    **RegisterPasskey** in recovery mode — the admin-assisted recovery path end to end.

**ResetPassword (valid).** Submit `admin@localhost` on `/Identity/Account/ForgotPassword`.

32. The reset link appears in the terminal. Open it for the real form. Compare against
    `/Identity/Account/ResetPassword?code=bad`, which renders the same page in its invalid state.

## Phase F — Arabic / RTL

Every page above renders right-to-left with the Arabic resources. Only `en` and `ar` are supported;
anything else falls back to `en`.

**One page at a time — the query string.** Add `culture=ar` to any URL. Either parameter works on its
own (`culture` and `ui-culture` fill in for each other), and use `&` rather than `?` when the URL
already has a query:

```
/Identity/Account/Login?culture=ar
/Identity/Account/Login?methods=passkey%20email_code&culture=ar
```

**A whole flow — the culture cookie.** The query string applies to that one request only: it sets no
cookie, so anything that redirects (authorize → login → email code, the invitation link, the TAP
flow) drops back to English at the first hop. Set the cookie once instead and every later page,
redirect targets included, comes back Arabic. In the browser console on the site's origin:

```js
document.cookie = ".AspNetCore.Culture=c%3Dar%7Cuic%3Dar; path=/"
```

That is the standard ASP.NET Core culture cookie holding `c=ar|uic=ar`. Delete it, or set it to
`c%3Den%7Cuic%3Den`, to go back. Setting the browser's preferred language to Arabic works too and
survives everything, but it also affects every other site you have open.

Confirm `<html lang="ar" dir="rtl">` and that the layout mirrors — this exercises `SharedResources.ar.resx`
and the `[lang]:lang(ar)` rules in the vendored token stylesheet.

---

## Coverage check

| Area | Pages |
|---|---|
| Account (13) | AccessDenied, DeviceApproved, EmailCode, ExternalLogin, ForgotPassword, Invitation, LoggedOut, Login, Logout, RegisterPasskey, ResetPassword, Setup, Tap |
| Manage (4) | Index, Passkeys, Sessions, EnableAuthenticator |
| MVC views (3) | Consent, device Verify, Error |

The five remaining `.cshtml` files are not pages: `_Layout`, `_PasskeyScripts`, `_StatusMessage`, and
the two `_ViewImports` / `_ViewStart` pairs. `_Layout` is visible on every page above; `_StatusMessage`
shows up as the banner on EmailCode ("If an account exists for that address…").

## Stopping and resetting

Ctrl-C in the `dotnet run` terminal. To start from a clean store, drop the database and let the next
run re-migrate and re-seed it:

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "DROP DATABASE TellmaIdentity"
```
