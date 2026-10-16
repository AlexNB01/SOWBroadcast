using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Server;

namespace SowBroadcast.Tests;

public sealed class AssetViewModelTests : IDisposable
{
    private static readonly string FixturesAssets = Path.Combine(AppContext.BaseDirectory, "Fixtures", "assets");

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-assetvm-" + Guid.NewGuid().ToString("N"));
    private readonly FakePicker _picker = new();
    private readonly FakeMessages _messages = new();
    private readonly AssetService _assets;

    public AssetViewModelTests()
    {
        _assets = new AssetService(Path.Combine(_tmp, "Scoreboard"), Array.Empty<string>());
        _assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Modes, Name = "Control", ImageSourcePath = Fx("control.png") });
        _assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Modes, Name = "Hybrid", ImageSourcePath = Fx("hybrid.png") });
    }

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private static string Fx(string name) => Path.Combine(FixturesAssets, name);

    private AssetManagerViewModel Manager(AssetCategory category) => new(category, _assets, _picker, _messages);

    [Fact]
    public async Task Adding_a_hero_with_both_images_lists_and_selects_it()
    {
        var vm = Manager(AssetCategory.Heroes);
        vm.Name = "Lúcio";
        vm.Role = "Support";
        _picker.Next = Fx("lucio.png");
        await vm.BrowseImageCommand.ExecuteAsync(null);
        _picker.Next = Fx("lucio_body.png");
        await vm.BrowseBodyCommand.ExecuteAsync(null);

        await vm.AddOrUpdateCommand.ExecuteAsync(null);

        Assert.Empty(_messages.Errors);
        Assert.Equal(new[] { "Lúcio" }, vm.Names);
        Assert.Equal("Lúcio", vm.SelectedName);
        Assert.Equal("Support", _assets.HeroRole("Lúcio"));
    }

    [Fact]
    public async Task Adding_without_a_name_shows_the_legacy_message_and_changes_nothing()
    {
        var vm = Manager(AssetCategory.Maps);

        await vm.AddOrUpdateCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Missing name: Type a name first." }, _messages.Errors);
        Assert.Empty(vm.Names);
    }

    [Fact]
    public async Task A_hero_without_a_body_image_is_refused()
    {
        var vm = Manager(AssetCategory.Heroes);
        vm.Name = "Mizuki";

        await vm.AddOrUpdateCommand.ExecuteAsync(null);

        Assert.Single(_messages.Errors);
        Assert.StartsWith("Missing body image", _messages.Errors[0]);
        Assert.Empty(vm.Names);
    }

    [Fact]
    public async Task Selecting_an_entry_fills_the_form_and_updating_without_new_files_keeps_the_images()
    {
        _assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "Busan", Mode = "Control", ImageSourcePath = Fx("busan.png") });
        var vm = Manager(AssetCategory.Maps);

        vm.SelectedName = "Busan";
        Assert.Equal("Busan", vm.Name);
        Assert.Equal("Control", vm.Mode);
        Assert.False(string.IsNullOrEmpty(vm.ImagePath));

        vm.Mode = "Hybrid";
        await vm.AddOrUpdateCommand.ExecuteAsync(null);

        Assert.Equal("Hybrid", _assets.Find(AssetCategory.Maps, "Busan")!.Mode);
        Assert.NotNull(_assets.Find(AssetCategory.Maps, "Busan")!.SourcePath);
    }

    [Fact]
    public async Task Cancelling_the_file_picker_leaves_the_path_alone()
    {
        var vm = Manager(AssetCategory.Maps);
        vm.ImagePath = "kept";
        _picker.Next = null;

        await vm.BrowseImageCommand.ExecuteAsync(null);

        Assert.Equal("kept", vm.ImagePath);
    }

    [Fact]
    public void Deleting_removes_the_entry_and_clears_the_form()
    {
        _assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "Busan", Mode = "Control", ImageSourcePath = Fx("busan.png") });
        var vm = Manager(AssetCategory.Maps);
        vm.SelectedName = "Busan";

        vm.DeleteCommand.Execute(null);

        Assert.Empty(vm.Names);
        Assert.Equal("", vm.Name);
        Assert.Null(vm.SelectedName);
        Assert.Equal("", vm.ImagePath);
    }

    [Fact]
    public void Mode_and_role_lists_come_from_the_catalogue()
    {
        var vm = Manager(AssetCategory.Maps);

        Assert.Equal(new[] { "Control", "Hybrid" }, vm.ModeNames);
        Assert.Equal(new[] { "Tank", "Damage", "Support" }, vm.RoleNames);
        Assert.True(vm.IsMaps);
        Assert.False(vm.IsHeroes);
    }

    // ---- bulk import -------------------------------------------------------------------------------------

    private void DropImages()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "Scoreboard", "Heroes"));
        Directory.CreateDirectory(Path.Combine(_tmp, "Scoreboard", "Maps"));
        File.Copy(Fx("lucio.png"), Path.Combine(_tmp, "Scoreboard", "Heroes", "new-hero.png"));
        File.Copy(Fx("busan.png"), Path.Combine(_tmp, "Scoreboard", "Maps", "havana.png"));
        File.Copy(Fx("kingsrow.png"), Path.Combine(_tmp, "Scoreboard", "Maps", "skipped-map.png"));
    }

    [Fact]
    public async Task Bulk_import_lists_new_pictures_and_imports_the_checked_ones_with_edited_names()
    {
        DropImages();
        var vm = new BulkImportViewModel(_assets, _messages);
        var imported = false;
        vm.Imported += () => imported = true;
        Assert.Equal(3, vm.Rows.Count);

        vm.Rows.Single(r => r.FileName == "skipped-map.png").Include = false;
        var havana = vm.Rows.Single(r => r.FileName == "havana.png");
        havana.Name = "Havana";
        havana.Mode = "Control";
        await vm.ImportCommand.ExecuteAsync(null);

        Assert.True(imported);
        Assert.Equal(new[] { "Imported 1 heroes and 1 maps." }, _messages.Infos);
        Assert.Equal("Control", _assets.Find(AssetCategory.Maps, "Havana")!.Mode);
        Assert.Contains("New Hero", _assets.Names(AssetCategory.Heroes));
        Assert.DoesNotContain("Skipped Map", _assets.Names(AssetCategory.Maps));
    }

    [Fact]
    public void Bulk_import_with_nothing_new_has_no_rows()
    {
        var vm = new BulkImportViewModel(_assets, _messages);

        Assert.False(vm.HasRows);
    }

    // ---- publishing asset changes ------------------------------------------------------------------------

    [Fact]
    public void Asset_changes_are_announced_to_overlays_with_the_next_update_only()
    {
        var hub = new EventHub(StateDiff.AllKeys);
        var publisher = new OverlayPublisher(new ScoreboardExporter(Path.Combine(_tmp, "out"), new SkiaLogoConverter()), hub, "http://127.0.0.1:1");
        _assets.Changed += publisher.MarkAssetsChanged;
        publisher.Publish(AppState.CreateDefault());
        var reader = hub.Subscribe(null).Reader;

        _assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "Busan", Mode = "Control", ImageSourcePath = Fx("busan.png") });
        Assert.False(reader.TryRead(out _)); // nothing is sent while the asset is only saved

        var update = publisher.Publish(AppState.CreateDefault());
        var again = publisher.Publish(AppState.CreateDefault());

        Assert.Equal(new[] { "assets.maps" }, update.ChangedKeys);
        Assert.Empty(again.ChangedKeys);
        Assert.True(reader.TryRead(out var ev));
        Assert.Equal(new[] { "assets.maps" }, ev!.Changed);
    }

    private sealed class FakePicker : IImagePicker
    {
        public string? Next { get; set; }
        public Task<string?> PickImageAsync(string title, string? initialDirectory = null) => Task.FromResult(Next);
    }

    private sealed class FakeMessages : IMessages
    {
        public List<string> Errors { get; } = new();
        public List<string> Infos { get; } = new();

        public Task ShowErrorAsync(string title, string message)
        {
            Errors.Add($"{title}: {message}");
            return Task.CompletedTask;
        }

        public Task ShowInfoAsync(string title, string message)
        {
            Infos.Add(message);
            return Task.CompletedTask;
        }
    }
}
