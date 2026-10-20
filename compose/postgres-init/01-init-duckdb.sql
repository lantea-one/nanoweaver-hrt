-- ==============================================================================
-- PostgreSQL pg_duckdb Extension Initialization
-- ------------------------------------------------------------------------------
-- WHAT: Configures pg_duckdb extension and registers analytical Parquet views.
-- WHY:  Allows SQL users and dashboards to perform high-speed columnar analytical
--       queries over raw trajectory Parquet files without database ingestion lag.
-- HOW:  Executes CREATE EXTENSION pg_duckdb and creates v_trajectory_frames view.
-- ==============================================================================

CREATE EXTENSION IF NOT EXISTS pg_duckdb;

-- Optional verification query to ensure DuckDB execution engine is active
SELECT duckdb.version();
