import { canDownload, emptyState, labelFor, listFor } from './detect.js';

const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
const statusLine = document.getElementById('status');

function page() {
  return { url: tab.url, title: tab.title, userAgent: navigator.userAgent };
}

async function save(items, withCookies, button) {
  button.disabled = true;
  statusLine.textContent = 'Sending to STX Downloader…';
  const reply = await chrome.runtime.sendMessage({ type: 'send', items, page: page(), withCookies });
  if (reply?.ok) {
    const count = reply.jobs + reply.resumed;
    statusLine.textContent = reply.resumed > 0 ? 'Resumed in the queue.' : `Added to the queue${count > 1 ? `: ${count} items` : ''}.`;
  } else {
    statusLine.textContent = reply?.message || 'STX Downloader didn\'t answer.';
    button.disabled = false;
  }
}

function action(text, title, onClick) {
  const button = document.createElement('button');
  button.textContent = text;
  button.title = title;
  button.addEventListener('click', () => onClick(button));
  return button;
}

function row(item) {
  const li = document.createElement('li');
  const label = document.createElement('span');
  label.textContent = labelFor(item);
  li.append(label);
  if (!canDownload(item)) {
    li.classList.add('drm');
    return li;
  }
  const actions = document.createElement('div');
  actions.className = 'actions';
  if (item.kind === 'youtube') {
    actions.append(action('Save', 'Save without your sign-in', b => save([item], false, b)));
    actions.append(action('Save signed in', 'Send your YouTube cookies with this one item', b => save([item], true, b)));
  } else {
    actions.append(action('Save', 'Save this stream', b => save([item], true, b)));
  }
  li.append(actions);
  return li;
}

async function show() {
  const key = `tab:${tab.id}`;
  const state = (await chrome.storage.session.get(key))[key] || emptyState();
  const items = listFor(state);
  document.getElementById('list').replaceChildren(...items.map(row));
  document.getElementById('empty').hidden = items.length > 0;
  const usable = items.filter(canDownload);
  const all = document.getElementById('all');
  all.hidden = usable.length < 2;
  all.onclick = () => save(usable, true, all);
  document.getElementById('drm').hidden = !state.drm;
}

// Ask the page for its latest facts, then keep the list current while the popup is open.
chrome.tabs.sendMessage(tab.id, { type: 'report' }).catch(() => {});
chrome.storage.session.onChanged.addListener(show);
await show();
