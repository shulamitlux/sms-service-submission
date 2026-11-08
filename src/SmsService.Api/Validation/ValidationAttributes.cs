using System.ComponentModel.DataAnnotations;

namespace SmsService.Api.Validation;

/// <summary>
/// Format check for the recipient: required, and a valid Israeli mobile number.
/// Uses the same rules as <see cref="PhoneNumberNormalizer"/>, so the HTTP layer and the service never disagree.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IsraeliMobilePhoneAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context) =>
        PhoneNumberNormalizer.Normalize(value as string) is { IsValid: false, Error: var error }
            ? new ValidationResult(error, context.MemberName is { } name ? [name] : null)
            : ValidationResult.Success;
}

/// <summary>
/// Format check for the message body: required (after trimming) and not longer than <see cref="MessageBodyRules.MaxLength"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SmsBodyAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context) =>
        MessageBodyRules.Validate((value as string)?.Trim() ?? "") is { } error
            ? new ValidationResult(error, context.MemberName is { } name ? [name] : null)
            : ValidationResult.Success;
}
