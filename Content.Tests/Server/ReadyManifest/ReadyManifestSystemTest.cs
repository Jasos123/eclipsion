using System.Collections.Generic;
using Content.Server.ReadyManifest;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.Tests.Server.ReadyManifest;

[TestFixture]
[TestOf(typeof(ReadyManifestSystem))]
public sealed class ReadyManifestSystemTest
{
    [Test]
    public void ManifestCountsOnlyTheHighPriorityJob()
    {
        var priorities = new Dictionary<string, JobPriority>
        {
            ["HighFactionJob"] = JobPriority.High,
            ["WeightedFallbackInAnotherFaction"] = JobPriority.Medium,
            ["AnotherFallback"] = JobPriority.Low,
        };

        var found = ReadyManifestSystem.TryGetManifestJob(priorities, out var jobId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(jobId, Is.EqualTo(new ProtoId<JobPrototype>("HighFactionJob")));
        }
    }

    [Test]
    public void ManifestDoesNotCountAProfileWithoutAHighPriorityJob()
    {
        var priorities = new Dictionary<string, JobPriority>
        {
            ["MediumFallback"] = JobPriority.Medium,
            ["LowFallback"] = JobPriority.Low,
        };

        Assert.That(ReadyManifestSystem.TryGetManifestJob(priorities, out _), Is.False);
    }
}
