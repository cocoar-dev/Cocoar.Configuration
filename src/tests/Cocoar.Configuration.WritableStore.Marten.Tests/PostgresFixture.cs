using Testcontainers.PostgreSql;

namespace Cocoar.Configuration.WritableStore.Marten.Tests;

/// <summary>
/// Spins up a throwaway PostgreSQL container for the Marten backend tests and creates one database per test
/// tenant (database-per-tenant). If Docker is not reachable, <see cref="Available"/> stays false and the tests
/// skip themselves instead of failing — so the default suite stays green on machines/CI without Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string TenantA = "tenant-a";
    public const string TenantB = "tenant-b";
    private const string TenantADatabase = "tenant_a";
    private const string TenantBDatabase = "tenant_b";

    // Built inside InitializeAsync, NOT in a field initializer: PostgreSqlBuilder.Build() validates Docker
    // availability and throws DockerUnavailableException when no Docker daemon is reachable (e.g. GitHub
    // macOS runners). Constructing it inside the try lets that throw be caught so the tests skip, not fail.
    private PostgreSqlContainer? _container;

    /// <summary>True once the container is up and the tenant databases exist.</summary>
    public bool Available { get; private set; }

    /// <summary>Why the tests are skipped (Docker not available), or null when <see cref="Available"/> is true.</summary>
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithDatabase("postgres")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _container.StartAsync();

            foreach (var database in new[] { TenantADatabase, TenantBDatabase })
            {
                var result = await _container.ExecAsync(["psql", "-U", "postgres", "-c", $"CREATE DATABASE {database};"]);
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to create database '{database}': {result.Stderr}");
                }
            }

            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = $"Docker / PostgreSQL not available: {ex.Message}";
        }
    }

    /// <summary>Connection string to the default (single-tenant) database. Only valid when <see cref="Available"/>.</summary>
    public string DefaultConnectionString => _container!.GetConnectionString();

    /// <summary>Connection string to a specific tenant's database (database-per-tenant).</summary>
    public string ConnectionStringForTenantA => ConnectionStringFor(TenantADatabase);

    /// <summary>Connection string to a specific tenant's database (database-per-tenant).</summary>
    public string ConnectionStringForTenantB => ConnectionStringFor(TenantBDatabase);

    private string ConnectionStringFor(string database)
        => _container!.GetConnectionString()
            .Replace("Database=postgres", $"Database={database}", StringComparison.OrdinalIgnoreCase);

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
