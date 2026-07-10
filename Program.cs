namespace MixTray;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var config = Config.Load();

        using var dialog = new SchemeSelectorDialog();
        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        int schemeIndex = dialog.SelectedScheme;
        config.SchemeIndex = schemeIndex;
        config.Save();

        Application.Run(new MainForm(config));
    }
}
