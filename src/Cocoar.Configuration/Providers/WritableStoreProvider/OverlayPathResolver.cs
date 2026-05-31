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
    internal static string ResolveKeyPath<T, TValue>(Expression<Func<T, TValue>> selector, bool allowSecretMembers = false)
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

            if (!allowSecretMembers && IsSecretType(memberType))
            {
                throw new NotSupportedException(
                    $"Member '{member.Name}' is a secret and cannot be set as plaintext via WritableStore. " +
                    "Use SetSecretAsync with a pre-encrypted envelope, or manage secrets via the Secrets CLI/provider.");
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

    /// <summary>
    /// True if <paramref name="type"/> is a secret, or contains a secret anywhere in its object graph
    /// (nested property/field, collection element, array element). Used to reject plaintext writes of
    /// objects that carry secrets — those must be set per-leaf via SetSecretAsync with an encrypted envelope.
    /// </summary>
    internal static bool ContainsSecret(Type type) => ContainsSecret(type, new HashSet<Type>());

    private static bool ContainsSecret(Type? type, HashSet<Type> visited)
    {
        if (type is null || !visited.Add(type))
        {
            return false;
        }

        if (IsSecretType(type))
        {
            return true;
        }

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return ContainsSecret(underlying, visited);
        }

        if (type.IsArray && ContainsSecret(type.GetElementType(), visited))
        {
            return true;
        }

        // Generic arguments cover collections/dictionaries (List<Secret<T>>, Dictionary<string, Secret<T>>, …).
        foreach (var arg in type.GetGenericArguments())
        {
            if (ContainsSecret(arg, visited))
            {
                return true;
            }
        }

        // Don't walk the members of BCL/primitive types — their generic args (handled above) are enough.
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
        {
            return false;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length == 0 && ContainsSecret(property.PropertyType, visited))
            {
                return true;
            }
        }

        foreach (var field in type.GetFields(flags))
        {
            if (ContainsSecret(field.FieldType, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static NotSupportedException Unsupported<T, TValue>(Expression<Func<T, TValue>> selector)
        => new(
            $"Selector '{selector}' is not supported. Only simple member-access chains are allowed " +
            "(no method calls, indexers, or array element access). " +
            "Use the raw Overlay surface for dynamic key paths.");
}
