import { canDownload, emptyState, labelFor, listFor } from './detect.js';

const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });

async function show() {
  const key = `tab:${tab.id}`;
  const state = (await chrome.storage.session.get(key))[key] || emptyState();
  const items = listFor(state);
  document.getElementById('list').replaceChildren(...items.map(item => {
    const row = document.createElement('li');
    row.textContent = labelFor(item);
    row.classList.toggle('drm', !canDownload(item));
    return row;
  }));
  document.getElementById('empty').hidden = items.length > 0;
}

// Ask the page for its latest facts, then keep the list current while the popup is open.
chrome.tabs.sendMessage(tab.id, { type: 'report' }).catch(() => {});
chrome.storage.session.onChanged.addListener(show);
await show();
