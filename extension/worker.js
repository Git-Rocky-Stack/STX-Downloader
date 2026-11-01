// Watches the tab's traffic. The worker can stop at any time, so state lives in storage.session and
// listeners are registered at the top level, where Chrome finds them when it wakes the worker.
import { addResponse, applyPage, classify, emptyState, groupStreams, parseMaster } from './detect.js';

const keyFor = tabId => `tab:${tabId}`;

// One change at a time, so two responses arriving together can't overwrite each other.
let queue = Promise.resolve();
function update(tabId, change) {
  queue = queue.then(async () => {
    const key = keyFor(tabId);
    const stored = (await chrome.storage.session.get(key))[key];
    await chrome.storage.session.set({ [key]: change(stored || emptyState()) });
  }).catch(error => console.warn('STX state update failed', error));
  return queue;
}

function header(details, name) {
  return details.responseHeaders?.find(h => h.name.toLowerCase() === name)?.value || '';
}

async function readMaster(tabId, url) {
  try {
    // A page can load dozens of renditions. Only a stream still on the list is worth another request.
    const key = keyFor(tabId);
    const state = (await chrome.storage.session.get(key))[key];
    const item = state && groupStreams(state.items).find(i => i.url === url);
    if (!item || item.variants) return;
    const response = await fetch(url, { credentials: 'omit' });
    if (!response.ok) return;
    const { width, height, variants } = parseMaster(await response.text(), url);
    if (variants.length === 0) return;
    await update(tabId, state => ({
      ...state,
      items: state.items.map(i => (i.url === url ? { ...i, width, height, variants } : i)),
    }));
  } catch {
    // The label just goes without a resolution.
  }
}

chrome.webRequest.onBeforeRequest.addListener(details => {
  if (details.tabId >= 0) chrome.storage.session.remove(keyFor(details.tabId));
}, { urls: ['<all_urls>'], types: ['main_frame'] });

chrome.webRequest.onHeadersReceived.addListener(details => {
  if (details.tabId < 0) return;
  const contentType = header(details, 'content-type');
  const kind = classify(details.url, contentType);
  if (!kind) return;
  const isNew = update(details.tabId, state => addResponse(state, details.url, contentType));
  if (kind === 'hls') isNew.then(() => readMaster(details.tabId, details.url));
}, { urls: ['<all_urls>'], types: ['media', 'xmlhttprequest', 'other'] }, ['responseHeaders']);

// Brightcove's tracker is an image beacon, so it only needs a look at its address.
chrome.webRequest.onBeforeRequest.addListener(details => {
  if (details.tabId >= 0) update(details.tabId, state => addResponse(state, details.url, ''));
}, { urls: ['*://metrics.brightcove.com/*'] });

chrome.runtime.onMessage.addListener((message, sender) => {
  if (message?.type === 'page' && sender.tab?.id >= 0) update(sender.tab.id, state => applyPage(state, message));
});

chrome.tabs.onRemoved.addListener(tabId => chrome.storage.session.remove(keyFor(tabId)));
