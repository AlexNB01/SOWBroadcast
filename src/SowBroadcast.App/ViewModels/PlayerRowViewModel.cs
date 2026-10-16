using CommunityToolkit.Mvvm.ComponentModel;
using SowBroadcast.Core.Model;

namespace SowBroadcast.App.ViewModels;

/// <summary>One player line of a team panel: name, hero and role.</summary>
public sealed partial class PlayerRowViewModel : ObservableObject
{
    private readonly Func<string?, string?> _roleOfHero;
    private bool _loading;

    public PlayerRowViewModel(int number, Func<string?, string?> roleOfHero)
    {
        Number = number;
        _roleOfHero = roleOfHero;
    }

    public int Number { get; }
    public string Label => $"Player {Number}";
    public IReadOnlyList<string> RoleNames { get; } = new[] { "Tank", "Damage", "Support", "Flex" };

    /// <summary>The heroes to choose from (set by the Match view model whenever the catalogue changes).</summary>
    [ObservableProperty] private IReadOnlyList<string> _heroNames = Array.Empty<string>();

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _hero;
    [ObservableProperty] private string? _role;
    [ObservableProperty] private bool _isShown = true;

    /// <summary>Picking a hero sets its role; the role is never cleared and a role the user chose is only replaced by picking another hero.</summary>
    partial void OnHeroChanged(string? value)
    {
        if (_loading || string.IsNullOrEmpty(value)) return;
        if (_roleOfHero(value) is { Length: > 0 } role) Role = role;
    }

    public void Load(Player player)
    {
        _loading = true;
        try
        {
            Name = player.Name;
            Hero = string.IsNullOrEmpty(player.Hero) ? null : player.Hero;
            Role = string.IsNullOrEmpty(player.Role) ? null : player.Role;
        }
        finally { _loading = false; }
    }

    public Player ToPlayer() => new() { Name = Name.Trim(), Hero = Hero ?? "", Role = Role ?? "" };
}
