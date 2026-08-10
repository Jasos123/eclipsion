using Content.Shared._EE.Contractors.Components;
using Content.Shared._EE.Contractors.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._EE;

[TestFixture]
[TestOf(typeof(PassportComponent))]
public sealed class PassportTest
{
    [Test]
    public async Task NoOpSaveDoesNotMarkPassportAsTampered()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            entMan.System<SharedPassportSystem>();
            var passport = entMan.SpawnEntity("NCWLPassport", map.GridCoords);
            var actor = entMan.SpawnEntity(null, map.GridCoords);
            var component = entMan.GetComponent<PassportComponent>(passport);

            component.FullName = "Test Person";
            component.Age = 30;
            component.Species = "Human";
            component.Sex = "Female";
            component.HeightCm = 170;
            component.SkinColor = "#112233";
            component.EyeColor = "#445566";
            component.Nationality = "Test Nation";
            component.Religion = "None";
            component.PassportId = "ABCDE-FGHIJ-KLMNO";
            component.IssueYear = 2450;
            component.ExpirationYear = 2455;

            var noOpSave = CreateSaveMessage(component, actor);
            entMan.EventBus.RaiseLocalEvent(passport, noOpSave);
            Assert.That(component.Tampered, Is.False);

            var changedSave = new PassportSaveMessage(
                "Changed Person",
                component.Age,
                component.Species,
                component.Sex,
                component.HeightCm,
                component.SkinColor,
                component.EyeColor,
                component.Nationality,
                component.Religion,
                component.PassportId,
                component.IssueYear,
                component.ExpirationYear)
            {
                Actor = actor,
            };
            entMan.EventBus.RaiseLocalEvent(passport, changedSave);
            Assert.That(component.Tampered, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    private static PassportSaveMessage CreateSaveMessage(PassportComponent component, EntityUid actor)
    {
        return new PassportSaveMessage(
            component.FullName,
            component.Age,
            component.Species,
            component.Sex,
            component.HeightCm,
            component.SkinColor,
            component.EyeColor,
            component.Nationality,
            component.Religion,
            component.PassportId,
            component.IssueYear,
            component.ExpirationYear)
        {
            Actor = actor,
        };
    }
}
