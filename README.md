# Frans Studios

Website and private client-delivery system for Frans Studios (wedding photography & cinematography).

- **Public site**: home, portfolio stories, films, about, packages (inquiry), contact
- **Client gallery** (`/client`): each couple signs in and views, streams and downloads their photos and films,
  which stay in the studio's Google Drive
- **Admin dashboard** (`/admin`): clients, albums, Google Drive folders, portfolio, films, packages, inquiries, activity log

| Layer | Tech |
|---|---|
| Frontend | React 19 · Vite · JavaScript/JSX · React Router · plain CSS (`Frontend/`) |
| Backend | ASP.NET Core Web API on .NET 10 · C# · EF Core (`Backend/FransStudios.Api`) |
| Database | SQL Server |
| Client file storage | Google Drive API v3 (server-side only) |

Docs: [architecture & audit](docs/ARCHITECTURE.md) · [deploying to Windows/IIS](docs/DEPLOYMENT.md)

## Run locally

Prerequisites: .NET 10 SDK, Node.js 22+, SQL Server LocalDB (included with Visual Studio) or any SQL Server.

```powershell
# 1. Backend (https://localhost:7220). Creates/migrates the LocalDB database on first run.
dotnet run --project Backend/FransStudios.Api --launch-profile https

# 2. Create an admin login (in another terminal)
dotnet run --project Backend/FransStudios.Api -- create-admin studio

# 3. Frontend (http://localhost:5173, proxies /api and /media to the backend)
cd Frontend
npm install
npm run dev
```

Open http://localhost:5173 (site), http://localhost:5173/admin (admin), http://localhost:5173/client (client gallery).

**Google Drive locally:** create an OAuth client (see [DEPLOYMENT.md §9](docs/DEPLOYMENT.md#9-google-cloud-oauth-for-google-drive-one-time-15-minutes))
with redirect URI `http://localhost:5173/api/admin/google-drive/callback`, then store the credentials as user secrets.
They are never put in files that go into Git:

```powershell
cd Backend/FransStudios.Api
dotnet user-secrets set "GoogleDrive:ClientId" "….apps.googleusercontent.com"
dotnet user-secrets set "GoogleDrive:ClientSecret" "…"
```

## Tests

```powershell
dotnet test Backend/FransStudios.Api.Tests
```

These are integration tests that run the real API against a throw-away LocalDB database, with Google's endpoints
faked at the HTTP layer. They cover:
- client isolation (IDOR)
- role checks
- CSRF
- lockout
- session revocation on disable and password reset
- Range streaming
- unsafe-type handling
- section ZIPs
- sync ordering

## Project layout

```
Backend/
  FransStudios.Api/
    Authentication/   cookie sessions, password hashing, CurrentUser
    Configuration/    strongly-typed options (appsettings + environment variables)
    Data/             DbContext, entity configuration, migrations
    Entities/         EF Core entities
    Features/         controllers + DTOs by feature (Auth, Admin, ClientPortal, Public)
    GoogleDrive/      OAuth tokens, Drive client, album sync, streaming proxy
    Infrastructure/   audit log, rate limits, CSRF + security headers, email, CLI commands
    Seo/              server-side <head> injection, robots.txt, sitemap.xml, legacy 301s
    Storage/          public portfolio media (WebP/JPEG variants)
  FransStudios.Api.Tests/
Frontend/
  src/
    components/  ui/, media/, admin/, public/
    content/     studio copy (founder text, FAQ, contact details)
    context/     AuthContext (session state for /client and /admin)
    hooks/       useApi, usePageTitle
    i18n/        customer-facing strings (en)
    layouts/     Public, Client, Admin
    pages/       public/, client/, admin/
    routes/      router (route-level code splitting)
    services/    api.js (fetch + CSRF + ProblemDetails)
    styles/      tokens, base, public, client, admin
deploy/          publish.ps1, production.env.example, app_offline.htm
docs/
```

## Security model (short version)

- **Sessions:** HttpOnly + Secure + `SameSite=Strict` cookie (`__Host-` prefix) holding a server-side session id.
  Sessions are revoked on logout, password reset or disabling a client.
- **CSRF:** every POST/PUT/DELETE needs the `X-XSRF-TOKEN` header (antiforgery).
- **Passwords:** stored as PBKDF2 hashes (ASP.NET Core Identity's `PasswordHasher`).
- **Brute force:** account lockout plus IP rate limiting.
- **Client isolation:** every client query is filtered by the client id from the session; URL ids are only extra
  filters. Anything not owned returns **404**.
- **Clients never see Drive ids:** they see only our own file ids, and the server maps those to Drive.
- **Google credentials:** the refresh token is encrypted with Data Protection and never leaves the server.
- **File streams:** files are streamed through the server with Range support and never made public. Unsafe file
  types (SVG, HTML, PDF…) are always sent as downloads with a sandbox CSP.
- **Response headers:** CSP, HSTS, `X-Frame-Options: DENY`, `nosniff`, and `noindex` for `/admin` and `/client`.
