using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.Core.Profiles;

namespace SowBroadcast.App.ViewModels;

/// <summary>One profile in the list.</summary>
public sealed class ProfileItemViewModel
{
    public ProfileItemViewModel(ProfileInfo info, bool active)
    {
        Id = info.Id;
        Name = info.Name;
        IsActive = active;
        Title = active ? $"{info.Name}  ·  active" : info.Name;
        Details = $"{info.Summary}  ·  saved {info.Saved:d MMM yyyy, HH:mm}";
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsActive { get; }
    public string Title { get; }
    public string Details { get; }
}

/// <summary>The profiles window: keep the current setup under a name, and put a saved one back.</summary>
public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    public ProfilesViewModel(MainWindowViewModel main)
    {
        _main = main;
        _name = main.ActiveProfileName;
        Refresh();
    }

    public ObservableCollection<ProfileItemViewModel> Items { get; } = new();

    [ObservableProperty] private string _name;
    [ObservableProperty] private ProfileItemViewModel? _selected;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isEmpty;

    partial void OnSelectedChanged(ProfileItemViewModel? value)
    {
        // Choosing a profile puts its name in the box, so "Save" replaces it.
        if (value is not null) Name = value.Name;
    }

    private void Refresh()
    {
        var store = _main.Profiles;
        var keep = Selected?.Id;
        Items.Clear();
        if (store is not null)
        {
            var active = store.ActiveId;
            foreach (var info in store.List()) Items.Add(new ProfileItemViewModel(info, info.Id == active));
        }
        Selected = Items.FirstOrDefault(i => i.Id == keep) ?? Items.FirstOrDefault(i => i.IsActive);
        IsEmpty = Items.Count == 0;
    }

    [RelayCommand]
    private void Save()
    {
        var name = Name.Trim();
        Message = _main.SaveProfile(name);
        Refresh();
        Selected = Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;
    }

    [RelayCommand]
    private void ApplySettings() => Apply(wholeMatch: false);

    [RelayCommand]
    private void ApplyWhole() => Apply(wholeMatch: true);

    private void Apply(bool wholeMatch)
    {
        if (Selected is not { } item) return;
        Message = _main.ApplyProfile(item.Id, wholeMatch);
        Refresh();
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is not { } item) return;
        Message = _main.DeleteProfile(item.Id);
        Refresh();
    }
}
