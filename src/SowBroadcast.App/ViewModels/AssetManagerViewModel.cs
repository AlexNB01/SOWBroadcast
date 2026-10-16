using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Assets;

namespace SowBroadcast.App.ViewModels;

/// <summary>Edit window for one category: Heroes, Maps or Game Modes.</summary>
public sealed partial class AssetManagerViewModel : ObservableObject
{
    private readonly AssetService _assets;
    private readonly IImagePicker _picker;
    private readonly IMessages _messages;

    /// <summary>Set only when the user picks a new file; empty keeps the existing image.</summary>
    private string? _pickedImage;
    private string? _pickedBody;

    public AssetManagerViewModel(AssetCategory category, AssetService assets, IImagePicker picker, IMessages messages)
    {
        Category = category;
        _assets = assets;
        _picker = picker;
        _messages = messages;
        ModeNames = assets.Names(AssetCategory.Modes);
        RefreshNames();
    }

    public AssetCategory Category { get; }
    public string Title => Category.Title();
    public bool IsHeroes => Category == AssetCategory.Heroes;
    public bool IsMaps => Category == AssetCategory.Maps;

    public IReadOnlyList<string> ModeNames { get; }
    public IReadOnlyList<string> RoleNames => AssetService.Roles;
    public ObservableCollection<string> Names { get; } = new();

    [ObservableProperty] private string? _selectedName;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _mode;
    [ObservableProperty] private string? _role;
    [ObservableProperty] private string _imagePath = "";
    [ObservableProperty] private string _bodyPath = "";

    partial void OnSelectedNameChanged(string? value)
    {
        _pickedImage = null;
        _pickedBody = null;
        var asset = _assets.Find(Category, value);
        if (asset is null) return;

        Name = asset.Name;
        Mode = asset.Mode;
        Role = asset.Role;
        ImagePath = asset.SourcePath ?? "";
        BodyPath = asset.BodySourcePath ?? "";
    }

    [RelayCommand]
    private async Task BrowseImageAsync()
    {
        var path = await _picker.PickImageAsync(IsHeroes ? "Select hero image" : IsMaps ? "Select map image" : "Select game mode image");
        if (path is null) return;
        _pickedImage = path;
        ImagePath = path;
    }

    [RelayCommand]
    private async Task BrowseBodyAsync()
    {
        var path = await _picker.PickImageAsync("Select full body image");
        if (path is null) return;
        _pickedBody = path;
        BodyPath = path;
    }

    [RelayCommand]
    private async Task AddOrUpdateAsync()
    {
        var result = _assets.AddOrUpdate(new AssetEdit
        {
            Category = Category,
            Name = Name,
            ImageSourcePath = _pickedImage,
            BodySourcePath = IsHeroes ? _pickedBody : null,
            Mode = IsMaps ? Mode : null,
            Role = IsHeroes ? Role : null,
        });

        if (!result.Ok)
        {
            await _messages.ShowErrorAsync(result.ErrorTitle!, result.ErrorMessage!);
            return;
        }

        RefreshNames();
        SelectedName = result.Asset!.Name;
    }

    [RelayCommand]
    private void Delete()
    {
        var name = SelectedName ?? Name.Trim();
        if (!_assets.Delete(Category, name)) return;

        RefreshNames();
        SelectedName = null;
        Name = "";
        Mode = null;
        Role = null;
        ImagePath = "";
        BodyPath = "";
        _pickedImage = null;
        _pickedBody = null;
    }

    private void RefreshNames()
    {
        Names.Clear();
        foreach (var name in _assets.Names(Category)) Names.Add(name);
    }
}
