using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="Core.Entities.MessageLifecycle"/>: SendMessage (including the "time capsule"
/// no-interception guard and a forced interception roll), MarkRead's multi-recipient gate, and
/// DeleteReadMessages.
/// </summary>
public class MessageLifecycleTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    [Test]
    public async Task SendMessage_SingleRecipient_AddsMessageAndNewsToRecipientOnly()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);
        var sender = NewEmpire("Sender");
        var recipient = NewEmpire("Recipient");
        game.Empires.Add(sender);
        game.Empires.Add(recipient);

        MessageLifecycle.SendMessage(game, sender, new HashSet<Empire> { recipient }, ["Hello there."], new FixedRandom(99));

        await Assert.That(game.Messages).Count().IsEqualTo(1);
        var message = game.Messages[0];
        await Assert.That(message.Sender).IsEqualTo(sender);
        await Assert.That(message.Recipients).Contains(recipient);
        await Assert.That(message.Read).IsFalse();
        await Assert.That(message.Intercepted).IsFalse();
        await Assert.That(message.Lines).IsEquivalentTo(new[] { "Hello there." });
        await Assert.That(recipient.News).Contains(n => n.Headline == NewsType.MessageReceived && n.OtherEmpire == sender);
        await Assert.That(sender.News).IsEmpty();
    }

    [Test]
    public async Task SendMessage_ToSelf_IsATimeCapsuleWithNoInterception()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);
        var sender = NewEmpire("Sender");
        sender.Capital = new Planet { Location = new Coordinate(0, 0), Owner = sender };

        // A third empire with a planet right on top of the sender's own capital -- if interception were
        // attempted at all, distance 0 would make the chance certain. It must never be rolled.
        var bystander = NewEmpire("Bystander");
        var bystanderPlanet = new Planet { Location = new Coordinate(0, 0), Owner = bystander };
        galaxy.Planets.Add(bystanderPlanet);
        game.Empires.Add(sender);
        game.Empires.Add(bystander);

        MessageLifecycle.SendMessage(game, sender, new HashSet<Empire> { sender }, ["Note to self."], new FixedRandom(0));

        await Assert.That(game.Messages).Count().IsEqualTo(1);
        await Assert.That(sender.News).Contains(n => n.Headline == NewsType.MessageReceived && n.OtherEmpire == sender);
        await Assert.That(bystander.News).IsEmpty();
    }

    [Test]
    public async Task SendMessage_ForcedIntercept_AddsGarbledCopyAndNewsToInterceptor()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);
        var sender = NewEmpire("Sender");
        var recipient = NewEmpire("Recipient");
        recipient.Capital = new Planet { Location = new Coordinate(10, 10), Owner = recipient };

        var interceptor = NewEmpire("Interceptor");
        var interceptorPlanet = new Planet { Location = new Coordinate(11, 10), Owner = interceptor }; // distance 1 -> chance = 150, always rolls true.
        galaxy.Planets.Add(interceptorPlanet);
        game.Empires.Add(sender);
        game.Empires.Add(recipient);
        game.Empires.Add(interceptor);

        // FixedRandom(0): Rnd(1,100) = 1, always <= the (clamped or not) chance -- guarantees the roll succeeds.
        MessageLifecycle.SendMessage(game, sender, new HashSet<Empire> { recipient }, ["Header line.", "A rather longer body line to garble."], new FixedRandom(0));

        await Assert.That(game.Messages).Count().IsEqualTo(2);
        var intercepted = game.Messages.Single(m => m.Intercepted);
        await Assert.That(intercepted.Sender).IsEqualTo(sender);
        await Assert.That(intercepted.Recipients).IsEquivalentTo(new HashSet<Empire> { interceptor });
        await Assert.That(intercepted.Lines[0]).IsEqualTo("Header line."); // first line always survives verbatim.
        await Assert.That(interceptor.News).Contains(n => n.Headline == NewsType.MessageIntercepted && n.OtherEmpire == sender && n.Subject == interceptorPlanet);
    }

    [Test]
    public async Task MarkRead_SingleRecipient_FlipsReadImmediately()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);
        var sender = NewEmpire("Sender");
        var recipient = NewEmpire("Recipient");

        MessageLifecycle.SendMessage(game, sender, new HashSet<Empire> { recipient }, ["Hi."], new FixedRandom(99));
        MessageLifecycle.MarkRead(game, game.Messages[0], recipient);

        await Assert.That(game.Messages[0].Read).IsTrue();
    }

    [Test]
    public async Task MarkRead_MultipleRecipients_StaysUnreadUntilEveryoneHasRead()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);
        var sender = NewEmpire("Sender");
        var a = NewEmpire("A");
        var b = NewEmpire("B");
        game.Empires.Add(sender);
        game.Empires.Add(a);
        game.Empires.Add(b);

        MessageLifecycle.SendMessage(game, sender, new HashSet<Empire> { a, b }, ["Hi both."], new FixedRandom(99));

        MessageLifecycle.MarkRead(game, game.Messages[0], a);
        await Assert.That(game.Messages[0].Read).IsFalse();

        // A year turns over before B has read it -- must survive, or B never sees it (the bug the
        // Read-flips-on-any-read version of this had).
        MessageLifecycle.DeleteReadMessages(game);
        await Assert.That(game.Messages).Count().IsEqualTo(1);

        MessageLifecycle.MarkRead(game, game.Messages[0], b);
        await Assert.That(game.Messages[0].Read).IsTrue();

        MessageLifecycle.DeleteReadMessages(game);
        await Assert.That(game.Messages).IsEmpty();
    }

    [Test]
    public async Task DeleteReadMessages_RemovesOnlyReadOnes()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);
        var sender = NewEmpire("Sender");
        var recipient = NewEmpire("Recipient");

        MessageLifecycle.SendMessage(game, sender, new HashSet<Empire> { recipient }, ["Read me."], new FixedRandom(99));
        MessageLifecycle.SendMessage(game, sender, new HashSet<Empire> { recipient }, ["Leave me."], new FixedRandom(99));
        MessageLifecycle.MarkRead(game, game.Messages[0], recipient);

        MessageLifecycle.DeleteReadMessages(game);

        await Assert.That(game.Messages).Count().IsEqualTo(1);
        await Assert.That(game.Messages[0].Lines).IsEquivalentTo(new[] { "Leave me." });
    }
}
