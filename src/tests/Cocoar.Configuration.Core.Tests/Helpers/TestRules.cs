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

    public static ConfigRule StaticJson<T>(string json, bool required = false) where T : class
    {
        var rule = Builder.For<T>().FromStaticJson(json);
        return required ? rule.Required() : rule;
    }

    public static ConfigRule Observable<T>(System.IObservable<T> observable, bool required = false) where T : class
    {
        var rule = Builder.For<T>().FromObservable(observable);
        return required ? rule.Required() : rule;
    }

    public static ConfigRule ObservableString<T>(System.IObservable<string> jsonObservable, bool required = false) where T : class
    {
        var rule = Builder.For<T>().FromObservable(jsonObservable);
        return required ? rule.Required() : rule;
    }

    public static ConfigRule File<T>(string filePath, string? selectPath = null, bool required = false) where T : class
    {
        var builder = Builder.For<T>().FromFile(filePath);
        if (selectPath != null)
        {
            builder = builder.Select(selectPath);
        }
        var rule = builder;
        return required ? rule.Required() : rule;
    }

    public static ConfigRule Environment<T>(string? prefix = null, bool required = false) where T : class
    {
        var rule = Builder.For<T>().FromEnvironment(prefix);
        return required ? rule.Required() : rule;
    }
}



