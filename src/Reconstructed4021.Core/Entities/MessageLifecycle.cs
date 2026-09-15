using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Empire menu > Send Message / Read Messages (MESS.PAS, DESIGN.PAS's SendMessageCommand/
/// ReadMessageCommand). Kept separate from <see cref="Turns.AnnualTickHandler"/> the same way
/// <see cref="ConstructionLifecycle"/> is: player-command mutators, not annual-tick steps --
/// <see cref="DeleteReadMessages"/> is the one exception, called from the tick itself (UPDATE.PAS:1485,
/// the very end of UpdateUniverse -- once per game year, after every empire's own UpdateEmpire has run,
/// not per-empire-turn like news erasure is).
/// </summary>
public static class MessageLifecycle
{
    /// <summary>
    /// SendMessage (MESS.PAS:189-240). Appends the real message, news-notifies every actual recipient,
    /// then rolls interception against every OTHER empire's own planets -- skipped entirely for a
    /// "time capsule" (Recipients is just the sender themselves, matching ReadMessageCommand's own
    /// "Time capsule from the past" case), matching Pascal's own <c>Empires&lt;&gt;[Emp]</c> guard.
    /// </summary>
    public static void SendMessage(Game game, Empire sender, IReadOnlySet<Empire> recipients, IReadOnlyList<string> lines, Random random)
    {
        // Snapshot the set -- a caller's own HashSet could keep changing after this call returns.
        var recipientSet = recipients.ToHashSet();
        game.Messages.Add(new Message(sender, recipientSet, Read: false, Intercepted: false, lines));

        foreach (var recipient in recipientSet)
        {
            recipient.AddNews(NewsType.MessageReceived, otherEmpire: sender);
        }

        var isTimeCapsule = recipientSet.Count == 1 && recipientSet.Contains(sender);
        if (isTimeCapsule)
        {
            return;
        }

        // MESS.PAS:211-213: TargetEmp ends up being whichever recipient real Pascal's own Empire1..
        // Empire8 loop enumerates LAST -- interception distance is measured from THAT recipient's
        // capital alone, even when the message goes to several empires at once (every recipient still
        // gets the message; only the interception odds skew toward whichever one is enumerated last).
        // This port has no surviving Empire1..Empire8 slot ordinal to reproduce that with (Game.Empires
        // is populated in scenario-encounter order, and no slot number survives a save/load round trip
        // -- see ScenarioLoader's own _empireBySlot, which is load-time-only), so this picks whichever
        // recipient Game.Empires happens to enumerate first instead: a deliberate, documented departure
        // from an already-arbitrary-feeling Pascal quirk, not an attempt to reproduce it exactly.
        var targetCapital = game.Empires.FirstOrDefault(recipientSet.Contains)?.Capital;
        if (targetCapital is null)
        {
            return;
        }

        var intercepts = 0;
        foreach (var candidate in game.Empires)
        {
            if (ReferenceEquals(candidate, sender) || recipientSet.Contains(candidate) || intercepts > 5)
            {
                continue;
            }

            foreach (var planet in game.Galaxy.Planets.Where(p => ReferenceEquals(p.Owner, candidate)))
            {
                var distance = targetCapital.Location.DistanceTo(planet.Location);
                var chance = PascalRound(1.0 / (distance * distance) * 150);
                if (Rnd(random, 1, 100) > chance)
                {
                    continue;
                }

                game.Messages.Add(new Message(sender, new HashSet<Empire> { candidate }, Read: false, Intercepted: true, GarbleLines(lines, random)));
                candidate.AddNews(NewsType.MessageIntercepted, subject: planet, otherEmpire: sender);
                intercepts++;
            }
        }
    }

    /// <summary>
    /// InterceptMessage's own garbling (MESS.PAS:141-187): the first line survives verbatim (it's the
    /// "TO:"/"FROM:" header, not the message body proper -- SendMessageCommand's own Edit.Txt always
    /// opens with one), every later line over 5 characters gets 0-7 random dot-out runs of up to 10
    /// characters each, at random start positions.
    /// </summary>
    private static List<string> GarbleLines(IReadOnlyList<string> lines, Random random)
    {
        var garbled = new List<string>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (i == 0 || line.Length <= 5)
            {
                garbled.Add(line);
                continue;
            }

            var chars = line.ToCharArray();
            var passes = Rnd(random, 0, 7);
            for (var pass = 0; pass < passes; pass++)
            {
                // MESS.PAS:163-168, kept 1-based throughout to match its own StartGarb/MaxLen exactly.
                var startGarb = Rnd(random, 1, chars.Length - 5);
                var maxLen = Math.Min(1 + chars.Length - startGarb, 10);
                var runEnd = startGarb + Rnd(random, 1, maxLen);
                for (var j = startGarb; j <= runEnd && j <= chars.Length; j++)
                {
                    chars[j - 1] = '.';
                }
            }

            garbled.Add(new string(chars));
        }

        return garbled;
    }

    /// <summary>
    /// SetMessageRead (MESS.PAS:242-250): <paramref name="reader"/> joins <see cref="Message.ReadBy"/>,
    /// and <see cref="Message.Read"/> only flips true once every one of <see cref="Message.Recipients"/>
    /// has read it -- a single-recipient message (the overwhelming common case) flips on the first
    /// read, same as Pascal; a multi-recipient one stays visible to whoever hasn't read it yet even
    /// after another recipient has, so <see cref="DeleteReadMessages"/> can't remove it out from under
    /// them.
    /// </summary>
    public static void MarkRead(Game game, Message message, Empire reader)
    {
        message.ReadBy.Add(reader);
        if (message.Read || !message.Recipients.All(message.ReadBy.Contains))
        {
            return;
        }

        var index = game.Messages.FindIndex(m => ReferenceEquals(m, message));
        if (index >= 0)
        {
            game.Messages[index] = message with { Read = true };
        }
    }

    /// <summary>DeleteReadMessages (MESS.PAS:93-109), called once per game year (UPDATE.PAS:1485).</summary>
    public static void DeleteReadMessages(Game game) => game.Messages.RemoveAll(m => m.Read);
}
