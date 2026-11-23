using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArchScada.Desktop.Views;

public partial class SplashView : UserControl
{
    public SplashView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}