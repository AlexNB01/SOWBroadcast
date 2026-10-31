using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SowBroadcast.App.ViewModels;

namespace SowBroadcast.App.Views;

/// <summary>Tells the producer that a newer version exists and lets them install it now or later.</summary>
public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow() => InitializeComponent();

    public UpdateAvailableWindow(UpdatePromptViewModel model) : this()
    {
        VersionText.Text = model.VersionLine;
        SizeText.Text = model.SizeLine;
        SizeText.IsVisible = model.HasSize;
        NotesText.Text = model.Notes;
        NotesBox.IsVisible = model.HasNotes;
        WarningText.Text = model.Warning;
        // The focus starts on "later": a stray Enter while typing in the main window must not start an installation.
        Opened += (_, _) => LaterButton.Focus();
    }

    /// <summary>What was chosen; closing the window any other way (Esc, the close button) means later.</summary>
    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
        base.OnKeyDown(e);
    }

    private void OnLater(object? sender, RoutedEventArgs e) => Close();

    private void OnInstall(object? sender, RoutedEventArgs e)
    {
        Choice = UpdateChoice.InstallNow;
        Close();
    }
}
