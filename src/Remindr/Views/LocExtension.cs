using System.Windows.Data;
using System.Windows.Markup;
using Remindr.Services;

namespace Remindr.Views;

/// <summary>
/// <c>{l:Loc Settings_Title}</c>: the text for a resource key, bound to <see cref="Loc.Instance"/> so it changes the moment the
/// language does (no restart, no re-navigation). Works anywhere a binding does (Text, Content, Header, ToolTip, PlaceholderText,
/// AutomationProperties.Name, Run.Text) and inside styles and templates.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding("[" + Key + "]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
