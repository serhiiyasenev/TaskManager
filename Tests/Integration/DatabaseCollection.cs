using Xunit;

namespace Tests.Integration;

/// <summary>
/// Collection definition for database integration tests.
/// This ensures tests across all classes in this collection share the same DatabaseFixture instance
/// and run sequentially, avoiding concurrency issues.
/// </summary>
[CollectionDefinition("Database collection")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
