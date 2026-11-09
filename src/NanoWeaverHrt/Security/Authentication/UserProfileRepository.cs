using System.Data.Common;
using System.Text.Json;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Authoritative persistence repository for patient and user demographic profiles,
/// structured addresses, and trusted emergency/caregiver contact lookup tables,
/// supporting both PostgreSQL (pg-duckdb) and embedded SQLite fallback.
/// </summary>
public sealed class UserProfileRepository : IUserProfileRepository, IDisposable
{
    private static readonly string[] ProfileColumns =
    [
        "username", "first_name", "last_name", "preferred_name", "email_address",
        "phone_number", "date_of_birth", "picture", "regimen", "gender_assigned_at_birth",
        "pronouns_json", "emergency_medical_notes", "bio", "language_preference",
        "created_at_utc", "updated_at_utc"
    ];

    private static readonly string[] ProfileConflictColumns = ["username"];

    private static readonly string[] ProfileUpdateColumns =
    [
        "first_name", "last_name", "preferred_name", "email_address",
        "phone_number", "date_of_birth", "picture", "regimen", "gender_assigned_at_birth",
        "pronouns_json", "emergency_medical_notes", "bio", "language_preference",
        "updated_at_utc"
    ];

    private static readonly string[] ProfileParameterNames =
    [
        "u", "fn", "ln", "pn", "em",
        "ph", "dob", "pic", "reg", "gen",
        "pro", "emn", "bio", "lang",
        "cat", "uat"
    ];

    private static readonly string[] AddressColumns =
    [
        "id", "username", "address_type", "address_line1", "address_line2",
        "city", "state_or_province", "postal_code", "country", "is_primary",
        "created_at_utc", "updated_at_utc"
    ];

    private static readonly string[] AddressConflictColumns = ["id"];

    private static readonly string[] AddressUpdateColumns =
    [
        "address_type", "address_line1", "address_line2",
        "city", "state_or_province", "postal_code", "country", "is_primary",
        "updated_at_utc"
    ];

    private static readonly string[] AddressParameterNames =
    [
        "id", "u", "at", "l1", "l2",
        "ci", "sp", "pc", "co", "ip",
        "cat", "uat"
    ];

    private static readonly string[] ContactColumns =
    [
        "id", "username", "contact_name", "relationship", "phone_number",
        "email_address", "priority_order", "can_discuss_medical_info", "notes",
        "created_at_utc", "updated_at_utc"
    ];

    private static readonly string[] ContactConflictColumns = ["id"];

    private static readonly string[] ContactUpdateColumns =
    [
        "contact_name", "relationship", "phone_number",
        "email_address", "priority_order", "can_discuss_medical_info", "notes",
        "updated_at_utc"
    ];

    private static readonly string[] ContactParameterNames =
    [
        "id", "u", "cn", "rel", "ph",
        "em", "po", "cdm", "nt",
        "cat", "uat"
    ];

    /// <summary>
    /// Thread synchronization lock for SQLite write operations.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Connection seam that decides which database engine each call talks to.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Profile cryptographic data protector for sensitive fields and avatars.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserProfileRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Factory that opens connections to the selected backend.</param>
    /// <param name="protector">Cryptographic profile data protector.</param>
    public UserProfileRepository(IWeaverDbConnectionFactory connectionFactory, ProfileDataProtector protector)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
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
            UserProfile? result = null;
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                await using DbCommand cmd = session.CreateCommand($@"
                    SELECT username, first_name, last_name, preferred_name, email_address,
                           phone_number, date_of_birth, picture, regimen, gender_assigned_at_birth,
                           pronouns_json, emergency_medical_notes, bio, language_preference,
                           created_at_utc, updated_at_utc
                    FROM weaver_user_profiles
                    WHERE username = {session.Dialect.Parameter("u")};");

                WeaverDbParameters.Add(cmd, "u", NormalizeUsername(username));

                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    result = MapProfileFromReader(reader, _protector);
                }
            }

            return result;
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
        // HOW:  Encrypts picture with AES-256-GCM and executes dialect-compliant upsert SQL statement.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Username);

        await _lock.WaitAsync(ct);
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                string? encryptedPicture = !string.IsNullOrEmpty(profile.Picture) ? _protector.EncryptDataUri(profile.Picture) : null;
                string pronounsJson = JsonSerializer.Serialize(profile.Pronouns ?? []);

                string sql = session.Dialect.BuildUpsertSql(
                    "weaver_user_profiles",
                    ProfileColumns,
                    ProfileConflictColumns,
                    ProfileUpdateColumns,
                    ProfileParameterNames);

                await using DbCommand cmd = session.CreateCommand(sql);

                WeaverDbParameters.Add(cmd, "u", NormalizeUsername(profile.Username));
                WeaverDbParameters.Add(cmd, "fn", _protector.EncryptRequiredField(profile.FirstName ?? string.Empty));
                WeaverDbParameters.Add(cmd, "ln", _protector.EncryptRequiredField(profile.LastName ?? string.Empty));
                WeaverDbParameters.AddNullable(cmd, "pn", _protector.EncryptField(profile.PreferredName));
                WeaverDbParameters.AddNullable(cmd, "em", _protector.EncryptField(profile.EmailAddress));
                WeaverDbParameters.AddNullable(cmd, "ph", _protector.EncryptField(profile.PhoneNumber));
                WeaverDbParameters.AddNullable(cmd, "dob", _protector.EncryptField(profile.DateOfBirth));
                WeaverDbParameters.AddNullable(cmd, "pic", encryptedPicture);
                // WHAT: Seals the gender-identity trio. WHY: regimen plus sex at birth identifies a transgender patient.
                // HOW:  A null regimen stays NULL (EncryptField passes null through); the NOT NULL columns use the required flavour.
                WeaverDbParameters.AddNullable(cmd, "reg", _protector.EncryptField(profile.Regimen?.ToString()));
                WeaverDbParameters.Add(cmd, "gen", _protector.EncryptRequiredField(profile.GenderAssignedAtBirth.ToString()));
                WeaverDbParameters.Add(cmd, "pro", _protector.EncryptRequiredField(pronounsJson));
                WeaverDbParameters.AddNullable(cmd, "emn", _protector.EncryptField(profile.EmergencyMedicalNotes));
                WeaverDbParameters.AddNullable(cmd, "bio", _protector.EncryptField(profile.Bio));
                WeaverDbParameters.Add(cmd, "lang", string.IsNullOrWhiteSpace(profile.LanguagePreference) ? "en-US" : profile.LanguagePreference);

                WeaverDbParameters.Add(cmd, "cat", session.Dialect.TimestampValue(profile.CreatedAtUtc));
                WeaverDbParameters.Add(cmd, "uat", session.Dialect.TimestampValue(DateTimeOffset.UtcNow));

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
            List<PatientAddress> list = new();
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                await using DbCommand cmd = session.CreateCommand($@"
                    SELECT id, username, address_type, address_line1, address_line2,
                           city, state_or_province, postal_code, country, is_primary,
                           created_at_utc, updated_at_utc
                    FROM weaver_patient_addresses
                    WHERE username = {session.Dialect.Parameter("u")}
                    ORDER BY is_primary DESC, created_at_utc ASC;");

                WeaverDbParameters.Add(cmd, "u", NormalizeUsername(username));

                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    list.Add(MapAddressFromReader(reader, _protector));
                }
            }

            return list;
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
        // HOW:  Executes parameterized SQL with dialect-compliant upsert statement.
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(address.Username);

        await _lock.WaitAsync(ct);
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                string sql = session.Dialect.BuildUpsertSql(
                    "weaver_patient_addresses",
                    AddressColumns,
                    AddressConflictColumns,
                    AddressUpdateColumns,
                    AddressParameterNames);

                await using DbCommand cmd = session.CreateCommand(sql);

                WeaverDbParameters.Add(cmd, "id", address.Id);
                WeaverDbParameters.Add(cmd, "u", NormalizeUsername(address.Username));
                WeaverDbParameters.Add(cmd, "at", address.AddressType);
                WeaverDbParameters.Add(cmd, "l1", _protector.EncryptRequiredField(address.AddressLine1));
                WeaverDbParameters.AddNullable(cmd, "l2", _protector.EncryptField(address.AddressLine2));
                WeaverDbParameters.Add(cmd, "ci", _protector.EncryptRequiredField(address.City));
                WeaverDbParameters.Add(cmd, "sp", _protector.EncryptRequiredField(address.StateOrProvince));
                WeaverDbParameters.Add(cmd, "pc", _protector.EncryptRequiredField(address.PostalCode));
                WeaverDbParameters.Add(cmd, "co", address.Country);

                WeaverDbParameters.Add(cmd, "ip", session.Dialect.BooleanValue(address.IsPrimary));
                WeaverDbParameters.Add(cmd, "cat", session.Dialect.TimestampValue(address.CreatedAtUtc));
                WeaverDbParameters.Add(cmd, "uat", session.Dialect.TimestampValue(DateTimeOffset.UtcNow));

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
            bool result;
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                await using DbCommand cmd = session.CreateCommand($"DELETE FROM weaver_patient_addresses WHERE id = {session.Dialect.Parameter("id")} AND username = {session.Dialect.Parameter("u")};");
                WeaverDbParameters.Add(cmd, "id", id);
                WeaverDbParameters.Add(cmd, "u", NormalizeUsername(username));

                int affected = await cmd.ExecuteNonQueryAsync(ct);
                result = affected > 0;
            }

            return result;
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
            List<PatientContact> list = new();
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                await using DbCommand cmd = session.CreateCommand($@"
                    SELECT id, username, contact_name, relationship, phone_number,
                           email_address, priority_order, can_discuss_medical_info, notes,
                           created_at_utc, updated_at_utc
                    FROM weaver_patient_contacts
                    WHERE username = {session.Dialect.Parameter("u")}
                    ORDER BY priority_order ASC, created_at_utc ASC;");

                WeaverDbParameters.Add(cmd, "u", NormalizeUsername(username));

                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    list.Add(MapContactFromReader(reader, _protector));
                }
            }

            return list;
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
        // HOW:  Executes parameterized SQL with dialect-compliant upsert statement.
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentException.ThrowIfNullOrWhiteSpace(contact.Username);

        await _lock.WaitAsync(ct);
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                string sql = session.Dialect.BuildUpsertSql(
                    "weaver_patient_contacts",
                    ContactColumns,
                    ContactConflictColumns,
                    ContactUpdateColumns,
                    ContactParameterNames);

                await using DbCommand cmd = session.CreateCommand(sql);

                WeaverDbParameters.Add(cmd, "id", contact.Id);
                WeaverDbParameters.Add(cmd, "u", NormalizeUsername(contact.Username));
                WeaverDbParameters.Add(cmd, "cn", _protector.EncryptRequiredField(contact.ContactName));
                WeaverDbParameters.Add(cmd, "rel", _protector.EncryptRequiredField(contact.Relationship));
                WeaverDbParameters.Add(cmd, "ph", _protector.EncryptRequiredField(contact.PhoneNumber));
                WeaverDbParameters.AddNullable(cmd, "em", _protector.EncryptField(contact.EmailAddress));
                WeaverDbParameters.Add(cmd, "po", contact.PriorityOrder);
                WeaverDbParameters.AddNullable(cmd, "nt", _protector.EncryptField(contact.Notes));

                WeaverDbParameters.Add(cmd, "cdm", session.Dialect.BooleanValue(contact.CanDiscussMedicalInfo));
                WeaverDbParameters.Add(cmd, "cat", session.Dialect.TimestampValue(contact.CreatedAtUtc));
                WeaverDbParameters.Add(cmd, "uat", session.Dialect.TimestampValue(DateTimeOffset.UtcNow));

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
            bool result;
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                await using DbCommand cmd = session.CreateCommand($"DELETE FROM weaver_patient_contacts WHERE id = {session.Dialect.Parameter("id")} AND username = {session.Dialect.Parameter("u")};");
                WeaverDbParameters.Add(cmd, "id", id);
                WeaverDbParameters.Add(cmd, "u", NormalizeUsername(username));

                int affected = await cmd.ExecuteNonQueryAsync(ct);
                result = affected > 0;
            }

            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Maps a database reader row to a strongly-typed <see cref="UserProfile"/>.
    /// </summary>
    /// <param name="reader">A reader positioned on the profile row to read.</param>
    /// <param name="protector">The service that decrypts the personal fields held encrypted at rest.</param>
    /// <returns>The fully populated patient profile for the current row.</returns>
    internal static UserProfile MapProfileFromReader(DbDataReader reader, ProfileDataProtector protector)
    {
        // WHAT: Reads all 16 user profile columns and decrypts sealed personal identifiers and picture envelopes.
        // WHY:  Isolates decryption and JSON deserialization logic in a single reader row mapper.
        // HOW:  Uses protector to decrypt fields, parses optional therapeutic regimen enum, and deserializes pronouns.
        string username = reader.GetString(0);
        string firstName = protector.DecryptRequiredField(reader.GetString(1));
        string lastName = protector.DecryptRequiredField(reader.GetString(2));
        string? preferredName = protector.DecryptField(reader.IsDBNull(3) ? null : reader.GetString(3));
        string? email = protector.DecryptField(reader.IsDBNull(4) ? null : reader.GetString(4));
        string? phone = protector.DecryptField(reader.IsDBNull(5) ? null : reader.GetString(5));
        string? dob = protector.DecryptField(reader.IsDBNull(6) ? null : reader.GetString(6));
        string? encryptedPic = reader.IsDBNull(7) ? null : reader.GetString(7);
        // WHAT: Decrypts the gender-identity trio before any parsing.
        // WHY:  The stored text is an encrypted envelope; parsing it directly would find neither an enum nor JSON.
        // HOW:  Decrypt first; Enum.TryParse and JsonSerializer.Deserialize below then run on the plaintext.
        string? regimenStr = protector.DecryptField(reader.IsDBNull(8) ? null : reader.GetString(8));
        string genderStr = protector.DecryptRequiredField(reader.GetString(9));
        string pronounsJson = protector.DecryptRequiredField(reader.GetString(10));
        string? emergencyNotes = protector.DecryptField(reader.IsDBNull(11) ? null : reader.GetString(11));
        string? bio = protector.DecryptField(reader.IsDBNull(12) ? null : reader.GetString(12));
        string languagePref = reader.GetString(13);

        DateTimeOffset created = WeaverDbValues.ReadTimestamp(reader, 14);
        DateTimeOffset updated = WeaverDbValues.ReadTimestamp(reader, 15);

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
    /// <param name="protector">Protector that decrypts the street, city, state and postal-code columns.</param>
    /// <returns>The postal address described by the current row.</returns>
    internal static PatientAddress MapAddressFromReader(DbDataReader reader, ProfileDataProtector protector)
    {
        // WHAT: Reads all 12 patient address columns and decrypts encrypted address line and locality fields.
        // WHY:  Enforces patient location data protection at rest across database engines.
        // HOW:  Decrypts required and optional address lines via protector and parses timestamps and booleans.
        string id = reader.GetString(0);
        string username = reader.GetString(1);
        string addressType = reader.GetString(2);
        string line1 = protector.DecryptRequiredField(reader.GetString(3));
        string? line2 = protector.DecryptField(reader.IsDBNull(4) ? null : reader.GetString(4));
        string city = protector.DecryptRequiredField(reader.GetString(5));
        string state = protector.DecryptRequiredField(reader.GetString(6));
        string postalCode = protector.DecryptRequiredField(reader.GetString(7));
        string country = reader.GetString(8);
        bool isPrimary = WeaverDbValues.ReadBoolean(reader, 9);
        DateTimeOffset created = WeaverDbValues.ReadTimestamp(reader, 10);
        DateTimeOffset updated = WeaverDbValues.ReadTimestamp(reader, 11);

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
    /// <param name="protector">Protector that decrypts the name, phone, e-mail and notes columns.</param>
    /// <returns>The emergency or next-of-kin contact described by the current row.</returns>
    internal static PatientContact MapContactFromReader(DbDataReader reader, ProfileDataProtector protector)
    {
        // WHAT: Reads all 11 emergency contact columns and decrypts sensitive contact name, phone, email, and notes.
        // WHY:  Protects sensitive third-party emergency contact details at rest.
        // HOW:  Decrypts contact name, relationship, phone, email, notes with protector and parses timestamps.
        string id = reader.GetString(0);
        string username = reader.GetString(1);
        string contactName = protector.DecryptRequiredField(reader.GetString(2));
        string relationship = protector.DecryptRequiredField(reader.GetString(3));
        string phone = protector.DecryptRequiredField(reader.GetString(4));
        string? email = protector.DecryptField(reader.IsDBNull(5) ? null : reader.GetString(5));
        int priority = reader.GetInt32(6);
        bool canDiscuss = WeaverDbValues.ReadBoolean(reader, 7);
        string? notes = protector.DecryptField(reader.IsDBNull(8) ? null : reader.GetString(8));
        DateTimeOffset created = WeaverDbValues.ReadTimestamp(reader, 9);
        DateTimeOffset updated = WeaverDbValues.ReadTimestamp(reader, 10);

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

    /// <inheritdoc/>
    public void Dispose()
    {
        _lock.Dispose();
    }

    /// <summary>
    /// Normalizes an account username by trimming surrounding whitespace and converting to lowercase invariant form.
    /// </summary>
    /// <param name="username">The raw username supplied by the caller.</param>
    /// <returns>The trimmed, lowercase-normalized username string.</returns>
    private static string NormalizeUsername(string username)
    {
        // WHAT: Trims whitespace and normalizes the username to lowercase invariant characters.
        // WHY:  Ensures username case-normalization parity across repositories and services.
        // HOW:  Calls Trim().ToLowerInvariant() on the validated non-empty string.
        return username.Trim().ToLowerInvariant();
    }
}
