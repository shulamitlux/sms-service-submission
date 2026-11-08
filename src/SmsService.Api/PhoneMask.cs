namespace SmsService.Api;

public static class PhoneMask
{
    private const int VisiblePrefix = 6; // "+97250": country code and mobile prefix
    private const int VisibleSuffix = 3;

    /// <summary>
    /// Privacy: API lists show a masked number, e.g. +972507654321 -> +97250****321.
    /// Enough to recognize a recipient, not enough to collect employee numbers from the list.
    /// </summary>
    public static string Mask(string e164) =>
        e164.Length <= VisiblePrefix + VisibleSuffix
            ? new string('*', e164.Length)
            : e164[..VisiblePrefix] + new string('*', e164.Length - VisiblePrefix - VisibleSuffix) + e164[^VisibleSuffix..];
}
