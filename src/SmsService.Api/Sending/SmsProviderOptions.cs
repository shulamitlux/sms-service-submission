namespace SmsService.Api.Sending;

/// <summary>
/// Provider settings from configuration section "SmsProvider" (env vars SmsProvider__BaseUrl, SmsProvider__SendPath, ...).
/// The provider contract was not given, so the paths are configurable: if the real simulator only uses
/// different paths, adapting to it is a configuration change, not a code change.
/// </summary>
public sealed class SmsProviderOptions
{
    public const string Section = "SmsProvider";
    public const string IdPlaceholder = "{id}";
    public const string DefaultSendPath = "send";
    public const string DefaultStatusPath = "status/" + IdPlaceholder;

    /// <summary>Provider base URL, e.g. http://localhost:5005.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Path of the send endpoint (POST), relative to <see cref="BaseUrl"/>.</summary>
    public string SendPath { get; set; } = DefaultSendPath;

    /// <summary>Path of the status endpoint (GET), relative to <see cref="BaseUrl"/>. Must contain {id}.</summary>
    public string StatusPath { get; set; } = DefaultStatusPath;

    /// <summary>An empty value (e.g. an env var that is set but empty) means "use the default".</summary>
    public void ApplyDefaultsForEmptyPaths()
    {
        if (string.IsNullOrWhiteSpace(SendPath)) SendPath = DefaultSendPath;
        if (string.IsNullOrWhiteSpace(StatusPath)) StatusPath = DefaultStatusPath;
    }
}
