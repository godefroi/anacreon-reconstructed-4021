using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;

namespace Reconstructed4021.Tests;

public class FleetOrderHelpTests
{
    [Test]
    public async Task EveryHelpCommand_IsARealCompilerToken()
    {
        var owner = new Empire { Name = "Owner" };
        var game = new Game(new Galaxy(size: 20));
        game.Empires.Add(owner);

        foreach (var command in FleetOrderHelp.Commands) {
            // DEST/TRAN need valid arguments to compile; anything but "Unknown command" proves the token is recognised.
            var result = FleetOrderCompiler.Compile(game, owner, [command.Token]);
            await Assert.That(result.ErrorMessage).IsNotEqualTo("Unknown command in line");
        }
    }

    [Test]
    [Arguments("dest 1,2", "DEST")]
    [Arguments("  DESTINATION x", "DEST")]
    [Arguments("join over", "JOIN")]
    public async Task For_MatchesOnFirstFourLettersCaseInsensitively(string line, string token)
    {
        await Assert.That(FleetOrderHelp.For(line)?.Token).IsEqualTo(token);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("DE")]
    [Arguments("BOGUS")]
    public async Task For_BlankOrUnrecognised_IsNull(string line)
    {
        await Assert.That(FleetOrderHelp.For(line)).IsNull();
    }
}
