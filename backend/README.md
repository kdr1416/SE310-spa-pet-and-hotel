# PetSpa Backend — M3 schema foundation

- Core: 15 enums, 33 entities; no EF dependency.
- Api/Data: one configuration per entity, PetSpaDbContext and a connectionless design-time factory.
- Migrations: InitialCoreSchema and snapshot; no database has been updated by this task.

## Local verification

Run from repository root:

```powershell
dotnet tool restore
dotnet build backend/src/PetSpa.Core -m:1
dotnet build backend/PetSpa.sln -m:1
dotnet test backend/PetSpa.sln -m:1
```

The local dotnet-ef tool is pinned to 10.0.14. EF Relational is explicitly pinned to match EF Design so tests do not resolve an older runtime assembly through Npgsql.

## Configuration and offline migrations

Runtime uses the existing ConnectionStrings:DefaultConnection key (environment variable ConnectionStrings__DefaultConnection), provided outside source control; runtime Neon uses pooler port 6543. The existing MigrationConnection key is reserved for an explicitly authorized future database operation via direct port 5432.

PetSpaDbContextFactory builds UseNpgsql() options without reading configuration or supplying a connection string. Creating migrations and generating migration SQL do not open a database. The command used to create the initial migration was:

```powershell
dotnet ef migrations add InitialCoreSchema --project backend/src/PetSpa.Api --startup-project backend/src/PetSpa.Api --no-build
```

Do not rerun that name after the migration exists. This task did not run database update, EnsureCreated or Migrate.

DatabaseModelTests inspect the real Npgsql model, DI wiring, restrictive FKs, keys/indexes/types and the generated migration script, including snapshot consistency. The SQL script has 33 application tables plus EF's own __EFMigrationsHistory; that history table is not a 34th domain entity.

## Boundaries

FKs use Restrict and simple row invariants use CHECK constraints. They do not implement authorization, cross-row ownership, price/state transitions, paid-invoice immutability, normalization, update timestamp maintenance or SELECT FOR UPDATE capacity protocols. Those require the later service layer and PostgreSQL integration tests. JSONB is persisted as a string; services must validate its schema before saving.

The latest M3 request makes ChiTietCombo a composite-key entity. ChiTietDon preserves declared weight and READY timestamp; see system-design section 4.6. No endpoint, authentication flow, seed credentials, remote database operation or frontend change is included.
