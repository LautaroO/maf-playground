using MafPlayground.Retrieval;

namespace MafPlayground.Tests.Retrieval;

public sealed class KnowledgeMetadataTests
{
    [Fact]
    public void Create_NormalizesKeysAndComparesByValue()
    {
        KnowledgeMetadata first = KnowledgeMetadata.Create(new Dictionary<string, string>
        {
            [" Audience "] = " customer ",
            ["Product"] = "support",
        });
        KnowledgeMetadata second = KnowledgeMetadata.Create(new Dictionary<string, string>
        {
            ["product"] = "support",
            ["audience"] = "customer",
        });

        Assert.Equal("customer", first.Values["audience"]);
        Assert.Equal(second, first);
        Assert.Equal(second.GetHashCode(), first.GetHashCode());
    }

    [Fact]
    public void Create_RejectsDuplicateNormalizedKeys()
    {
        Assert.Throws<ArgumentException>(() => KnowledgeMetadata.Create(
        [
            new("Audience", "customer"),
            new("audience", "internal"),
        ]));
    }
}
