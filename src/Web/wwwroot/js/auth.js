// auth.js — Browser-side authentication helpers for cookie-based web auth.
// The fetch request must originate from the browser so that the server can
// write the HttpOnly session cookie into the browser's cookie jar.

// Fallback for non-secure contexts (HTTP) where crypto.randomUUID is unavailable.
function _fallbackUUID() {
    return '10000000-1000-4000-8000-100000000000'.replace(/[018]/g, c =>
        (+c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> +c / 4).toString(16));
}

window.authHelper = {
    /**
     * Sends login credentials to /api/auth/login via browser-side fetch.
     * On success the server sets the edda_session cookie (HttpOnly).
     * @param {string} username
     * @param {string} password
     * @returns {Promise<boolean>} true when login succeeded.
     */
    login: async (username, password) => {
        try {
            const resp = await fetch('/api/auth/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ username, password }),
                credentials: 'include'
            });
            return resp.ok;
        } catch {
            return false;
        }
    },

    /**
     * Sends a password-only login to /api/auth/simple-login via browser-side fetch.
     * Used when BASIC_AUTH_PASSWORD is configured instead of full WEB_AUTH.
     * On success the server sets the edda_session cookie (HttpOnly).
     * @param {string} password
     * @returns {Promise<boolean>} true when login succeeded.
     */
    simpleLogin: async (password) => {
        try {
            const resp = await fetch('/api/auth/simple-login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ password }),
                credentials: 'include'
            });
            return resp.ok;
        } catch {
            return false;
        }
    },

    /**
     * Returns a stable anonymous user ID persisted in localStorage.
     * Creates one on first call and reuses it across page reloads.
     * @returns {string} A stable GUID-based anonymous identifier.
     */
    getAnonId: () => {
        const key = 'edda_anon_id';
        let id = localStorage.getItem(key);
        if (!id) {
            id = typeof crypto.randomUUID === 'function'
                ? crypto.randomUUID.bind(crypto)()
                : _fallbackUUID();
            localStorage.setItem(key, id);
        }
        return id;
    },

    getLastConversationId: () => {
        return localStorage.getItem('edda_last_conv_id') || '';
    },

    setLastConversationId: (id) => {
        if (id) localStorage.setItem('edda_last_conv_id', id);
    }
};
