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
    const src = finish === 'light' ? img.dataset.srcLight : img.dataset.srcDark;
    if (img.getAttribute('src') !== src) img.setAttribute('src', src);
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
  initPointerLight();
}
