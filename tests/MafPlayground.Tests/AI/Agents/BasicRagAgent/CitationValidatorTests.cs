using MafPlayground.AI.Agents.BasicRagAgent;

namespace MafPlayground.Tests.AI.Agents.BasicRagAgent;

public sealed class CitationValidatorTests
{
    private readonly CitationValidator _validator = new();
    private readonly Dictionary<string, RagEvidence> _evidence = new()
    {
        ["e1"] = new(
            "e1",
            "The reset link expires after 30 minutes.",
            "[Help, page 4, source: guides/help.pdf]",
            0.9),
    };

    [Fact]
    public void Validate_AcceptsAtomicClaimsWithAllowedCitationIds()
    {
        RagAnswerDraft draft = new(
            false,
            [new RagClaim("Reset links expire after 30 minutes.", ["e1"])]);

        RagAnswerValidationResult result = _validator.Validate(draft, _evidence);

        Assert.True(result.IsValid);
        Assert.Equal(
            "Reset links expire after 30 minutes. " +
            "[Help, page 4, source: guides/help.pdf]",
            _validator.Render(draft, _evidence));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Validate_RejectsEveryClaimWithoutAnAllowedCitation(string? citationId)
    {
        IReadOnlyList<string> citationIds = citationId is null ? [] : [citationId];
        RagAnswerDraft draft = new(
            false,
            [
                new RagClaim("A supported claim.", ["e1"]),
                new RagClaim("An unsupported claim.", citationIds),
            ]);

        RagAnswerValidationResult result = _validator.Validate(draft, _evidence);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RequiresClaimFreeInsufficientEvidenceResult()
    {
        Assert.True(_validator.Validate(
            new RagAnswerDraft(true, []),
            new Dictionary<string, RagEvidence>()).IsValid);
        Assert.False(_validator.Validate(
            new RagAnswerDraft(true, [new RagClaim("Invented.", ["e1"])]),
            _evidence).IsValid);
        Assert.Equal(
            CitationValidator.NoEvidenceAnswer,
            _validator.Render(
                new RagAnswerDraft(true, []),
                new Dictionary<string, RagEvidence>()));
    }
}
