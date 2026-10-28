using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Manages the durable lifecycle, at-rest AES-256-GCM sealing, in-memory cache, and GDPR Article 17 cryptographic
/// erasure of per-subject ML-KEM-768 key pairs.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What sealing is:</strong> Each subject's post-quantum ML-KEM-768 private key is wrapped (sealed) before
/// it touches the database using AES-256-GCM under this node's 256-bit PHI master key (<c>phi_master.key</c> via
/// <see cref="ProfileDataProtector"/>). The sealed plaintext binds the normalized subject key, key identifier, and
/// public key via a SHA-256 digest so that a sealed value cannot be moved between subjects or paired with a
/// swapped public key. Opening uses the current node key only and never falls back to the historic legacy key.
/// PHI master key rotation is not supported unless a future migration re-seals <c>weaver_subject_keys</c> alongside
/// the patient PHI columns.
/// </para>
/// <para>
/// <strong>Why erasure clears the column and compacts the database:</strong> Under GDPR Article 17 (Right to
/// Erasure), destroying a subject's private decryption key renders every record and dataset bundle encrypted to
/// that key permanently unreadable. Erasure sets <c>sealed_private_key</c> and <c>public_key_base64</c> to
/// <c>NULL</c> (enforced by a database <c>CHECK</c> constraint), leaves a permanent tombstone row so no new key is
/// ever re-issued for that subject identifier, and runs <c>VACUUM</c> so SQLite rebuilds the database file from
/// live rows and purges leftover bytes from freed pages.
/// </para>
/// <para>
/// <strong>What erasure cannot destroy and what backups contain:</strong> Software erasure on a live node cannot
/// alter database backups taken before the erasure occurred, PostgreSQL write-ahead log (WAL) archives or old row
/// versions prior to compaction, or uncollected managed strings still in process memory. A database backup contains
/// sealed private keys for active subjects and key-free tombstones for erased subjects. Because default deployments
/// place <c>weaver_config.db</c> and <c>node-keys/phi_master.key</c> under the same volume tree, operators should
/// place <c>WEAVER_NODE_KEYS_DIR</c> on separate storage with independent backup retention and expire pre-erasure
/// backups according to their compliance schedule.
/// </para>
/// </remarks>
public sealed class SubjectKeyVault : IDisposable
{
    /// <summary>
    /// Versioned header prefix inside the sealed plaintext of every subject private key.
    /// </summary>
    internal const string SealPrefix = "weaver-subject-key-v1\n";

    /// <summary>
    /// Error message thrown when any vault operation is called before <see cref="InitializeAsync"/> completes.
    /// </summary>
    internal const string NotLoadedMessage =
        "The subject key store has not been loaded. SubjectKeyVault.InitializeAsync must complete before any subject key is used.";

    /// <summary>
    /// Error message thrown when <see cref="InitializeAsync"/> is called more than once on the same vault instance.
    /// </summary>
    internal const string AlreadyLoadedMessage =
        "The subject key store is already loaded.";

    /// <summary>
    /// Error message thrown when an erasure targets a cached active key whose database row was altered externally.
    /// </summary>
    internal const string ExternalChangeMessage =
        "The subject's stored key row is no longer active in the database, although this process loaded it as active. Something other than this process changed the database; restart the service so the key store is reloaded. Nothing was erased by this call.";

    /// <summary>
    /// Error message thrown when a conflicting insert finds no row on follow-up read.
    /// </summary>
    internal const string RowMissingMessage =
        "The subject key row expected after a conflicting insert was not found.";

    /// <summary>
    /// Database repository for reading and mutating <c>weaver_subject_keys</c> rows.
    /// </summary>
    private readonly ISubjectKeyRepository _repository;

    /// <summary>
    /// AES-256-GCM protector keyed by this node's PHI master key.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// Post-quantum cryptographic provider used to mint ML-KEM-768 key pairs.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// External append-only erasure journal used to survive pre-erasure database backup restores.
    /// </summary>
    private readonly SubjectErasureJournal _journal;

    /// <summary>
    /// In-memory cache of active profiles and erasure tombstones keyed by normalized uppercase subject key.
    /// </summary>
    private readonly ConcurrentDictionary<string, SubjectKeyProfile> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Mutex serializing initialization, key creation, and erasure writes.
    /// </summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>
    /// Flag indicating whether <see cref="InitializeAsync"/> has completed successfully.
    /// </summary>
    private volatile bool _isLoaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectKeyVault"/> class.
    /// </summary>
    /// <param name="repository">The subject key database repository.</param>
    /// <param name="protector">The node PHI data protector used to seal and open private keys.</param>
    /// <param name="cryptoProvider">The post-quantum cryptographic provider.</param>
    /// <param name="journal">The external append-only subject erasure journal.</param>
    /// <exception cref="ArgumentNullException">Any parameter is <c>null</c>.</exception>
    public SubjectKeyVault(
        ISubjectKeyRepository repository,
        ProfileDataProtector protector,
        IPostQuantumCryptoProvider cryptoProvider,
        SubjectErasureJournal journal)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(cryptoProvider);
        ArgumentNullException.ThrowIfNull(journal);

        _repository = repository;
        _protector = protector;
        _cryptoProvider = cryptoProvider;
        _journal = journal;
    }

    /// <summary>
    /// Gets a value indicating whether the subject key store has been loaded and verified.
    /// </summary>
    public bool IsLoaded => _isLoaded;

    /// <summary>
    /// Reconciles the database against the external erasure journal, then eagerly loads and verifies every stored
    /// subject key row, publishing the in-memory cache only if all remaining active rows open cleanly.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="SubjectKeyLoadReport"/> summarizing active, erased, and reconciled key counts.</returns>
    /// <exception cref="InvalidOperationException">The vault has already been loaded.</exception>
    /// <exception cref="SubjectKeyStoreLoadException">The erasure journal is malformed, or one or more active rows failed verification.</exception>
    public async Task<SubjectKeyLoadReport> InitializeAsync(CancellationToken ct = default)
    {
        // WHAT: Reconciles weaver_subject_keys with subject_erasures.journal, then unseals every active key and populates the cache.
        // WHY:  Re-applies erasures before opening active rows so a restored database backup cannot undo a GDPR Article 17 erasure,
        //       and fails startup immediately if the node PHI master key was lost or active rows were tampered with.
        // HOW:  Reads the journal and database rows, applies missing tombstones/erasures, backfills unjournalled tombstones,
        //       compacts storage if needed, and stages profiles in a local dictionary before publishing.
        if (_isLoaded)
        {
            throw new InvalidOperationException(AlreadyLoadedMessage);
        }

        SubjectKeyLoadReport? report = null;
        SubjectKeyStoreLoadException? loadError = null;

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IReadOnlyList<SubjectErasureJournalEntry> journalEntries = _journal.ReadAll();
            IReadOnlyList<SubjectKeyRow> rows = await _repository.ListAllAsync(ct).ConfigureAwait(false);
            Dictionary<string, SubjectKeyRow> rowsByKey = new(rows.Count + journalEntries.Count, StringComparer.Ordinal);
            foreach (SubjectKeyRow row in rows)
            {
                rowsByKey[row.SubjectKey] = row;
            }

            HashSet<string> journalledKeys = new(journalEntries.Count, StringComparer.Ordinal);
            int reappliedErasureCount = 0;
            int rebuiltTombstoneCount = 0;
            int journalBackfillCount = 0;

            foreach (SubjectErasureJournalEntry entry in journalEntries)
            {
                journalledKeys.Add(entry.SubjectKey);
                if (!rowsByKey.TryGetValue(entry.SubjectKey, out SubjectKeyRow? existingRow))
                {
                    await _repository.TryInsertTombstoneAsync(entry.SubjectKey, entry.SubjectKey, entry.ShreddedAtUtc, ct).ConfigureAwait(false);
                    rebuiltTombstoneCount++;
                    rowsByKey[entry.SubjectKey] = new SubjectKeyRow
                    {
                        SubjectKey = entry.SubjectKey,
                        SubjectId = entry.SubjectKey,
                        KeyId = null,
                        Algorithm = "ML-KEM-768",
                        PublicKeyBase64 = null,
                        SealedPrivateKey = null,
                        IsShredded = true,
                        CreatedAtUtc = entry.ShreddedAtUtc,
                        ShreddedAtUtc = entry.ShreddedAtUtc
                    };
                }
                else if (!existingRow.IsShredded)
                {
                    await _repository.MarkShreddedAsync(entry.SubjectKey, entry.ShreddedAtUtc, ct).ConfigureAwait(false);
                    reappliedErasureCount++;
                    rowsByKey[entry.SubjectKey] = existingRow with
                    {
                        PublicKeyBase64 = null,
                        SealedPrivateKey = null,
                        IsShredded = true,
                        ShreddedAtUtc = entry.ShreddedAtUtc
                    };
                }
            }

            foreach (KeyValuePair<string, SubjectKeyRow> pair in rowsByKey)
            {
                SubjectKeyRow row = pair.Value;
                if (row.IsShredded && !journalledKeys.Contains(row.SubjectKey))
                {
                    // ck_weaver_subject_keys_state guarantees ShreddedAtUtc is non-null on shredded rows.
                    _journal.Append(row.SubjectKey, row.ShreddedAtUtc!.Value);
                    journalBackfillCount++;
                }
            }

            if (reappliedErasureCount + rebuiltTombstoneCount > 0)
            {
                await _repository.CompactStorageAsync(ct).ConfigureAwait(false);
            }

            Dictionary<string, SubjectKeyProfile> staged = new(rowsByKey.Count, StringComparer.Ordinal);
            int activeCount = 0;
            int erasedCount = 0;
            int failedCount = 0;

            foreach (KeyValuePair<string, SubjectKeyRow> pair in rowsByKey)
            {
                SubjectKeyRow row = pair.Value;
                if (row.IsShredded)
                {
                    erasedCount++;
                    staged[row.SubjectKey] = ToTombstoneProfile(row);
                }
                else
                {
                    activeCount++;
                    if (TryOpenActiveRow(row, out SubjectKeyProfile? activeProfile))
                    {
                        // TryOpenActiveRow returned true, guaranteeing a non-null profile.
                        staged[row.SubjectKey] = activeProfile!;
                    }
                    else
                    {
                        failedCount++;
                    }
                }
            }

            if (failedCount > 0)
            {
                loadError = new SubjectKeyStoreLoadException(LoadFailureMessage(failedCount, activeCount));
            }
            else
            {
                foreach (KeyValuePair<string, SubjectKeyProfile> pair in staged)
                {
                    _cache[pair.Key] = pair.Value;
                }

                _isLoaded = true;
                report = new SubjectKeyLoadReport
                {
                    ActiveCount = activeCount,
                    ErasedCount = erasedCount,
                    ReappliedErasureCount = reappliedErasureCount,
                    RebuiltTombstoneCount = rebuiltTombstoneCount,
                    JournalBackfillCount = journalBackfillCount
                };
            }
        }
        finally
        {
            _writeGate.Release();
        }

        if (loadError is not null)
        {
            throw loadError;
        }

        // When loadError is null, report was assigned in the try block.
        return report!;
    }

    /// <summary>
    /// Finds a cached active or tombstone profile by subject identifier, or returns <c>null</c> when absent or whitespace.
    /// </summary>
    /// <param name="subjectId">The subject identifier to look up.</param>
    /// <returns>The cached <see cref="SubjectKeyProfile"/>, or <c>null</c> if whitespace or unknown.</returns>
    /// <exception cref="InvalidOperationException">The vault has not been loaded yet.</exception>
    public SubjectKeyProfile? Find(string subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return null;
        }

        EnsureLoaded();
        _cache.TryGetValue(NormalizeSubjectKey(subjectId), out SubjectKeyProfile? profile);
        return profile;
    }

    /// <summary>
    /// Returns a list of all cached subject profiles with private key material stripped (<c>PrivateKeyBase64 = null</c>).
    /// </summary>
    /// <returns>A read-only list of sanitized subject key profiles.</returns>
    /// <exception cref="InvalidOperationException">The vault has not been loaded yet.</exception>
    public IReadOnlyList<SubjectKeyProfile> ListWithoutPrivateKeys()
    {
        // WHAT: Copies all cached profiles while redacting PrivateKeyBase64 to null.
        // WHY:  Status and telemetry callers only inspect subject counts and erasure flags, never raw private keys.
        // HOW:  Iterates the dictionary with foreach (no LINQ) and clones each record with PrivateKeyBase64 = null.
        EnsureLoaded();
        List<SubjectKeyProfile> result = new(_cache.Count);
        foreach (KeyValuePair<string, SubjectKeyProfile> entry in _cache)
        {
            SubjectKeyProfile profile = entry.Value;
            result.Add(profile with
            {
                KeyMaterial = profile.KeyMaterial with
                {
                    PrivateKeyBase64 = null
                }
            });
        }

        return result;
    }

    /// <summary>
    /// Returns an existing active subject key profile or generates, seals, and persists a new ML-KEM-768 key pair.
    /// Refuses to issue a key if the subject has been crypto-shredded.
    /// </summary>
    /// <param name="subjectId">The pseudonymized subject identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The active <see cref="SubjectKeyProfile"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="subjectId"/> is null, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The vault has not been loaded yet.</exception>
    /// <exception cref="SubjectKeyUnavailableException">The subject was previously crypto-shredded.</exception>
    /// <exception cref="SubjectKeyStoreLoadException">A concurrently inserted row failed cryptographic verification.</exception>
    public async Task<SubjectKeyProfile> GetOrCreateAsync(string subjectId, CancellationToken ct = default)
    {
        // WHAT: Resolves an active subject key or atomically generates, seals, and persists a new ML-KEM-768 key.
        // WHY:  Keys must be durably persisted before any record is encrypted to them, and erased subjects must never receive a new key.
        // HOW:  Checks the cache first; on miss, acquires _writeGate, seals the new private key, inserts with ON CONFLICT DO NOTHING,
        //       and reconciles from the database if a concurrent insert already populated the row.
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        EnsureLoaded();

        string subjectKey = NormalizeSubjectKey(subjectId);
        if (_cache.TryGetValue(subjectKey, out SubjectKeyProfile? cached))
        {
            if (cached.IsShredded)
            {
                throw new SubjectKeyUnavailableException(subjectId, SubjectKeyUnavailableReason.Erased, cached.ShreddedAtUtc);
            }

            return cached;
        }

        SubjectKeyProfile? result = null;
        SubjectKeyUnavailableException? erasedError = null;
        SubjectKeyStoreLoadException? loadError = null;

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset createdAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);
            PqcKeyMaterial generated = _cryptoProvider.GenerateKemKeyPair($"subj_kem_{subjectId}") with
            {
                CreatedAtUtc = createdAt
            };
            // GenerateKemKeyPair always populates PrivateKeyBase64 on a newly generated key pair.
            string sealedPrivate = SealPrivateKey(subjectKey, generated.KeyId, generated.PublicKeyBase64, generated.PrivateKeyBase64!);

            SubjectKeyRow newRow = new()
            {
                SubjectKey = subjectKey,
                SubjectId = subjectId,
                KeyId = generated.KeyId,
                Algorithm = generated.Algorithm,
                PublicKeyBase64 = generated.PublicKeyBase64,
                SealedPrivateKey = sealedPrivate,
                IsShredded = false,
                CreatedAtUtc = createdAt,
                ShreddedAtUtc = null
            };

            bool inserted = await _repository.TryInsertActiveAsync(newRow, ct).ConfigureAwait(false);
            if (inserted)
            {
                SubjectKeyProfile createdProfile = new()
                {
                    SubjectId = subjectId,
                    KeyMaterial = generated,
                    IsShredded = false,
                    CreatedAtUtc = createdAt,
                    ShreddedAtUtc = null
                };
                _cache.TryAdd(subjectKey, createdProfile);
                result = createdProfile;
            }
            else
            {
                SubjectKeyRow existingRow = await _repository.GetRequiredAsync(subjectKey, ct).ConfigureAwait(false);
                if (existingRow.IsShredded)
                {
                    SubjectKeyProfile tombstone = ToTombstoneProfile(existingRow);
                    _cache[subjectKey] = tombstone;
                    erasedError = new SubjectKeyUnavailableException(subjectId, SubjectKeyUnavailableReason.Erased, existingRow.ShreddedAtUtc);
                }
                else if (!TryOpenActiveRow(existingRow, out SubjectKeyProfile? existingProfile))
                {
                    loadError = new SubjectKeyStoreLoadException(LoadFailureMessage(1, 1));
                }
                else
                {
                    // TryOpenActiveRow returned true, so existingProfile is non-null.
                    _cache.TryAdd(subjectKey, existingProfile!);
                    result = existingProfile!;
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }

        if (erasedError is not null)
        {
            throw erasedError;
        }

        if (loadError is not null)
        {
            throw loadError;
        }

        // When neither error is set, result was assigned in the try block.
        return result!;
    }

    /// <summary>
    /// Permanently destroys a subject's stored private key pursuant to GDPR Article 17, records the erasure in
    /// the external journal prior to the database update, and compacts database storage.
    /// </summary>
    /// <param name="subjectId">The pseudonymized subject identifier to erase.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="SubjectErasureOutcome"/> describing whether the key was erased, already erased, or unknown.</returns>
    /// <exception cref="InvalidOperationException">The vault is not loaded, or the database row was modified externally.</exception>
    /// <remarks>
    /// The erasure journal entry is appended before the database <c>UPDATE</c> executes, and the in-memory cache
    /// transitions to a tombstone only after the database <c>UPDATE</c> succeeds. A database failure after the
    /// journal append leaves the erasure pending in the journal while the cache remains active; a retry or the
    /// next startup reconciliation completes the database erasure.
    /// </remarks>
    public async Task<SubjectErasureOutcome> EraseAsync(string subjectId, CancellationToken ct = default)
    {
        // WHAT: Appends the erasure journal entry, destroys the sealed private key in the database, updates the cache to a tombstone, and runs VACUUM.
        // WHY:  Makes GDPR Article 17 erasure durable across restarts and database restores, and purges freed SQLite database pages.
        // HOW:  Appends to _journal under _writeGate, executes MarkShreddedAsync, replaces the cached entry with a key-free tombstone, and compacts storage.
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return SubjectErasureOutcome.UnknownSubject;
        }

        EnsureLoaded();
        string subjectKey = NormalizeSubjectKey(subjectId);

        SubjectErasureOutcome outcome = SubjectErasureOutcome.UnknownSubject;
        InvalidOperationException? externalError = null;

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_cache.TryGetValue(subjectKey, out SubjectKeyProfile? existingProfile))
            {
                outcome = SubjectErasureOutcome.UnknownSubject;
            }
            else if (existingProfile.IsShredded)
            {
                outcome = SubjectErasureOutcome.AlreadyErased;
            }
            else
            {
                DateTimeOffset shreddedAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);
                _journal.Append(subjectKey, shreddedAt);
                bool updated = await _repository.MarkShreddedAsync(subjectKey, shreddedAt, ct).ConfigureAwait(false);
                if (!updated)
                {
                    externalError = new InvalidOperationException(ExternalChangeMessage);
                }
                else
                {
                    SubjectKeyProfile tombstone = existingProfile with
                    {
                        KeyMaterial = existingProfile.KeyMaterial with
                        {
                            PublicKeyBase64 = string.Empty,
                            PrivateKeyBase64 = null
                        },
                        IsShredded = true,
                        ShreddedAtUtc = shreddedAt
                    };
                    _cache[subjectKey] = tombstone;
                    await _repository.CompactStorageAsync(ct).ConfigureAwait(false);
                    outcome = SubjectErasureOutcome.Erased;
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }

        if (externalError is not null)
        {
            throw externalError;
        }

        return outcome;
    }

    /// <summary>
    /// Retrieves the subject's unsealed Base64 private key for envelope decryption, or throws a typed <see cref="SubjectKeyUnavailableException"/>.
    /// </summary>
    /// <param name="subjectId">The pseudonymized subject identifier.</param>
    /// <returns>The Base64-encoded ML-KEM-768 private key.</returns>
    /// <exception cref="ArgumentException"><paramref name="subjectId"/> is null, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The vault has not been loaded yet.</exception>
    /// <exception cref="SubjectKeyUnavailableException">The subject is unknown or has been crypto-shredded.</exception>
    public string GetPrivateKeyForDecryption(string subjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        EnsureLoaded();

        if (!_cache.TryGetValue(NormalizeSubjectKey(subjectId), out SubjectKeyProfile? profile))
        {
            throw new SubjectKeyUnavailableException(subjectId, SubjectKeyUnavailableReason.UnknownSubject, null);
        }

        if (profile.IsShredded)
        {
            throw new SubjectKeyUnavailableException(subjectId, SubjectKeyUnavailableReason.Erased, profile.ShreddedAtUtc);
        }

        // Active cached profiles always hold a non-empty PrivateKeyBase64 verified at load or creation.
        return profile.KeyMaterial.PrivateKeyBase64!;
    }

    /// <summary>
    /// Releases the internal write-gate synchronization primitive.
    /// </summary>
    public void Dispose()
    {
        _writeGate.Dispose();
    }

    /// <summary>
    /// Normalizes a subject identifier to its canonical uppercase primary key form.
    /// </summary>
    /// <param name="subjectId">The subject identifier to normalize.</param>
    /// <returns>The invariant uppercase form of <paramref name="subjectId"/>.</returns>
    internal static string NormalizeSubjectKey(string subjectId) => subjectId.ToUpperInvariant();

    /// <summary>
    /// Computes the lowercase hex SHA-256 row-binding token over the subject key, key identifier, and public key.
    /// </summary>
    /// <param name="subjectKey">The normalized uppercase subject key.</param>
    /// <param name="keyId">The key identifier.</param>
    /// <param name="publicKeyBase64">The Base64-encoded public key.</param>
    /// <returns>A 64-character lowercase hexadecimal SHA-256 digest.</returns>
    internal static string ComputeBinding(string subjectKey, string keyId, string publicKeyBase64)
    {
        // WHAT: Computes a deterministic SHA-256 binding over the unambiguous Base64 concatenation of row identity fields.
        // WHY:  Prevents an attacker with database access from swapping sealed private keys between subjects or replacing a public key.
        // HOW:  Base64-encodes subjectKey and keyId (which cannot contain '.'), joins with '.', and hashes the UTF-8 bytes.
        string b64SubjectKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(subjectKey));
        string b64KeyId = Convert.ToBase64String(Encoding.UTF8.GetBytes(keyId));
        byte[] material = Encoding.UTF8.GetBytes($"{b64SubjectKey}.{b64KeyId}.{publicKeyBase64}");
        return Convert.ToHexString(SHA256.HashData(material)).ToLowerInvariant();
    }

    /// <summary>
    /// Formats the diagnostic message used when one or more stored subject keys cannot be opened at startup.
    /// </summary>
    /// <param name="failedCount">The number of active rows that failed to open.</param>
    /// <param name="totalCount">The total number of active rows inspected.</param>
    /// <returns>The formatted client-safe startup failure message.</returns>
    internal static string LoadFailureMessage(int failedCount, int totalCount) =>
        $"The subject key store could not be loaded: {failedCount} of {totalCount} stored subject keys could not be opened with this node's PHI master key (phi_master.key, or the configured pepper override). The key was replaced, rotated or regenerated, or the rows were altered. The service will not start, because continuing would leave every record encrypted to those keys undecryptable. Restore the original key file; nothing was changed.";

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the vault has not completed <see cref="InitializeAsync"/>.
    /// </summary>
    private void EnsureLoaded()
    {
        if (!_isLoaded)
        {
            throw new InvalidOperationException(NotLoadedMessage);
        }
    }

    /// <summary>
    /// Seals a subject's private key Base64 together with its row-binding token under the node PHI master key.
    /// </summary>
    /// <param name="subjectKey">The normalized uppercase subject key.</param>
    /// <param name="keyId">The key identifier.</param>
    /// <param name="publicKeyBase64">The Base64-encoded public key.</param>
    /// <param name="privateKeyBase64">The Base64-encoded PKCS#8 private key.</param>
    /// <returns>The AES-256-GCM encrypted envelope string.</returns>
    private string SealPrivateKey(string subjectKey, string keyId, string publicKeyBase64, string privateKeyBase64)
    {
        // WHAT: Formats the bound plaintext payload and seals it with ProfileDataProtector.
        // WHY:  Protects the private key at rest while cryptographically binding it to its row columns.
        // HOW:  Concatenates SealPrefix, the 64-char binding hex, a newline, and the private key Base64.
        string binding = ComputeBinding(subjectKey, keyId, publicKeyBase64);
        string plaintext = $"{SealPrefix}{binding}\n{privateKeyBase64}";
        return _protector.EncryptRequiredField(plaintext);
    }

    /// <summary>
    /// Attempts to unseal and verify an active database row into an in-memory <see cref="SubjectKeyProfile"/>.
    /// </summary>
    /// <param name="row">The active database row to open.</param>
    /// <param name="profile">When this method returns <c>true</c>, holds the verified active profile; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if the row algorithm, AES-GCM tag, prefix, row binding, and private key are valid; otherwise <c>false</c>.</returns>
    private bool TryOpenActiveRow(SubjectKeyRow row, out SubjectKeyProfile? profile)
    {
        // WHAT: Verifies algorithm, opens the AES-256-GCM envelope with the current node key only, and checks the row binding.
        // WHY:  Detects a missing/regenerated PHI master key, legacy-key injection, or column tampering before serving traffic.
        // HOW:  Checks ML-KEM-768, decrypts via DecryptRequiredFieldWithCurrentKeyOnly, and verifies expectedHeader prefix.
        if (row.Algorithm != "ML-KEM-768")
        {
            profile = null;
            return false;
        }

        // ck_weaver_subject_keys_state guarantees SealedPrivateKey, KeyId, and PublicKeyBase64 are non-null on active rows.
        string opened;
        try
        {
            opened = _protector.DecryptRequiredFieldWithCurrentKeyOnly(row.SealedPrivateKey!);
        }
        catch (CryptographicException)
        {
            profile = null;
            return false;
        }

        string expectedHeader = $"{SealPrefix}{ComputeBinding(row.SubjectKey, row.KeyId!, row.PublicKeyBase64!)}\n";
        if (!opened.StartsWith(expectedHeader, StringComparison.Ordinal) || opened.Length == expectedHeader.Length)
        {
            profile = null;
            return false;
        }

        string privateKeyBase64 = opened[expectedHeader.Length..];
        profile = new SubjectKeyProfile
        {
            SubjectId = row.SubjectId,
            KeyMaterial = new PqcKeyMaterial
            {
                KeyId = row.KeyId!,
                Algorithm = row.Algorithm,
                PublicKeyBase64 = row.PublicKeyBase64!,
                PrivateKeyBase64 = privateKeyBase64,
                CreatedAtUtc = row.CreatedAtUtc
            },
            IsShredded = false,
            CreatedAtUtc = row.CreatedAtUtc,
            ShreddedAtUtc = null
        };
        return true;
    }

    /// <summary>
    /// Converts a shredded database row into an in-memory tombstone <see cref="SubjectKeyProfile"/>.
    /// </summary>
    /// <param name="row">The shredded database row.</param>
    /// <returns>A tombstone profile with empty public key and <c>null</c> private key.</returns>
    private static SubjectKeyProfile ToTombstoneProfile(SubjectKeyRow row) =>
        new()
        {
            SubjectId = row.SubjectId,
            KeyMaterial = new PqcKeyMaterial
            {
                KeyId = row.KeyId ?? string.Empty,
                Algorithm = row.Algorithm,
                PublicKeyBase64 = string.Empty,
                PrivateKeyBase64 = null,
                CreatedAtUtc = row.CreatedAtUtc
            },
            IsShredded = true,
            CreatedAtUtc = row.CreatedAtUtc,
            ShreddedAtUtc = row.ShreddedAtUtc
        };

    /// <summary>
    /// Truncates a timestamp to whole UTC microseconds so in-memory and database-reloaded values compare equal across engines.
    /// </summary>
    /// <param name="timestamp">The timestamp to truncate.</param>
    /// <returns>The UTC timestamp truncated to 10-tick (1 microsecond) resolution.</returns>
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset timestamp)
    {
        DateTimeOffset utc = timestamp.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}
