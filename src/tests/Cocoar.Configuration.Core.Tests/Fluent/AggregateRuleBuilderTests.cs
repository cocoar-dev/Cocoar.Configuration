using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Core.Tests.Aggregate;

[Trait("Category", "Core")]
[Trait("Component", "AggregateRule")]
public class AggregateRuleBuilderTests
{
    private static readonly RulesBuilder Rule = new();

    public class SimpleConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class OtherConfig
    {
        public bool Enabled { get; set; }
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void FromFiles_ProducesAggregateConfigRule()
    {
        ConfigRule rule = Rule.For<SimpleConfig>()
            .FromFiles("base.json", "overlay.json");

        Assert.IsType<AggregateConfigRule>(rule);
        var aggregate = (AggregateConfigRule)rule;
        Assert.Equal(2, aggregate.SubRules.Count);
        Assert.Equal(typeof(SimpleConfig), aggregate.ConcreteType);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void FromFiles_SingleFile_ProducesSingleSubRule()
    {
        ConfigRule rule = Rule.For<SimpleConfig>()
            .FromFiles("base.json");

        var aggregate = (AggregateConfigRule)rule;
        Assert.Single(aggregate.SubRules);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void FromFiles_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Rule.For<SimpleConfig>().FromFiles());
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void FromFiles_Required_SetsRequiredOnAggregate()
    {
        ConfigRule rule = Rule.For<SimpleConfig>()
            .FromFiles("base.json", "overlay.json")
            .Required();

        Assert.True(rule.Options?.Required);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void FromFiles_Named_SetsNameOnAggregate()
    {
        ConfigRule rule = Rule.For<SimpleConfig>()
            .FromFiles("base.json", "overlay.json")
            .Named("MyFiles");

        Assert.Equal("MyFiles", rule.Options?.Name);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_ProducesAggregateConfigRule()
    {
        ConfigRule rule = Rule.For<SimpleConfig>()
            .Aggregate(r => [
                r.FromStaticJson("""{"Name": "base"}"""),
                r.FromStaticJson("""{"Name": "overlay"}""")
            ]);

        Assert.IsType<AggregateConfigRule>(rule);
        var aggregate = (AggregateConfigRule)rule;
        Assert.Equal(2, aggregate.SubRules.Count);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_SubRuleRequiredPreserved()
    {
        ConfigRule rule = Rule.For<SimpleConfig>()
            .Aggregate(r => [
                r.FromStaticJson("""{"Name": "base"}""").Required(),
                r.FromStaticJson("""{"Name": "overlay"}""")
            ]);

        var aggregate = (AggregateConfigRule)rule;
        Assert.True(aggregate.SubRules[0].Options?.Required);
        Assert.NotEqual(true, aggregate.SubRules[1].Options?.Required);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Rule.For<SimpleConfig>().Aggregate(r => []));
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_InnerBuilder_CannotCallAggregate()
    {
        // TypedProviderBuilder<T> does NOT have Aggregate() or FromFiles().
        // This is enforced at compile time. We verify the type is correct.
        Rule.For<SimpleConfig>().Aggregate(r =>
        {
            Assert.IsType<TypedProviderBuilder<SimpleConfig>>(r);
            Assert.IsNotType<TypedRuleBuilder<SimpleConfig>>(r);
            return [r.FromStaticJson("""{"Name": "test"}""")];
        });
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_ImplicitConversionToConfigRule()
    {
        // Verify aggregate fits naturally in ConfigRule[] collection expressions
        ConfigRule[] rules = [
            Rule.For<SimpleConfig>().FromStaticJson("""{"Name": "direct"}"""),
            Rule.For<SimpleConfig>().Aggregate(r => [
                r.FromStaticJson("""{"Name": "aggregated"}""")
            ])
        ];

        Assert.Equal(2, rules.Length);
        Assert.IsNotType<AggregateConfigRule>(rules[0]);
        Assert.IsType<AggregateConfigRule>(rules[1]);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void AggregateConfigRule_PreservesSubRuleCount()
    {
        ConfigRule aggregate = Rule.For<SimpleConfig>()
            .FromFiles("a.json", "b.json", "c.json");

        Assert.IsType<AggregateConfigRule>(aggregate);
        Assert.Equal(3, ((AggregateConfigRule)aggregate).SubRules.Count);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void AggregateConfigRule_NamedAggregate_SetsName()
    {
        ConfigRule aggregate = Rule.For<SimpleConfig>()
            .FromFiles("a.json", "b.json")
            .Named("MyGroup");

        Assert.Equal("MyGroup", aggregate.Options?.Name);
    }
}
