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
}

/// <summary>Picks one image file (png, jpg, jpeg or webp).</summary>
public interface IImagePicker
{
    Task<string?> PickImageAsync(string title, string? initialDirectory = null);
}

/// <summary>Simple message surface (errors and notices) implemented by the window.</summary>
public interface IMessages
{
    Task ShowErrorAsync(string title, string message);
    Task ShowInfoAsync(string title, string message);
}
