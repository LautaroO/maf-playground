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

    [Fact]
    public void Validate_RejectsInlineCodeMissingFromCitedEvidence()
    {
        Dictionary<string, RagEvidence> evidence = new()
        {
            ["e1"] = new(
                "e1",
                "Run `dotnet run --project src/MafPlayground.CLI -- devui`.",
                "[CLI reference, source: cli-reference.md]",
                0.9),
        };
        RagAnswerDraft draft = new(
            false,
            [new RagClaim("Run `devui [arguments]`.", ["e1"])]);

        RagAnswerValidationResult result = _validator.Validate(draft, evidence);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains(
                "does not appear verbatim",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsInlineCodeCopiedFromCitedEvidence()
    {
        Dictionary<string, RagEvidence> evidence = new()
        {
            ["e1"] = new(
                "e1",
                "Run `dotnet run --project src/MafPlayground.CLI -- devui`.",
                "[CLI reference, source: cli-reference.md]",
                0.9),
        };
        RagAnswerDraft draft = new(
            false,
            [new RagClaim(
                "Run `dotnet run --project src/MafPlayground.CLI -- devui`.",
                ["e1"])]);

        RagAnswerValidationResult result = _validator.Validate(draft, evidence);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RequiresApplicationOwnedInlineCodeFromCitedEvidence()
    {
        const string invocation =
            "dotnet run --project src/MafPlayground.CLI -- devui";
        Dictionary<string, RagEvidence> evidence = new()
        {
            ["e1"] = new(
                "e1",
                $"Run `{invocation}`.",
                "[CLI reference, source: cli-reference.md]",
                1,
                [invocation]),
        };
        RagAnswerDraft draft = new(
            false,
            [new RagClaim($"Run '{invocation}'.", ["e1"])]);

        RagAnswerValidationResult result = _validator.Validate(draft, evidence);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains(
                "must include required inline code",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsRefusalWhenApplicationRequiresEvidence()
    {
        Dictionary<string, RagEvidence> evidence = new()
        {
            ["e1"] = new(
                "e1",
                "Command `rag ingest` uses the exact invocation `dotnet run -- rag ingest`.",
                "[CLI reference, source: cli-reference.md]",
                1,
                ["dotnet run -- rag ingest"]),
        };

        RagAnswerValidationResult result = _validator.Validate(
            new RagAnswerDraft(true, []),
            evidence);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains(
                "cannot ignore application-required evidence",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsBoundedClaimFreeClarifyingQuestion()
    {
        RagAnswerDraft draft = new(
            true,
            [],
            "Which part of the application do you want help with?");

        RagAnswerValidationResult result = _validator.Validate(
            draft,
            new Dictionary<string, RagEvidence>());

        Assert.True(result.IsValid);
        Assert.Equal(
            draft.ClarifyingQuestion,
            _validator.Render(draft, new Dictionary<string, RagEvidence>()));
    }

    [Theory]
    [InlineData(false, "Which area?")]
    [InlineData(true, "")]
    public void Validate_RejectsInvalidClarifyingQuestionShape(
        bool insufficientEvidence,
        string clarifyingQuestion)
    {
        RagAnswerDraft draft = new(
            insufficientEvidence,
            [new RagClaim("Unsupported claim.", [])],
            clarifyingQuestion);

        Assert.False(_validator.Validate(
            draft,
            new Dictionary<string, RagEvidence>()).IsValid);
    }

    [Fact]
    public void Validate_RejectsOversizedClarifyingQuestion()
    {
        RagAnswerDraft draft = new(
            true,
            [],
            new string('x', CitationValidator.MaximumClarifyingQuestionCharacters + 1));

        Assert.False(_validator.Validate(
            draft,
            new Dictionary<string, RagEvidence>()).IsValid);
    }

    [Fact]
    public void Validate_AcceptsBoundedClaimFreeConversationalResponse()
    {
        RagAnswerDraft draft = new(
            false,
            [],
            ConversationalResponse: "¡Hola, Lautaro! ¿En qué puedo ayudarte?");

        RagAnswerValidationResult result = _validator.Validate(
            draft,
            new Dictionary<string, RagEvidence>());

        Assert.True(result.IsValid);
        Assert.Equal(
            draft.ConversationalResponse,
            _validator.Render(draft, new Dictionary<string, RagEvidence>()));
    }

    [Fact]
    public void Validate_RejectsConversationalResponseWithAnotherOutputShape()
    {
        RagAnswerDraft draft = new(
            true,
            [new RagClaim("Application fact.", ["e1"])],
            "Which area?",
            "Hello!");

        RagAnswerValidationResult result = _validator.Validate(draft, _evidence);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Contains("insufficient evidence", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Contains("clarifying question", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Contains("cannot contain claims", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsOversizedConversationalResponse()
    {
        RagAnswerDraft draft = new(
            false,
            [],
            ConversationalResponse: new string(
                'x',
                CitationValidator.MaximumConversationalResponseCharacters + 1));

        Assert.False(_validator.Validate(
            draft,
            new Dictionary<string, RagEvidence>()).IsValid);
    }

    [Fact]
    public void Validate_RejectsConversationalResponseThatIgnoresRequiredEvidence()
    {
        Dictionary<string, RagEvidence> evidence = new()
        {
            ["e1"] = new(
                "e1",
                "Run `maf help`.",
                "[CLI reference]",
                1,
                ["maf help"]),
        };
        RagAnswerDraft draft = new(
            false,
            [],
            ConversationalResponse: "Hello!");

        Assert.False(_validator.Validate(draft, evidence).IsValid);
    }
}
