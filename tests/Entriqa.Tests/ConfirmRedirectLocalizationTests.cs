using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The other half of the localized double opt-in: after the participant confirms, the redirect has
/// to land on the page of THEIR language. ConfirmedRedirectPathFor was covered in isolation, but
/// nothing asserted the use case passes the submission's locale into it - replacing
/// <c>ConfirmedRedirectPathFor(s.Locale)</c> with <c>(null)</c> left the whole suite green.
/// </summary>
public class ConfirmRedirectLocalizationTests
{
    private static (IConfirmSubmissionUseCase UseCase, string Token) Build(string locale, EntriqaOptions options)
    {
        var submission = new Submission
        {
            Id = "whitepaper:1", Slug = "whitepaper", Version = 1, CreatedAt = TestData.Time.GetUtcNow(),
            Values = new Dictionary<string, string>(), Email = "a@b.de", Locale = locale,
        };
        var getSubmission = Substitute.For<ITryGetSubmissionQuery>();
        getSubmission.ExecuteAsync(submission.Id, Arg.Any<CancellationToken>()).Returns(submission);
        var getVersion = Substitute.For<ITryGetFormVersionQuery>();
        getVersion.ExecuteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new FormVersion("whitepaper", 1, TestData.Contact(), TestData.Time.GetUtcNow(), "seed"));

        var opts = Microsoft.Extensions.Options.Options.Create(options);
        var tokens = new FormTokenService(opts, TestData.Time);
        var pipeline = new SubmissionPipelineService(
            [new Entriqa.Application.Pipeline.Steps.NotifyMailStep(Substitute.For<ISendTransactionalMailPort>())],
            opts, TestData.Time, NullLogger<SubmissionPipelineService>.Instance);

        var useCase = new ConfirmSubmissionUseCase(
            getSubmission, getVersion, Substitute.For<ISaveSubmissionCommand>(), tokens,
            new IpHasher(opts), pipeline, opts, TestData.Time);

        return (useCase, tokens.Issue(FormTokenService.KindConfirm, submission.Id));
    }

    [Fact]
    public async Task GivenASubmissionInASecondaryLocale_WhenConfirming_ThenTheRedirectPointsAtThatLanguage()
    {
        var options = TestData.Options();
        options.ConfirmedRedirectPaths["en"] = "/en/confirmed/";
        var (useCase, token) = Build("en", options);

        var result = await useCase.ExecuteAsync(token, "203.0.113.9");

        Assert.Equal("https://example.org/en/confirmed/", result.RedirectUrl);
    }

    [Fact]
    public async Task GivenASubmissionInTheDefaultLocale_WhenConfirming_ThenTheRedirectStaysAtTheRoot()
    {
        var (useCase, token) = Build("de", TestData.Options());

        var result = await useCase.ExecuteAsync(token, "203.0.113.9");

        Assert.Equal("https://example.org/bestaetigt/", result.RedirectUrl);
    }
}
