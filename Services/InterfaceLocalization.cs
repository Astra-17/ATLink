using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using ATLink.Core;

namespace ATLink.Services;

/// <summary>Live resource binding for XAML, including template and style setters.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding($"[{Key}]")
        { Source = UiText.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

/// <summary>Updates resource-backed strings created in C# without rebuilding controls
/// or touching editable text, user data, selections or source bindings.</summary>
public static class InterfaceLocalization
{
    static bool initialized;
    static readonly List<WeakReference<Observer>> observers = [];
    static readonly DependencyProperty ObserverProperty = DependencyProperty.RegisterAttached(
        "Observer", typeof(List<Observer>), typeof(InterfaceLocalization));

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(Loaded));
        UiText.Current.LanguageChanged += Refresh;
    }

    static void Loaded(object sender, RoutedEventArgs e)
    {
        if(sender is not FrameworkElement element)return;
        AttachTree(element);
        if(element is Window window)
        {
            window.LayoutUpdated-=WindowLayoutUpdated;
            window.LayoutUpdated+=WindowLayoutUpdated;
            window.Closed-=WindowClosed;
            window.Closed+=WindowClosed;
        }
    }

    static void WindowLayoutUpdated(object? sender,EventArgs e)
    {
        // LayoutUpdated has no sender. Scanning live roots also covers generated
        // item templates: WPF can suppress Loaded broadcasts on plain TextBlocks.
        foreach(Window window in Application.Current.Windows)if(window.IsLoaded)AttachTree(window);
    }
    static void WindowClosed(object? sender,EventArgs e)
    {
        if(sender is Window window){window.LayoutUpdated-=WindowLayoutUpdated;window.Closed-=WindowClosed;}
    }
    static void AttachTree(DependencyObject root)
    {
        if(root is FrameworkElement element)Attach(element);
        if(root is not Visual && root is not System.Windows.Media.Media3D.Visual3D)return;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)AttachTree(VisualTreeHelper.GetChild(root,i));
    }
    static void Attach(FrameworkElement element)
    {
        if(element is DatePicker picker)picker.Language=XmlLanguage.GetLanguage(UiText.Current.DisplayCulture.IetfLanguageTag);
        if(element.GetValue(ObserverProperty) is not null)return;
        List<DependencyProperty> properties = [];
        foreach(var property in new[]{FrameworkElement.ToolTipProperty,System.Windows.Automation.AutomationProperties.NameProperty})
            if(element.GetValue(property) is string || BindingOperations.IsDataBound(element,property))properties.Add(property);
        if (element is TextBlock) properties.Add(TextBlock.TextProperty);
        if (element is ContentControl && element is not TextBoxBase) properties.Add(ContentControl.ContentProperty);
        if (element is HeaderedContentControl) properties.Add(HeaderedContentControl.HeaderProperty);
        if (element is HeaderedItemsControl) properties.Add(HeaderedItemsControl.HeaderProperty);
        if (element is Window) properties.Add(Window.TitleProperty);
        var list = properties.Distinct().Select(p => new Observer(element, p)).ToList();
        element.SetValue(ObserverProperty, list);
        element.Unloaded += Unloaded;
    }

    static void Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.GetValue(ObserverProperty) is List<Observer> list) foreach (var observer in list) observer.Dispose();
        element.ClearValue(ObserverProperty);
        element.Unloaded -= Unloaded;
    }

    static void Refresh()
    {
        if (Application.Current is { } app && !app.Dispatcher.CheckAccess()) { app.Dispatcher.Invoke(Refresh); return; }
        if(Application.Current is {} current)foreach(Window window in current.Windows)if(window.IsLoaded)AttachTree(window);
        observers.RemoveAll(reference => !reference.TryGetTarget(out var observer) || observer.Disposed);
        foreach (var reference in observers.ToArray()) if (reference.TryGetTarget(out var observer)) observer.Refresh();
    }

    sealed class Observer : IDisposable
    {
        readonly FrameworkElement target;
        readonly DependencyProperty property;
        readonly DependencyPropertyDescriptor? descriptor;
        UiText.Message? message;
        bool updating;
        public bool Disposed { get; private set; }
        public Observer(FrameworkElement target, DependencyProperty property)
        {
            this.target = target; this.property = property;
            descriptor = DependencyPropertyDescriptor.FromProperty(property, target.GetType());
            descriptor?.AddValueChanged(target, Changed);
            observers.Add(new(this)); Changed(null, EventArgs.Empty);
        }
        void Changed(object? sender, EventArgs e)
        {
            if (updating || Disposed) return;
            message = target.GetValue(property) is string text && UiText.TryGetMessage(text, out var resource) ? resource : null;
            Refresh();
        }
        public void Refresh()
        {
            if (Disposed || updating) return;
            if(BindingOperations.GetBindingExpression(target,property) is {} binding)
            {
                updating=true;
                try { binding.UpdateTarget(); }
                finally { updating=false; }
                message=target.GetValue(property) is string text&&UiText.TryGetMessage(text,out var resource)?resource:null;
            }
            if(message is null)return;
            updating = true;
            try { target.SetCurrentValue(property, UiText.Format(message.Key,message.Arguments)); }
            finally { updating = false; }
        }
        public void Dispose() { Disposed = true; descriptor?.RemoveValueChanged(target, Changed); }
    }
}
