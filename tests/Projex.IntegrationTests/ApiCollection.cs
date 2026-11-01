namespace Projex.IntegrationTests;

/// <summary>Serializes the test classes that boot the real API (they configure it through process-wide environment variables).</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection { public const string Name = "Api"; }
