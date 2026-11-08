import { test } from 'node:test';
import assert from 'node:assert/strict';

// The service worker registers its listeners at the top level, as Chrome requires, so it's loaded once against a
// stand-in for the chrome API. Each test sets what the stand-in does.
const listeners = { message: [] };
const stand = {
  cookies: async () => [],
  native: async () => ({ ok: true }),
};
const event = () => ({ addListener() {} });
globalThis.chrome = {
  runtime: {
    id: 'stx-extension',
    getURL: path => `chrome-extension://stx-extension/${path}`,
    onMessage: { addListener: listener => listeners.message.push(listener) },
    sendNativeMessage: (...args) => stand.native(...args),
  },
  cookies: { getAll: details => stand.cookies(details) },
  storage: { session: { get: async () => ({}), set: async () => {}, remove: async () => {} } },
  tabs: { onRemoved: event() },
  webRequest: { onBeforeRequest: event(), onHeadersReceived: event() },
};
await import('../worker.js');

const popup = { id: 'stx-extension', url: 'chrome-extension://stx-extension/popup.html' };
const stream = { type: 'send', items: [{ kind: 'hls', url: 'https://cdn.example/v/master.m3u8' }], page: { url: 'https://site.example/watch', title: 'Clip', userAgent: 'x' }, withCookies: true };

// Hands a message to the worker's listeners the way Chrome does, and waits for the one that answers.
function ask(message, sender = popup) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('the worker never answered')), 2000);
    const answering = listeners.message.map(listener => listener(message, sender, reply => { clearTimeout(timer); resolve(reply); }));
    if (!answering.includes(true)) { clearTimeout(timer); resolve(undefined); }
  });
}

test('a send reaches the app and its answer comes back', async () => {
  stand.cookies = async () => [];
  stand.native = async () => ({ ok: true, queued: 1 });
  assert.deepEqual(await ask(stream), { ok: true, queued: 1 });
});

// Review 2026-11-07: when reading cookies threw, the worker never answered, and the popup sat on "Sending…" with its
// button disabled for good.
test('a send that fails before reaching the app still answers, and says so', async () => {
  stand.cookies = async () => { throw new Error('No host permissions for cookies at url'); };
  const reply = await ask(stream);
  assert.equal(reply.ok, false);
  assert.match(reply.message, /STX Downloader/);
});

test('a page, not the popup, is never answered', async () => {
  assert.equal(await ask(stream, { id: 'stx-extension', url: 'https://site.example/', tab: { id: 1 } }), undefined);
});
