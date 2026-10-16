namespace SowBroadcast.App.Services;

/// <summary>File pickers, kept behind an interface so the view models stay UI-free and testable.</summary>
public interface IFileDialogs
{
    Task<string?> PickOpenStateFileAsync(string? initialDirectory);
    Task<string?> PickSaveStateFileAsync(string? initialDirectory, string suggestedName);

    /// <summary>Teams menu: a <c>*.sowteam.json</c> file to import.</summary>
    Task<string?> PickOpenTeamFileAsync(string? initialDirectory);

    /// <summary>Teams menu: where to export a team.</summary>
    Task<string?> PickSaveTeamFileAsync(string? initialDirectory, string suggestedName);

    /// <summary>Picks a folder (the waiting-screen videos).</summary>
    Task<string?> PickFolderAsync(string title, string? initialDirectory);
}

/// <summary>Picks one image file (png, jpg, jpeg or webp).</summary>
public interface IImagePicker
{
    Task<string?> PickImageAsync(string title, string? initialDirectory = null);
}

/// <summary>Lets the user choose which of the given teams to import (the bracket's "Import from Standings").</summary>
public interface ITeamPicker
{
    /// <summary>The chosen teams in list order, or null when the user cancelled.</summary>
    Task<IReadOnlyList<SowBroadcast.Core.Model.TeamRef>?> PickTeamsAsync(IReadOnlyList<SowBroadcast.Core.Model.TeamRef> teams);
}

/// <summary>Simple message surface (errors and notices) implemented by the window.</summary>
public interface IMessages
{
    Task ShowErrorAsync(string title, string message);
    Task ShowInfoAsync(string title, string message);
}
