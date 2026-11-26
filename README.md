# Pointer

Multi-tenant appointment booking for small local shops (barbershops and salons first) in Belgium and the Netherlands.

Each shop gets a hosted booking page and an embeddable button for its own website. Customers pick an open slot and pay a deposit; the shop decides what happens to the deposit on a no-show.

"Pointer" is a working name. See [CLAUDE.md](CLAUDE.md) for the spec, hard rules, architecture and MVP scope.

## Layout

```
src/Pointer.Core   Domain model, EF Core DbContext + migrations, booking logic (no web dependencies)
src/Pointer.Web    ASP.NET Core host: Razor Pages, endpoints, configuration
tests/Pointer.Tests  xUnit tests, run against a real PostgreSQL database
```

## Prerequisites

- .NET 8 SDK
- PostgreSQL 16 (or Docker: `cp .env.example .env`, set a password, then `docker compose up -d`)

## Run locally

Configuration comes from environment variables. Nothing secret is committed.

```
export ConnectionStrings__Default="Host=localhost;Database=pointer_dev;Username=postgres;Password=<your local password>"
dotnet run --project src/Pointer.Web
```

In Development the app applies migrations on startup. Open http://localhost:5137 and http://localhost:5137/healthz.

## Tests

The tests create a throwaway database per run, apply the real migrations, and drop it afterwards.

```
export POINTER_TEST_DB="Host=localhost;Username=postgres;Password=<your local password>"
dotnet test
```

## Migrations

```
dotnet tool restore
dotnet ef migrations add <Name> --project src/Pointer.Core --startup-project src/Pointer.Web --output-dir Data/Migrations
```

## Container

```
docker build -t pointer .
docker run -p 8080:8080 -e ConnectionStrings__Default="..." -e Database__MigrateOnStartup=true pointer
```

CI (`.github/workflows/ci.yml`) builds and tests every push and pull request against PostgreSQL, builds the container image, and publishes it to `ghcr.io/<owner>/pointer` on pushes to `main`.

## Configuration

| Key | Default | Meaning |
| --- | --- | --- |
| `ConnectionStrings__Default` | none (required) | PostgreSQL connection string |
| `Database__MigrateOnStartup` | `false` (`true` in Development) | Apply EF Core migrations when the app starts |
| `Bookings__PaymentHoldMinutes` | `15` | How long a slot is held while the customer pays the deposit |
