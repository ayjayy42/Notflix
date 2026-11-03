# Notflix

Angular 22 + ASP.NET Core 10 movie catalogue, retaining the existing charcoal/yellow login and grey list/dialog styling. Signed-in users can list, add, edit, delete and rate movies. HTTP/session/search/list state uses RxJS observables. Ratings are 1-5 per user, with community average/count and the current user's vote. Existing catalogue ratings are retained separately as `legacyRating`; they are never converted into invented user votes.

## Run locally on Windows

Requires .NET 10 SDK, Node 24.15 or newer in the 24.x line, and SQL Server LocalDB. From the repository root:

```powershell
.\Setup-Local.ps1
.\Start-Notflix.ps1
```

Open http://127.0.0.1:4200. The generated login username/password is in `.local/development.json`, ignored by Git. `Stop-Notflix.ps1` stops only the recorded Notflix processes. Setup uses a separate `NotflixDevelopment` LocalDB database; the original configured database credentials are untouched. Setup is repeatable and never resets an existing user's hash. Preserve the local settings file: an existing file is always reused. To recover lost credentials, restore that file from your backup. To create a separate local account, first move the old settings file aside, then run `Setup-Local.ps1 -UserName another-name`; existing account hashes are never reset.

## Use another database

Set `ConnectionStrings__NotflixConnectionString` to the intended SQL Server database, and explicitly run the API with `--setup`. Schema changes preserve existing users/movies/legacy ratings and add nullable account fields plus a filtered unique username index and per-user votes. Never point setup at a system database. For seeding, also set `Seed__UserName` and `Seed__Password` (12+ characters), then run `--setup --seed`. No account is created during ordinary server startup. Back up important data before applying schema changes.

Development frontend uses `/api` through the proxy to loopback port 5246. In production serve frontend/API from the same HTTPS origin. Authentication uses HttpOnly cookies, unsafe requests require an antiforgery header, and login is rate-limited. The frontend refreshes antiforgery tokens after login/logout. API failures use safe problem responses.

## Tests

```powershell
dotnet test .\Notflix.API\Notflix.API.sln -c Release
cd Notflix.Web
npm ci --ignore-scripts
npm test
npx playwright install chromium
npm run test:e2e
```

API tests create isolated LocalDB databases named `NotflixTests_<guid>`. Browser tests use isolated local fixtures, never the saved remote application connection. Stop the development launcher before browser tests so ports 4200/5246 are available. Test cleanup checks the local server and generated database prefix before deletion. Browser automation covers the actual login/movie flows; SMTP is unrelated to this project.
