/**
 * ============================================================================
 * Weaver-Session Test Suite: 100% Line, Branch & Function Coverage Verification
 * ----------------------------------------------------------------------------
 * WHAT: Unit test suite for the browser session helpers and the DOM idle watcher:
 *       token storage, cookie attributes, legacy localStorage purging, heartbeat
 *       throttling and idle expiry hand-off.
 * WHY:  This is security-critical code behind Prime Directive 12. The two properties
 *       that fix the reload bypass - the token never reaching localStorage, and the
 *       cookie dying with the browser session - must stay pinned by the coverage gate,
 *       not merely by today's behaviour.
 * HOW:  Node.js native test runner with a mock DOM: a cookie jar with real expiry
 *       semantics, mock Web Storage that can be told to deny access, and controllable
 *       Date.now / setTimeout so no assertion depends on wall-clock time.
 * ============================================================================
 */

import test from 'node:test';
import assert from 'node:assert/strict';

// WHAT: Creates a minimal in-memory Web Storage implementation.
// WHY:  Permits headless testing of token persistence, and lets a test simulate the
//       storage-denied browsers (private mode, partitioned contexts) that the module
//       must survive without throwing.
// HOW:  Backs getItem/setItem/removeItem with a Map, optionally throwing on every call.
function createStorage(deny = false) {
    const map = new Map();
    return {
        writes: [],
        removals: [],
        getItem(key) {
            if (deny) throw new Error('storage denied');
            return map.has(key) ? map.get(key) : null;
        },
        setItem(key, value) {
            if (deny) throw new Error('storage denied');
            this.writes.push(key);
            map.set(key, value);
        },
        removeItem(key) {
            if (deny) throw new Error('storage denied');
            this.removals.push(key);
            map.delete(key);
        }
    };
}

// WHAT: Mutable mock DOM state shared by every test in this file.
// WHY:  The module under test binds window and document once at load time, exactly as a
//       browser script does, so the globals must be installed before the import and then
//       mutated rather than replaced.
// HOW:  A cookie jar honouring the epoch-expiry convention, a listener registry, a timer
//       registry, and a Date.now the tests advance by hand.
const cookieJar = new Map();
const listeners = new Map();
const timers = [];
const errors = [];
let fakeNow = 1700000000000;

globalThis.document = {
    readyState: 'complete',
    addEventListener: () => {},
    lastCookieAttributes: [],
    get cookie() {
        return [...cookieJar.entries()].map(([name, value]) => `${name}=${value}`).join('; ');
    },
    set cookie(raw) {
        const parts = raw.split(';').map(part => part.trim());
        const pair = parts[0];
        const attributes = parts.slice(1);
        const separator = pair.indexOf('=');
        const name = pair.slice(0, separator);
        const value = pair.slice(separator + 1);
        if (attributes.some(attribute => attribute.toLowerCase().startsWith('expires=thu, 01 jan 1970'))) {
            cookieJar.delete(name);
        } else {
            cookieJar.set(name, value);
        }
        globalThis.document.lastCookieAttributes = attributes;
    }
};

globalThis.window = {
    location: { protocol: 'http:', href: 'http://localhost:5000/' },
    addEventListener: (name, handler) => {
        const existing = listeners.get(name) || [];
        existing.push(handler);
        listeners.set(name, existing);
    },
    removeEventListener: (name, handler) => {
        listeners.set(name, (listeners.get(name) || []).filter(entry => entry !== handler));
    }
};

globalThis.sessionStorage = createStorage();
globalThis.localStorage = createStorage();
globalThis.console = { ...console, error: (...args) => errors.push(args) };
globalThis.Date = { now: () => fakeNow };
globalThis.setTimeout = (callback, delay) => {
    timers.push({ callback, delay, cleared: false });
    return timers.length;
};
globalThis.clearTimeout = (id) => {
    if (timers[id - 1]) timers[id - 1].cleared = true;
};

// WHAT: Loads the session module under test (a classic browser script, CommonJS in Node).
// WHY:  A static import is hoisted above the mock DOM assignments above, and the module
//       publishes its helpers onto window at load time exactly as a browser script does -
//       so it has to be imported after those globals exist.
// HOW:  Top-level dynamic import, which is evaluated in statement order.
const WeaverSession = (await import('../src/NanoWeaverHrt/wwwroot/js/weaver-session.js')).default;

/**
 * Resets all mock DOM state between tests.
 */
function resetDom() {
    cookieJar.clear();
    listeners.clear();
    timers.length = 0;
    errors.length = 0;
    fakeNow = 1700000000000;
    globalThis.document.lastCookieAttributes = [];
    globalThis.window.location.protocol = 'http:';
    globalThis.window.location.href = 'http://localhost:5000/';
    globalThis.window._weaverIdleCleanup = null;
    globalThis.sessionStorage = createStorage();
    globalThis.localStorage = createStorage();
}

/**
 * Dispatches one activity event to every registered click listener.
 */
function fireActivity() {
    for (const handler of listeners.get('click') || []) {
        handler();
    }
}

test('the module publishes every helper as a browser global', () => {
    resetDom();
    assert.equal(globalThis.window.weaverSetSession, WeaverSession.weaverSetSession);
    assert.equal(globalThis.window.weaverGetSessionToken, WeaverSession.weaverGetSessionToken);
    assert.equal(globalThis.window.weaverClearSession, WeaverSession.weaverClearSession);
    assert.equal(globalThis.window.weaverPurgeLegacySession, WeaverSession.weaverPurgeLegacySession);
    assert.equal(globalThis.window.weaverRegisterIdleWatcher, WeaverSession.weaverRegisterIdleWatcher);
    assert.equal(globalThis.window.weaverUnregisterIdleWatcher, WeaverSession.weaverUnregisterIdleWatcher);
});

test('weaverSetSession stores the token in sessionStorage and a session cookie, never in localStorage', () => {
    resetDom();
    globalThis.localStorage.setItem('weaver_session', 'token-from-a-previous-release');

    WeaverSession.weaverSetSession('abc.def');

    assert.equal(globalThis.sessionStorage.getItem('weaver_session'), 'abc.def');
    assert.equal(cookieJar.get('weaver_session'), 'abc.def');

    // The defect being fixed: a persistent localStorage copy survived tab close and reload.
    assert.equal(globalThis.localStorage.getItem('weaver_session'), null);
    assert.deepEqual(globalThis.localStorage.writes, ['weaver_session']); // only the test's own seed write
    assert.deepEqual(globalThis.localStorage.removals, ['weaver_session']);

    // Session cookie: no max-age and no expires, so it dies with the browser session.
    const attributes = globalThis.document.lastCookieAttributes.map(a => a.toLowerCase());
    assert.ok(attributes.includes('path=/'));
    assert.ok(attributes.includes('samesite=strict'));
    assert.ok(!attributes.some(a => a.startsWith('max-age')));
    assert.ok(!attributes.some(a => a.startsWith('expires')));
    assert.ok(!attributes.includes('secure'));
});

test('weaverSetSession marks the cookie Secure when the page was served over HTTPS', () => {
    resetDom();
    globalThis.window.location.protocol = 'https:';
    WeaverSession.weaverSetSession('secure.token');
    assert.ok(globalThis.document.lastCookieAttributes.map(a => a.toLowerCase()).includes('secure'));
});

test('weaverSetSession ignores an empty token and reports storage failures without throwing', () => {
    resetDom();
    WeaverSession.weaverSetSession('');
    assert.equal(cookieJar.size, 0);
    assert.equal(errors.length, 0);

    globalThis.sessionStorage = createStorage(true);
    assert.doesNotThrow(() => WeaverSession.weaverSetSession('abc.def'));
    assert.equal(errors.length, 1);
    // The cookie is written first, so a storage-denied browser still has a usable session.
    assert.equal(cookieJar.get('weaver_session'), 'abc.def');
});

test('weaverGetSessionToken prefers sessionStorage, falls back to the cookie, and never reads localStorage', () => {
    resetDom();
    assert.equal(WeaverSession.weaverGetSessionToken(), null);

    globalThis.localStorage.setItem('weaver_session', 'stale-persistent-token');
    assert.equal(WeaverSession.weaverGetSessionToken(), null);

    globalThis.document.cookie = 'weaver_session=cookie%2Etoken; path=/; SameSite=Strict';
    assert.equal(WeaverSession.weaverGetSessionToken(), 'cookie.token');

    globalThis.sessionStorage.setItem('weaver_session', 'session-storage-token');
    assert.equal(WeaverSession.weaverGetSessionToken(), 'session-storage-token');

    globalThis.sessionStorage = createStorage(true);
    assert.equal(WeaverSession.weaverGetSessionToken(), null);
});

test('weaverClearSession purges session storage, the legacy localStorage copy and the cookie', () => {
    resetDom();
    WeaverSession.weaverSetSession('abc.def');
    globalThis.localStorage.setItem('weaver_session', 'stale-persistent-token');

    WeaverSession.weaverClearSession();

    assert.equal(globalThis.sessionStorage.getItem('weaver_session'), null);
    assert.equal(globalThis.localStorage.getItem('weaver_session'), null);
    assert.equal(cookieJar.has('weaver_session'), false);

    globalThis.sessionStorage = createStorage(true);
    assert.doesNotThrow(() => WeaverSession.weaverClearSession());
    assert.equal(errors.length, 1);
});

test('weaverPurgeLegacySession swallows a storage-denied localStorage', () => {
    resetDom();
    globalThis.localStorage = createStorage(true);
    assert.doesNotThrow(() => WeaverSession.weaverPurgeLegacySession());
    assert.equal(errors.length, 0);
});

test('weaverRegisterIdleWatcher throttles heartbeats and hands idle expiry to the server', () => {
    resetDom();
    const invocations = [];
    const helper = {
        invokeMethodAsync: (name) => {
            invocations.push(name);
            return Promise.resolve();
        }
    };

    WeaverSession.weaverRegisterIdleWatcher(helper, 900);

    // Arming the watcher is not activity, so it must raise no heartbeat.
    assert.deepEqual(invocations, []);
    assert.equal(timers.length, 1);
    assert.equal(timers[0].delay, 900 * 1000);
    assert.equal((listeners.get('mousemove') || []).length, 1);

    // Inside the 30-second throttle window: still no heartbeat, but the timer is rearmed.
    fakeNow += 5000;
    fireActivity();
    assert.deepEqual(invocations, []);
    assert.ok(timers[0].cleared);
    assert.equal(timers.length, 2);

    // Past the throttle window: exactly one heartbeat.
    fakeNow += 31000;
    fireActivity();
    assert.deepEqual(invocations, ['RecordActivity']);
    assert.equal(timers.length, 3);

    // Firing the idle timer hands the sign-out to the server and leaves the redirect to it.
    timers[timers.length - 1].callback();
    assert.deepEqual(invocations, ['RecordActivity', 'OnUserIdleTimeout']);
    assert.equal(globalThis.window.location.href, 'http://localhost:5000/');
});

test('weaverRegisterIdleWatcher survives a circuit that rejects the heartbeat', async () => {
    resetDom();
    let settle;
    const settled = new Promise(resolve => { settle = resolve; });
    const helper = {
        invokeMethodAsync: () => Promise.reject(new Error('circuit disconnected')).finally(() => settle())
    };

    WeaverSession.weaverRegisterIdleWatcher(helper, 900);
    fakeNow += 31000;
    assert.doesNotThrow(() => fireActivity());

    await settled;
    await new Promise(resolve => setImmediate(resolve));

    // A missed heartbeat is not fatal: the authoritative server clock still governs expiry.
    assert.equal(errors.length, 0);
});

test('weaverRegisterIdleWatcher purges client state when the server hand-off is rejected', async () => {
    resetDom();
    WeaverSession.weaverSetSession('abc.def');

    let settle;
    const settled = new Promise(resolve => { settle = resolve; });
    const helper = {
        invokeMethodAsync: () => Promise.reject(new Error('circuit disconnected')).finally(() => settle())
    };

    WeaverSession.weaverRegisterIdleWatcher(helper, 900);
    timers[timers.length - 1].callback();

    await settled;
    await new Promise(resolve => setImmediate(resolve));

    // The server could not be told, so the browser must not keep a usable token around.
    assert.equal(globalThis.sessionStorage.getItem('weaver_session'), null);
    assert.equal(cookieJar.has('weaver_session'), false);
    assert.equal(globalThis.window.location.href, '/login?reason=idle_timeout');
});

test('weaverRegisterIdleWatcher purges client state itself when the circuit is gone', () => {
    resetDom();
    WeaverSession.weaverSetSession('abc.def');

    // A helper object with no invokeMethodAsync must be treated as a dead circuit.
    WeaverSession.weaverRegisterIdleWatcher({}, 900);
    timers[timers.length - 1].callback();

    assert.equal(globalThis.sessionStorage.getItem('weaver_session'), null);
    assert.equal(cookieJar.has('weaver_session'), false);
    assert.equal(globalThis.window.location.href, '/login?reason=idle_timeout');
});

test('weaverRegisterIdleWatcher falls back to the fifteen-minute default window', () => {
    resetDom();

    // Omitted timeout: the && short-circuits to the default.
    WeaverSession.weaverRegisterIdleWatcher(null, undefined);
    assert.equal(timers[timers.length - 1].delay, 900 * 1000);

    // Present but non-positive timeout: the comparison rejects it and the default applies.
    WeaverSession.weaverRegisterIdleWatcher(null, -5);
    assert.equal(timers[timers.length - 1].delay, 900 * 1000);

    // An explicit shorter window is honoured verbatim.
    WeaverSession.weaverRegisterIdleWatcher(null, 60);
    assert.equal(timers[timers.length - 1].delay, 60 * 1000);

    // No .NET helper at all: the watcher clears client state and redirects by itself.
    WeaverSession.weaverSetSession('abc.def');
    timers[timers.length - 1].callback();
    assert.equal(cookieJar.has('weaver_session'), false);
    assert.equal(globalThis.window.location.href, '/login?reason=idle_timeout');
});

test('weaverUnregisterIdleWatcher detaches listeners, is idempotent, and never stacks watchers', () => {
    resetDom();
    WeaverSession.weaverRegisterIdleWatcher(null, 900);
    assert.equal((listeners.get('click') || []).length, 1);

    WeaverSession.weaverUnregisterIdleWatcher();
    assert.equal((listeners.get('click') || []).length, 0);
    assert.ok(timers[0].cleared);
    assert.equal(globalThis.window._weaverIdleCleanup, null);

    // Calling it again with no watcher registered is a no-op.
    assert.doesNotThrow(() => WeaverSession.weaverUnregisterIdleWatcher());

    // Re-registering replaces the previous watcher rather than stacking a second one.
    WeaverSession.weaverRegisterIdleWatcher(null, 900);
    WeaverSession.weaverRegisterIdleWatcher(null, 900);
    assert.equal((listeners.get('click') || []).length, 1);
});
