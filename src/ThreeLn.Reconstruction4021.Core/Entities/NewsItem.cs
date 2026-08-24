using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

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
/// <see cref="TechCatalog.TechGrantIdentity"/>.
/// </summary>
public sealed record NewsItem(
    NewsType Headline,
    ISectorObject? Subject = null,
    Coordinate? Position = null,
    Empire? OtherEmpire = null,
    TechCatalog.TechGrantIdentity? TechGrant = null,
    int Parm1 = 0,
    int Parm2 = 0,
    int Parm3 = 0);
