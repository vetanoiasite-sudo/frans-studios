# Deploying Frans Studios on Windows Server + IIS

This guide sets up the production site on a Windows server with IIS and SQL Server. The result is one IIS
site that serves the React app and the `/api` from the same origin (`https://fransstudios.com`).

```
Browser ──HTTPS──► IIS (fransstudios.com) ──in-process──► ASP.NET Core (FransStudios.Api.exe)
                                                        ├── wwwroot/            React build (public site, /client, /admin)
                                                        ├── SQL Server          application data
                                                        ├── D:\FransStudios\data\public-media   portfolio images & web films
                                                        ├── D:\FransStudios\data\private-cache  client thumbnail cache
                                                        ├── D:\FransStudios\data\keys           Data Protection key ring
                                                        └── Google Drive API    client deliveries (streamed)
```

> **Two storage systems, never mixed.**
> *Public portfolio media* (optimised WebP/JPEG images and web-encoded films) is uploaded in the admin
> dashboard and stored in `public-media`, served at `/media/...` with year-long caching.
> *Private client deliveries* stay in Google Drive and are only ever streamed through
> `/api/client/files/...` after the client's session and album ownership are checked. The private
> thumbnail cache lives in a different folder and is never exposed as static files.

---

## 1. Server prerequisites

| Component | Version / notes |
|---|---|
| Windows Server | 2019 or 2022 (Windows 10/11 Pro also works for testing) |
| IIS | Install the **Web Server (IIS)** role with: *Static Content*, *Default Document*, *HTTP Errors*, *Request Filtering*, *Static Compression*, *Dynamic Compression*, *IIS Management Console*. WebSocket support is not needed. |
| **ASP.NET Core 10 Hosting Bundle** | Download "Hosting Bundle" from https://dotnet.microsoft.com/download/dotnet/10.0. Install it **after** IIS, then run `net stop was /y` followed by `net start w3svc` (or reboot). |
| SQL Server | SQL Server 2019+ (Express is fine to start; the database is small because photos aren't stored in it). |
| Disk | Space for public media (portfolio + films) plus thumbnail cache. Client originals stay in Google Drive. |
| Outbound internet | HTTPS to `oauth2.googleapis.com`, `www.googleapis.com` and `*.googleusercontent.com` (Drive thumbnails). |

**Bandwidth:** every client download is streamed *through* this server (Google Drive → server → client).
A wedding delivery of ~15 GB that a couple downloads twice is ~30 GB outbound. Choose hosting with generous or unmetered egress.

## 2. SQL Server

Run in SQL Server Management Studio (adjust the password):

```sql
CREATE DATABASE FransStudios;
GO
CREATE LOGIN fransstudios_app WITH PASSWORD = 'use-a-long-random-password', CHECK_POLICY = ON;
GO
USE FransStudios;
CREATE USER fransstudios_app FOR LOGIN fransstudios_app;
ALTER ROLE db_datareader ADD MEMBER fransstudios_app;
ALTER ROLE db_datawriter ADD MEMBER fransstudios_app;
-- Needed only while running migrations (step 7). You can remove it afterwards and re-add it for upgrades.
ALTER ROLE db_ddladmin ADD MEMBER fransstudios_app;
GO
```

Connection string (used in step 4):

```
Server=localhost;Database=FransStudios;User Id=fransstudios_app;Password=…;Encrypt=True;TrustServerCertificate=True
```

Use `TrustServerCertificate=True` only when SQL Server runs on the same machine with its self-signed certificate.
For a remote SQL Server, install a proper certificate and use `TrustServerCertificate=False`.

Integrated security also works: grant the app-pool identity `IIS AppPool\FransStudios` a database user instead
of a SQL login, and use `Trusted_Connection=True`.

## 3. Folders and permissions

```powershell
New-Item -ItemType Directory -Force D:\FransStudios\site, D:\FransStudios\data\public-media, D:\FransStudios\data\private-cache, D:\FransStudios\data\keys, D:\FransStudios\site\logs
```

Create the IIS application pool first (step 5), then grant it access:

```powershell
icacls D:\FransStudios\site "IIS AppPool\FransStudios:(OI)(CI)RX"
icacls D:\FransStudios\site\logs "IIS AppPool\FransStudios:(OI)(CI)M"
icacls D:\FransStudios\data "IIS AppPool\FransStudios:(OI)(CI)M"
```

Keep `data` **outside** the site folder so deployments never overwrite media or keys.

> **Data Protection keys** (`D:\FransStudios\data\keys`) encrypt login cookies **and the stored Google
> refresh token**. Back them up. If they are lost, everyone is signed out and Google Drive must be
> reconnected (no data is lost). Keys are additionally protected with Windows DPAPI (machine scope), so
> they can't be copied to a different server and used there. After moving servers, reconnect Drive once.

## 4. Configuration (environment variables, not files)

Secrets are never stored in `appsettings.json` or Git. Set them as environment variables **on the IIS
application pool** so only this site can read them. See `deploy/production.env.example` for the full list.

```powershell
$pool = 'FransStudios'
$vars = @{
  'ConnectionStrings__Default'       = 'Server=localhost;Database=FransStudios;User Id=fransstudios_app;Password=…;Encrypt=True;TrustServerCertificate=True'
  'Site__PublicBaseUrl'              = 'https://fransstudios.com'
  'Storage__PublicMediaPath'         = 'D:\FransStudios\data\public-media'
  'Storage__PrivateCachePath'        = 'D:\FransStudios\data\private-cache'
  'Storage__DataProtectionKeysPath'  = 'D:\FransStudios\data\keys'
  'GoogleDrive__ClientId'            = '….apps.googleusercontent.com'
  'GoogleDrive__ClientSecret'        = '…'
}
foreach ($k in $vars.Keys) {
  & "$env:windir\system32\inetsrv\appcmd.exe" set config -section:system.applicationHost/applicationPools `
    "/+`"[name='$pool'].environmentVariables.[name='$k',value='$($vars[$k])']`"" /commit:apphost
}
```

To change a value later, remove and re-add it, or edit it in IIS Manager →
*Configuration Editor* → `system.applicationHost/applicationPools` → the pool → `environmentVariables`.
Recycle the pool afterwards.

`appsettings.json` contains only non-secret defaults (session lengths, limits, legacy redirects, business info).

## 5. IIS site and application pool

IIS Manager, or:

```powershell
Import-Module WebAdministration
New-WebAppPool -Name FransStudios
Set-ItemProperty IIS:\AppPools\FransStudios -Name managedRuntimeVersion -Value ''          # "No Managed Code"
Set-ItemProperty IIS:\AppPools\FransStudios -Name startMode -Value AlwaysRunning
Set-ItemProperty IIS:\AppPools\FransStudios -Name processModel.idleTimeout -Value '00:00:00'
Set-ItemProperty IIS:\AppPools\FransStudios -Name processModel.loadUserProfile -Value $true
Set-ItemProperty IIS:\AppPools\FransStudios -Name recycling.periodicRestart.time -Value '00:00:00'

New-Website -Name FransStudios -ApplicationPool FransStudios -PhysicalPath D:\FransStudios\site -Port 80 -HostHeader fransstudios.com
New-WebBinding -Name FransStudios -Protocol http -Port 80 -HostHeader www.fransstudios.com
Set-ItemProperty IIS:\Sites\FransStudios -Name applicationDefaults.preloadEnabled -Value $true
```

Keep **one worker process** per pool (the default; don't enable web gardens). The background album sync and
per-album locks assume a single process. If you ever scale out to several servers, see §12.

`web.config` (shipped with the build) already sets in-process hosting, removes the `Server` and `X-Powered-By`
headers, allows uploads up to 4 GB at the IIS level (the app enforces tighter limits per endpoint), and enables
compression for text responses only. Photo and video streams are never buffered or compressed by IIS.

## 6. HTTPS

1. Get a certificate for `fransstudios.com` and `www.fransstudios.com`. The free option is
   [win-acme](https://www.win-acme.com/) (Let's Encrypt), which installs it and renews automatically:
   `wacs.exe` → *Create certificate (full options)* → *Read bindings from IIS* → choose the FransStudios site.
   Or import a commercial certificate into *Local Computer → Personal*.
2. Add an `https` binding on port 443 for both host names with that certificate (win-acme does this for you).
3. Keep the port 80 bindings: the app redirects HTTP → HTTPS itself (`UseHttpsRedirection`) and sends
   `Strict-Transport-Security` (HSTS) on HTTPS responses.
4. **www → apex (optional):** add an IIS URL Rewrite rule, or add a 301 at the DNS/CDN level.

Cookies are `Secure`, `HttpOnly`, `SameSite=Strict`, with the `__Host-` prefix, so the site **must** be served over HTTPS.

## 7. Deploy a release

On a build machine (or the server) with the .NET 10 SDK and Node.js 22+:

```powershell
.\deploy\publish.ps1 -Output D:\FransStudios\release
```

The script runs `npm ci` and `vite build`, publishes the API in Release mode, and puts the React build in
`wwwroot`. Development settings and local data are left out.

Copy to the server:

```powershell
# 1. Take the site offline gracefully (IIS serves this file and releases file locks)
Copy-Item .\deploy\app_offline.htm D:\FransStudios\site\app_offline.htm
# 2. Copy the new build over the site folder (keep logs)
robocopy D:\FransStudios\release D:\FransStudios\site /MIR /XD logs /XF app_offline.htm
# 3. Apply database migrations (runs as you; needs the same env vars as the pool, or pass them in this shell)
cd D:\FransStudios\site
$env:ConnectionStrings__Default = '…'
.\FransStudios.Api.exe migrate
# 4. Bring the site back
Remove-Item D:\FransStudios\site\app_offline.htm
```

`migrate` is idempotent and safe to run on every release. If you prefer reviewing SQL first, generate a script on the
build machine: `dotnet ef migrations script --idempotent -p Backend/FransStudios.Api -o migrate.sql`.

## 8. First-time setup

1. **Create the studio admin** (prompts for the password; at least 10 characters):
   ```powershell
   cd D:\FransStudios\site
   $env:ConnectionStrings__Default = '…'
   .\FransStudios.Api.exe create-admin joe
   ```
   Forgotten admin password: `.\FransStudios.Api.exe reset-admin-password joe`.
2. Sign in at `https://fransstudios.com/admin/login`.
3. **Connect Google Drive** in *Admin → Google Drive* (needs §9 first).
4. Upload portfolio stories in *Admin → Portfolio*, using the same slugs as the old site (`sara-amr`,
   `mariam-mina`, `patti-franc`, `adelina-abdulla`, `maria-mina`, `europe`) so the old URLs redirect correctly.
5. Create clients and albums.

## 9. Google Cloud: OAuth for Google Drive (one time, ~15 minutes)

1. Go to https://console.cloud.google.com/ and sign in with the **studio's Google account** (the one that owns the Drive).
2. Create a project, e.g. **Frans Studios Website**.
3. *APIs & Services → Library* → enable the **Google Drive API**.
4. *APIs & Services → OAuth consent screen* (in newer consoles: *Google Auth Platform*):
   - User type: **External** (personal @gmail.com). For Google Workspace, choose **Internal** instead (no verification and no warning screen).
   - App name *Frans Studios Website*, support email, developer email.
   - Scopes: add `https://www.googleapis.com/auth/drive` (the site reads albums and uploads into them).
   - **Publishing status: click "Publish app" → In production.**
     ⚠ Don't leave it in *Testing*: Google expires refresh tokens for apps in Testing after **7 days**, which
     would silently disconnect Drive every week.
   - Because `drive` is a *restricted* scope, an unverified production app shows a "Google hasn't verified
     this app" screen during consent. That's expected here: only the studio owner ever goes through it, once.
     Click *Advanced → Go to Frans Studios Website (unsafe)* → allow. Verification isn't required for the owner's own account.
5. *APIs & Services → Credentials → Create credentials → OAuth client ID*:
   - Application type: **Web application**
   - Authorized JavaScript origins (the admin's browser uploads album files straight to Google):
     - `https://fransstudios.com`
     - (for local development) `http://localhost:5173`
   - Authorized redirect URIs:
     - `https://fransstudios.com/api/admin/google-drive/callback`
     - (for local development) `http://localhost:5173/api/admin/google-drive/callback`
6. Copy the **Client ID** and **Client secret** into the app-pool environment variables
   `GoogleDrive__ClientId` / `GoogleDrive__ClientSecret` (§4), then recycle the pool.
7. In the admin: *Google Drive → Connect Google Drive* → sign in with the studio account → allow.
8. Nothing else to set up: new album folders go in `My Drive / Frans Studios Albums`, which the website creates the first time an album needs a folder.

**Switching to another Google account** (for example from a developer's test account to the studio's): press
*Reconnect / switch account* and sign in with the new account. Albums already in the previous account's Drive keep
working (the previous account's access stays stored; only *Disconnect* revokes it) and are flagged "Old Drive" in
*Albums*. *Move all to the current Drive* then has Google copy their files into the new Drive and switches each album
over; clients notice nothing. The originals stay in the old Drive. While the consent screen is in *Testing*, the old
account's access expires 7 days after it was connected, so move the albums before then.

**Uploading albums from the admin.** On an album page, *Upload to Google Drive* sends files into the album's Drive
folder in resumable chunks, straight from the admin's browser to Google, so large weddings don't pass through this
server. If Google ever refuses the browser's requests, the page switches to sending chunks through the server on its
own; to force that for everyone set `GoogleDrive__UploadMode=Proxy`. A connection made before uploads existed is
read-only: press *Reconnect* once to allow uploads. The studio's Google storage plan must have room for the albums.

**Moving to Google Workspace later:** create (or switch) the consent screen to *Internal* in the Workspace
account's Cloud project, update the Client ID/secret if they change, and click *Reconnect* in the admin. Shared
drives work too (the app sends `supportsAllDrives`). Existing albums keep working as long as the folders stay
reachable by the connected account; folder IDs don't change when files are moved between drives.

**Recommended Drive structure:**

```
Clients/
  Sara & Ahmed Wedding/
    Photos/          ← becomes the "Photos" tab
    Videos/          ← put a 1080p "web" MP4 (H.264, fast start) next to each master
    Documents/
```

## 10. Email (optional)

Inquiries are always saved and visible in *Admin → Inquiries*. To also get an email for each one:

```
Email__Provider=Smtp
Email__FromAddress=no-reply@fransstudios.com
Email__NotifyAddress=Frans.Weddings@gmail.com
Email__Smtp__Host=smtp.gmail.com         (or your provider: Microsoft 365, Brevo, Postmark, Amazon SES…)
Email__Smtp__Port=587
Email__Smtp__Username=…
Email__Smtp__Password=…                  (for Gmail: an App Password, not the account password)
```

Email failures are logged and never block the form.

## 11. Operations

**Backups (schedule all three):**

| What | How |
|---|---|
| SQL database | SQL Server Agent / maintenance plan, or `BACKUP DATABASE FransStudios TO DISK='…' WITH COMPRESSION` nightly. Keep copies off the server. |
| `D:\FransStudios\data\public-media` | Nightly file backup (portfolio images and films). |
| `D:\FransStudios\data\keys` | Back up once, and again after changes (keys roll every 90 days). |

`private-cache` is disposable: it rebuilds itself from Google Drive.

**Logs:** the app writes to the Windows **Event Log** (Application, source ".NET Runtime"/"FransStudios.Api").
For startup problems, temporarily set `stdoutLogEnabled="true"` in `web.config`. Logs go to `D:\FransStudios\site\logs`.
Turn it off again afterwards.

**Health check:** `https://fransstudios.com/api/health` returns `{"status":"ok"}` when the app and database are up.

**Activity / audit:** *Admin → Settings & activity* shows sign-ins, failed sign-ins, downloads and admin changes.

## 12. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| HTTP 500.30 / 500.31 at startup | Hosting Bundle missing or installed before IIS → reinstall the bundle and run `iisreset`. Check the Event Log. |
| HTTP 500.30 + "ConnectionStrings:Default is not set" | Environment variables not on the app pool (§4), or the pool wasn't recycled. |
| Site shows "Frontend build not found" | `wwwroot\index.html` missing: deploy with `deploy\publish.ps1`. |
| Everyone is signed out after a server move | Data Protection keys weren't carried over (expected with DPAPI). Sign in again and reconnect Drive. |
| Admin shows "Google Drive access has expired or was revoked" | The consent screen is still in *Testing* (7-day tokens), the studio changed its Google password, or access was removed. Fix §9.4, then *Reconnect*. |
| A client sees "This file is very popular right now" | Google's per-file download quota was hit. Wait ~1 hour. Files are never made public as a workaround. |
| Videos won't seek / start slowly | The MP4 lacks "fast start" (moov atom at the end). Re-export with *Web optimized / Fast start* (HandBrake: "Web Optimized"). |
| Uploads > 30 MB fail with 413 | Make sure the shipped `web.config` (with `maxAllowedContentLength`) is deployed. |
| Wrong client IPs in the activity log | A proxy/CDN is in front of IIS. Set `ReverseProxy__KnownProxies__0=<proxy IP>`. For Cloudflare, consider the official IIS module that restores visitor IPs. |

## 13. Moving to another host later

Nothing in the app is IIS-specific:

- **Linux / Docker / Azure App Service:** `dotnet publish -r linux-x64` and run behind Nginx or the platform's proxy.
  Set the same environment variables. Data Protection uses the file system: on Linux, drop DPAPI (it's
  only applied on Windows automatically) and protect the keys folder by file permissions, or use Azure Key Vault/Blob.
  Add `app.UseResponseCompression()` if no proxy compresses responses, and pass `X-Forwarded-*` from the proxy
  (configure `ReverseProxy:KnownProxies`).
- **Azure SQL** works unchanged (SQL Server provider).
- **Object storage for public media:** `PublicMediaStore` is the only class that touches that folder. Swap it for
  Azure Blob/S3 if the server disk becomes a limit.
- **More than one server:** move the album-sync lock and access-token cache to a shared store (for example Redis
  or SQL `sp_getapplock`) and share the Data Protection key ring between servers.
