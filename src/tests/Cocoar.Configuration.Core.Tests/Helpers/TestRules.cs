using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Core.Tests.Helpers;

/// <summary>
/// Test helper to create configuration rules without using the fluent RulesBuilder API.
/// This allows tests to build individual rules and store them in variables.
/// </summary>
public static class TestRules
{
    private static readonly RulesBuilder Builder = new();

    public static ConfigRule StaticJson<T>(string json, bool required = false)
    {
        var rule = Builder.StaticJson(json).For<T>();
        return required ? rule.Required() : rule;
    }

    public static ConfigRule Observable<T>(System.IObservable<T> observable, bool required = false)
    {
        var rule = Builder.Observable(observable).For<T>();
        return required ? rule.Required() : rule;
    }

    public static ConfigRule ObservableString<T>(System.IObservable<string> jsonObservable, bool required = false)
    {
        var rule = Builder.Observable(jsonObservable).For<T>();
        return required ? rule.Required() : rule;
    }

    public static ConfigRule File<T>(string filePath, string? selectPath = null, bool required = false)
    {
        var builder = Builder.File(filePath);
        if (selectPath != null)
        {
            builder = builder.Select(selectPath);
        }
        var rule = builder.For<T>();
        return required ? rule.Required() : rule;
    }

    public static ConfigRule Environment<T>(string? prefix = null, bool required = false)
    {
        var rule = Builder.Environment(prefix).For<T>();
        return required ? rule.Required() : rule;
    }
}
