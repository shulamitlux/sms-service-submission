using FluentAssertions;
using SmsService.Api.Validation;

namespace SmsService.Tests;

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("050-7654321")]
    [InlineData("0507654321")]
    [InlineData("+972507654321")]
    [InlineData("972507654321")]
    [InlineData("972 50 765 4321")]
    [InlineData("(050) 765-4321")]
    public void Valid_israeli_mobile_formats_are_normalized_to_E164(string input)
    {
        var result = PhoneNumberNormalizer.Normalize(input);

        result.IsValid.Should().BeTrue();
        result.E164.Should().Be("+972507654321");
    }

    [Fact]
    public void Number_in_unassigned_mobile_block_is_rejected()
    {
        // 05X-1xxxxxx blocks are not assigned according to libphonenumber's tables.
        var result = PhoneNumberNormalizer.Normalize("050-1234567");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(PhoneNumberNormalizer.InvalidError);
    }

    [Fact]
    public void Landline_is_rejected()
    {
        var result = PhoneNumberNormalizer.Normalize("02-6543210");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(PhoneNumberNormalizer.InvalidError);
    }

    [Theory]
    [InlineData(null, PhoneNumberNormalizer.RequiredError)]
    [InlineData("", PhoneNumberNormalizer.RequiredError)]
    [InlineData("   ", PhoneNumberNormalizer.RequiredError)]
    [InlineData("abcdefghij", PhoneNumberNormalizer.InvalidError)]
    [InlineData("050ABCDEFG", PhoneNumberNormalizer.InvalidError)]
    [InlineData("050123", PhoneNumberNormalizer.InvalidError)]
    public void Empty_letters_and_too_short_are_rejected(string? input, string expectedError)
    {
        var result = PhoneNumberNormalizer.Normalize(input);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(expectedError);
    }

    [Fact]
    public void Foreign_number_is_rejected()
    {
        PhoneNumberNormalizer.Normalize("+14155550100").IsValid.Should().BeFalse();
    }
}
