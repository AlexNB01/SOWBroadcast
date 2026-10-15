using Avalonia;
using Avalonia.Controls;

namespace SowBroadcast.App.Views;

public partial class Placeholder : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<Placeholder, string>(nameof(Title), "");

    public static readonly StyledProperty<string> MilestoneProperty =
        AvaloniaProperty.Register<Placeholder, string>(nameof(Milestone), "");

    public Placeholder() => InitializeComponent();

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Milestone { get => GetValue(MilestoneProperty); set => SetValue(MilestoneProperty, value); }
}
