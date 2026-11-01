using Projex.Scheduling.Engine.Validation;

namespace Projex.Domain.Tests;

public class RelationshipCycleDetectorTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    [Fact] public void Self_link_is_a_cycle() => Assert.True(RelationshipCycleDetector.WouldCreateCycle([], A, A));
    [Fact] public void Reverse_of_existing_link_is_a_cycle() => Assert.True(RelationshipCycleDetector.WouldCreateCycle([(A, B)], B, A));
    [Fact] public void Closing_a_longer_chain_is_a_cycle() => Assert.True(RelationshipCycleDetector.WouldCreateCycle([(A, B), (B, C)], C, A));
    [Fact] public void Parallel_paths_are_not_a_cycle() => Assert.False(RelationshipCycleDetector.WouldCreateCycle([(A, B), (A, C), (B, D)], C, D));
    [Fact] public void Unrelated_activities_are_not_a_cycle() => Assert.False(RelationshipCycleDetector.WouldCreateCycle([(A, B)], C, D));
    [Fact] public void Duplicate_forward_link_is_not_reported_as_a_cycle() => Assert.False(RelationshipCycleDetector.WouldCreateCycle([(A, B)], A, B));
}
