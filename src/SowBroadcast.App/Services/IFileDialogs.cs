namespace SowBroadcast.App.Services;

/// <summary>File pickers, kept behind an interface so the view model stays UI-free and testable.</summary>
public interface IFileDialogs
{
    Task<string?> PickOpenStateFileAsync(string? initialDirectory);
    Task<string?> PickSaveStateFileAsync(string? initialDirectory, string suggestedName);
}

/// <summary>Simple message surface (errors and confirmations) implemented by the window.</summary>
public interface IMessages
{
    Task ShowErrorAsync(string title, string message);
}
