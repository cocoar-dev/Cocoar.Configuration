using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Tests;

public record Address(string Street, string City, string Zip);
public record Person(string Name, int Age, Address Address);
public record Credentials(string Username, string Password, string? ApiKey = null);
public record Nested(Inner Inner);
public record Inner(Deep Deep);
public record Deep(string Value);

public class ComplexTypesTests
{
    [Fact]
    public void Secret_SimpleRecord_PlainValue()
    {
        var address = new Address("123 Main St", "Springfield", "12345");
        var secret = Secret<Address>.FromPlain(address);
        using var lease = secret.Open();
        Assert.Equal(address, lease.Value);
    }

    [Fact]
    public void Secret_NestedRecord_PlainValue()
    {
        var person = new Person(
            "John Doe",
            30,
            new Address("456 Oak Ave", "Portland", "97201")
        );
        var secret = Secret<Person>.FromPlain(person);
        using var lease = secret.Open();
        Assert.Equal(person, lease.Value);
    }

    [Fact]
    public void Secret_RecordWithNullable_AllFields()
    {
        var creds = new Credentials("admin", "secret123", "api-key-xyz");
        var secret = Secret<Credentials>.FromPlain(creds);
        using var lease = secret.Open();
        Assert.Equal(creds, lease.Value);
    }

    [Fact]
    public void Secret_RecordWithNullable_NullField()
    {
        var creds = new Credentials("admin", "secret123", null);
        var secret = Secret<Credentials>.FromPlain(creds);
        using var lease = secret.Open();
        Assert.Equal(creds, lease.Value);
        Assert.Null(lease.Value.ApiKey);
    }

    [Fact]
    public void Secret_DeeplyNested_PlainValue()
    {
        var nested = new Nested(new Inner(new Deep("deep value")));
        var secret = Secret<Nested>.FromPlain(nested);
        using var lease = secret.Open();
        Assert.Equal(nested, lease.Value);
        Assert.Equal("deep value", lease.Value.Inner.Deep.Value);
    }

    [Fact]
    public void Secret_Array_PlainValue()
    {
        var array = new[] { "one", "two", "three" };
        var secret = Secret<string[]>.FromPlain(array);
        using var lease = secret.Open();
        Assert.Equal(array, lease.Value);
    }

    [Fact]
    public void Secret_List_PlainValue()
    {
        var list = new List<int> { 1, 2, 3, 4, 5 };
        var secret = Secret<List<int>>.FromPlain(list);
        using var lease = secret.Open();
        Assert.Equal(list, lease.Value);
    }

    [Fact]
    public void Secret_Dictionary_PlainValue()
    {
        var dict = new Dictionary<string, int>
        {
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3
        };
        var secret = Secret<Dictionary<string, int>>.FromPlain(dict);
        using var lease = secret.Open();
        Assert.Equal(dict, lease.Value);
    }

    [Fact]
    public void Secret_ComplexList_PlainValue()
    {
        var addresses = new List<Address>
        {
            new("123 Main", "City1", "11111"),
            new("456 Oak", "City2", "22222")
        };
        var secret = Secret<List<Address>>.FromPlain(addresses);
        using var lease = secret.Open();
        Assert.Equal(addresses, lease.Value);
    }

    [Fact]
    public void Secret_EmptyCollection_PlainValue()
    {
        var empty = new List<string>();
        var secret = Secret<List<string>>.FromPlain(empty);
        using var lease = secret.Open();
        Assert.Empty(lease.Value);
    }
}
