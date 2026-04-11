# FirstMud Integration Tests

Real end-to-end tests that spin up SQL Server 2022 and Neo4j 5 containers via Testcontainers.

## Requirements

- **Docker Desktop** (Windows) must be running before executing these tests.
  Tests fail loudly if Docker is unavailable — they do not silently skip.
- .NET 9 SDK

## Running

```bash
# Run integration tests only
dotnet test tests/FirstMud.IntegrationTests

# Run with category filter (equivalent)
dotnet test tests/FirstMud.IntegrationTests --filter "Category=Integration"

# Run everything (unit + integration)
dotnet test FirstMud.slnx
```

## First run

The first run pulls container images (~400 MB for SQL Server, ~500 MB for Neo4j).
Subsequent runs reuse cached images and are faster (~20–40 s total).

## What is tested

| File | Container(s) | Tests |
|------|-------------|-------|
| `Repositories/PlayerRepositoryTests.cs` | SQL Server | 4 |
| `Repositories/ZoneRepositoryTests.cs`   | SQL Server | 2 |
| `Repositories/RecipeRepositoryTests.cs` | SQL Server | 2 |
| `Neo4j/QuestGraphRepositoryTests.cs`    | Neo4j      | 5 |
| `EndToEnd/SeedingTests.cs`              | Both       | 3 |

Containers are shared within each xUnit collection (not per-test) to keep startup cost low.
