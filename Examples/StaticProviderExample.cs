/*
 * STATIC PROVIDER EXAMPLE
 *
 * Demonstrates seeding configuration with a static object and composing
 * another configuration type that depends on it during recompute.
 *
 * Pattern:
 * 1. Seed a base configuration via Rule.From.Static
 * 2. Reference that config inside a second static rule using ConfigManager
 */

using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;

public sealed class CoreDefaults
{
    public string Feature { get; set; } = "A";
    public bool Enabled { get; set; } = true;
}

public sealed class Wrapper
{
    public CoreDefaults? Inner { get; set; }
}

var rules = new []
{
    Rule.From.Static(_ => new CoreDefaults { Feature = "A", Enabled = true })
        .For<CoreDefaults>()
        .Required(),
    Rule.From.Static(cm => new Wrapper { Inner = cm.GetRequiredConfig<CoreDefaults>() })
        .For<Wrapper>()
        .Required()
};

var manager = new ConfigManager(rules).Initialize();
var wrapper = manager.GetRequiredConfig<Wrapper>();

// wrapper.Inner.Feature == "A"
