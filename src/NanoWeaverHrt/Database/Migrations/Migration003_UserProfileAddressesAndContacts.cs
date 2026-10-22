namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 003: Establishes schema tables and indexes for user profiles, patient address lookup,
/// and trusted emergency / caregiver contact lookup.
/// </summary>
public sealed class Migration003_UserProfileAddressesAndContacts : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 3;

    /// <inheritdoc/>
    public override string Name => "003_UserProfileAddressesAndContacts";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits DDL for user profile metadata, patient address lookup, and patient contact lookup.
        // WHY:  Supports patient demographic, clinical, and emergency proxy record retention under HIPAA/GDPR hardening.
        // HOW:  Tailors data types for PostgreSQL vs SQLite with idempotent CREATE TABLE and CREATE INDEX statements.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
                CREATE TABLE IF NOT EXISTS weaver_user_profiles (
                    username VARCHAR(128) PRIMARY KEY REFERENCES weaver_users(username) ON DELETE CASCADE,
                    first_name VARCHAR(100) NOT NULL DEFAULT '',
                    last_name VARCHAR(100) NOT NULL DEFAULT '',
                    preferred_name VARCHAR(100),
                    email_address VARCHAR(256),
                    phone_number VARCHAR(50),
                    date_of_birth VARCHAR(32),
                    picture TEXT,
                    regimen VARCHAR(64),
                    gender_assigned_at_birth VARCHAR(32) NOT NULL DEFAULT 'Female',
                    pronouns_json TEXT NOT NULL DEFAULT '[]',
                    emergency_medical_notes TEXT,
                    bio TEXT,
                    language_preference VARCHAR(32) NOT NULL DEFAULT 'en-US',
                    created_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                    updated_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
                );

                CREATE TABLE IF NOT EXISTS weaver_patient_addresses (
                    id VARCHAR(64) PRIMARY KEY,
                    username VARCHAR(128) NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
                    address_type VARCHAR(32) NOT NULL DEFAULT 'Primary',
                    address_line1 VARCHAR(256) NOT NULL,
                    address_line2 VARCHAR(256),
                    city VARCHAR(128) NOT NULL,
                    state_or_province VARCHAR(128) NOT NULL,
                    postal_code VARCHAR(32) NOT NULL,
                    country VARCHAR(64) NOT NULL DEFAULT 'United States',
                    is_primary BOOLEAN NOT NULL DEFAULT TRUE,
                    created_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                    updated_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
                );

                CREATE TABLE IF NOT EXISTS weaver_patient_contacts (
                    id VARCHAR(64) PRIMARY KEY,
                    username VARCHAR(128) NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
                    contact_name VARCHAR(128) NOT NULL,
                    relationship VARCHAR(64) NOT NULL,
                    phone_number VARCHAR(50) NOT NULL,
                    email_address VARCHAR(256),
                    priority_order INTEGER NOT NULL DEFAULT 1,
                    can_discuss_medical_info BOOLEAN NOT NULL DEFAULT FALSE,
                    notes TEXT,
                    created_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                    updated_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
                );

                CREATE INDEX IF NOT EXISTS idx_weaver_addresses_user ON weaver_patient_addresses (username);
                CREATE INDEX IF NOT EXISTS idx_weaver_contacts_user ON weaver_patient_contacts (username, priority_order);
            ";
        }

        return @"
            CREATE TABLE IF NOT EXISTS weaver_user_profiles (
                username TEXT PRIMARY KEY REFERENCES weaver_users(username) ON DELETE CASCADE,
                first_name TEXT NOT NULL DEFAULT '',
                last_name TEXT NOT NULL DEFAULT '',
                preferred_name TEXT,
                email_address TEXT,
                phone_number TEXT,
                date_of_birth TEXT,
                picture TEXT,
                regimen TEXT,
                gender_assigned_at_birth TEXT NOT NULL DEFAULT 'Female',
                pronouns_json TEXT NOT NULL DEFAULT '[]',
                emergency_medical_notes TEXT,
                bio TEXT,
                language_preference TEXT NOT NULL DEFAULT 'en-US',
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS weaver_patient_addresses (
                id TEXT PRIMARY KEY,
                username TEXT NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
                address_type TEXT NOT NULL DEFAULT 'Primary',
                address_line1 TEXT NOT NULL,
                address_line2 TEXT,
                city TEXT NOT NULL,
                state_or_province TEXT NOT NULL,
                postal_code TEXT NOT NULL,
                country TEXT NOT NULL DEFAULT 'United States',
                is_primary INTEGER NOT NULL DEFAULT 1,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS weaver_patient_contacts (
                id TEXT PRIMARY KEY,
                username TEXT NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
                contact_name TEXT NOT NULL,
                relationship TEXT NOT NULL,
                phone_number TEXT NOT NULL,
                email_address TEXT,
                priority_order INTEGER NOT NULL DEFAULT 1,
                can_discuss_medical_info INTEGER NOT NULL DEFAULT 0,
                notes TEXT,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_weaver_addresses_user ON weaver_patient_addresses (username);
            CREATE INDEX IF NOT EXISTS idx_weaver_contacts_user ON weaver_patient_contacts (username, priority_order);
        ";
    }
}
