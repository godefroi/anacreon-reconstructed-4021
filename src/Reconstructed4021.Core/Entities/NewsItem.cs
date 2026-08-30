using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// NewsRecord (NEWS.PAS:113-122), minus the intrusive linked-list <c>Next</c> pointer (a plain
/// <c>List&lt;NewsItem&gt;</c> on <see cref="Empire.News"/> replaces it) and Pascal's <c>Loc.XY</c>/
/// <c>Loc.ID</c> union, which splits here into two real nullable fields typed via
/// <see cref="ISectorObject"/> rather than an untyped reference — see <c>Subject</c>/<c>Position</c>
/// below. <c>OtherEmpire</c> exists for the same reason: Pascal packed a second empire into
/// <c>Parm1..3</c> via <c>Ord(Emp)</c> because those were its only generic slots (a recurring
/// <c>(Loc,Emp)</c> shape across most combat/interaction headlines); this port has a real
/// <see cref="Entities.Empire"/> reference to use instead. <c>TechGrant</c> exists for the same
/// reason, scoped to <c>NCapTech</c>: Pascal passed <c>Ord(NewTech)</c>, a flat ordinal this port has
/// no equivalent for since <see cref="TechCatalog"/> splits it into four typed enums — see
/// <see cref="TechCatalog.TechGrantIdentity"/>. <c>Defender</c> exists for the same reason as
/// <c>OtherEmpire</c>, one level further: the four Global combat headlines (<c>GLBDest</c>/
/// <c>GLBConq</c>/<c>GLBCapConq</c>/<c>GLBLAMStrk</c>) are <c>(Loc,Attacker,Defender)</c>-shaped, not
/// <c>(Loc,Emp)</c> — Pascal packs both empires into <c>Parm1</c>/<c>Parm2</c> (<c>Ord(Emp),
/// Ord(EnemyEmp)</c>) for exactly these four, the one place a single generic empire slot wasn't
/// enough (ATTACK.PAS's ResolveAttack). <c>OtherEmpire</c> holds the attacker for
/// these; <c>Defender</c> is the second, added rather than reused because every other headline's
/// single <c>OtherEmpire</c> already means "the empire this news item is about," a different role.
/// </summary>
public sealed record NewsItem(
    NewsType Headline,
    ISectorObject? Subject = null,
    Coordinate? Position = null,
    Empire? OtherEmpire = null,
    TechCatalog.TechGrantIdentity? TechGrant = null,
    int Parm1 = 0,
    int Parm2 = 0,
    int Parm3 = 0,
    Empire? Defender = null);
