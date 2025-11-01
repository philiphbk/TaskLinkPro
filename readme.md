# TaskLink Pro — Starter Backend (Phase 1)
Generated: {datetime.datetime.utcnow().isoformat()}Z

## Prereqs
- .NET 8 SDK
- SQL Server (local or container)

## Quick Start
```bash
git clone <your-repo-url>
cd TaskLinkPro/src/TaskLinkPro.Api

# Add EF Core tools if needed
dotnet tool install --global dotnet-ef

# Create DB (adjust connection string in appsettings.json)
cd ../TaskLinkPro.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
cd ../TaskLinkPro.Api

# Create initial migration
dotnet ef migrations add Initial --startup-project ../TaskLinkPro.Api --project ../TaskLinkPro.Infrastructure

# Apply migration
dotnet ef database update --startup-project ../TaskLinkPro.Api --project ../TaskLinkPro.Infrastructure

# Run API
cd ../TaskLinkPro.Api
dotnet run