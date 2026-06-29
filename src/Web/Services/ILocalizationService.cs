namespace Edda.Web.Services;

/// <summary>
/// Provides UI string localisation for the Blazor frontend.
/// Supports German (de, default) and English (en).
/// Registered as scoped so each Blazor circuit (user session) has an independent language preference.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Active language code: 'de' (German, default) or 'en' (English).</summary>
    string Language { get; }

    /// <summary>
    /// Returns the localised string for the given key.
    /// Falls back to the key itself when the key is not found.
    /// </summary>
    string this[string key] { get; }

    /// <summary>Fired on the UI thread after the active language has changed.</summary>
    event Action OnLanguageChanged;

    /// <summary>
    /// Sets the active language and fires <see cref="OnLanguageChanged"/>
    /// so all subscribed Blazor components re-render.
    /// </summary>
    /// <param name="languageCode">'de' or 'en'.</param>
    void SetLanguage(string languageCode);
}
