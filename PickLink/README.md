\# PickLink — Pickleball Community Connection Platform



SEP490-G5 | ASP.NET Core 10 + ReactJS + SQL Server



\## Prerequisites

\- .NET 10 SDK

\- Node.js 20+

\- SQL Server (local or Docker)

\- Visual Studio 2022 / VS Code



\## Getting Started (Backend)

1\. Clone the repo

2\. Copy connection string: update `appsettings.json` → `ConnectionStrings.DefaultConnection`

3\. Run migrations: `dotnet ef database update --project src/PickLink.Infrastructure --startup-project src/PickLink.API`

4\. Run: `dotnet run --project src/PickLink.API`

5\. Swagger UI: http://localhost:5000/swagger



\## Getting Started (Frontend)

1\. `cd frontend/picklink-web`

2\. `cp .env.example .env.local` then set `VITE\_API\_URL=http://localhost:5000`

3\. `npm install`

4\. `npm run dev` → http://localhost:5173



\## Branch Strategy

\- `main`        → stable, defense-ready only

\- `develop`     → primary integration branch

\- `feature/xxx` → individual features (branch from develop)

\- `bugfix/xxx`  → bug fixes



\## Team

SEP490-G5 SU26 | Supervisor: Chu Thị Minh Huệ

