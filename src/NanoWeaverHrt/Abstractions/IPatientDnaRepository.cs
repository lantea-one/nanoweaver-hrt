using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Abstractions;

/// <summary>
/// Service contract for persistent storage and retrieval of patient-associated digitized DNA blueprints,
/// imported sequence/variant data, and personalized biophysical/simulation results.
/// </summary>
public interface IPatientDnaRepository
{
    /// <summary>
    /// Retrieves all digitized DNA nanocarrier blueprint records associated with a specific patient.
    /// </summary>
    /// <param name="username">The patient's username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only collection of <see cref="PatientDigitalDnaRecord"/> instances ordered by creation date descending.</returns>
    Task<IReadOnlyList<PatientDigitalDnaRecord>> GetDnaRecordsAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific digitized DNA blueprint record by its unique identifier.
    /// </summary>
    /// <param name="id">The unique DNA record identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching <see cref="PatientDigitalDnaRecord"/> if found; otherwise, null.</returns>
    Task<PatientDigitalDnaRecord?> GetDnaRecordByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a digitized DNA blueprint record for a patient.
    /// </summary>
    /// <param name="record">The digitized DNA blueprint record to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertDnaRecordAsync(PatientDigitalDnaRecord record, CancellationToken ct = default);

    /// <summary>
    /// Permanently removes a digitized DNA blueprint record by its unique identifier.
    /// </summary>
    /// <param name="id">The unique DNA record identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the record was found and deleted; otherwise, false.</returns>
    Task<bool> DeleteDnaRecordAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all imported DNA sequences, variant panels, and dataset archives associated with a specific patient.
    /// </summary>
    /// <param name="username">The patient's username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only collection of <see cref="PatientDnaImportRecord"/> instances ordered by import date descending.</returns>
    Task<IReadOnlyList<PatientDnaImportRecord>> GetImportsAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific imported sequence or dataset record by its unique identifier.
    /// </summary>
    /// <param name="id">The unique import identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching <see cref="PatientDnaImportRecord"/> if found; otherwise, null.</returns>
    Task<PatientDnaImportRecord?> GetImportByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates an imported sequence or dataset record for a patient.
    /// </summary>
    /// <param name="importRecord">The import record to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertImportAsync(PatientDnaImportRecord importRecord, CancellationToken ct = default);

    /// <summary>
    /// Permanently removes an imported sequence or dataset record by its unique identifier.
    /// </summary>
    /// <param name="id">The unique import identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the record was found and deleted; otherwise, false.</returns>
    Task<bool> DeleteImportAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all simulation, endocrine kinetics, and pharmacogenomic results associated with a specific patient.
    /// </summary>
    /// <param name="username">The patient's username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only collection of <see cref="PatientResultRecord"/> instances ordered by creation date descending.</returns>
    Task<IReadOnlyList<PatientResultRecord>> GetResultsAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific simulation or clinical result by its unique identifier.
    /// </summary>
    /// <param name="id">The unique result identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching <see cref="PatientResultRecord"/> if found; otherwise, null.</returns>
    Task<PatientResultRecord?> GetResultByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a simulation or clinical result record for a patient.
    /// </summary>
    /// <param name="result">The result record to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertResultAsync(PatientResultRecord result, CancellationToken ct = default);

    /// <summary>
    /// Permanently removes a simulation or clinical result record by its unique identifier.
    /// </summary>
    /// <param name="id">The unique result identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the record was found and deleted; otherwise, false.</returns>
    Task<bool> DeleteResultAsync(string id, CancellationToken ct = default);
}
