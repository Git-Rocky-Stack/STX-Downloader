// The page's behavior. Everything here is extra: with no script the page keeps every unit, all its text and both
// download links, and loses only motion and the finish pads. Each init is idempotent and works without the others.
// The helpers above the DOM code are pure, so tools/site/stx.test.mjs can test them in Node.

export const FINISH_KEY = 'stx-finish';
const FINISHES = ['dark', 'light'];
const SCREWS = ['a', 'b', 'c', 'd'];
// A dark position on a seven-segment readout: the digits strip's last cell lights no segment (tools/site/SiteArt/Parts.cs).
const DEAD_CELL = 10;
// src/Stx.Look/PointerLightLayout.cs: peak alpha out of 255, brighter on bright metal.
const POINTER_PEAK = { dark: 22, light: 64 };

/** The finish to show: a stored choice wins, otherwise the OS's color scheme. */
export function chooseFinish(stored, prefersLight) {
  return FINISHES.includes(stored) ? stored : prefersLight ? 'light' : 'dark';
}

/** The stored finish, or null when there is none or storage is blocked (private windows, blocked site data). */
export function readStoredFinish(storage) {
  try {
    const value = storage?.getItem(FINISH_KEY) ?? null;
    return FINISHES.includes(value) ? value : null;
  } catch {
    return null;
  }
}

/** Remembers the finish; returns false when storage refuses, and the choice then lasts for this visit only. */
export function storeFinish(storage, finish) {
  try {
    if (!storage) return false;
    storage.setItem(FINISH_KEY, finish);
    return true;
  } catch {
    return false;
  }
}

/** Segments lit for a scroll position: any scroll lights one, the end lights all, a page that can't scroll is all seen. */
export function litSegments(scrollTop, scrollRange, count) {
  if (scrollRange <= 0) return count;
  const t = Math.min(1, Math.max(0, scrollTop / scrollRange));
  return t === 0 ? 0 : Math.max(1, Math.round(t * count));
}

/** Strip cells for a readout's text: digits 0-9 are their own cells, anything else is a dark position, and a '.' lights the dot of the digit before it. */
export function digitCells(text) {
  const cells = [];
  for (const ch of text) {
    if (ch === '.' && cells.length) cells[cells.length - 1].dot = true;
    else if (ch !== '.') cells.push({ cell: ch >= '0' && ch <= '9' ? Number(ch) : DEAD_CELL, dot: false });
  }
  return cells;
}

/** Screws are drawn at four rotations; turning them round robin keeps a plate from looking stamped. */
export const screwFor = (index) => SCREWS[index % SCREWS.length];

/** The pointer light's peak opacity, 0 to 1. */
export const pointerPeakAlpha = (finish) => POINTER_PEAK[finish === 'light' ? 'light' : 'dark'] / 255;

/** The tab a key moves to in a horizontal tablist of `count` tabs, or null when the key isn't a move. */
export function nextTab(index, key, count) {
  if (key === 'ArrowRight') return (index + 1) % count;
  if (key === 'ArrowLeft') return (index - 1 + count) % count;
  if (key === 'Home') return 0;
  if (key === 'End') return count - 1;
  return null;
}

/** How long a COPY pad stays lit after it copies, in ms. */
export const COPY_FLASH_MS = 1200;

/** Copies with the Clipboard API; 'select' means it couldn't (no API, or refused) and the caller selects the text instead. */
export async function copyText(text, clipboard) {
  try {
    if (!clipboard?.writeText) return 'select';
    await clipboard.writeText(text);
    return 'copied';
  } catch {
    return 'select';
  }
}

/** How long a level meter holds its peak segment, in ms. */
export const PEAK_HOLD_MS = 900;

/**
 * The demo meters' made-up programme: a beat twice a second and some phrasing, per channel. It is the approved
 * mockup's level() (docs/design/site-v2/hero-rack-stack.html); `noise` is -0.5 to 0.5.
 */
export function levelAt(ch, t, noise) {
  const beat = Math.pow(Math.max(0, Math.sin(t * Math.PI * 2 * 2)), 6);
  const phrase = 0.62 + 0.25 * Math.sin(t * 0.7 + ch) + 0.1 * Math.sin(t * 2.3 + ch * 2);
  return Math.min(1.05, phrase * 0.82 + beat * 0.25 + noise * 0.08);
}

/** One frame of a level meter: fast attack, slow release, and a peak segment held for PEAK_HOLD_MS. */
export function meterStep(state, target, now, count) {
  const v = state.v + (target - state.v) * (target > state.v ? 0.55 : 0.12);
  const lit = Math.round(Math.max(0, Math.min(1, v)) * count);
  const fresh = lit >= state.peak || now - state.peakT > PEAK_HOLD_MS;
  return { v, lit, peak: fresh ? lit : state.peak, peakT: fresh ? now : state.peakT };
}

// ---- DOM ----

const root = () => document.documentElement;
const media = (q) => typeof matchMedia === 'function' && matchMedia(q).matches;
const safeStorage = () => { try { return globalThis.localStorage; } catch { return undefined; } };
const once = (el, key) => (el.dataset[key] ? false : (el.dataset[key] = '1'));

/** Shows a finish: sets data-finish, lights its pad, and swaps every capture that has a light and a dark version. */
function applyFinish(finish) {
  root().dataset.finish = finish;
  root().style.setProperty('--pointer-peak', String(pointerPeakAlpha(finish)));
  for (const pad of document.querySelectorAll('[data-finish-pad]')) {
    const on = pad.dataset.finishPad === finish;
    pad.setAttribute('aria-pressed', String(on));
    pad.dataset.tone = on ? 'green' : 'off';
  }
  for (const img of document.querySelectorAll('img[data-src-dark][data-src-light]')) {
    // A <picture> picks by the OS's scheme before the script runs; from here on the finish pads decide.
    img.closest('picture')?.querySelectorAll('source').forEach((source) => source.remove());
    const src = finish === 'light' ? img.dataset.srcLight : img.dataset.srcDark;
    if (img.getAttribute('src') !== src) img.setAttribute('src', src);
  }
  // Links to a full-size capture point at the finish on show.
  for (const a of document.querySelectorAll('a[data-href-dark][data-href-light]')) {
    a.setAttribute('href', finish === 'light' ? a.dataset.hrefLight : a.dataset.hrefDark);
  }
}

/** BLACK and ALUMINUM: the OS's scheme until a pad is pressed, then the stored choice. Storage can be blocked; the pads still work. */
export function initFinish() {
  const storage = safeStorage();
  applyFinish(chooseFinish(readStoredFinish(storage), media('(prefers-color-scheme: light)')));
  for (const pad of document.querySelectorAll('[data-finish-pad]')) {
    if (!once(pad, 'wired')) continue;
    pad.addEventListener('click', () => {
      applyFinish(pad.dataset.finishPad);
      storeFinish(storage, pad.dataset.finishPad);
    });
  }
  const scheme = typeof matchMedia === 'function' ? matchMedia('(prefers-color-scheme: light)') : null;
  if (scheme && once(root(), 'schemeWired')) {
    scheme.addEventListener('change', (e) => { if (!readStoredFinish(storage)) applyFinish(chooseFinish(null, e.matches)); });
  }
}

/** Builds a meter's segments once. Each segment is a cell of its strip, placed by --i. */
function buildMeter(meter) {
  if (meter.children.length) return [...meter.children];
  const count = Number(meter.dataset.segments) || 24;
  meter.style.setProperty('--cells', String(count));
  const segs = [];
  for (let i = 0; i < count; i++) {
    const seg = document.createElement('i');
    seg.style.setProperty('--i', String(i));
    segs.push(seg);
  }
  meter.append(...segs);
  return segs;
}

/** The sticky strip: screws, the scroll bargraph, and the nav LED for the unit in view. */
export function initStrip() {
  document.querySelectorAll('.screw').forEach((screw, i) => { screw.dataset.screw = screwFor(i); });

  if (!once(root(), 'stripWired')) return;
  const doc = () => document.scrollingElement || root();
  const atBottom = () => doc().scrollTop >= doc().scrollHeight - doc().clientHeight - 1;

  // Nav LEDs: the unit crossing a line at 40% of the viewport is in view, or the last unit once the page is at its end.
  // Every unit is watched, so a unit the nav doesn't list (DOWNLOAD has its own pad) turns the nav LEDs off.
  const links = new Map([...document.querySelectorAll('.strip nav a[href^="#"]')].map((a) => [a.getAttribute('href').slice(1), a]));
  const units = [...document.querySelectorAll('main > [id]')];
  let crossing = null;
  const lightNav = () => {
    const id = atBottom() && units.length ? units[units.length - 1].id : crossing;
    for (const [linkId, link] of links) {
      const on = linkId === id;
      link.querySelector('.led')?.setAttribute('data-state', on ? 'on' : 'off');
      if (on) link.setAttribute('aria-current', 'location'); else link.removeAttribute('aria-current');
    }
  };
  if (links.size && units.length && typeof IntersectionObserver === 'function') {
    const observer = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) crossing = entry.target.id;
        else if (crossing === entry.target.id) crossing = null;
      }
      lightNav();
    }, { rootMargin: '-40% 0px -59% 0px' });
    units.forEach((unit) => observer.observe(unit));
  }

  // The scroll bargraph: an honest meter of how far down the page you are.
  const meter = document.querySelector('.strip .meter[data-kind="progress"]');
  const segs = meter ? buildMeter(meter) : [];
  let queued = false;
  const paint = () => {
    queued = false;
    const lit = litSegments(doc().scrollTop, doc().scrollHeight - doc().clientHeight, segs.length);
    segs.forEach((seg, i) => seg.classList.toggle('on', i < lit));
    if (links.size) lightNav();
  };
  const queue = () => { if (!queued) { queued = true; requestAnimationFrame(paint); } };
  addEventListener('scroll', queue, { passive: true });
  addEventListener('resize', queue, { passive: true });
  paint();
}

/** Seven-segment readouts: each character becomes a cell of the digits strip, with a lit dot where the text has one. */
export function initSeven() {
  for (const el of document.querySelectorAll('.seven[data-value]')) {
    if (!once(el, 'built')) continue;
    const value = el.dataset.value;
    el.setAttribute('role', 'img');
    el.setAttribute('aria-label', el.getAttribute('aria-label') || value);
    el.replaceChildren(...digitCells(value).map(({ cell, dot }) => {
      const digit = document.createElement('i');
      digit.style.setProperty('--d', String(cell));
      if (dot) digit.dataset.dot = '';
      return digit;
    }));
    el.classList.add('built');
  }
}

/**
 * The VFD types its line once. The whole line stays in the HTML, in aria-label and in a screen-reader copy. Every
 * character is laid out from the start and only its opacity changes, so nothing on the page moves while it types
 * (a block cursor sits on the next character). Under reduced motion the line is simply there.
 */
export function initTyping() {
  const reduce = media('(prefers-reduced-motion: reduce)');
  document.querySelectorAll('[data-type]').forEach((el, n) => {
    if (!once(el, 'typing')) return;
    const full = el.textContent.replace(/\s+/g, ' ').trim();
    el.setAttribute('aria-label', full);
    if (reduce) return;
    const copy = document.createElement('span');
    copy.className = 'sr';
    copy.textContent = full;
    const shown = document.createElement('span');
    shown.setAttribute('aria-hidden', 'true');
    const chars = [...full].map((ch) => {
      const span = document.createElement('span');
      span.className = 'ch';
      span.textContent = ch;
      return span;
    });
    shown.append(...chars);
    el.replaceChildren(copy, shown);
    chars[0]?.classList.add('cur');
    let i = 0;
    const tick = () => {
      chars[i].classList.replace('cur', 'on');
      i += 1;
      if (i < chars.length) { chars[i].classList.add('cur'); setTimeout(tick, 38 + Math.random() * 40); }
      else el.dataset.typed = '';
    };
    setTimeout(tick, 300 + n * 150);
  });
}

/**
 * The L/R pair silkscreened DEMO: decoration, labeled as such. They run only while on screen and hold still under
 * reduced motion, showing 20 of 32 segments as the mockup does.
 */
export function initMeters() {
  const meters = [...document.querySelectorAll('.meter[data-kind="level"]')].filter((m) => once(m, 'wired'));
  if (!meters.length) return;
  const all = meters.map((el) => ({ el, segs: buildMeter(el), ch: Number(el.dataset.ch) || 0, state: { v: 0, peak: 0, peakT: 0 } }));
  const show = (m, lit, peak) => m.segs.forEach((seg, i) => {
    seg.classList.toggle('on', i < lit);
    seg.classList.toggle('peak', i === peak - 1 && i >= lit);
  });
  if (media('(prefers-reduced-motion: reduce)')) { all.forEach((m) => show(m, 20, 0)); return; }
  const t0 = performance.now();
  let running = false, visible = false;
  const frame = (now) => {
    if (!visible) { running = false; return; }
    const t = (now - t0) / 1000;
    for (const m of all) {
      m.state = meterStep(m.state, levelAt(m.ch, t, Math.random() - 0.5), now, m.segs.length);
      show(m, m.state.lit, m.state.peak);
    }
    requestAnimationFrame(frame);
  };
  const start = () => { if (!running) { running = true; requestAnimationFrame(frame); } };
  if (typeof IntersectionObserver !== 'function') { visible = true; start(); return; }
  const seen = new Set();
  const observer = new IntersectionObserver((entries) => {
    for (const e of entries) { if (e.isIntersecting) seen.add(e.target); else seen.delete(e.target); }
    visible = seen.size > 0;
    if (visible) start();
  });
  all.forEach((m) => observer.observe(m.el));
}

/**
 * More screens: the channel pads are a tablist (arrows, Home, End; the lit pad is the selected tab) that switch
 * one monitor. Every monitor links to its full-size capture, which opens in a dialog: Esc or CLOSE shuts it and
 * focus goes back to the monitor. Without the script, the links open the capture and all channels show.
 */
export function initChannels() {
  for (const list of document.querySelectorAll('[role="tablist"][data-channels]')) {
    if (!once(list, 'wired')) continue;
    const tabs = [...list.querySelectorAll('[role="tab"]')];
    const panels = tabs.map((tab) => document.getElementById(tab.getAttribute('aria-controls')));
    const select = (i, focus) => {
      tabs.forEach((tab, j) => {
        const on = i === j;
        tab.setAttribute('aria-selected', String(on));
        tab.tabIndex = on ? 0 : -1;
        tab.dataset.tone = on ? 'green' : 'off';
        if (panels[j]) panels[j].hidden = !on;
      });
      if (focus) tabs[i].focus();
    };
    tabs.forEach((tab, i) => tab.addEventListener('click', () => select(i, false)));
    list.addEventListener('keydown', (e) => {
      const i = nextTab(tabs.indexOf(document.activeElement), e.key, tabs.length);
      if (i === null || !tabs.includes(document.activeElement)) return;
      e.preventDefault();
      select(i, true);
    });
    select(Math.max(0, tabs.findIndex((t) => t.getAttribute('aria-selected') === 'true')), false);
    list.closest('.channels')?.classList.add('tabbed');
  }

  const dialog = document.getElementById('lightbox');
  if (!dialog || typeof dialog.showModal !== 'function' || !once(dialog, 'wired')) return;
  const view = dialog.querySelector('img');
  let opener = null;
  for (const link of document.querySelectorAll('a.monitor-link')) {
    link.addEventListener('click', (e) => {
      if (e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey) return;
      e.preventDefault();
      opener = link;
      const thumb = link.querySelector('img');
      view.src = link.getAttribute('href');
      view.alt = thumb?.alt ?? '';
      view.width = Number(link.dataset.w) || view.width;
      view.height = Number(link.dataset.h) || view.height;
      dialog.showModal();
    });
  }
  dialog.addEventListener('click', (e) => { if (e.target === dialog) dialog.close(); });
  dialog.addEventListener('close', () => { opener?.focus(); opener = null; });
}

/**
 * COPY pads beside the SHA-256 hashes: the Clipboard API, or the hash selected when that isn't allowed, then the
 * pad lights green for COPY_FLASH_MS and a status line says which happened.
 */
export function initCopy() {
  const status = document.getElementById('copy-status');
  for (const pad of document.querySelectorAll('[data-copy]')) {
    if (!once(pad, 'wired')) continue;
    let timer = 0;
    pad.addEventListener('click', async () => {
      const source = document.getElementById(pad.dataset.copy);
      if (!source) return;
      const text = source.textContent.replace(/\s+/g, '');
      const outcome = await copyText(text, navigator.clipboard);
      if (outcome === 'select') {
        const range = document.createRange();
        range.selectNodeContents(source);
        const selection = getSelection();
        selection?.removeAllRanges();
        selection?.addRange(range);
      }
      if (status) status.textContent = outcome === 'copied' ? 'SHA-256 copied.' : 'SHA-256 selected. Press Ctrl+C to copy it.';
      pad.dataset.tone = 'green';
      clearTimeout(timer);
      timer = setTimeout(() => { pad.dataset.tone = 'off'; }, COPY_FLASH_MS);
    });
  }
}

/** A soft light on the plate under the pointer, as the app's PointerLight. Off on touch screens and under reduced motion. */
export function initPointerLight() {
  if (media('(prefers-reduced-motion: reduce)') || media('(hover: none)') || !once(root(), 'pointerWired')) return;
  let pending = null, lit = null;
  const paint = () => {
    const e = pending;
    pending = null;
    const plate = e.target instanceof Element ? e.target.closest('.plate') : null;
    if (lit && lit !== plate) lit.style.removeProperty('--pl');
    lit = plate;
    if (!plate) return;
    const box = plate.getBoundingClientRect();
    plate.style.setProperty('--px', `${e.clientX - box.left}px`);
    plate.style.setProperty('--py', `${e.clientY - box.top}px`);
    plate.style.setProperty('--pl', '1');
  };
  addEventListener('pointermove', (e) => {
    if (e.pointerType === 'touch') return;
    if (!pending) requestAnimationFrame(paint);
    pending = e;
  }, { passive: true });
  document.addEventListener('pointerleave', () => { lit?.style.removeProperty('--pl'); lit = null; });
}

if (typeof document !== 'undefined') {
  initFinish();
  initStrip();
  initSeven();
  initTyping();
  initMeters();
  initChannels();
  initCopy();
  initPointerLight();
}
