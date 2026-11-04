# Multi-Engine Database Persistence & Storage Architecture

> **Decentralized, Multi-Engine Relational and Analytical Storage for Sovereign Nanotechnology Nodes**

NanoWeaverHrt is engineered for complete infrastructure sovereignty. Research labs, sovereign clinical collectives, and personal home laboratories run across vastly different environments—from single-board computers (Raspberry Pi, Odroid) and laptops without running database servers, up to clustered university servers with high-availability enterprise database instances.

To achieve this flexibility without compromising ACID durability, data safety, or SQL compliance, NanoWeaverHrt implements a decoupled multi-engine persistence seam:
- **`IWeaverDbConnectionFactory`**: Unified database connection provider supporting SQLite, PostgreSQL, DuckDB, and MySQL / MariaDB.
- **`DatabaseMigrationRunner`**: Transaction-safe schema migration engine with native SQL dialect translation for each target RDBMS.
- **Dialect Adapters**: `SqliteDialect`, `PostgreSqlDialect`, `DuckDbDialect`, and `MySqlDialect`.

---

## 1. Supported Database Engines Matrix

| Database Engine | Target Deployment | Dialect Adapter | Concurrency Model | Best For |
| :--- | :--- | :--- | :--- | :--- |
| **SQLite** | Local / Edge / Homelab | `SqliteDialect` | File-locked WAL | Zero-dependency default, embedded standalone nodes, single-user mobile/desktop apps |
| **PostgreSQL 14+** | Production Cluster / Cloud | `PostgreSqlDialect` | Multi-Version Concurrency (MVCC) | Enterprise clusters, centralized clinical registries, FDA 21 CFR Part 11 ledgers |
| **DuckDB** | In-Process Analytics & Edge | `DuckDbDialect` | Columnar Vectorized / Thread-safe | High-throughput trajectory analytics, zero-copy Parquet queries, embedded fast analytics |
| **MySQL / MariaDB** | Self-Hosted Linux Stacks | `MySqlDialect` | InnoDB Row-level Locking | Shared LAMP/LEMP institutional hosting, existing MariaDB infrastructures |

---

## 2. Architecture & Connection Factory

The core abstraction is `IWeaverDbConnectionFactory`, which manages connection pooling, dialect resolution, and connection lifecycle.

```
                      +-----------------------------------+
                      |   IWeaverDbConnectionFactory      |
                      +-----------------+-----------------+
                                        |
         +------------------+-----------+-----------+--------------------+
         |                  |                       |                    |
         v                  v                       v                    v
  +--------------+  +-----------------+     +---------------+    +-----------------+
  | SqliteConnection| NpgsqlConnection|     | DuckDBConnection | MySqlConnection |
  | (weaver.db)  |  | (PostgreSQL)    |     | (analytics.db)|    | (MariaDB/MySQL) |
  +--------------+  +-----------------+     +---------------+    +-----------------+
```

### Configuration Resolution
The connection factory inspects application settings (`appsettings.json`, environment variables, or command-line flags) using the following precedence:
1. `POSTGRES_CONNECTION_STRING` or `ConnectionStrings:PostgreSQL` → Resolves PostgreSQL.
2. `DUCKDB_CONNECTION_STRING` or `ConnectionStrings:DuckDB` → Resolves DuckDB.
3. `MYSQL_CONNECTION_STRING` or `ConnectionStrings:MySQL` → Resolves MySQL / MariaDB.
4. Default fallback: SQLite file in `Data Source=./weaver_data/weaver.db;Cache=Shared`.

### Example Configuration Snippets

#### SQLite (Default Zero-Configuration)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=./weaver_data/weaver.db;Cache=Shared"
  }
}
```

#### PostgreSQL
```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=db.lantea.internal;Port=5432;Database=nanoweaver;Username=weaver;Password=SovereignSecureSecret123!;SSL Mode=Prefer;"
  }
}
```

#### DuckDB
```json
{
  "ConnectionStrings": {
    "DuckDB": "Data Source=./weaver_data/weaver_analytics.duckdb"
  }
}
```

#### MySQL / MariaDB
```json
{
  "ConnectionStrings": {
    "MySQL": "Server=mariadb.local;Port=3306;Database=nanoweaver;Uid=weaver;Pwd=SovereignMariaPass!;CharSet=utf8mb4;"
  }
}
```

---

## 3. Deterministic Schema Migrations

NanoWeaverHrt prohibits ad-hoc DDL in repositories or application startup code. Every database modification is encapsulated in an `IDatabaseMigration` executed by `DatabaseMigrationRunner`.

### Migration Invariants
1. **Idempotency**: All DDL commands execute with `IF NOT EXISTS` or dialect-appropriate transactional guards.
2. **Dialect Translation**:
   - Primary Keys: `INTEGER PRIMARY KEY AUTOINCREMENT` (SQLite) vs `BIGSERIAL PRIMARY KEY` (PostgreSQL) vs `BIGINT PRIMARY KEY AUTO_INCREMENT` (MySQL) vs `BIGINT PRIMARY KEY` with sequences (DuckDB).
   - Timestamp Columns: `DATETIME` (SQLite, DuckDB) vs `TIMESTAMPTZ` (PostgreSQL) vs `DATETIME(6)` (MySQL).
   - Upsert Syntax: `INSERT OR REPLACE INTO` (SQLite, DuckDB) vs `ON CONFLICT (...) DO UPDATE` (PostgreSQL) vs `ON DUPLICATE KEY UPDATE` (MySQL).
3. **Audit Trail**: Successful migrations are recorded in the `weaver_schema_migrations` ledger table with timestamp and execution checksum.

---

## 5. Migration012_GuiPreferences & DuckDB Sandboxing
- **`Migration012_GuiPreferences`**: Establishes the `weaver_gui_preferences` table across all 4 database dialects, storing user UI configuration, localization choices, and theme states with transactional safety.
- **DuckDB Sandboxing**: Enforces `enable_external_access = false` on DuckDB connections to prevent unauthorized local file system traversal or remote network calls during analytical query execution.
