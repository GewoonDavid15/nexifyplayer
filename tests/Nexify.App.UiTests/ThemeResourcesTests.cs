using System.IO.Packaging;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Nexify.App.Services;
using Nexify.Core.Settings;
using Xunit;

namespace Nexify.App.UiTests;

/// <summary>Loads the theme dictionaries in-process and instantiates every control template (catches XAML errors the compiler cannot).</summary>
public class ThemeResourcesTests
{
    private static readonly string[] BrushKeys =
    [
        "Bg.Base", "Bg.Surface", "Bg.SurfaceHover", "Border.Subtle", "Text.Primary", "Text.Body", "Text.Muted",
        "Accent", "Accent.OnAccent", "Accent.Text", "Accent.Soft", "Status.Success", "Status.Error",
    ];

    private static Uri Theme(string file) => new($"pack://application:,,,/NexifyPlayer;component/Themes/{file}", UriKind.Absolute);

    /// <summary>Registers pack://application: without creating an Application (its static constructor does it).</summary>
    private static void EnsurePackScheme()
    {
        _ = PackUriHelper.UriSchemePack;
        RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
    }

    private static ResourceDictionary Load(string file)
    {
        EnsurePackScheme();
        return new ResourceDictionary { Source = Theme(file) };
    }

    private static ResourceDictionary AppResources()
    {
        var root = new ResourceDictionary();
        root.MergedDictionaries.Add(Load("Tokens.Dark.xaml"));
        root.MergedDictionaries.Add(Load("Typography.xaml"));
        root.MergedDictionaries.Add(Load("Controls.xaml"));
        return root;
    }

    [WindowsOnlyFact]
    public void Tokens_HaveAllKeys_WithSpecValues() => Sta.Run(() =>
    {
        var dark = Load("Tokens.Dark.xaml");
        var light = Load("Tokens.Light.xaml");
        foreach (var key in BrushKeys)
        {
            Assert.IsType<SolidColorBrush>(dark[key]);
            Assert.IsType<SolidColorBrush>(light[key]);
        }
        Assert.IsType<LinearGradientBrush>(dark["Accent.Gradient"]);
        Assert.IsType<LinearGradientBrush>(light["Accent.Gradient"]);

        Color C(ResourceDictionary d, string k) => ((SolidColorBrush)d[k]).Color;
        Assert.Equal(Color.FromRgb(0x0A, 0x0C, 0x10), C(dark, "Bg.Base"));
        Assert.Equal(Color.FromRgb(0xF6, 0xF7, 0xF9), C(light, "Bg.Base"));
        Assert.Equal(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), C(dark, "Border.Subtle"));
        Assert.Equal(Color.FromRgb(0x5F, 0x7A, 0x0E), C(light, "Accent.Text"));
        Assert.Equal(Color.FromArgb(0x40, 0xC6, 0xF4, 0x32), C(light, "Accent.Soft"));
    });

    [WindowsOnlyFact]
    public void Typography_HasSizesAndFonts() => Sta.Run(() =>
    {
        var t = Load("Typography.xaml");
        Assert.Equal(28d, t["Size.Display"]);
        Assert.Equal(20d, t["Size.Title"]);
        Assert.Equal(15d, t["Size.Subtitle"]);
        Assert.Equal(14d, t["Size.Body"]);
        Assert.Equal(12d, t["Size.Caption"]);
        Assert.IsType<FontFamily>(t["Font.Heading"]);
        Assert.IsType<FontFamily>(t["Font.Body"]);
    });

    [WindowsOnlyFact]
    public void EmbeddedFonts_ExposeInterAndArchivoFamilies() => Sta.Run(() =>
    {
        EnsurePackScheme();
        var families = Fonts.GetFontFamilies(new Uri("pack://application:,,,/NexifyPlayer;component/Assets/Fonts/"))
            .SelectMany(f => f.FamilyNames.Values).ToHashSet();
        Assert.True(families.Contains("Inter") && families.Contains("Archivo"), "Families found: " + string.Join(", ", families));
    });

    [WindowsOnlyFact]
    public void ControlStyles_ApplyTemplates() => Sta.Run(() =>
    {
        var res = AppResources();
        var host = new StackPanel { Resources = res };
        var styled = new List<FrameworkElement>();

        void Add(FrameworkElement e, string styleKey)
        {
            e.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
            host.Children.Add(e);
            styled.Add(e);
        }

        Add(new Button { Content = "A" }, "Button.Accent");
        Add(new Button { Content = "S" }, "Button.Subtle");
        Add(new Button { Content = "" }, "Button.Icon");
        Add(new ToggleButton { Content = "", IsChecked = true }, "Button.Icon");
        Add(new Button { Content = "" }, "Button.PlayRound");
        Add(new Border(), "Card");
        Add(new Slider { Minimum = 0, Maximum = 100, Value = 40, Width = 200 }, "Slider.Nexify");
        var nav = new ListBox();
        nav.Items.Add(new ListBoxItem { Content = "Home", IsSelected = true });
        nav.Items.Add(new ListBoxItem { Content = "Settings" });
        foreach (ListBoxItem item in nav.Items)
        {
            item.SetResourceReference(FrameworkElement.StyleProperty, "ListBoxItem.Nav");
            styled.Add(item);
        }
        host.Children.Add(nav);
        Add(new RadioButton { Content = "Dark", IsChecked = true }, "RadioButton.Segmented");
        var combo = new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
        Add(combo, "ComboBox.Nexify");
        Add(new ScrollBar { Orientation = Orientation.Vertical, Height = 100 }, "ScrollBar.Thin");
        Add(new ScrollBar { Orientation = Orientation.Horizontal, Width = 100 }, "ScrollBar.Thin");
        Add(new TextBlock { Text = "Title" }, "TextBlock.Title");
        var queue = new ListBox();
        queue.Items.Add(new ListBoxItem { Content = "Current", Tag = true });
        queue.Items.Add(new ListBoxItem { Content = "Other", Tag = false, IsSelected = true });
        foreach (ListBoxItem item in queue.Items)
        {
            item.SetResourceReference(FrameworkElement.StyleProperty, "ListBoxItem.Queue");
            styled.Add(item);
        }
        host.Children.Add(queue);
        var menu = new ContextMenu { Resources = res };
        menu.SetResourceReference(FrameworkElement.StyleProperty, "ContextMenu.Nexify");
        var menuItem = new MenuItem { Header = "Remove", Icon = new TextBlock { Text = "\uE74D" } };
        menuItem.SetResourceReference(FrameworkElement.StyleProperty, "MenuItem.Nexify");
        menu.Items.Add(menuItem);

        host.Measure(new Size(800, 2000));
        host.Arrange(new Rect(0, 0, 800, 2000));
        host.UpdateLayout();

        foreach (var element in styled)
        {
            Assert.True(element.Style is not null, $"{element.GetType().Name} did not get its style");
            if (element is Control c) Assert.True(VisualTreeHelper.GetChildrenCount(c) > 0, $"{c.GetType().Name} has no template");
        }
        combo.IsDropDownOpen = true;
        combo.IsDropDownOpen = false;

        // The context menu lives in its own popup: open it to instantiate the menu and item templates.
        menu.PlacementTarget = host;
        menu.IsOpen = true;
        menu.UpdateLayout();
        Assert.True(menu.Style is not null && menuItem.Style is not null, "context menu styles not applied");
        Assert.True(VisualTreeHelper.GetChildrenCount(menu) > 0, "ContextMenu has no template");
        Assert.True(VisualTreeHelper.GetChildrenCount(menuItem) > 0, "MenuItem has no template");
        menu.IsOpen = false;
    });

    [WindowsOnlyFact]
    public void ThemeService_ReplacesTokenDictionaryInPlace() => Sta.Run(() =>
    {
        var res = AppResources();
        var typography = res.MergedDictionaries[1];
        var probe = new Border();
        probe.Resources = res;
        probe.SetResourceReference(Border.BackgroundProperty, "Bg.Base");

        using var service = new ThemeService(res, Dispatcher.CurrentDispatcher);
        var raised = 0;
        service.EffectiveThemeChanged += (_, _) => raised++;

        service.Apply(ThemeMode.Light);
        Assert.False(service.IsDarkEffective);
        Assert.Equal(ThemeMode.Light, service.Mode);
        Assert.EndsWith("Tokens.Light.xaml", res.MergedDictionaries[0].Source.OriginalString);
        Assert.Same(typography, res.MergedDictionaries[1]);
        Assert.Equal(3, res.MergedDictionaries.Count);
        Assert.Equal(Color.FromRgb(0xF6, 0xF7, 0xF9), ((SolidColorBrush)probe.Background).Color);
        Assert.Equal(1, raised);

        service.Apply(ThemeMode.Dark);
        Assert.True(service.IsDarkEffective);
        Assert.EndsWith("Tokens.Dark.xaml", res.MergedDictionaries[0].Source.OriginalString);
        Assert.Equal(Color.FromRgb(0x0A, 0x0C, 0x10), ((SolidColorBrush)probe.Background).Color);
        Assert.Equal(2, raised);
        Assert.IsType<bool>(res["Anim.Enabled"]);
    });
}
