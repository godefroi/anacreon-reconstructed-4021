using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

public class GameTests
{
    private static Game BuildGame(params Empire[] empires)
    {
        var game = new Game(new Core.Galaxy.Galaxy(size: 20));
        foreach (var empire in empires) {
            game.Empires.Add(empire);
        }
        return game;
    }

    [Test]
    public async Task AddGlobalNews_ScoutedAndNotExcluded_ReceivesTheNews()
    {
        var recipient = new Empire { Name = "Recipient" };
        var owner = new Empire { Name = "Owner" };
        var game = BuildGame(recipient, owner);

        var planet = new Planet { Owner = owner, Location = new Coordinate(1, 1) };
        game.Galaxy.Planets.Add(planet);
        recipient.Planets.MarkScouted(planet);

        game.AddGlobalNews([], planet, NewsType.WorldRevoltedGlobal);

        await Assert.That(recipient.News).Count().IsEqualTo(1);
        await Assert.That(recipient.News[0].Headline).IsEqualTo(NewsType.WorldRevoltedGlobal);
        await Assert.That(recipient.News[0].Subject).IsSameReferenceAs(planet);
    }

    [Test]
    public async Task AddGlobalNews_NotScouted_ReceivesNothing()
    {
        var recipient = new Empire { Name = "Recipient" };
        var owner = new Empire { Name = "Owner" };
        var game = BuildGame(recipient, owner);

        var planet = new Planet { Owner = owner, Location = new Coordinate(1, 1) };
        game.Galaxy.Planets.Add(planet);

        game.AddGlobalNews([], planet, NewsType.WorldRevoltedGlobal);

        await Assert.That(recipient.News).IsEmpty();
    }

    [Test]
    public async Task AddGlobalNews_Excluded_ReceivesNothingEvenIfScouted()
    {
        var recipient = new Empire { Name = "Recipient" };
        var owner = new Empire { Name = "Owner" };
        var game = BuildGame(recipient, owner);

        var planet = new Planet { Owner = owner, Location = new Coordinate(1, 1) };
        game.Galaxy.Planets.Add(planet);
        recipient.Planets.MarkScouted(planet);

        game.AddGlobalNews([recipient], planet, NewsType.WorldRevoltedGlobal);

        await Assert.That(recipient.News).IsEmpty();
    }
}
