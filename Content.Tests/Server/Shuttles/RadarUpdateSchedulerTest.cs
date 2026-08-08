using System;
using System.Collections.Generic;
using Content.Server.Shuttles.Systems;
using NUnit.Framework;

namespace Content.Tests.Server.Shuttles;

[TestFixture]
[TestOf(typeof(RadarUpdateScheduler))]
public sealed class RadarUpdateSchedulerTest
{
    private static int CountUpdatesInOneSecond(float uiTps, int tickRate, int staggerSeed = 0)
    {
        var period = RadarUpdateScheduler.GetPeriod(uiTps, tickRate);
        var tick = TimeSpan.FromSeconds(1d / tickRate);

        var next = TimeSpan.Zero;
        var curTime = TimeSpan.Zero;
        var count = 0;

        // Warm up past the staggered first update before measuring the steady-state rate.
        for (var i = 0; i < tickRate; i++)
        {
            RadarUpdateScheduler.TryConsume(ref next, curTime, period, staggerSeed);
            curTime += tick;
        }

        for (var i = 0; i < tickRate; i++)
        {
            if (RadarUpdateScheduler.TryConsume(ref next, curTime, period, staggerSeed))
                count++;

            curTime += tick;
        }

        return count;
    }

    [Test]
    [TestCase(30, 10f, 10)]
    [TestCase(30, 20f, 20)]
    [TestCase(30, 12f, 12)]
    [TestCase(30, 8f, 8)]
    [TestCase(20, 15f, 15)]
    [TestCase(15, 10f, 10)]
    [TestCase(60, 10f, 10)]
    public void DeliversRequestedRate(int tickRate, float uiTps, int expected)
    {
        var actual = CountUpdatesInOneSecond(uiTps, tickRate);

        // Rates that do not divide the tick rate can be off by one update over this short window.
        Assert.That(actual, Is.EqualTo(expected).Within(1),
            $"tickrate {tickRate} at {uiTps} tps produced {actual} updates/sec, expected ~{expected}");
    }

    [Test]
    public void RateAboveTickRateIsClampedToEveryTick()
    {
        Assert.That(CountUpdatesInOneSecond(120f, 30), Is.EqualTo(30));
    }

    [Test]
    [TestCase(0f)]
    [TestCase(-5f)]
    public void NonPositiveRateDisablesLimiting(float uiTps)
    {
        Assert.That(RadarUpdateScheduler.GetPeriod(uiTps, 30), Is.EqualTo(TimeSpan.Zero));

        var next = TimeSpan.Zero;
        Assert.That(RadarUpdateScheduler.TryConsume(ref next, TimeSpan.FromSeconds(1), TimeSpan.Zero, 0), Is.True);
    }

    [Test]
    public void TickRateChangeKeepsRequestedRate()
    {
        // A mid-round tick-rate change should not alter the configured update rate.
        Assert.That(CountUpdatesInOneSecond(10f, 30), Is.EqualTo(10).Within(1));
        Assert.That(CountUpdatesInOneSecond(10f, 15), Is.EqualTo(10).Within(1));
    }

    [Test]
    public void StaggerSpreadsConsolesAcrossThePeriod()
    {
        var period = RadarUpdateScheduler.GetPeriod(10f, 30);
        var firstDeadlines = new HashSet<TimeSpan>();

        for (var seed = 0; seed < RadarUpdateScheduler.StaggerBuckets; seed++)
        {
            var next = TimeSpan.Zero;
            RadarUpdateScheduler.TryConsume(ref next, TimeSpan.Zero, period, seed);
            firstDeadlines.Add(next);
        }

        // Each bucket should produce a distinct deadline.
        Assert.That(firstDeadlines, Has.Count.EqualTo(RadarUpdateScheduler.StaggerBuckets));
    }

    [Test]
    public void StallDoesNotQueueCatchUpBurst()
    {
        var period = RadarUpdateScheduler.GetPeriod(10f, 30);
        var next = TimeSpan.FromSeconds(0.1);

        // Simulate a five-second stall, or fifty missed updates.
        var curTime = TimeSpan.FromSeconds(5);

        Assert.That(RadarUpdateScheduler.TryConsume(ref next, curTime, period, 0), Is.True);

        // The next deadline should be in the future, not fifty periods behind.
        Assert.That(next, Is.EqualTo(curTime + period));
    }
}
