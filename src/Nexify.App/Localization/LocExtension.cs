using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using Nexify.Core.Localization;

namespace Nexify.App.Localization;

/// <summary>
/// Bindable access to the localization service: <c>LocProxy.Instance["nav.home"]</c>. Raises
/// <c>PropertyChanged("Item[]")</c> on <see cref="ILocalizationService.LanguageChanged"/>, so every
/// <c>{loc:T key}</c> binding refreshes live.
/// </summary>
public sealed class LocProxy : INotifyPropertyChanged
{
    private const string IndexerName = "Item[]";

    private ILocalizationService? _service;

    private LocProxy()
    {
    }

    public static LocProxy Instance { get; } = new();

    /// <summary>The service in use; null at design time or before startup, in which case keys are shown as-is.</summary>
    public ILocalizationService? Service => _service;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => _service?[key] ?? key;

    /// <summary>Sets the service once at startup (later calls replace it, e.g. in tests).</summary>
    public static void Initialize(ILocalizationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        var proxy = Instance;
        if (proxy._service is not null)
        {
            proxy._service.LanguageChanged -= proxy.OnLanguageChanged;
        }

        proxy._service = service;
        service.LanguageChanged += proxy.OnLanguageChanged;
        proxy.OnLanguageChanged(service, EventArgs.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
}

/// <summary>
/// Live localized text: <c>{loc:Loc nav.home}</c>. Produces a one-way <see cref="Binding"/> to the
/// <see cref="LocProxy"/> indexer, so the text follows language switches.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => ProvideBinding().ProvideValue(serviceProvider);

    /// <summary>The live binding for <see cref="Key"/>, for use from code (<c>SetBinding</c>).</summary>
    public Binding ProvideBinding() => new($"[{Key}]")
    {
        Source = LocProxy.Instance,
        Mode = BindingMode.OneWay,
        FallbackValue = Key,
    };
}

/// <summary>Short form used in XAML: <c>{loc:T nav.home}</c>.</summary>
public sealed class TExtension : LocExtension
{
    public TExtension()
    {
    }

    public TExtension(string key)
        : base(key)
    {
    }
}
