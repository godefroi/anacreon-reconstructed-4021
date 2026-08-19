namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// One empire's knowledge of one kind of entity. Scouted is always a subset of Known — verified
/// against PRIMINTR.PAS's ScoutObject, which sets both together; INTRFACE.PAS's DetermineIfScouted
/// confirms an entity can be Known without being Scouted (the weaker tier), never the reverse.
/// Mutation only through MarkKnown/MarkScouted so that invariant can't be broken from outside.
/// </summary>
public sealed class EntityVisibility<T> where T : notnull
{
    private readonly HashSet<T> _known = [];
    private readonly HashSet<T> _scouted = [];

    public IReadOnlySet<T> Known => _known;
    public IReadOnlySet<T> Scouted => _scouted;

    public void MarkKnown(T entity) => _known.Add(entity);

    public void MarkScouted(T entity)
    {
        _known.Add(entity);
        _scouted.Add(entity);
    }

    public void ClearScouted()
    {
        _scouted.Clear();
    }

    public void Clear()
    {
        _known.Clear();
        _scouted.Clear();
    }
}
