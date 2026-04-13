# Local Developer Setup

## Secrets / local credentials

This repo does **not** commit any real credentials — not even local-dev ones.
Every developer creates their own passwords for local SQL Server + Neo4j
containers.

### First-time setup

1. Pick local passwords for SQL Server `sa` and Neo4j. Anything you like; they
   only bind to `localhost`. Example:
   - `MSSQL_SA_PASSWORD=YourLocal_SA_Pwd#1`
   - `NEO4J_PASSWORD=YourLocal_Neo4j_Pwd#1`

2. Create `docker/.env` (gitignored) with:

   ```dotenv
   MSSQL_SA_PASSWORD=YourLocal_SA_Pwd#1
   NEO4J_PASSWORD=YourLocal_Neo4j_Pwd#1
   ```

   `docker compose -f docker/docker-compose.yml up` picks these up automatically.

3. For running the GameServer **outside** Docker (e.g. in an IDE), copy the
   template and fill in the same values:

   ```bash
   cp src/FirstMud.GameServer/appsettings.Development.template.json \
      src/FirstMud.GameServer/appsettings.Development.json
   ```

   Edit the two `REPLACE_ME_*` placeholders.

   **Preferred**: use `dotnet user-secrets` instead of the file, so the creds
   never touch a disk path git might see:

   ```bash
   cd src/FirstMud.GameServer
   dotnet user-secrets init
   dotnet user-secrets set "ConnectionStrings:GameDb" \
     "Server=localhost,1433;Database=FirstMud;User Id=sa;Password=YourLocal_SA_Pwd#1;TrustServerCertificate=True;"
   dotnet user-secrets set "Neo4j:Password" "YourLocal_Neo4j_Pwd#1"
   ```

4. For EF Core design-time tooling (migrations), the connection string comes
   from the env var `FIRSTMUD_DESIGNTIME_CONNECTION` — export it before
   running `dotnet ef …`:

   ```bash
   export FIRSTMUD_DESIGNTIME_CONNECTION="Server=localhost,1433;Database=FirstMud;User Id=sa;Password=YourLocal_SA_Pwd#1;TrustServerCertificate=True;"
   ```

## Build + run

```bash
dotnet build FirstMud.slnx
dotnet test FirstMud.slnx
docker compose -f docker/docker-compose.yml up --build
```
