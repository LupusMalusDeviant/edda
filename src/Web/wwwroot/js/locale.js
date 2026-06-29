// Locale management for Edda / Edda.
// Persists language preference ('de' or 'en') in localStorage.
// Sets data-locale on <html> for potential CSS-side use.
window.localeManager = {

    /**
     * Reads the saved locale from localStorage and marks it on <html>.
     * @returns {string} The active locale code ('de' or 'en').
     */
    getLocale: function () {
        return localStorage.getItem('ag-locale') ?? 'de';
    },

    /**
     * Persists the given locale and marks it on <html>.
     * @param {string} locale - 'de' or 'en'.
     */
    setLocale: function (locale) {
        localStorage.setItem('ag-locale', locale);
        document.documentElement.setAttribute('data-locale', locale);
    }
};
