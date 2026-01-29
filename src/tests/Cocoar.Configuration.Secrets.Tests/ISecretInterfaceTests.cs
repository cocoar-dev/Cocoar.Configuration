using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Tests;

/// <summary>
/// Tests verifying that Secret&lt;T&gt; correctly implements ISecret&lt;T&gt;
/// and can be used polymorphically through the interface.
/// </summary>
public class ISecretInterfaceTests
{
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void Secret_Implements_ISecret_Interface()
    {
        // Verify Secret<T> can be assigned to ISecret<T>
        ISecret<string> secret = Secret<string>.FromPlain("test");
        Assert.NotNull(secret);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void ISecret_Open_Returns_SecretLease()
    {
        ISecret<string> secret = Secret<string>.FromPlain("test");
        using var lease = secret.Open();
        Assert.Equal("test", lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void ISecret_Can_Be_Used_In_Method_Parameter()
    {
        // Simulates library author accepting ISecret<T>
        static string ProcessSecret(ISecret<string> secret)
        {
            using var lease = secret.Open();
            return lease.Value.ToUpperInvariant();
        }

        Secret<string> concreteSecret = Secret<string>.FromPlain("hello");
        var result = ProcessSecret(concreteSecret);
        Assert.Equal("HELLO", result);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void ISecret_Dispose_Works_Through_Interface()
    {
        ISecret<string> secret = Secret<string>.FromPlain("test");
        secret.Dispose();
        // Should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => secret.Open());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void SecretLease_From_Abstractions_Package_Works()
    {
        // Verify SecretLease<T> type from abstractions works correctly
        var lease = new SecretLease<string>("test-value", null);
        Assert.Equal("test-value", lease.Value);
        lease.Dispose(); // Should not throw
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void SecretLease_Dispose_Invokes_Cleanup_Action()
    {
        // Verify the cleanup action is invoked on dispose
        var cleanupCalled = false;
        var lease = new SecretLease<string>("test-value", () => cleanupCalled = true);

        Assert.False(cleanupCalled);
        lease.Dispose();
        Assert.True(cleanupCalled);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void ISecret_Works_With_Complex_Types()
    {
        // Verify ISecret works with complex types, not just primitives
        var testData = new TestData { Name = "Test", Value = 42 };
        ISecret<TestData> secret = Secret<TestData>.FromPlain(testData);

        using var lease = secret.Open();
        Assert.NotNull(lease.Value);
        Assert.Equal("Test", lease.Value.Name);
        Assert.Equal(42, lease.Value.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void ISecret_Works_With_Numeric_Types()
    {
        ISecret<int> secret = Secret<int>.FromPlain(12345);
        using var lease = secret.Open();
        Assert.Equal(12345, lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void ISecret_Interface_Allows_Generic_Processing()
    {
        // Demonstrates generic code that works with any ISecret<T>
        static T ExtractValue<T>(ISecret<T> secret)
        {
            using var lease = secret.Open();
            return lease.Value;
        }

        var stringSecret = Secret<string>.FromPlain("hello");
        var intSecret = Secret<int>.FromPlain(42);

        Assert.Equal("hello", ExtractValue(stringSecret));
        Assert.Equal(42, ExtractValue(intSecret));
    }

    private record TestData
    {
        public string? Name { get; init; }
        public int Value { get; init; }
    }
}
