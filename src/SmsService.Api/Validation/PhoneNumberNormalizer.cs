using PhoneNumbers;

namespace SmsService.Api.Validation;

public sealed record PhoneNormalizationResult(bool IsValid, string? E164, string? Error)
{
    public static PhoneNormalizationResult Valid(string e164) => new(true, e164, null);
    public static PhoneNormalizationResult Invalid(string error) => new(false, null, error);
}

/// <summary>Validates an Israeli mobile number and converts it to E.164 (+972507654321).</summary>
public static class PhoneNumberNormalizer
{
    public const string RequiredError = "Phone number is required";
    public const string InvalidError = "Invalid phone number. Please enter an Israeli mobile number";

    private const string DefaultRegion = "IL";
    private const int IsraelCountryCode = 972;
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    public static PhoneNormalizationResult Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return PhoneNormalizationResult.Invalid(RequiredError);

        var cleaned = new string(input.Where(c => c is not (' ' or '-' or '(' or ')')).ToArray());

        // Only digits with an optional leading '+'. libphonenumber would otherwise
        // silently convert letters to digits ("vanity numbers").
        var digits = cleaned.StartsWith('+') ? cleaned[1..] : cleaned;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return PhoneNormalizationResult.Invalid(InvalidError);

        // "972507654321" (international without '+'): Israeli national numbers start with 0,
        // so a leading 972 can only be the country code.
        if (!cleaned.StartsWith('+') && cleaned.StartsWith("972"))
            cleaned = "+" + cleaned;

        PhoneNumber number;
        try
        {
            number = Util.Parse(cleaned, DefaultRegion);
        }
        catch (NumberParseException)
        {
            return PhoneNormalizationResult.Invalid(InvalidError);
        }

        // Validity comes from libphonenumber's allocation tables, which also tell mobile from landline.
        // Blocks the tables consider unassigned (e.g. 050-1xxxxxx) are rejected.
        var isIsraeliMobile = number.CountryCode == IsraelCountryCode
                              && Util.IsValidNumber(number)
                              && Util.GetNumberType(number) == PhoneNumberType.MOBILE;

        return isIsraeliMobile
            ? PhoneNormalizationResult.Valid(Util.Format(number, PhoneNumberFormat.E164))
            : PhoneNormalizationResult.Invalid(InvalidError);
    }
}
