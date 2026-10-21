using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Specifies the platform access gating boundaries enforced for a registered user account.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccessGate
{
    /// <summary>
    /// User is restricted exclusively to headless REST API endpoints via cryptographic challenge-response tokens.
    /// </summary>
    Api,

    /// <summary>
    /// User is restricted exclusively to the interactive sovereign Web UI dashboard via session authentication.
    /// </summary>
    Web,

    /// <summary>
    /// User possesses dual access across both interactive Web UI and automated public REST API interfaces.
    /// </summary>
    ApiWeb
}
