using System.Collections.ObjectModel;
using System.Windows;

namespace StorkDrop.App.Localization;

public static class LocalizationManager
{
    private static readonly Dictionary<string, Uri> LanguageResources = new Dictionary<string, Uri>
    {
        ["en"] = new Uri(
            "pack://application:,,,/StorkDrop.App;component/Localization/Strings.en.xaml"
        ),
        ["de"] = new Uri(
            "pack://application:,,,/StorkDrop.App;component/Localization/Strings.de.xaml"
        ),
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

    public static IReadOnlyList<string> AvailableLanguages { get; } =
        new List<string> { "en", "de" };

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

        ResourceDictionary newDictionary = new ResourceDictionary { Source = uri };
        Collection<ResourceDictionary> mergedDicts = Application
            .Current
            .Resources
            .MergedDictionaries;

        if (_currentDictionary is not null)
            mergedDicts.Remove(_currentDictionary);

        mergedDicts.Add(newDictionary);
        _currentDictionary = newDictionary;
    }
}
