using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Authoritative persistence repository for patient and user demographic profiles,
/// structured addresses, and trusted emergency/caregiver contact lookup tables,
/// supporting both PostgreSQL (pg-duckdb) and embedded SQLite fallback.
/// </summary>
public sealed class UserProfileRepository : IUserProfileRepository, IDisposable
{
    /// <summary>
    /// Thread synchronization lock for SQLite write operations.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Local fallback SQLite database path.
    /// </summary>
    private readonly string _sqlitePath;

    /// <summary>
    /// PostgreSQL connection string.
    /// </summary>
    private readonly string _postgresConnString;

    /// <summary>
    /// Configuration repository used to discover active backend name.
    /// </summary>
    private readonly IConfigurationRepository _configRepo;

    /// <summary>
    /// Profile cryptographic data protector for sensitive fields and avatars.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// Migration runner ensuring database schema versioning.
    /// </summary>
    private readonly DatabaseMigrationRunner _migrationRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserProfileRepository"/> class.
    /// </summary>
    /// <param name="configuration">Host application configuration.</param>
    /// <param name="configRepo">Authoritative configuration repository.</param>
    /// <param name="protector">Cryptographic profile data protector.</param>
    /// <param name="migrationRunner">Optional explicit database migration runner.</param>
    public UserProfileRepository(
        IConfiguration configuration,
        IConfigurationRepository configRepo,
        ProfileDataProtector protector,
        DatabaseMigrationRunner? migrationRunner = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configRepo = configRepo ?? throw new ArgumentNullException(nameof(configRepo));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _migrationRunner = migrationRunner ?? new DatabaseMigrationRunner();

        string? envPostgres = configuration["POSTGRES_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        // WHAT: Uses the configured PostgreSQL connection string, or no connection string at all.
        // WHY:  Shipping a built-in default that embeds a password means every deployment which
        //       forgets to set POSTGRES_CONNECTION_STRING shares one publicly known credential,
        //       and that credential also leaks through any surface that echoes configuration.
        //       PostgreSQL is therefore opt-in: with nothing configured the stack uses its
        //       embedded SQLite database, exactly as it already does when PostgreSQL is
        //       unreachable. An empty string fails the connection attempt immediately and that
        //       existing fallback path takes over.
        // HOW:  Falls back to the empty string instead of a hardcoded credential.
        _postgresConnString = envPostgres ?? string.Empty;

        string incomingDir = configuration["TRAJECTORY_INCOMING_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
        Directory.CreateDirectory(incomingDir);
        _sqlitePath = Path.Combine(incomingDir, "weaver_config.db");
    }

    /// <summary>
    /// Creates an open database connection appropriate for the active configuration backend.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple containing the open connection and dialect string ("postgresql" or "sqlite").</returns>
    private async Task<(DbConnection Conn, string Dialect)> OpenConnectionAsync(CancellationToken ct)
    {
        // WHAT: Discovers the active storage engine from IConfigurationRepository and opens a database connection.
        // WHY:  Allows transparent execution across PostgreSQL clusters and standalone SQLite nodes.
        // HOW:  Attempts PostgreSQL if active backend indicates pg-duckdb; falls back to local SQLite on failure.
        bool isPostgres = _configRepo.ActiveBackendName.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase);
        if (isPostgres)
        {
            try
            {
                NpgsqlConnection pgConn = new(_postgresConnString);
                await pgConn.OpenAsync(ct);
                await _migrationRunner.ApplyPendingMigrationsAsync(pgConn, "postgresql", ct);
                return (pgConn, "postgresql");
            }
            catch
            {
                // Fall back to SQLite if PostgreSQL fails
            }
        }

        SqliteConnection sqliteConn = new($"Data Source={_sqlitePath}");
        await sqliteConn.OpenAsync(ct);
        await _migrationRunner.ApplyPendingMigrationsAsync(sqliteConn, "sqlite", ct);
        return (sqliteConn, "sqlite");
    }

    /// <inheritdoc/>
    public async Task<UserProfile?> GetProfileAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Queries weaver_user_profiles table by username and decrypts sensitive picture payload.
        // WHY:  Supplies user demographic, pronoun, and clinical data to UI and API callers.
        // HOW:  Executes SELECT parameterized query, maps fields, and decrypts picture data-URI.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT username, first_name, last_name, preferred_name, email_address,
                           phone_number, date_of_birth, picture, regimen, gender_assigned_at_birth,
                           pronouns_json, emergency_medical_notes, bio, language_preference,
                           created_at_utc, updated_at_utc
                    FROM weaver_user_profiles
                    WHERE username = @u;";

                DbParameter param = cmd.CreateParameter();
                param.ParameterName = "@u";
                param.Value = username;
                cmd.Parameters.Add(param);

                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    return MapProfileFromReader(reader, _protector);
                }

                return null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UpsertProfileAsync(UserProfile profile, CancellationToken ct = default)
    {
        // WHAT: Inserts or updates a user profile record in weaver_user_profiles with encrypted picture.
        // WHY:  Persists changes to demographic, regimen, gender, and contact details with audit timestamps.
        // HOW:  Encrypts picture with AES-256-GCM and executes upsert SQL statement.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, dialect) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                string? encryptedPicture = !string.IsNullOrEmpty(profile.Picture) ? _protector.EncryptDataUri(profile.Picture) : null;
                string pronounsJson = JsonSerializer.Serialize(profile.Pronouns ?? []);

                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO weaver_user_profiles (
                        username, first_name, last_name, preferred_name, email_address,
                        phone_number, date_of_birth, picture, regimen, gender_assigned_at_birth,
                        pronouns_json, emergency_medical_notes, bio, language_preference,
                        created_at_utc, updated_at_utc
                    ) VALUES (
                        @u, @fn, @ln, @pn, @em,
                        @ph, @dob, @pic, @reg, @gen,
                        @pro, @emn, @bio, @lang,
                        @cat, @uat
                    )
                    ON CONFLICT(username) DO UPDATE SET
                        first_name = @fn,
                        last_name = @ln,
                        preferred_name = @pn,
                        email_address = @em,
                        phone_number = @ph,
                        date_of_birth = @dob,
                        picture = @pic,
                        regimen = @reg,
                        gender_assigned_at_birth = @gen,
                        pronouns_json = @pro,
                        emergency_medical_notes = @emn,
                        bio = @bio,
                        language_preference = @lang,
                        updated_at_utc = @uat;";

                AddParam(cmd, "@u", profile.Username);
                AddParam(cmd, "@fn", profile.FirstName ?? string.Empty);
                AddParam(cmd, "@ln", profile.LastName ?? string.Empty);
                AddParam(cmd, "@pn", (object?)profile.PreferredName ?? DBNull.Value);
                AddParam(cmd, "@em", (object?)profile.EmailAddress ?? DBNull.Value);
                AddParam(cmd, "@ph", (object?)profile.PhoneNumber ?? DBNull.Value);
                AddParam(cmd, "@dob", (object?)profile.DateOfBirth ?? DBNull.Value);
                AddParam(cmd, "@pic", (object?)encryptedPicture ?? DBNull.Value);
                AddParam(cmd, "@reg", profile.Regimen.HasValue ? profile.Regimen.Value.ToString() : DBNull.Value);
                AddParam(cmd, "@gen", profile.GenderAssignedAtBirth.ToString());
                AddParam(cmd, "@pro", pronounsJson);
                AddParam(cmd, "@emn", (object?)profile.EmergencyMedicalNotes ?? DBNull.Value);
                AddParam(cmd, "@bio", (object?)profile.Bio ?? DBNull.Value);
                AddParam(cmd, "@lang", string.IsNullOrWhiteSpace(profile.LanguagePreference) ? "en-US" : profile.LanguagePreference);

                if (dialect == "postgresql")
                {
                    AddParam(cmd, "@cat", profile.CreatedAtUtc.UtcDateTime);
                    AddParam(cmd, "@uat", DateTime.UtcNow);
                }
                else
                {
                    AddParam(cmd, "@cat", profile.CreatedAtUtc.ToString("o"));
                    AddParam(cmd, "@uat", DateTimeOffset.UtcNow.ToString("o"));
                }

                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientAddress>> GetAddressesAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Retrieves all address records for a specified patient or user.
        // WHY:  Exposes patient address lookup table for clinic logistics, lab testing, and medication delivery.
        // HOW:  Selects from weaver_patient_addresses ordered by primary status and creation timestamp.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, username, address_type, address_line1, address_line2,
                           city, state_or_province, postal_code, country, is_primary,
                           created_at_utc, updated_at_utc
                    FROM weaver_patient_addresses
                    WHERE username = @u
                    ORDER BY is_primary DESC, created_at_utc ASC;";

                AddParam(cmd, "@u", username);

                List<PatientAddress> list = new();
                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    list.Add(MapAddressFromReader(reader));
                }

                return list;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UpsertAddressAsync(PatientAddress address, CancellationToken ct = default)
    {
        // WHAT: Inserts or updates an address entry in weaver_patient_addresses.
        // WHY:  Ensures patient address data is saved deterministically.
        // HOW:  Executes parameterized SQL with ON CONFLICT(id) DO UPDATE.
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(address.Username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, dialect) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO weaver_patient_addresses (
                        id, username, address_type, address_line1, address_line2,
                        city, state_or_province, postal_code, country, is_primary,
                        created_at_utc, updated_at_utc
                    ) VALUES (
                        @id, @u, @at, @l1, @l2,
                        @ci, @sp, @pc, @co, @ip,
                        @cat, @uat
                    )
                    ON CONFLICT(id) DO UPDATE SET
                        address_type = @at,
                        address_line1 = @l1,
                        address_line2 = @l2,
                        city = @ci,
                        state_or_province = @sp,
                        postal_code = @pc,
                        country = @co,
                        is_primary = @ip,
                        updated_at_utc = @uat;";

                AddParam(cmd, "@id", address.Id);
                AddParam(cmd, "@u", address.Username);
                AddParam(cmd, "@at", address.AddressType);
                AddParam(cmd, "@l1", address.AddressLine1);
                AddParam(cmd, "@l2", (object?)address.AddressLine2 ?? DBNull.Value);
                AddParam(cmd, "@ci", address.City);
                AddParam(cmd, "@sp", address.StateOrProvince);
                AddParam(cmd, "@pc", address.PostalCode);
                AddParam(cmd, "@co", address.Country);

                if (dialect == "postgresql")
                {
                    AddParam(cmd, "@ip", address.IsPrimary);
                    AddParam(cmd, "@cat", address.CreatedAtUtc.UtcDateTime);
                    AddParam(cmd, "@uat", DateTime.UtcNow);
                }
                else
                {
                    AddParam(cmd, "@ip", address.IsPrimary ? 1 : 0);
                    AddParam(cmd, "@cat", address.CreatedAtUtc.ToString("o"));
                    AddParam(cmd, "@uat", DateTimeOffset.UtcNow.ToString("o"));
                }

                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAddressAsync(string id, string username, CancellationToken ct = default)
    {
        // WHAT: Deletes a specific address record by ID and username.
        // WHY:  Allows users and administrators to remove outdated address entries.
        // HOW:  Executes parameterized DELETE matching both primary key and owner username.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM weaver_patient_addresses WHERE id = @id AND username = @u;";
                AddParam(cmd, "@id", id);
                AddParam(cmd, "@u", username);

                int affected = await cmd.ExecuteNonQueryAsync(ct);
                return affected > 0;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientContact>> GetContactsAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Retrieves all emergency and caregiver contacts for a specified patient.
        // WHY:  Provides healthcare providers with designated people to reach out to if a patient cannot be reached.
        // HOW:  Selects from weaver_patient_contacts ordered by priority sequence.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, username, contact_name, relationship, phone_number,
                           email_address, priority_order, can_discuss_medical_info, notes,
                           created_at_utc, updated_at_utc
                    FROM weaver_patient_contacts
                    WHERE username = @u
                    ORDER BY priority_order ASC, created_at_utc ASC;";

                AddParam(cmd, "@u", username);

                List<PatientContact> list = new();
                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    list.Add(MapContactFromReader(reader));
                }

                return list;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UpsertContactAsync(PatientContact contact, CancellationToken ct = default)
    {
        // WHAT: Inserts or updates a trusted emergency contact in weaver_patient_contacts.
        // WHY:  Ensures outreach delegates and medical disclosure permissions are maintained accurately.
        // HOW:  Executes parameterized SQL with ON CONFLICT(id) DO UPDATE.
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentException.ThrowIfNullOrWhiteSpace(contact.Username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, dialect) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO weaver_patient_contacts (
                        id, username, contact_name, relationship, phone_number,
                        email_address, priority_order, can_discuss_medical_info, notes,
                        created_at_utc, updated_at_utc
                    ) VALUES (
                        @id, @u, @cn, @rel, @ph,
                        @em, @po, @cdm, @nt,
                        @cat, @uat
                    )
                    ON CONFLICT(id) DO UPDATE SET
                        contact_name = @cn,
                        relationship = @rel,
                        phone_number = @ph,
                        email_address = @em,
                        priority_order = @po,
                        can_discuss_medical_info = @cdm,
                        notes = @nt,
                        updated_at_utc = @uat;";

                AddParam(cmd, "@id", contact.Id);
                AddParam(cmd, "@u", contact.Username);
                AddParam(cmd, "@cn", contact.ContactName);
                AddParam(cmd, "@rel", contact.Relationship);
                AddParam(cmd, "@ph", contact.PhoneNumber);
                AddParam(cmd, "@em", (object?)contact.EmailAddress ?? DBNull.Value);
                AddParam(cmd, "@po", contact.PriorityOrder);
                AddParam(cmd, "@nt", (object?)contact.Notes ?? DBNull.Value);

                if (dialect == "postgresql")
                {
                    AddParam(cmd, "@cdm", contact.CanDiscussMedicalInfo);
                    AddParam(cmd, "@cat", contact.CreatedAtUtc.UtcDateTime);
                    AddParam(cmd, "@uat", DateTime.UtcNow);
                }
                else
                {
                    AddParam(cmd, "@cdm", contact.CanDiscussMedicalInfo ? 1 : 0);
                    AddParam(cmd, "@cat", contact.CreatedAtUtc.ToString("o"));
                    AddParam(cmd, "@uat", DateTimeOffset.UtcNow.ToString("o"));
                }

                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteContactAsync(string id, string username, CancellationToken ct = default)
    {
        // WHAT: Deletes a contact record by ID and username.
        // WHY:  Allows patients and administrators to remove outdated contact proxies.
        // HOW:  Executes parameterized DELETE matching both contact ID and patient username.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM weaver_patient_contacts WHERE id = @id AND username = @u;";
                AddParam(cmd, "@id", id);
                AddParam(cmd, "@u", username);

                int affected = await cmd.ExecuteNonQueryAsync(ct);
                return affected > 0;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Helper creating and attaching a command parameter.
    /// </summary>
    /// <param name="cmd">The command the parameter is attached to.</param>
    /// <param name="name">The placeholder name used in the SQL text, including its leading marker.</param>
    /// <param name="value">The value bound to that placeholder.</param>
    private static void AddParam(DbCommand cmd, string name, object value)
    {
        DbParameter p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    /// <summary>
    /// Maps a database reader row to a strongly-typed <see cref="UserProfile"/>.
    /// </summary>
    /// <param name="reader">A reader positioned on the profile row to read.</param>
    /// <param name="protector">The service that decrypts the personal fields held encrypted at rest.</param>
    /// <returns>The fully populated patient profile for the current row.</returns>
    internal static UserProfile MapProfileFromReader(DbDataReader reader, ProfileDataProtector protector)
    {
        string username = reader.GetString(0);
        string firstName = reader.GetString(1);
        string lastName = reader.GetString(2);
        string? preferredName = reader.IsDBNull(3) ? null : reader.GetString(3);
        string? email = reader.IsDBNull(4) ? null : reader.GetString(4);
        string? phone = reader.IsDBNull(5) ? null : reader.GetString(5);
        string? dob = reader.IsDBNull(6) ? null : reader.GetString(6);
        string? encryptedPic = reader.IsDBNull(7) ? null : reader.GetString(7);
        string? regimenStr = reader.IsDBNull(8) ? null : reader.GetString(8);
        string genderStr = reader.GetString(9);
        string pronounsJson = reader.GetString(10);
        string? emergencyNotes = reader.IsDBNull(11) ? null : reader.GetString(11);
        string? bio = reader.IsDBNull(12) ? null : reader.GetString(12);
        string languagePref = reader.GetString(13);

        DateTimeOffset created = ParseDateTimeOffset(reader, 14);
        DateTimeOffset updated = ParseDateTimeOffset(reader, 15);

        TherapeuticRegimen? regimen = null;
        if (!string.IsNullOrEmpty(regimenStr) && Enum.TryParse(regimenStr, ignoreCase: true, out TherapeuticRegimen parsedRegimen))
        {
            regimen = parsedRegimen;
        }

        Enum.TryParse(genderStr, ignoreCase: true, out GenderAssignedAtBirth gender);

        string[] pronouns = [];
        try
        {
            pronouns = JsonSerializer.Deserialize<string[]>(pronounsJson) ?? [];
        }
        catch
        {
            // Fallback for corrupted json
        }

        string? decryptedPic = protector.DecryptDataUri(encryptedPic);

        return new UserProfile
        {
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            PreferredName = preferredName,
            EmailAddress = email,
            PhoneNumber = phone,
            DateOfBirth = dob,
            Picture = decryptedPic,
            Regimen = regimen,
            GenderAssignedAtBirth = gender,
            Pronouns = pronouns,
            EmergencyMedicalNotes = emergencyNotes,
            Bio = bio,
            LanguagePreference = languagePref,
            CreatedAtUtc = created,
            UpdatedAtUtc = updated
        };
    }

    /// <summary>
    /// Maps a database reader row to a strongly-typed <see cref="PatientAddress"/>.
    /// </summary>
    /// <param name="reader">A reader positioned on the address row to read.</param>
    /// <returns>The postal address described by the current row.</returns>
    internal static PatientAddress MapAddressFromReader(DbDataReader reader)
    {
        string id = reader.GetString(0);
        string username = reader.GetString(1);
        string addressType = reader.GetString(2);
        string line1 = reader.GetString(3);
        string? line2 = reader.IsDBNull(4) ? null : reader.GetString(4);
        string city = reader.GetString(5);
        string state = reader.GetString(6);
        string postalCode = reader.GetString(7);
        string country = reader.GetString(8);
        bool isPrimary = reader.GetFieldType(9) == typeof(bool) ? reader.GetBoolean(9) : reader.GetInt64(9) != 0;
        DateTimeOffset created = ParseDateTimeOffset(reader, 10);
        DateTimeOffset updated = ParseDateTimeOffset(reader, 11);

        return new PatientAddress
        {
            Id = id,
            Username = username,
            AddressType = addressType,
            AddressLine1 = line1,
            AddressLine2 = line2,
            City = city,
            StateOrProvince = state,
            PostalCode = postalCode,
            Country = country,
            IsPrimary = isPrimary,
            CreatedAtUtc = created,
            UpdatedAtUtc = updated
        };
    }

    /// <summary>
    /// Maps a database reader row to a strongly-typed <see cref="PatientContact"/>.
    /// </summary>
    /// <param name="reader">A reader positioned on the contact row to read.</param>
    /// <returns>The emergency or next-of-kin contact described by the current row.</returns>
    internal static PatientContact MapContactFromReader(DbDataReader reader)
    {
        string id = reader.GetString(0);
        string username = reader.GetString(1);
        string contactName = reader.GetString(2);
        string relationship = reader.GetString(3);
        string phone = reader.GetString(4);
        string? email = reader.IsDBNull(5) ? null : reader.GetString(5);
        int priority = reader.GetInt32(6);
        bool canDiscuss = reader.GetFieldType(7) == typeof(bool) ? reader.GetBoolean(7) : reader.GetInt64(7) != 0;
        string? notes = reader.IsDBNull(8) ? null : reader.GetString(8);
        DateTimeOffset created = ParseDateTimeOffset(reader, 9);
        DateTimeOffset updated = ParseDateTimeOffset(reader, 10);

        return new PatientContact
        {
            Id = id,
            Username = username,
            ContactName = contactName,
            Relationship = relationship,
            PhoneNumber = phone,
            EmailAddress = email,
            PriorityOrder = priority,
            CanDiscussMedicalInfo = canDiscuss,
            Notes = notes,
            CreatedAtUtc = created,
            UpdatedAtUtc = updated
        };
    }

    /// <summary>
    /// Parses a DateTimeOffset safely from varying database engine column types.
    /// </summary>
    /// <param name="reader">A reader positioned on the row to read.</param>
    /// <param name="ordinal">The zero-based index of the column holding the timestamp.</param>
    /// <returns>The column value as a timestamp anchored to UTC.</returns>
    private static DateTimeOffset ParseDateTimeOffset(DbDataReader reader, int ordinal)
    {
        if (reader.GetFieldType(ordinal) == typeof(DateTime))
        {
            return new DateTimeOffset(reader.GetDateTime(ordinal), TimeSpan.Zero);
        }
        return DateTimeOffset.Parse(reader.GetString(ordinal));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _lock.Dispose();
        _migrationRunner.Dispose();
    }
}
