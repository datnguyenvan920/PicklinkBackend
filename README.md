# Picklink Backend

Active ASP.NET Core backend solution for Picklink.

## Active Projects

- `PicklinkBackend/PicklinkBackend.csproj`: API application.
- `PicklinkBackend.Tests/PicklinkBackend.Tests.csproj`: xUnit test project.

`PicklinkBackend.sln` includes only the API project and the test project. Other
local folders are not part of the active solution unless they are explicitly
added to the solution.

## Important Folders

- `PicklinkBackend/Controllers`: API controllers.
- `PicklinkBackend/Data`: Entity Framework Core DbContext.
- `PicklinkBackend/DTOs`: API request/response models.
- `PicklinkBackend/Models`: EF Core entities.
- `PicklinkBackend/Services`: application services, background services, and
  notifier services.
- `PicklinkBackend/Startup`: startup helpers such as schema repair checks that
  run during application boot.
- `PicklinkBackend/Migrations`: EF Core migrations.
- `database/seeds`: SQL scripts for local/demo data.
- `PicklinkBackend/wwwroot/uploads`: runtime upload output. Files inside this
  folder are generated locally and are ignored by Git except `.gitkeep`
  placeholders.

## Test Organization

- `PicklinkBackend.Tests/ApiContracts`: tests that lock API contract assumptions.
- `PicklinkBackend.Tests/Policies`: authorization and business rule policy tests.
- `PicklinkBackend.Tests/Schema`: startup/schema repair contract tests.
- `PicklinkBackend.Tests/SeedData`: SQL seed data contract tests.
- `PicklinkBackend.Tests/Services`: service-level behavior tests.
- `PicklinkBackend.Tests/Startup`: startup configuration safety tests.

## Local Commands

```powershell
dotnet build PicklinkBackend.sln
dotnet test PicklinkBackend.sln
dotnet run --project PicklinkBackend\PicklinkBackend.csproj --launch-profile http
```

## Phase 2 Cleanup Candidates

- Split startup service registration and middleware setup out of `Program.cs`.
- Split `CommunityController.cs` by responsibility or move business logic into
  services while preserving routes.
- Consider DbContext mapping extraction only if it improves readability without
  fighting EF Core conventions.
