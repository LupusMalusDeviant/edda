// Theme management for Edda.
// Supports 'dark' and 'light' themes, persisted in localStorage.
// Applies data-theme and data-bs-theme on <html> for both custom vars and Bootstrap 5 dark mode.
window.themeManager = {

    /** @type {Record<string, cytoscape.Core>} */
    _cy: {},

    /**
     * Reads the saved theme from localStorage and applies it.
     * Called on first Blazor render; the inline flash-prevention script in <head>
     * already did this before JS loaded, so this is a no-op in practice but safe to call.
     * @returns {string} The active theme name ('dark' or 'light').
     */
    init: function () {
        const saved = localStorage.getItem('ag-theme') ?? 'dark';
        this._apply(saved);
        return saved;
    },

    /**
     * Toggles between 'dark' and 'light', persists the choice, and returns the new name.
     * @returns {string} The new theme name.
     */
    toggle: function () {
        const current = document.documentElement.getAttribute('data-theme') ?? 'dark';
        const next = current === 'dark' ? 'light' : 'dark';
        this._apply(next);
        localStorage.setItem('ag-theme', next);
        return next;
    },

    /**
     * Returns the current theme name without modifying anything.
     * @returns {string} 'dark' or 'light'.
     */
    getTheme: function () {
        return document.documentElement.getAttribute('data-theme') ?? 'dark';
    },

    /**
     * Applies a theme by setting data-theme and data-bs-theme on <html>.
     * @param {string} theme - 'dark' or 'light'.
     */
    _apply: function (theme) {
        document.documentElement.setAttribute('data-theme', theme);
        document.documentElement.setAttribute('data-bs-theme', theme);
    }
};
