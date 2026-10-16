namespace SowBroadcast.App.Services;

/// <summary>File pickers, kept behind an interface so the view models stay UI-free and testable.</summary>
public interface IFileDialogs
{
    Task<string?> PickOpenStateFileAsync(string? initialDirectory);
    Task<string?> PickSaveStateFileAsync(string? initialDirectory, string suggestedName);
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
