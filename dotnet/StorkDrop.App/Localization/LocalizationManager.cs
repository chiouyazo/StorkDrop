using System.Windows;

namespace StorkDrop.App.Localization;

public static class LocalizationManager
{
    private static readonly Dictionary<string, Uri> LanguageResources = new()
    {
        ["en"] = new Uri("Localization/Strings.en.xaml", UriKind.Relative),
        ["de"] = new Uri("Localization/Strings.de.xaml", UriKind.Relative),
    };

    private static string _currentLanguage = "en";
    private static ResourceDictionary? _currentDictionary;

    public static string Language
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage == value || !LanguageResources.ContainsKey(value))
                return;
            _currentLanguage = value;
            ApplyLanguage();
        }
    }

    public static IReadOnlyList<string> AvailableLanguages { get; } = new List<string> { "en", "de" };

    public static void Initialize(string language = "en")
    {
        _currentLanguage = LanguageResources.ContainsKey(language) ? language : "en";
        ApplyLanguage();
    }

    public static string GetString(string key)
    {
        return Application.Current.TryFindResource(key) as string ?? key;
    }

    public static string GetString(string key, params object[] args)
    {
        string template = GetString(key);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private static void ApplyLanguage()
    {
        if (!LanguageResources.TryGetValue(_currentLanguage, out Uri? uri))
            return;

        ResourceDictionary newDictionary = new() { Source = uri };
        var mergedDicts = Application.Current.Resources.MergedDictionaries;

        if (_currentDictionary is not null)
            mergedDicts.Remove(_currentDictionary);

        mergedDicts.Add(newDictionary);
        _currentDictionary = newDictionary;
    }
}
