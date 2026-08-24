using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.NewGame;

/// <summary>
/// Empire creation (PRIMINTR.PAS:982-1016's CreateEmpire, wrapped by NEWGAME.PAS:1186-1259's
/// CreatePlayerEmpire/CreateNPEmpire — the starting-tech-set computation those two add on top of the
/// otherwise-identical CreateEmpire call). No randomness involved here: a scenario's player-vs-NPE
/// sex coin flip (Pascal's Boolean(Rnd(0,1)) for NPE, NEWGAME.PAS:1255) is the caller's concern, not
/// this factory's — pass whatever <paramref name="isEmpress"/> the caller already decided.
/// </summary>
public static class EmpireFactory
{
    /// <param name="extraTechs">
    /// Scenario-specified techs beyond the starting level's own set (NEWGAME.PAS's
    /// TechnologyTypes(NextInteger(SF)) list, e.g. TechCatalog.Grant(ShipType.HunterKiller)) —
    /// deliberately typed against this port's own enums, not any file format's raw ordinal; a
    /// scenario-file parser decodes into these, not the other way around.
    /// </param>
    public static Empire CreateEmpire(
        string name, string? password, bool isEmpress, TechLevel techLevel,
        int restlessness, bool centralModifier, int foundingYear,
        params Action<UnlockedTechnology>[] extraTechs)
    {
        var empire = new Empire {
            Name = name,
            Password = password,
            IsEmpress = isEmpress,
            TechnologyLevel = techLevel,
            RevolutionFactor = restlessness,
            FoundingYear = foundingYear,
            LosesIfCapitalConquered = centralModifier,
        };

        SeedTechnology(empire.Technology, techLevel, extraTechs);
        SeedDefenseSettings(empire.DefenseSettings);

        return empire;
    }

    /// <summary>
    /// NEWGAME.PAS:1203,1207 (CreatePlayerEmpire) / :1240,1243 (CreateNPEmpire):
    /// <c>KnownTechs:=TechDev[Pred(Tech)]; KnownTechs:=KnownTechs+extras;
    /// KnownTechs:=KnownTechs*TechDev[Tech]</c> — union the previous level's full set with the
    /// scenario's extras, then clamp back down to what's actually legal at this level (an extra tech
    /// beyond the current level's own TechDev gets dropped). TechDev[Pred(Tech)] at TechLevel.PreTech
    /// (the lowest level) is undefined in Pascal (Pred of the first enum value) and never occurs in
    /// any real scenario file (reference/scenarios/dos_131/*.SCN's CreatePlayerEmpire/CreateNPEmpire
    /// calls are all Bio or PreGate) — skip the union entirely rather than modeling a state that
    /// can't happen, matching this codebase's existing defensive-not-modeled precedent.
    /// </summary>
    private static void SeedTechnology(UnlockedTechnology technology, TechLevel techLevel, IReadOnlyList<Action<UnlockedTechnology>> extraTechs)
    {
        if (techLevel > TechLevel.PreTech) {
            foreach (var (_, unlock) in TechCatalog.MissingTechAt(new UnlockedTechnology(), techLevel - 1)) {
                unlock(technology);
            }
        }

        foreach (var grant in extraTechs) {
            grant(technology);
        }

        var allowedAtTech = new UnlockedTechnology();

        foreach (var (_, unlock) in TechCatalog.MissingTechAt(new UnlockedTechnology(), techLevel)) {
            unlock(allowedAtTech);
        }

        technology.Defenses.IntersectWith(allowedAtTech.Defenses);
        technology.Ships.IntersectWith(allowedAtTech.Ships);
        technology.Resources.IntersectWith(allowedAtTech.Resources);
        technology.Constructions.IntersectWith(allowedAtTech.Constructions);
    }

    /// <summary>
    /// DATACNST.PAS:373-379 (InitDefenseRecord) — only ShellDefDist (Fleets) is given explicit
    /// per-shell/per-ship-type percentages; StarbaseDefDist (Starbases) is left at the typed
    /// constant's default of all zeros. Not a port gap: Pascal's own InitDefenseRecord omits that
    /// field too (DATASTRC.PAS:165-168 declares both fields, but the constant only sets one).
    /// </summary>
    private static void SeedDefenseSettings(DefenseSettings settings)
    {
        SetShell(settings.Fleets.DeepSpace, fighters: 5, hunterKillers: 50, jumpships: 10, jumptransports: 0, penetrators: 15, starships: 0, transports: 0);
        SetShell(settings.Fleets.HighOrbit, fighters: 10, hunterKillers: 10, jumpships: 20, jumptransports: 0, penetrators: 30, starships: 50, transports: 0);
        SetShell(settings.Fleets.Orbit, fighters: 10, hunterKillers: 10, jumpships: 30, jumptransports: 0, penetrators: 30, starships: 30, transports: 0);
        SetShell(settings.Fleets.SubOrbit, fighters: 55, hunterKillers: 30, jumpships: 40, jumptransports: 0, penetrators: 25, starships: 20, transports: 0);
        SetShell(settings.Fleets.Ground, fighters: 20, hunterKillers: 0, jumpships: 0, jumptransports: 100, penetrators: 0, starships: 0, transports: 100);
    }

    private static void SetShell(ShipDistribution shell, int fighters, int hunterKillers, int jumpships, int jumptransports, int penetrators, int starships, int transports)
    {
        shell.Fighters = fighters;
        shell.HunterKillers = hunterKillers;
        shell.Jumpships = jumpships;
        shell.Jumptransports = jumptransports;
        shell.Penetrators = penetrators;
        shell.Starships = starships;
        shell.Transports = transports;
    }
}
