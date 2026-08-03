using MafPlayground.AI.Agents.BasicRagAgent;

namespace MafPlayground.Tests.AI.Agents.BasicRagAgent;

public sealed class CitationValidatorTests
{
    private readonly CitationValidator _validator = new();

    [Fact]
    public void IsValid_AcceptsOnlyRetrievedCitation()
    {
        HashSet<string> allowed = ["[Help, page 4, source: guides/help.pdf]"];

        Assert.True(_validator.IsValid("Reset it as described. [Help, page 4, source: guides/help.pdf]", allowed));
        Assert.False(_validator.IsValid("Reset it. [Other, page 1, source: other.pdf]", allowed));
        Assert.False(_validator.IsValid("Reset it.", allowed));
    }

    [Fact]
    public void IsValid_RequiresSafeAnswerWhenNoEvidence() =>
        Assert.True(_validator.IsValid(CitationValidator.NoEvidenceAnswer, new HashSet<string>()));
}
