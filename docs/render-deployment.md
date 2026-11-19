# Deploy Hishaber Khata to Render with Neon PostgreSQL

## Connect from local development

In Neon, open your project's **Connect** dialog and select the branch, database, and role. Copy the DNS hostname, database name, and role name. Use port `5432` and obtain the role password from the dialog. If the dialog offers a .NET format, select it. Otherwise, use those values to assemble the Npgsql string shown below. Use a development branch for local CRUD checks and disable pooling when preparing the schema-migration connection. [Neon connection guide](https://neon.com/docs/connect/connect-from-any-app)

The project already declares a `UserSecretsId`. From the repository root, enter the complete Npgsql string at the masked prompt. The value is written to the existing user-secret key for Development.

```powershell
$project = './ExpenseTracker/ExpenseTracker/ExpenseTracker.csproj'
$neonSecret = Read-Host 'Enter the Npgsql development connection string' -AsSecureString
try {
    @{ 'ConnectionStrings:DefaultConnection' = [System.Net.NetworkCredential]::new('', $neonSecret).Password } |
        ConvertTo-Json -Compress |
        dotnet user-secrets set --project $project
    if ($LASTEXITCODE -ne 0) { throw 'Could not save the development connection.' }
} finally {
    Remove-Variable neonSecret
}
dotnet build $project
dotnet run --project $project --launch-profile http
```

An environment variable named `ConnectionStrings__DefaultConnection` takes precedence over the development secret. Before running CRUD checks, initialize the development database using the direct connection procedure below.

## Provision Neon and prepare the schema

Create a Neon database in a region close to Render. In the Neon Connect dialog, choose the intended branch, database, and role. Use the direct hostname for EF migrations. The runtime can use the direct hostname or Neon's pooled hostname. Use a schema owner for migrations and give the runtime role the database connection, schema usage, table DML, and identity-sequence privileges it needs. [Neon connection guide](https://neon.com/docs/guides/dotnet-npgsql) · [Neon pooling](https://neon.com/docs/connect/connection-pooling)

Configure a native Npgsql connection string. Enter the decoded credential value and quote it if it contains connection-string delimiters.

```text
Host=<neon-hostname>;Port=5432;Database=<database>;Username=<role>;Password=<password>;SSL Mode=VerifyFull
```

From the repository root, generate a reviewable script using a placeholder connection. This command does not contact Neon.

```powershell
$project = './ExpenseTracker/ExpenseTracker/ExpenseTracker.csproj'
dotnet tool restore
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Database=design_only'
$script = Join-Path $env:TEMP 'hishaber-postgresql.sql'
dotnet tool run dotnet-ef -- migrations script 0 --idempotent --project $project --output $script -- --environment Production
Remove-Item Env:ConnectionStrings__DefaultConnection
```

Review the script and confirm the Neon branch and database. Apply it to an empty database or one already managed by this PostgreSQL migration history. If application tables exist without the baseline history entry, stop and reconcile the schema and history before applying migrations. Apply schema changes separately from the Render deployment. If existing MySQL records are needed, follow the [approved data copy procedure](../README.md#preserve-existing-mysql-records) before cutover.

```powershell
$secret = Read-Host 'Enter the direct Neon migration connection string' -AsSecureString
$env:ConnectionStrings__DefaultConnection = [System.Net.NetworkCredential]::new('', $secret).Password
$env:Database__RequireTls = 'true'
try {
    dotnet tool run dotnet-ef -- migrations list --project $project -- --environment Production
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect migration history.' }
    $confirm = Read-Host 'After confirming the target and migration history, type APPLY to proceed'
    if ($confirm -cne 'APPLY') { throw 'Schema migration cancelled.' }
    dotnet tool run dotnet-ef -- database update --project $project -- --environment Production
    if ($LASTEXITCODE -ne 0) { throw 'Schema migration failed.' }
    dotnet tool run dotnet-ef -- migrations list --project $project -- --environment Production
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify migration history.' }
} finally {
    Remove-Item Env:ConnectionStrings__DefaultConnection, Env:Database__RequireTls
    Remove-Variable secret
}
```

## Configure the Render web service

Create a Docker Web Service from the repository. Leave the root directory and Docker command blank, use `./Dockerfile`, and set the health check path to `/health`. [Render Docker setup](https://render.com/docs/docker) · [Render health checks](https://render.com/docs/health-checks)

| Variable | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | The runtime role's Npgsql connection string, stored as an environment secret |
| `PORT` | Render's HTTP port, typically `10000` |
| `Database__CaCertificatePath` | Optional absolute path to a PEM file when using a private CA |
| `DataProtection__KeyPath` | Optional absolute app-writable path on a persistent disk |

On Render the app binds to `0.0.0.0`, consumes the forwarded HTTPS scheme, and enforces PostgreSQL TLS certificate and hostname validation. Use the Neon DNS hostname for TLS. Set `Database__RequireTls=true` on other hosts to enforce the same validation. The web process leaves schema migrations to the deployment operator. [Npgsql TLS modes](https://www.npgsql.org/doc/security.html) · [Render environment secrets](https://render.com/docs/configure-environment-variables)

Deploy and confirm that `/health` returns HTTP 200 and that an anonymous visit to the dashboard redirects to login. Health checks the web process. Run the [PostgreSQL browser checks](../README.md#verify-postgresql) on a staging deployment to exercise the database, TLS, and application data paths.

On Render Free, the local Data Protection key ring can be lost on restart or redeploy, requiring users to sign in again. With persistent storage, set `DataProtection__KeyPath` to an app-writable directory such as `/var/data/hishaber-keys` and restrict access to the container user (UID 1654). [Render Free filesystem behavior](https://render.com/docs/free#local-files-lost-on-redeploy)
