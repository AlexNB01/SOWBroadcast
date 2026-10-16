using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Waiting;

namespace SowBroadcast.App.ViewModels;

/// <summary>One social network field: what the user typed (a handle or a whole address).</summary>
public sealed partial class SocialFieldViewModel : ObservableObject
{
    public SocialFieldViewModel(string kind, string label, string placeholder)
    {
        Kind = kind;
        Label = label;
        Placeholder = placeholder;
    }

    public string Kind { get; }
    public string Label { get; }
    public string Placeholder { get; }

    [ObservableProperty] private string _text = "";
}

/// <summary>The Waiting Screen tab: video folder, countdown, on-screen texts, ticker override and socials.</summary>
public sealed partial class WaitingViewModel : ObservableObject
{
    public const string DefaultStarting = "STARTING SOON!";
    public const string DefaultBrb = "BE RIGHT BACK!";
    public const string DefaultEnd = "THANK YOU FOR WATCHING";

    // Not edits of the tab's settings: the clock and what is derived from it.
    private static readonly HashSet<string> NotEdits = new() { nameof(TimerText), nameof(IsRunning), nameof(VideosFolderText) };

    private readonly IFileDialogs _dialogs;
    private bool _loading;

    public WaitingViewModel(IFileDialogs dialogs, string? defaultVideosDir, Func<long>? clock = null)
    {
        _dialogs = dialogs;
        DefaultVideosDir = defaultVideosDir ?? "";
        var stopwatch = Stopwatch.StartNew();
        Timer = new CountdownTimer(clock ?? (() => stopwatch.ElapsedMilliseconds));
        Socials = new[]
        {
            new SocialFieldViewModel("twitch", "Twitch", "twitch.tv/"),
            new SocialFieldViewModel("twitter", "Twitter/X", "@user or x.com/…"),
            new SocialFieldViewModel("youtube", "YouTube", "@user"),
            new SocialFieldViewModel("instagram", "Instagram", "@user"),
            new SocialFieldViewModel("discord", "Discord", "/invite"),
            new SocialFieldViewModel("web", "Website", "domain.com"),
        };
        foreach (var social in Socials) social.PropertyChanged += (_, _) => RaiseEdited();
        _useDefaultVideos = true;
    }

    public CountdownTimer Timer { get; }
    public IReadOnlyList<SocialFieldViewModel> Socials { get; }

    /// <summary>The folder used when none is chosen.</summary>
    public string DefaultVideosDir { get; }

    [ObservableProperty] private bool _useDefaultVideos;
    [ObservableProperty] private string _videosDir = "";
    [ObservableProperty] private string _textStarting = DefaultStarting;
    [ObservableProperty] private string _textBrb = DefaultBrb;
    [ObservableProperty] private string _textEnd = DefaultEnd;
    [ObservableProperty] private string _tickerOverride = "";
    [ObservableProperty] private bool _tickerOverrideEnabled;
    [ObservableProperty] private string _timerText = CountdownTimer.Format(0);
    [ObservableProperty] private bool _isRunning;

    /// <summary>What the (read-only) folder box shows: the chosen folder, or the default one.</summary>
    public string VideosFolderText => UseDefaultVideos ? DefaultVideosDir : VideosDir;

    /// <summary>Raised when the user changed a setting (not while a state is being loaded).</summary>
    public event Action? Edited;

    /// <summary>A timer command was issued: the new value goes to the overlays at once.</summary>
    public event Action? TimerCommanded;

    /// <summary>A second passed while the timer runs: the files are refreshed, nothing is announced.</summary>
    public event Action? TimerTicked;

    partial void OnUseDefaultVideosChanged(bool value)
    {
        if (value) VideosDir = "";
        OnPropertyChanged(nameof(VideosFolderText));
    }

    partial void OnVideosDirChanged(string value) => OnPropertyChanged(nameof(VideosFolderText));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not null && !NotEdits.Contains(e.PropertyName)) RaiseEdited();
    }

    private void RaiseEdited()
    {
        if (!_loading) Edited?.Invoke();
    }

    // ---- countdown ---------------------------------------------------------------------------------------

    private void RefreshTimer()
    {
        TimerText = CountdownTimer.Format(Timer.Remaining);
        IsRunning = Timer.IsRunning;
    }

    /// <summary>Called by the window's clock. Refreshes the display and the files when the second changed.</summary>
    public void Tick()
    {
        var wasRunning = Timer.IsRunning;
        var before = TimerText;
        RefreshTimer();
        if (!wasRunning) return;
        if (!Timer.IsRunning) TimerCommanded?.Invoke(); // ran out: announce the stop
        else if (TimerText != before) TimerTicked?.Invoke();
    }

    [RelayCommand]
    private void AdjustTimer(string seconds)
    {
        Timer.Adjust(int.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture));
        RefreshTimer();
        TimerCommanded?.Invoke();
    }

    [RelayCommand]
    private void StartTimer()
    {
        Timer.Start();
        RefreshTimer();
        TimerCommanded?.Invoke();
    }

    [RelayCommand]
    private void PauseTimer()
    {
        if (!Timer.Pause()) return;
        RefreshTimer();
        TimerCommanded?.Invoke();
    }

    [RelayCommand]
    private void ResetTimer()
    {
        Timer.Reset();
        RefreshTimer();
        TimerCommanded?.Invoke();
    }

    // ---- videos, reset -----------------------------------------------------------------------------------

    [RelayCommand]
    private async Task BrowseVideosAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Select videos folder", VideosFolderText);
        if (folder is null) return;
        UseDefaultVideos = false;
        VideosDir = folder;
    }

    /// <summary>
    /// Back to the defaults: default video folder, default texts, ticker off, countdown back to its preset. The socials stay.
    /// Nothing is sent, except that a countdown that was running is stopped for the overlays too.
    /// </summary>
    public void ResetTab()
    {
        var wasRunning = Timer.IsRunning;
        var before = Timer.Remaining;
        UseDefaultVideos = true;
        VideosDir = "";
        TextStarting = DefaultStarting;
        TextBrb = DefaultBrb;
        TextEnd = DefaultEnd;
        TickerOverrideEnabled = false;
        TickerOverride = "";
        Timer.Reset();
        RefreshTimer();
        if (wasRunning || before != Timer.Remaining) TimerCommanded?.Invoke();
    }

    // ---- state -------------------------------------------------------------------------------------------

    /// <summary>Shows the waiting part of <paramref name="state"/>. A saved countdown comes back stopped.</summary>
    public void Load(AppState state)
    {
        var w = state.Waiting;
        _loading = true;
        try
        {
            VideosDir = w.VideosDir;
            UseDefaultVideos = string.IsNullOrWhiteSpace(w.VideosDir);
            TextStarting = w.TextStarting;
            TextBrb = w.TextBrb;
            TextEnd = w.TextEnd;
            TickerOverride = w.TickerOverride;
            TickerOverrideEnabled = w.TickerOverrideEnabled;
            foreach (var social in Socials)
                social.Text = w.Socials.TryGetValue(social.Kind, out var handle) ? handle : "";
            Timer.Set(w.TimerSeconds);
            RefreshTimer();
        }
        finally { _loading = false; }
    }

    /// <summary>The countdown as it is now, written into the state (what the timer files are made from).</summary>
    public AppState ApplyTimerTo(AppState state) => state with
    {
        Waiting = state.Waiting with { TimerSeconds = Timer.Remaining, TimerRunning = Timer.IsRunning },
    };

    public AppState ApplyTo(AppState state) => ApplyTimerTo(state with
    {
        Waiting = state.Waiting with
        {
            VideosDir = UseDefaultVideos ? "" : VideosDir.Trim(),
            TextStarting = OrDefault(TextStarting, DefaultStarting),
            TextBrb = OrDefault(TextBrb, DefaultBrb),
            TextEnd = OrDefault(TextEnd, DefaultEnd),
            TickerOverride = TickerOverride.Trim(),
            TickerOverrideEnabled = TickerOverrideEnabled,
            Socials = Socials
                .Select(s => (s.Kind, Handle: SocialHandles.Normalize(s.Kind, s.Text)))
                .Where(s => s.Handle.Length > 0)
                .ToDictionary(s => s.Kind, s => s.Handle),
        },
    });

    private static string OrDefault(string text, string fallback) => string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
}
