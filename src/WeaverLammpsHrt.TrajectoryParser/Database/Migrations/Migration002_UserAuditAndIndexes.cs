namespace WeaverLammpsHrt.TrajectoryParser.Database.Migrations;

/// <summary>
/// Migration 002: Adds performance indexes on user accounts and configuration timestamps.
/// </summary>
public sealed class Migration002_UserAuditAndIndexes : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 2;

    /// <inheritdoc/>
    public override string Name => "002_UserAuditAndIndexes";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits secondary index creation statements for user accounts and configuration tables.
        // WHY:  Accelerates role filtering (HasAnyAdministrator), access gating checks, and chronological telemetry audit queries.
        // HOW:  Returns idempotent CREATE INDEX IF NOT EXISTS statements compatible with both engines.
        return @"
            CREATE INDEX IF NOT EXISTS idx_weaver_users_role_active ON weaver_users (role, is_active);
            CREATE INDEX IF NOT EXISTS idx_weaver_users_created ON weaver_users (created_at_utc);
            CREATE INDEX IF NOT EXISTS idx_weaver_config_updated ON weaver_configuration (updated_at_utc);
        ";
    }
}
