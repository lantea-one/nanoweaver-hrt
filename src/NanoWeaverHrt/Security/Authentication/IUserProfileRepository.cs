namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Authoritative persistence repository for patient and user demographic profiles,
/// structured addresses, and trusted emergency / caregiver contact lookup tables.
/// </summary>
public interface IUserProfileRepository
{
    /// <summary>
    /// Retrieves a user profile by account username.
    /// </summary>
    /// <param name="username">The username to query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The profile record if found; otherwise, null.</returns>
    Task<UserProfile?> GetProfileAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a user profile record in the database.
    /// </summary>
    /// <param name="profile">The profile to upsert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertProfileAsync(UserProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all registered addresses for a specified patient or user.
    /// </summary>
    /// <param name="username">The username of the address owner.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only collection of addresses.</returns>
    Task<IReadOnlyList<PatientAddress>> GetAddressesAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a patient address entry in the database.
    /// </summary>
    /// <param name="address">The address record to upsert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertAddressAsync(PatientAddress address, CancellationToken ct = default);

    /// <summary>
    /// Deletes an address record by its unique ID for a specified user.
    /// </summary>
    /// <param name="id">The unique identifier of the address.</param>
    /// <param name="username">The username of the address owner.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the address was found and deleted; otherwise, <c>false</c>.</returns>
    Task<bool> DeleteAddressAsync(string id, string username, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all trusted emergency and caregiver contacts for a specified patient, ordered by priority.
    /// </summary>
    /// <param name="username">The username of the patient.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only collection of contacts.</returns>
    Task<IReadOnlyList<PatientContact>> GetContactsAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a trusted contact entry for a specified patient.
    /// </summary>
    /// <param name="contact">The contact record to upsert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertContactAsync(PatientContact contact, CancellationToken ct = default);

    /// <summary>
    /// Deletes a contact record by its unique ID for a specified patient.
    /// </summary>
    /// <param name="id">The unique identifier of the contact.</param>
    /// <param name="username">The username of the contact owner.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the contact was found and deleted; otherwise, <c>false</c>.</returns>
    Task<bool> DeleteContactAsync(string id, string username, CancellationToken ct = default);
}
