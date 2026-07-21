# Database workflow

Entity Framework Core migrations in `PicklinkBackend/Migrations` are the source of truth for every new database and production deployment.

Run these commands from the backend repository root:

```powershell
dotnet ef migrations add <MigrationName> --project PicklinkBackend\PicklinkBackend.csproj --startup-project PicklinkBackend\PicklinkBackend.csproj --output-dir Migrations
dotnet ef database update --project PicklinkBackend\PicklinkBackend.csproj --startup-project PicklinkBackend\PicklinkBackend.csproj
```

For deployment, generate and review an idempotent SQL script before applying it:

```powershell
dotnet ef migrations script --idempotent --project PicklinkBackend\PicklinkBackend.csproj --startup-project PicklinkBackend\PicklinkBackend.csproj --output migration.sql
```

`Startup:RunSchemaChecks` and `SchemaStartup` exist only to repair older local development databases. Keep `Startup__RunSchemaChecks=false` in production and do not use schema repair as a replacement for a migration.

When the EF model changes:

1. Add a named migration.
2. Review the generated `Up` and `Down` methods.
3. Run the backend test suite.
4. Test the migration against a disposable database or reviewed backup before production.
