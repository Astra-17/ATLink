using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace ATLink.Core;

/// <summary>Interface resources only. Never changes numeric, date or file cultures.</summary>
public sealed class UiText : INotifyPropertyChanged
{
    public static UiText Current { get; } = new();
    readonly IReadOnlyDictionary<string, string> english;
    readonly IReadOnlyDictionary<string, string> french;
    readonly IReadOnlyDictionary<string, string> sourceKeys;
    static readonly ConditionalWeakTable<string, Message> messages = new();
    string language = "en";
    public string Language => language;
    public CultureInfo DisplayCulture => CultureInfo.GetCultureInfo(language=="fr"?"fr-FR":"en-GB");
    public string this[string key] => Get(key);
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    UiText()
    {
        english = Load("en"); french = Load("fr"); sourceKeys=Load("keys");
    }

    static IReadOnlyDictionary<string, string> Load(string code)
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"ATLink.Core.Resources.Interface.{code}.json")
            ?? throw new InvalidOperationException($"Missing interface resources: {code}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    public static string ResolveLanguage(string? selected, string? legacy, CultureInfo? system = null) =>
        selected is "fr" or "en" ? selected : legacy is "fr" or "fre_fr" ? "fr" :
        legacy is "en" or "eng_us" ? "en" : (system ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "fr" ? "fr" : "en";
    public static string LocalizationCode(string code) => code == "fr" ? "fre_fr" : "eng_us";
    public static string ResourceKey(string source) => "T"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..14];

    public void SetLanguage(string code)
    {
        if (code is not ("fr" or "en")) throw new ArgumentException("Unsupported interface language.", nameof(code));
        if (language == code) return;
        language = code;
        PropertyChanged?.Invoke(this, new("Item[]"));
        PropertyChanged?.Invoke(this, new(nameof(Language)));
        PropertyChanged?.Invoke(this, new(nameof(DisplayCulture)));
        LanguageChanged?.Invoke();
    }

    public static string Get(string key) => Remember(messages.TryGetValue(key,out var message)?message:new(key, []));
    /// <summary>Stable identity for legacy choices whose values used to be labels.
    /// Ordinary strings and user data retain their original value and reference.</summary>
    [return:NotNullIfNotNull(nameof(value))]
    public static string? Source(string? value)=>value is not null&&messages.TryGetValue(value,out var message)&&message.Arguments.Length==0
        ?Current.sourceKeys.GetValueOrDefault(message.Key)??message.Key:value;
    public static object? Source(object? value)=>value is string text?Source(text):value;
    public static string Format(string key, params object?[] arguments) => Remember(new(key, arguments));
    public static string Concat(params object?[] parts) => Format(string.Concat(Enumerable.Range(0,parts.Length).Select(i=>"{"+i+"}")),parts);
    public static string Join(string separator,IEnumerable<string?> values)
    {
        var parts=values.ToArray();
        return Format(string.Join(separator.Replace("{","{{").Replace("}","}}"),Enumerable.Range(0,parts.Length).Select(i=>"{"+i+"}")),parts.Cast<object?>().ToArray());
    }
    public static string Join(string separator,params string?[] values)=>Join(separator,(IEnumerable<string?>)values);
    public static string Join<T>(string separator,IEnumerable<T> values)=>Join(separator,values.Select(value=>value?.ToString()));
    public static string Join<T>(char separator,IEnumerable<T> values)=>Join(separator.ToString(),values);
    public static string Plural(int count, string singular, string plural) => Format(count == 1 ? singular : plural, count);
    static string Remember(Message message)
    {
        // A distinct string reference carries its resource identity into WPF, without
        // ever treating a user's identical text as an interface resource.
        string result = new(message.Render().AsSpan());
        if (result.Length > 0) messages.Add(result, message);
        return result;
    }
    public static bool TryGetMessage(string value, out Message? message) => messages.TryGetValue(value, out message);
    public static IReadOnlyDictionary<string, string> Resources(string code) => code == "fr" ? Current.french : Current.english;

    public sealed record Message(string Key, object?[] Arguments)
    {
        public string Render()
        {
            var resources = Current.language == "fr" ? Current.french : Current.english;
            string lookup=resources.ContainsKey(Key)?Key:ResourceKey(Key);
            string template = resources.GetValueOrDefault(lookup) ?? Current.english.GetValueOrDefault(lookup) ?? Key;
            var args = Arguments.Select(a => a is string text && TryGetMessage(text, out var nested) ? nested!.Render() : a).ToArray();
            return args.Length == 0 ? template : string.Format(DisplayFormatter.Instance, template, args);
        }
    }

    sealed class DisplayFormatter:IFormatProvider,ICustomFormatter
    {
        public static DisplayFormatter Instance {get;}=new();
        public object? GetFormat(Type? formatType)=>formatType==typeof(ICustomFormatter)?this:CultureInfo.CurrentCulture.GetFormat(formatType!);
        public string Format(string? format,object? arg,IFormatProvider? provider)
        {
            var culture=arg is DateTime or DateOnly&&format is not null&&(format.Contains("MMM")||format.Contains("ddd"))?Current.DisplayCulture:CultureInfo.CurrentCulture;
            return arg is IFormattable formattable?formattable.ToString(format,culture):arg?.ToString()??"";
        }
    }
}
