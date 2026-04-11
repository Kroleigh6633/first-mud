# FirstMud.E2E — Playwright browser smoke tests

End-to-end tests that drive a real Chromium browser against the full
docker stack (SQL Server + Neo4j + GameServer + Vite client).

## Requirements

- Docker Desktop running
- The `sqlserver`, `neo4j`, `gameserver`, and `client` compose services up

## Run

From the repo root:

```bash
docker compose -f docker/docker-compose.yml run --rm e2e
```

This spins up the official Microsoft Playwright image, mounts this folder,
installs deps (first run only, via volume-cached node_modules), and runs
the suite against `http://client:5173` on the internal compose network.

Override the target URL with `BASE_URL=http://host.docker.internal:5173`
to run from outside the compose network.

## What's covered

- SignalR negotiation + auth + WorldState push
- No "Failed to connect" error surfaces for React StrictMode dev aborts
- Status panel shows the dev player after auth
- Q key opens quest log with real seeded Neo4j quests
- Quest log renders exactly one canvas (regression guard for StrictMode
  double-mount of rot.js WorldMap)
- Accepting a quest adds a string message (not `[object Object]`) to
  the message log
