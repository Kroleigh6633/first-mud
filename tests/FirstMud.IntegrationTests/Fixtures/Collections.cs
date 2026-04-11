namespace FirstMud.IntegrationTests.Fixtures;

[CollectionDefinition("SqlServer", DisableParallelization = true)]
[Trait("Category", "Integration")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture> { }

[CollectionDefinition("Neo4j", DisableParallelization = true)]
[Trait("Category", "Integration")]
public sealed class Neo4jCollection : ICollectionFixture<Neo4jFixture> { }

[CollectionDefinition("FullStack", DisableParallelization = true)]
[Trait("Category", "Integration")]
public sealed class FullStackCollection : ICollectionFixture<FullStackFixture> { }
