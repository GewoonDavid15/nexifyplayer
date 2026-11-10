namespace Nexify.App;

public static class Program
{
    /// <summary>Entry point (ruling R2: App.xaml is built as a Page). Services are composed in <see cref="App.OnStartup"/>.</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App(args);
        app.InitializeComponent();
        app.Run();
    }
}
