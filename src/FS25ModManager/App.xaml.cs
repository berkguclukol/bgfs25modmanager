using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace FS25ModManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        ApplicationAccentColorManager.Apply(Color.FromRgb(0x81, 0xC3, 0x32), ApplicationTheme.Dark);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
