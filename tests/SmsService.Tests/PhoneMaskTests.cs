using FluentAssertions;
using SmsService.Api;

namespace SmsService.Tests;

public class PhoneMaskTests
{
    [Theory]
    [InlineData("+972507654321", "+97250****321")]
    [InlineData("+972533333333", "+97253****333")]
    public void Keeps_prefix_and_last_three_digits(string e164, string expected) =>
        PhoneMask.Mask(e164).Should().Be(expected);

    [Fact]
    public void Short_value_is_fully_masked() =>
        PhoneMask.Mask("+97250").Should().Be("******");
}
