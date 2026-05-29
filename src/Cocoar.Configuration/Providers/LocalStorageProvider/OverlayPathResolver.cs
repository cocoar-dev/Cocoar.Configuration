using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Translates a member-access selector (e.g. <c>x =&gt; x.Smtp.Port</c>) into a dotted JSON key path,
/// resolving each segment's JSON property name (honoring <see cref="JsonPropertyNameAttribute"/>) and
/// rejecting unsupported selectors (indexers, method calls, casts) and secret-typed members.
/// </summary>
internal static class OverlayPathResolver
{
    internal static string ResolveKeyPath<T, TValue>(Expression<Func<T, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var body = Unwrap(selector.Body);

        var members = new List<MemberInfo>();
        var current = body;
        while (current is MemberExpression memberExpression)
        {
            if (memberExpression.Member is not PropertyInfo and not FieldInfo)
            {
                throw Unsupported(selector);
            }

            members.Add(memberExpression.Member);
            current = Unwrap(memberExpression.Expression);
        }

        if (current is not ParameterExpression || members.Count == 0)
        {
            throw Unsupported(selector);
        }

        members.Reverse(); // root → leaf

        var segments = new string[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var memberType = GetMemberType(member);

            if (IsSecretType(memberType))
            {
                throw new NotSupportedException(
                    $"Member '{member.Name}' is a secret and cannot be overridden via LocalStorage. " +
                    "Manage secrets via the Secrets CLI/provider.");
            }

            segments[i] = ResolveJsonName(member);
        }

        return string.Join('.', segments);
    }

    private static Expression? Unwrap(Expression? expression)
        => expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
            ? unary.Operand
            : expression;

    private static string ResolveJsonName(MemberInfo member)
        => member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? member.Name;

    private static Type GetMemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => throw new NotSupportedException($"Unsupported member kind: {member.GetType().Name}."),
    };

    private static bool IsSecretType(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(Secret<>) || definition == typeof(ISecret<>);
    }

    private static NotSupportedException Unsupported<T, TValue>(Expression<Func<T, TValue>> selector)
        => new(
            $"Selector '{selector}' is not supported. Only simple member-access chains are allowed " +
            "(no method calls, indexers, or array element access). " +
            "Use the raw Overlay surface for dynamic key paths.");
}
