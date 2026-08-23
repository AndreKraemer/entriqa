using Entriqa.Domain.Errors;
using Entriqa.Domain.Validation;
using Xunit;

namespace Entriqa.Tests;

public class FormSubmissionValidatorTests
{
    private static Dictionary<string, string> Valid() => new()
    {
        ["name"] = "Anna", ["email"] = "anna@example.org", ["topic"] = "A", ["msg"] = "Hallo", ["consent"] = "true", ["src"] = "linkedin",
    };

    [Fact]
    public void Valid_values_pass() => FormSubmissionValidator.ValidateAndThrow(TestData.Contact(), Valid(), null);

    [Fact]
    public void Collects_all_errors_at_once()
    {
        var values = Valid();
        values["name"] = ""; values["email"] = "kein-mail"; values["topic"] = "Z"; values["consent"] = "false"; values["msg"] = new string('x', 51);

        var ex = Assert.Throws<ValidationException>(() => FormSubmissionValidator.ValidateAndThrow(TestData.Contact(), values, null));

        Assert.Equal(new[] { "name", "email", "topic", "msg", "consent" }, ex.Errors.Select(e => e.Field).ToArray());
    }

    [Fact]
    public void Optional_select_may_be_empty()
    {
        var values = Valid(); values.Remove("topic");
        FormSubmissionValidator.ValidateAndThrow(TestData.Contact(), values, null);
    }

    [Theory]
    [InlineData("[\"A\",\"B\"]", new[] { "A", "B" })]
    [InlineData("A, B", new[] { "A", "B" })]
    [InlineData("A", new[] { "A" })]
    public void SplitMulti_accepts_json_and_list(string input, string[] expected) => Assert.Equal(expected, FormSubmissionValidator.SplitMulti(input));
}
