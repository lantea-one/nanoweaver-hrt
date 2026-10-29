/**
 * ============================================================================
 * NanoWeaverHrt (nanoweaver-hrt): Sovereign Browser Session & Idle Watcher
 * ----------------------------------------------------------------------------
 * WHAT: The browser half of the authenticated session: where the Bearer token is
 *       kept, how it is read back, how it is purged, and the DOM activity watcher
 *       that drives the mandatory idle sign-out.
 * WHY:  Prime Directive 12 requires browser sessions to sign out after idle
 *       inactivity, monitored via DOM event heartbeats and validated server-side.
 *       This is security-critical code, so it lives in its own file held to the
 *       same 100% line, branch and function coverage gate as the visualizer
 *       rather than buried in the uncovered dashboard script.
 * HOW:  Plain browser globals (no bundler, no NPM runtime dependency). The token
 *       is stored in sessionStorage plus a session cookie - never localStorage,
 *       which outlives the browsing session - and the watcher reports heartbeats
 *       and expiry to the Blazor circuit, which holds the authoritative clock.
 * ============================================================================
 */

/**
 * Name of the storage key and cookie holding the authenticated Bearer token.
 */
const WEAVER_SESSION_KEY = 'weaver_session';

/**
 * Name of the storage key and cookie holding the active community localization language.
 */
const WEAVER_LANG_KEY = 'weaver_lang';

/**
 * Minimum interval, in milliseconds, between server heartbeats raised from DOM activity.
 */
const HEARTBEAT_THROTTLE_MS = 30000;

/**
 * Default inactivity window, in seconds, applied when the caller supplies none.
 */
const DEFAULT_IDLE_TIMEOUT_SECONDS = 900;

/**
 * DOM events treated as evidence that a human is still present at the terminal.
 */
const ACTIVITY_EVENTS = ['mousemove', 'mousedown', 'keydown', 'scroll', 'touchstart', 'click'];

/**
 * Purges any Bearer token left in localStorage by an earlier release of this platform.
 *
 * WHAT: Deletes the legacy session entry from window.localStorage.
 * WHY:  localStorage outlives the browser session - it survives closing the tab, closing
 *       the browser and rebooting the machine - so a token parked there is a credential
 *       left lying around on a shared lab terminal. Tokens now live in sessionStorage
 *       only, and this clears the copies written before that change.
 * HOW:  Removes the key, ignoring environments where localStorage is unavailable or blocked.
 */
function weaverPurgeLegacySession() {
    try {
        localStorage.removeItem(WEAVER_SESSION_KEY);
    } catch (e) {
        // Private browsing modes and storage-partitioned contexts may deny access entirely
    }
}

/**
 * Persists an authenticated session Bearer token for the lifetime of the browser session only.
 *
 * WHAT: Stores the token in a session cookie plus sessionStorage.
 * WHY:  The cookie is what authenticates /api requests, while sessionStorage is what the
 *       Blazor circuit reads back on first render so a reload or a second tab restores the
 *       session. Neither copy may outlive the browsing session, so the cookie carries no
 *       max-age and localStorage is not used at all.
 * HOW:  Writes the cookie first (so a storage-denied browser still has a usable session),
 *       marks it Secure whenever the page itself was served over HTTPS, then mirrors the
 *       token into sessionStorage and purges any copy left by an earlier release.
 * @param {string} token - The Bearer token string.
 */
function weaverSetSession(token) {
    if (!token) return;
    try {
        const secureAttribute = window.location.protocol === 'https:' ? '; Secure' : '';
        document.cookie = WEAVER_SESSION_KEY + '=' + encodeURIComponent(token) + '; path=/; SameSite=Strict' + secureAttribute;

        sessionStorage.setItem(WEAVER_SESSION_KEY, token);
        weaverPurgeLegacySession();
    } catch (e) {
        console.error('weaverSetSession failed:', e);
    }
}

/**
 * Retrieves the currently active session Bearer token from sessionStorage or the session cookie.
 *
 * WHAT: Reads the token the browser is holding for this session.
 * WHY:  The Blazor circuit needs it on first render to restore a session, and the idle
 *       watcher needs it to name the session it is reporting activity for.
 * HOW:  Prefers sessionStorage, falls back to the cookie (which is how a freshly opened
 *       tab inherits the session), and never consults localStorage.
 * @returns {string|null} The active token if present, otherwise null.
 */
function weaverGetSessionToken() {
    try {
        let token = sessionStorage.getItem(WEAVER_SESSION_KEY);
        if (!token) {
            const match = document.cookie.match(/(?:^|;\s*)weaver_session=([^;]*)/);
            if (match) token = decodeURIComponent(match[1]);
        }
        return token || null;
    } catch (e) {
        return null;
    }
}

/**
 * Clears the active session token from sessionStorage and expires the session cookie.
 *
 * WHAT: Removes every browser-side copy of the Bearer token.
 * WHY:  Sign-out and idle sign-out must leave nothing behind for the next person at the
 *       terminal to reload the page with.
 * HOW:  Removes the sessionStorage entry, purges the legacy localStorage copy, and
 *       back-dates the cookie so the browser drops it.
 */
function weaverClearSession() {
    try {
        sessionStorage.removeItem(WEAVER_SESSION_KEY);
        weaverPurgeLegacySession();
        document.cookie = WEAVER_SESSION_KEY + '=; path=/; expires=Thu, 01 Jan 1970 00:00:00 UTC; SameSite=Strict';
    } catch (e) {
        console.error('weaverClearSession failed:', e);
    }
}

/**
 * Persists the community localization language in sessionStorage and a SameSite=Strict cookie.
 *
 * WHAT: Stores the language tag in a browser session cookie and mirrors it to sessionStorage.
 * WHY:  Work Package P45 requires language preference to persist across page reloads and tab navigations
 *       without requiring server roundtrips or database calls.
 * HOW:  Writes to document.cookie with SameSite=Strict and Secure (when served over HTTPS), then updates
 *       sessionStorage. Ignores empty input and catches storage-denied exceptions gracefully.
 * @param {string} lang - The BCP-47 language tag or identifier (e.g. 'es-ES', 'en-US').
 */
function weaverSetLanguage(lang) {
    if (!lang) return;
    try {
        const secureAttribute = window.location.protocol === 'https:' ? '; Secure' : '';
        document.cookie = WEAVER_LANG_KEY + '=' + encodeURIComponent(lang) + '; path=/; SameSite=Strict' + secureAttribute;
        sessionStorage.setItem(WEAVER_LANG_KEY, lang);
    } catch (e) {
        console.error('weaverSetLanguage failed:', e);
    }
}

/**
 * Retrieves the persisted community localization language from sessionStorage or the session cookie.
 *
 * WHAT: Reads the active language preference stored in the browser.
 * WHY:  Enables Blazor components (LanguageSelector, MainLayout) to restore user language on first render.
 * HOW:  Prefers sessionStorage for instant retrieval, falls back to document.cookie, and returns null if not set.
 * @returns {string|null} The persisted language tag if found; otherwise, null.
 */
function weaverGetLanguage() {
    try {
        let lang = sessionStorage.getItem(WEAVER_LANG_KEY);
        if (!lang) {
            const match = document.cookie.match(/(?:^|;\s*)weaver_lang=([^;]*)/);
            if (match) lang = decodeURIComponent(match[1]);
        }
        return lang || null;
    } catch (e) {
        return null;
    }
}

/**
 * Registers an idle activity monitor that reports interactions and expiry to the server.
 *
 * WHAT: Attaches passive DOM listeners and arms an inactivity timer.
 * WHY:  Prime Directive 12's idle sign-out is monitored through these DOM heartbeats. The
 *       browser deliberately does not decide anything: it reports activity, and the server
 *       holds the authoritative idle clock that can actually refuse the token.
 * HOW:  Replaces any previous watcher, throttles heartbeats to one every thirty seconds,
 *       and rearms a timer on every interaction. When the timer finally fires it hands the
 *       sign-out to the circuit, falling back to purging client state and redirecting if
 *       that circuit is gone.
 * @param {object} dotNetHelper - DotNetObjectReference to MainLayout or SessionState.
 * @param {number} timeoutSeconds - Inactivity threshold in seconds (default: 900s = 15 minutes).
 */
function weaverRegisterIdleWatcher(dotNetHelper, timeoutSeconds) {
    if (window._weaverIdleCleanup) {
        window._weaverIdleCleanup();
    }

    const timeoutMs = (timeoutSeconds && timeoutSeconds > 0 ? timeoutSeconds : DEFAULT_IDLE_TIMEOUT_SECONDS) * 1000;
    let timerId = null;
    let lastNotified = Date.now();

    /**
     * Determines whether the supplied .NET object reference can still be invoked.
     * @returns {boolean} True when a usable circuit callback is available.
     */
    function hasLiveCircuit() {
        return !!dotNetHelper && typeof dotNetHelper.invokeMethodAsync === 'function';
    }

    /**
     * Handles any evidence of user interaction, rearming the inactivity timer.
     */
    function onActivity() {
        const now = Date.now();

        // WHAT: Throttled server heartbeat.
        // WHY:  The server extends the session only on recorded activity, but a mousemove
        //       stream must not become an interop storm; one heartbeat every thirty
        //       seconds keeps an active researcher signed in at negligible cost.
        // HOW:  Compares against the last notification timestamp before invoking .NET.
        if (now - lastNotified > HEARTBEAT_THROTTLE_MS) {
            lastNotified = now;
            if (hasLiveCircuit()) {
                dotNetHelper.invokeMethodAsync('RecordActivity').catch(() => {
                    // A disconnected circuit simply misses this heartbeat; the server
                    // clock is authoritative and will expire the session on its own.
                });
            }
        }

        if (timerId) {
            clearTimeout(timerId);
        }

        timerId = setTimeout(onIdleExpiry, timeoutMs);
    }

    /**
     * Handles the inactivity timer firing by signing the user out.
     */
    function onIdleExpiry() {
        // WHAT: Hands the expiry to the server, falling back to a hard redirect.
        // WHY:  The server owns the sign-out because only it can revoke the token; the
        //       fallback exists for a dead circuit and must still purge the browser's copy
        //       so the next person at this terminal cannot simply reload the page.
        // HOW:  Invokes the .NET callback, and on any failure clears client state first.
        if (hasLiveCircuit()) {
            dotNetHelper.invokeMethodAsync('OnUserIdleTimeout').catch(() => {
                weaverClearSession();
                window.location.href = '/login?reason=idle_timeout';
            });
            return;
        }

        weaverClearSession();
        window.location.href = '/login?reason=idle_timeout';
    }

    ACTIVITY_EVENTS.forEach(eventName => window.addEventListener(eventName, onActivity, { passive: true }));

    // Arm the initial timer. Deliberately raises no heartbeat: registering a watcher is
    // not activity, so it must not extend a session the server already considers idle.
    onActivity();

    window._weaverIdleCleanup = function () {
        if (timerId) {
            clearTimeout(timerId);
        }

        ACTIVITY_EVENTS.forEach(eventName => window.removeEventListener(eventName, onActivity));
        window._weaverIdleCleanup = null;
    };
}

/**
 * Unregisters the idle activity monitor and clears any pending timeout timers.
 *
 * WHAT: Tears down the watcher installed by weaverRegisterIdleWatcher.
 * WHY:  A signed-out circuit must stop listening, and a re-registration must never
 *       stack a second watcher on top of the first.
 * HOW:  Invokes the stored cleanup closure when one exists.
 */
function weaverUnregisterIdleWatcher() {
    if (window._weaverIdleCleanup) {
        window._weaverIdleCleanup();
    }
}

/**
 * Public surface of the session module, exposed as browser globals for Blazor JS interop.
 */
const WeaverSession = {
    weaverPurgeLegacySession,
    weaverSetSession,
    weaverGetSessionToken,
    weaverClearSession,
    weaverRegisterIdleWatcher,
    weaverUnregisterIdleWatcher,
    weaverSetLanguage,
    weaverGetLanguage
};

window.weaverPurgeLegacySession = weaverPurgeLegacySession;
window.weaverSetSession = weaverSetSession;
window.weaverGetSessionToken = weaverGetSessionToken;
window.weaverClearSession = weaverClearSession;
window.weaverRegisterIdleWatcher = weaverRegisterIdleWatcher;
window.weaverUnregisterIdleWatcher = weaverUnregisterIdleWatcher;
window.weaverSetLanguage = weaverSetLanguage;
window.weaverGetLanguage = weaverGetLanguage;

if (typeof module !== 'undefined') {
    module.exports = WeaverSession;
}
