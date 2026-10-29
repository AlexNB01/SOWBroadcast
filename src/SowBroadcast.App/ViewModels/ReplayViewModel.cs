using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Replay;

namespace SowBroadcast.App.ViewModels;

/// <summary>One row of the clip list.</summary>
public sealed class ReplayClipItem
{
    public ReplayClipItem(ReplayClip clip) => Clip = clip;

    public ReplayClip Clip { get; }
    public string Id => Clip.Id;
    public string Title => Clip.Title.Length > 0 ? Clip.Title : Clip.File;

    public string Subtitle
    {
        get
        {
            var parts = new List<string> { DateTimeOffset.FromUnixTimeMilliseconds(Clip.AddedAt).ToLocalTime().ToString("ddd HH:mm", CultureInfo.InvariantCulture) };
            if (Clip.ShownMs > 0) parts.Add(ReplayViewModel.Clock(Clip.ShownMs));
            if (Clip.StartMs > 0 || Clip.EndMs > 0) parts.Add("trimmed");
            if (Math.Abs(Clip.Speed - 1) > 0.001) parts.Add($"{Clip.Speed:0.##}×");
            if (Clip.Match.Length > 0) parts.Add(Clip.Match);
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>A choice in the category list (the first one stands for every clip).</summary>
public sealed class ReplayCategoryItem
{
    public ReplayCategoryItem(ReplayCategory? category, int allCount = 0) { Category = category; AllCount = allCount; }

    public ReplayCategory? Category { get; }
    public int AllCount { get; }
    public bool IsAll => Category is null;
    public bool IsCustom => Category?.Kind == ReplayCategoryKind.Custom;
    public string Name => Category?.Name ?? "";
    public string Label => Category is null ? $"All clips ({AllCount})" : $"{Category.Label} ({Category.Count})";
}

/// <summary>A custom category and whether the selected clip is in it.</summary>
public sealed partial class ReplayTagItem : ObservableObject
{
    [ObservableProperty] private bool _isOn;
    public ReplayTagItem(string name, bool isOn) { Name = name; _isOn = isOn; }
    public string Name { get; }
}

/// <summary>
/// The Replay tab: the saved clips, a preview with trimming and slow motion, categories, and what the stream shows. Trimming never
/// changes the file; the overlay plays the chosen part at the chosen speed.
/// </summary>
public sealed partial class ReplayViewModel : ObservableObject
{
    private readonly ReplayLibrary _library;
    private readonly IOverlayPublisher _publisher;
    private readonly Func<(string Match, string Tournament)> _context;
    private readonly IMessages _messages;
    private readonly Action<Action> _uiPost;
    private readonly Action<string> _say;
    private bool _loading;
    private long _stopAtMs;
    private int _ownChanges;   // library changes this view caused itself (its rows are already right)

    public ReplayViewModel(ReplayLibrary library, IOverlayPublisher publisher, IClipPlayer? player = null,
        Func<(string Match, string Tournament)>? context = null, IMessages? messages = null, Action<Action>? uiPost = null, Action<string>? say = null)
    {
        _library = library;
        _publisher = publisher;
        Player = player ?? new NoClipPlayer();
        _context = context ?? (() => ("", ""));
        _messages = messages ?? new NoMessages();
        _uiPost = uiPost ?? (action => action());
        _say = say ?? (_ => { });
        _library.Changed += () => _uiPost(() =>
        {
            if (_ownChanges > 0) { _ownChanges--; return; }
            Refresh();
        });
        Player.Muted = true;
        Refresh();
    }

    private sealed class NoMessages : IMessages
    {
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }

    public IClipPlayer Player { get; }
    public bool PreviewAvailable => Player.Available;
    public string PreviewProblem => Player.Problem ?? "";

    public ObservableCollection<ReplayClipItem> Clips { get; } = new();
    public ObservableCollection<ReplayCategoryItem> Categories { get; } = new();
    public ObservableCollection<ReplayTagItem> Tags { get; } = new();

    [ObservableProperty] private ReplayClipItem? _selected;
    [ObservableProperty] private ReplayCategoryItem? _selectedCategory;
    [ObservableProperty] private string _newCategoryName = "";
    [ObservableProperty] private string _summary = "";

    // the selected clip's settings, applied to the clip as they are changed
    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _editMatch = "";
    [ObservableProperty] private string _editTournament = "";
    [ObservableProperty] private decimal _editStart;
    [ObservableProperty] private decimal _editEnd;
    [ObservableProperty] private decimal _editSpeed = 1;

    // the preview
    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private double _lengthSeconds;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _muted = true;

    public bool HasSelection => Selected is not null;
    public bool HasClips => Clips.Count > 0;
    public bool CanDeleteCategory => SelectedCategory?.IsCustom == true;
    public string PositionText => $"{Clock((long)(PositionSeconds * 1000), tenths: true)} / {Clock((long)(LengthSeconds * 1000), tenths: true)}";

    public static string Clock(long ms, bool tenths = false)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        var whole = $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        return tenths ? whole + "." + (t.Milliseconds / 100) : whole;
    }

    // ---- the lists ---------------------------------------------------------------------------------------

    /// <summary>Shows the library again (a clip arrived, or something was edited elsewhere), keeping the selection when it still exists.</summary>
    public void Refresh()
    {
        var keepClip = Selected?.Id;
        var keepCategory = SelectedCategory?.Category?.Label;
        var all = _library.Clips();

        _loading = true;
        try
        {
            Categories.Clear();
            Categories.Add(new ReplayCategoryItem(null, all.Count));
            foreach (var c in _library.Categories()) Categories.Add(new ReplayCategoryItem(c));
            SelectedCategory = Categories.FirstOrDefault(c => c.Category?.Label == keepCategory) ?? Categories[0];

            var shown = SelectedCategory.IsAll ? all : _library.ClipsIn(SelectedCategory.Name).OrderByDescending(c => c.AddedAt).ToArray();
            Clips.Clear();
            foreach (var clip in shown) Clips.Add(new ReplayClipItem(clip));
            Selected = Clips.FirstOrDefault(c => c.Id == keepClip);
        }
        finally { _loading = false; }

        LoadEditor();
        if (Selected?.Id != keepClip) OpenPreview();
        OnPropertyChanged(nameof(HasClips));
        OnPropertyChanged(nameof(CanDeleteCategory));
        Summary = all.Count == 0 ? "No clips yet. Clips OBS saves with the replay buffer appear here." : $"{all.Count} clips";
    }

    partial void OnSelectedCategoryChanged(ReplayCategoryItem? value)
    {
        if (_loading) return;
        Refresh();
    }

    partial void OnSelectedChanged(ReplayClipItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        if (_loading) return;
        LoadEditor();
        OpenPreview();
    }

    private void LoadEditor()
    {
        var clip = Selected?.Clip;
        var was = _loading;
        _loading = true;
        try
        {
            EditTitle = clip?.Title ?? "";
            EditMatch = clip?.Match ?? "";
            EditTournament = clip?.Tournament ?? "";
            EditStart = (decimal)((clip?.StartMs ?? 0) / 1000.0);
            EditEnd = (decimal)((clip?.EndMs ?? 0) / 1000.0);
            EditSpeed = (decimal)(clip?.Speed ?? 1);
            Tags.Clear();
            foreach (var category in _library.Categories().Where(c => c.Kind == ReplayCategoryKind.Custom))
                Tags.Add(new ReplayTagItem(category.Name, clip?.Tags.Any(t => string.Equals(t, category.Name, StringComparison.OrdinalIgnoreCase)) == true));
            foreach (var tag in Tags) tag.PropertyChanged += (_, _) => { if (!_loading) ApplyEdits(); };
        }
        finally { _loading = was; }
    }

    // ---- editing the selected clip -----------------------------------------------------------------------

    partial void OnEditTitleChanged(string value) => ApplyEdits();
    partial void OnEditMatchChanged(string value) => ApplyEdits();
    partial void OnEditTournamentChanged(string value) => ApplyEdits();
    partial void OnEditStartChanged(decimal value) => ApplyEdits();
    partial void OnEditEndChanged(decimal value) => ApplyEdits();
    partial void OnEditSpeedChanged(decimal value) => ApplyEdits();

    private void ApplyEdits()
    {
        if (_loading || Selected is not { } item) return;
        var updated = item.Clip with
        {
            Title = EditTitle,
            Match = EditMatch,
            Tournament = EditTournament,
            StartMs = (long)Math.Round(EditStart * 1000),
            EndMs = (long)Math.Round(EditEnd * 1000),
            Speed = (double)EditSpeed,
            Tags = Tags.Where(t => t.IsOn).Select(t => t.Name).ToArray(),
        };
        _ownChanges++;
        if (!_library.Update(updated)) { _ownChanges--; return; }
        // The list row follows the clip; the selection and the typing stay where they are.
        var index = Clips.IndexOf(item);
        var fresh = new ReplayClipItem(_library.Find(updated.Id) ?? updated);
        _loading = true;
        try
        {
            if (index >= 0) Clips[index] = fresh;
            Selected = fresh;
        }
        finally { _loading = false; }
    }

    // ---- the preview -------------------------------------------------------------------------------------

    private void OpenPreview()
    {
        _stopAtMs = 0;
        IsPlaying = false;
        PositionSeconds = 0;
        LengthSeconds = (Selected?.Clip.DurationMs ?? 0) / 1000.0;
        if (!Player.Available) return;
        if (Selected is not { } item) { Player.Close(); return; }
        Player.SetRate(1);
        Player.Open(_library.PathOf(item.Clip));
    }

    /// <summary>Called a few times a second by the view: follows the player and stops a trimmed preview at its end.</summary>
    public void Tick(bool followPosition = true)
    {
        if (!Player.Available || Selected is null) return;
        IsPlaying = Player.IsPlaying;
        var length = Player.LengthMs;
        if (length > 0)
        {
            LengthSeconds = length / 1000.0;
            // The file's own length is kept when the library did not know it (a clip OBS wrote in pieces).
            if (Selected.Clip.DurationMs == 0 && _library.Find(Selected.Id) is { } known)
            {
                _library.Update(known with { DurationMs = length });   // the list shows the length after the refresh this causes
            }
        }
        if (followPosition) PositionSeconds = Player.PositionMs / 1000.0;
        if (_stopAtMs > 0 && Player.IsPlaying && Player.PositionMs >= _stopAtMs)
        {
            Player.Pause();
            Player.SetRate(1);
            _stopAtMs = 0;
        }
        OnPropertyChanged(nameof(PositionText));
    }

    partial void OnMutedChanged(bool value) => Player.Muted = value;

    /// <summary>The slider was moved to this many seconds.</summary>
    public void SeekTo(double seconds)
    {
        _stopAtMs = 0;
        Player.Seek((long)(Math.Max(0, seconds) * 1000));
        PositionSeconds = seconds;
        OnPropertyChanged(nameof(PositionText));
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (!Player.Available || Selected is null) return;
        _stopAtMs = 0;
        Player.SetRate(1);
        if (Player.IsPlaying) Player.Pause(); else Player.Play();
    }

    /// <summary>Plays the part that will be shown, at the speed it will be shown at.</summary>
    [RelayCommand]
    private void PreviewTrimmed()
    {
        if (!Player.Available || Selected?.Clip is not { } clip) return;
        Player.Seek(clip.StartMs);
        Player.SetRate(clip.Speed);
        _stopAtMs = clip.EndMs;
        Player.Play();
    }

    [RelayCommand]
    private void SetStartHere()
    {
        if (Selected is null) return;
        var ms = Player.Available ? Player.PositionMs : (long)(PositionSeconds * 1000);
        EditStart = (decimal)(ms / 1000.0);
        if (EditEnd > 0 && EditEnd <= EditStart) EditEnd = 0;
    }

    [RelayCommand]
    private void SetEndHere()
    {
        if (Selected is null) return;
        var ms = Player.Available ? Player.PositionMs : (long)(PositionSeconds * 1000);
        if (ms / 1000.0 > (double)EditStart) EditEnd = (decimal)(ms / 1000.0);
    }

    [RelayCommand]
    private void ClearTrim()
    {
        EditStart = 0;
        EditEnd = 0;
        EditSpeed = 1;
    }

    // ---- the stream --------------------------------------------------------------------------------------

    [RelayCommand]
    private void ShowSelected()
    {
        if (Selected is { } item) Show(new[] { item.Clip }, item.Title);
    }

    [RelayCommand]
    private void ShowCategory()
    {
        if (SelectedCategory is { IsAll: false } category) ShowCategoryNamed(category.Name);
        else ShowAll();
    }

    private void ShowAll()
    {
        var clips = _library.Clips().Reverse().ToArray();
        Show(clips, "all clips");
    }

    [RelayCommand]
    private void ShowLatest() => RunCommand(ControlCatalogIds.Latest, null);

    [RelayCommand]
    private void StopStream() => RunCommand(ControlCatalogIds.Stop, null);

    private (bool Ok, string Message) Show(IReadOnlyList<ReplayClip> clips, string what)
    {
        if (clips.Count == 0) return Said(false, $"No clips in {what}");
        _library.Play(clips);
        _publisher.AnnounceReplay();
        return Said(true, clips.Count == 1 ? $"Replay: showing {what}" : $"Replay: showing {clips.Count} clips of {what}");
    }

    private (bool Ok, string Message) ShowCategoryNamed(string name)
    {
        var clips = _library.ClipsIn(name);
        return Show(clips, name);
    }

    private (bool Ok, string Message) Said(bool ok, string message)
    {
        _say(ok ? message : $"Replay: {message}");
        return (ok, message);
    }

    private static class ControlCatalogIds
    {
        public const string Latest = SowBroadcast.Core.Control.ControlCatalog.ReplayLatest;
        public const string Stop = SowBroadcast.Core.Control.ControlCatalog.ReplayStop;
        public const string Clip = SowBroadcast.Core.Control.ControlCatalog.ReplayClip;
        public const string Category = SowBroadcast.Core.Control.ControlCatalog.ReplayCategory;
    }

    /// <summary>Runs a <c>replay.*</c> control command (the hotkeys and the HTTP interface end up here). <paramref name="arguments"/> hold <c>id</c> or <c>name</c>.</summary>
    public (bool Ok, string Message) RunCommand(string id, IReadOnlyDictionary<string, string>? arguments)
    {
        string Arg(string key) => arguments is not null && arguments.TryGetValue(key, out var v) ? v.Trim() : "";
        switch (id.ToLowerInvariant())
        {
            case ControlCatalogIds.Latest:
                return _library.Latest() is { } latest ? Show(new[] { latest }, latest.Title) : Said(false, "No clips yet");
            case ControlCatalogIds.Stop:
                _library.Stop();
                _publisher.AnnounceReplay();
                return Said(true, "Replay: back to the newest clip");
            case ControlCatalogIds.Clip:
            {
                var wanted = Arg("id");
                if (wanted.Length == 0) return Said(false, "Say which clip: ?id=clip id");
                return _library.Find(wanted) is { } clip ? Show(new[] { clip }, clip.Title) : Said(false, $"No clip \"{wanted}\"");
            }
            case ControlCatalogIds.Category:
            {
                var name = Arg("name");
                if (name.Length == 0) return Said(false, "Say which category: ?name=Category name");
                return ShowCategoryNamed(name);
            }
            default:
                return (false, $"Unknown command \"{id}\"");
        }
    }

    // ---- the library -------------------------------------------------------------------------------------

    /// <summary>A new clip reached the playlist: put it in the library with the match and tournament of the moment, then tell the overlay.</summary>
    public void OnClipArrived(string file)
    {
        var (match, tournament) = _context();
        _library.Add(file, match, tournament);
        _publisher.AnnounceReplay();
    }

    [RelayCommand]
    private void AddCategory()
    {
        var name = NewCategoryName.Trim();
        if (name.Length == 0) return;
        if (!_library.AddCategory(name)) { _say($"Replay: the category \"{name}\" exists already"); return; }
        NewCategoryName = "";
        // Put the new category in the clip that is selected: that is usually why it was made.
        if (Selected is { } item && _library.Find(item.Id) is { } clip)
            _library.Update(clip with { Tags = clip.Tags.Append(name).ToArray() });
    }

    [RelayCommand]
    private void DeleteCategory()
    {
        if (SelectedCategory is not { IsCustom: true } category) return;
        _library.RemoveCategory(category.Name);
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        if (Selected is not { } item) return;
        if (!await _messages.ConfirmAsync("Delete clip", $"Delete \"{item.Title}\" and its video file? This cannot be undone.")) return;
        Player.Close();   // the file must not be open
        _library.Remove(item.Id);
        _publisher.AnnounceReplay();   // the overlay may have been about to show it
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_library.PlaylistFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_library.PlaylistFolder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            _say($"Replay: the folder could not be opened: {ex.Message}");
        }
    }
}
