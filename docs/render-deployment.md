# Deploy Hishaber Khata to Render

This repository contains the .NET 8 MVC app at `ExpenseTracker/ExpenseTracker/ExpenseTracker.csproj`, with the solution one level above it. Render builds the root `Dockerfile` with the .NET 8 SDK and runs the published app on the .NET 8 ASP.NET runtime as a non-root user. The ASP.NET Core app and MySQL database run as separate services.

## 1. Provision MySQL 8

- Create a durable MySQL 8 database reachable from Render. Use the provider's DNS hostname that appears on its TLS certificate. The laptop's `localhost` points to the container when used on Render.
- Use separate database accounts. Give the web app account `SELECT`, `INSERT`, `UPDATE`, and `DELETE` on the application schema. Use a separate account with schema-migration privileges when upgrading the database.
- If the database firewall restricts addresses, allow the web service's outbound ranges from **Connect → Outbound** in Render. Also allow the host from which you will run migrations. Render's regional outbound ranges are shared, so keep database authentication and TLS in place. [Render outbound IP addresses](https://render.com/docs/outbound-ip-addresses)
- Rotate the old database password if it is still active. Earlier commits contained a credential, and changing the current file does not remove those commits from Git history.

## 2. Initialize or upgrade the schema

Run EF from a trusted machine or CI runner with the .NET 8 SDK and MySQL access. Render's free service lacks shell access. The Docker image starts the web process without applying migrations. Use a backup and rehearse the upgrade on a restored copy before changing an existing database. [Render free service limits](https://render.com/docs/free)

From the repository root in a fresh VS Code PowerShell terminal:

```powershell
$project = '.\ExpenseTracker\ExpenseTracker\ExpenseTracker.csproj'
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'EF tool restore failed.' }

$secret = Read-Host 'Enter the MySQL migration connection string' -AsSecureString
$env:ConnectionStrings__DefaultConnection = [System.Net.NetworkCredential]::new('', $secret).Password
$env:Database__RequireTls = 'true'
# Only when the database provider supplies a private CA, uncomment and set its local PEM path.
# $env:Database__CaCertificatePath = 'C:\path\to\mysql-ca.pem'

dotnet tool run dotnet-ef -- migrations list --project $project -- --environment Production
if ($LASTEXITCODE -ne 0) { throw 'Could not read migration history.' }
```

Enter a connection string using the migration account and the intended database. Use the format in Step 3. Set `$fromMigration` to `0` for a fresh, empty schema. For an existing schema, use the latest migration EF reports as applied. The pre-Identity baseline is `20260118182451_AddUserSettings`, and the latest migration in this repository is `20261118200857_AddIdentityAndOwnership`. If the schema already has application tables but lacks EF migration history, reconcile the schema and history before proceeding.

```powershell
$fromMigration = '0' # Replace with the last applied migration for an existing database.
$scriptPath = Join-Path $env:TEMP 'hishaber-khata-forward-migration.sql'
dotnet tool run dotnet-ef -- migrations script $fromMigration --project $project --output $scriptPath -- --environment Production
if ($LASTEXITCODE -ne 0) { throw 'Could not generate the migration script.' }
```

Review the generated SQL. The forward migrations create tables and add columns, indexes, and foreign keys. The Identity migration preserves legacy transactions and settings with a nullable owner. For an existing service, pause writes while upgrading. MySQL schema operations can commit independently, so investigate any partially applied migration before retrying. Keep legacy rows unassigned until a reviewed ownership assignment has been approved. See the [legacy ownership procedure](../README.md#assign-legacy-transactions-and-settings).

After reviewing the script and confirming the target database, apply the pending forward migrations and check the result:

```powershell
try {
    dotnet tool run dotnet-ef -- database update --project $project -- --environment Production
    if ($LASTEXITCODE -ne 0) { throw 'Migration failed. Check the database state before retrying.' }

    dotnet tool run dotnet-ef -- migrations list --project $project -- --environment Production
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify migration history.' }
} finally {
    Remove-Item Env:ConnectionStrings__DefaultConnection, Env:Database__RequireTls, Env:Database__CaCertificatePath -ErrorAction SilentlyContinue
    Remove-Variable secret -ErrorAction SilentlyContinue
}
```

For an existing database, compare row counts and financial totals with your backup and check the migrations are all applied. Keep the rollback plan based on a verified backup and a separately approved recovery. The EF `Down` migrations can remove tables and ownership information.

## 3. Create the Render Free Web Service

1. Open the [Render Dashboard](https://dashboard.render.com/), choose **New → Web Service**, and connect the GitHub repository `arrahmanriju/Hishaber-khata-expense-tracker-`.
2. Choose the branch containing the deployment changes, normally `main` after merging. Set **Language** to **Docker** and **Instance Type** to **Free**. Choose a region close to your database.
3. Leave **Root Directory** empty so the build context is the repository root. Use `./Dockerfile` for **Dockerfile Path** and leave **Docker Command** blank. The image starts the app through its default entrypoint. [Render Docker setup](https://render.com/docs/docker)
4. Set **Health Check Path** to `/health` under the service settings. It returns an anonymous HTTP 200 when the web process is responding. Use a separate sign-in and transaction check for database readiness. [Render health checks](https://render.com/docs/health-checks)
5. Add the following values in the Render **Environment** panel before deploying. Store production credentials only in Render's runtime environment or secret files. [Render environment variables and secrets](https://render.com/docs/configure-environment-variables)

| Variable | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production`. This is also the image default. |
| `PORT` | `10000`. Render supplies this default. Change it only if both the service and listener should use another port. |
| `ConnectionStrings__DefaultConnection` | The runtime MySQL account connection string. See the example below. |
| `Database__CaCertificatePath` | Optional. Set `/etc/secrets/mysql-ca.pem` if the provider requires a private CA. Upload the provider's CA PEM as a Render Secret File named `mysql-ca.pem`. |
| `Database__RequireTls` | Optional on Render. TLS verification is already enforced there. Set `true` on another host or a migration workstation to require the same policy. |
| `DataProtection__KeyPath` | Leave unset on Free. See the persistent-key option below. |

```text
Server=<provider DNS hostname>;Port=3306;Database=<database name>;User ID=<runtime user>;Password=<runtime password>;SslMode=VerifyFull;
```

Use the provider's port if it differs. A password containing connection-string delimiters must be quoted within its value. Render automatically sets `RENDER=true`. On Render the app binds to `0.0.0.0`, enforces TLS with certificate and hostname validation, and uses Render's forwarded HTTPS scheme. It relies on Render's edge for HTTP-to-HTTPS redirects. The app only consumes the scheme header and does not use forwarded client IP or host headers. Leave `RENDER` unset for directly exposed or non-Render deployments. [Render port and TLS behavior](https://render.com/docs/web-services) · [MySqlConnector TLS settings](https://mysqlconnector.net/connection-options/)

## 4. Deploy and verify

1. Deploy and check the logs for a listener on `0.0.0.0:10000`.
2. Visit `https://<service-name>.onrender.com/health` and confirm HTTP 200. Verify the HTTPS site redirects an anonymous visitor to the login page.
3. Register a test account, sign in, add and edit a test transaction, and verify that a second account cannot see it. This exercises the TLS connection and migration as well as application startup.
4. If health passes but sign-in or a dashboard query fails, check the application logs for a missing migration, MySQL credentials, firewall connectivity, or certificate errors. Correct the database hostname or CA if certificate verification fails.

### Sign-in sessions on the Free plan

The free web service spins down when idle and loses local filesystem changes on spin-down, restart, or redeploy. The default ASP.NET Core Data Protection key ring lives on that filesystem. Users may need to sign in again after a restart, and an open form may need to be reloaded. Accounts and financial records live in MySQL. [Render free filesystem behavior](https://render.com/docs/free#local-files-lost-on-redeploy)

On a deployment with a persistent disk, set `DataProtection__KeyPath` to an absolute, app-writable directory on that disk, such as `/var/data/hishaber-keys`. Restrict access to the app's container user (UID 1654) and protect the disk and backups. File persistence alone does not encrypt key XML. Services sharing the key ring should use the same application name and guard the keys as credentials. [ASP.NET Core Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-8.0)

### Local Docker smoke check

These commands check the port, health endpoint, and anonymous redirect with an intentionally fake database connection. They do not initialize MySQL.

```powershell
docker build -f Dockerfile -t hishaber-khata:local .
$env:ConnectionStrings__DefaultConnection = 'Server=localhost;Database=smoke_check;User ID=smoke_check;'
docker run --rm -p 127.0.0.1:10000:10000 --env ConnectionStrings__DefaultConnection hishaber-khata:local
```

In a second terminal, run `curl.exe --fail http://127.0.0.1:10000/health`. Stop the smoke-test container with **Ctrl+C** and clear the placeholder using `Remove-Item Env:ConnectionStrings__DefaultConnection`.
