using System.Collections.ObjectModel;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// "Designate" tab inside <see cref="WorldInfoWindow"/> (DESIGN.PAS: DesignateCommand, :656-861).
/// A persistent list, not a transient picker: unlike the standalone flow this replaced, there's
/// nothing to "reopen" after a declined risk confirm (<see cref="GameShell.ConfirmDesignate"/>'s own
/// decline branch just leaves this tab showing exactly what it already was).
///
/// Suitability percentage is its own column (every candidate's number visible at a glance); the
/// bottom hint panel is this port's own addition (<see cref="WorldDesignation.DesignationHint"/>'s
/// own doc comment) -- no real Pascal equivalent tells the player what a designation actually
/// changes, optimally choosing meant cross-referencing manual tables by hand.
/// </summary>
internal sealed class DesignateTabView : View
{
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue); // SYSDispWind = 23
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray); // SYSDispSelect = 112

    // GetDesignation's own menu filter (DESIGN.PAS:725-746): these 7 types are never offered here at
    // all, regardless of tech -- the 5 starbase-variant types are set only when a starbase is first
    // built, Outpost only by construction, Terraform only by the separate Terraform command.
    private static readonly HashSet<WorldType> NeverDesignable = [
        WorldType.Outpost, WorldType.BaseStarbase, WorldType.JumpshipBaseStarbase,
        WorldType.StarshipBaseStarbase, WorldType.TransportBaseStarbase, WorldType.RawMaterialMineStarbase,
        WorldType.Terraform,
    ];

    private readonly ListView<WorldTypeChoice> listView;
    private readonly Label hintLabel;

    public DesignateTabView(IEconomicWorld world, Action<WorldType> onSelected)
    {
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));

        var cls = world.EffectiveClass;
        var choices = Enum.GetValues<WorldType>()
            .Where(t => !NeverDesignable.Contains(t))
            .Where(t => WorldDesignation.MinTechForType[t] <= world.TechLevel)
            .Where(t => t != WorldType.Ambrosia || cls is WorldClass.Ambrosia or WorldClass.Paradise)
            .Select(t => new WorldTypeChoice(t, MenuLine(t, cls)))
            .ToList();

        Add(new Label { X = 0, Y = 0, Text = $"{"World Type",-30}{"Main Industry",-22}{"Suit.",4}" });

        const int hintLines = 4; // DesignationHint's longest sentence needs 3 wrapped rows at this width; 4 leaves headroom.
        listView = new ListView<WorldTypeChoice> { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(hintLines) };
        listView.SetScheme(new Scheme { Normal = DispWindAttribute, Focus = SelectedAttribute });
        listView.SetSource(new ObservableCollection<WorldTypeChoice>(choices));
        listView.Index = 0;
        Add(listView);

        hintLabel = new Label { X = 0, Y = Pos.AnchorEnd(hintLines), Width = Dim.Fill(), Height = hintLines, Text = WorldDesignation.DesignationHint(world, choices[0].Type) };
        Add(hintLabel);
        listView.ValueChanged += (_, _) => hintLabel.Text = listView.Value is { } chosen ? WorldDesignation.DesignationHint(world, chosen.Type) : string.Empty;

        listView.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Enter && listView.Value is { } selected) {
                onSelected(selected.Type);
                key.Handled = true;
            }
        };
    }

    // GetDesignation's own menu line (DESIGN.PAS:733-744): type name, left-padded, then main
    // industry -- three hardcoded overrides (University/RawMaterialMine/Capital) instead of
    // PrincipalIndustry's own entry for those.
    private static string MenuLine(WorldType type, WorldClass cls)
    {
        var name = WorldDesignation.TypeName(type);
        var capitalized = char.ToUpperInvariant(name[0]) + name[1..];
        var industry = type switch {
            WorldType.University => "(research)",
            WorldType.RawMaterialMine or WorldType.RawMaterialMineStarbase => "raw material mining",
            WorldType.Capital => "administration",
            _ => IndustryConstants.Name(WorldDesignation.PrincipalIndustry[type]),
        };
        var suitability = AnnualTickHandler.ClassIndustryAdjustment[(cls, WorldDesignation.PrincipalIndustry[type])];
        return $"{capitalized,-30}{industry,-22}{suitability,4}%";
    }

    private sealed record WorldTypeChoice(WorldType Type, string Label)
    {
        public override string ToString() => Label;
    }
}
