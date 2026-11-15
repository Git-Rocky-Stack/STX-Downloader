// The page script's decisions, tested without a browser. site/js/stx.js only touches the DOM when a document exists,
// so these pure helpers import cleanly into Node. Wiring (pads, observers, swaps) is checked in a browser per task.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chooseFinish, readStoredFinish, storeFinish, litSegments, digitCells, screwFor, pointerPeakAlpha, FINISH_KEY, levelAt, meterStep, PEAK_HOLD_MS } from '../../site/js/stx.js';

const storage = (items = {}) => ({ getItem: (k) => (k in items ? items[k] : null), setItem: (k, v) => { items[k] = String(v); }, items });
const throwing = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };

test('finish_follows_the_os_until_a_choice_is_stored', () => {
  assert.equal(chooseFinish(null, false), 'dark');
  assert.equal(chooseFinish(null, true), 'light');
  assert.equal(chooseFinish('dark', true), 'dark');
  assert.equal(chooseFinish('light', false), 'light');
  assert.equal(chooseFinish('silver', true), 'light', 'an unknown stored value is ignored');
  assert.equal(chooseFinish('silver', false), 'dark');
});

test('stored_finish_survives_blocked_storage', () => {
  assert.equal(readStoredFinish(storage({ [FINISH_KEY]: 'light' })), 'light');
  assert.equal(readStoredFinish(storage()), null);
  assert.equal(readStoredFinish(throwing), null);
  assert.equal(readStoredFinish(undefined), null);
  const s = storage();
  assert.equal(storeFinish(s, 'light'), true);
  assert.equal(s.items[FINISH_KEY], 'light');
  assert.equal(storeFinish(throwing, 'light'), false);
  assert.equal(storeFinish(undefined, 'light'), false);
});

test('scroll_meter_lights_in_proportion_to_scroll', () => {
  assert.equal(litSegments(0, 1000, 24), 0);
  assert.equal(litSegments(1000, 1000, 24), 24);
  assert.equal(litSegments(500, 1000, 24), 12);
  assert.equal(litSegments(1, 1000, 24), 1, 'any scroll lights the first segment');
  assert.equal(litSegments(1200, 1000, 24), 24, 'overscroll clamps');
  assert.equal(litSegments(-50, 1000, 24), 0, 'rubber-band clamps');
  assert.equal(litSegments(0, 0, 24), 24, 'a page that does not scroll is all seen');
});

test('digit_cells_map_characters_to_strip_cells_with_dots', () => {
  assert.deepEqual(digitCells('1.2.0'), [{ cell: 1, dot: true }, { cell: 2, dot: true }, { cell: 0, dot: false }]);
  assert.deepEqual(digitCells('09'), [{ cell: 0, dot: false }, { cell: 9, dot: false }]);
  assert.deepEqual(digitCells('0 1'), [{ cell: 0, dot: false }, { cell: 10, dot: false }, { cell: 1, dot: false }]);
  assert.deepEqual(digitCells('x'), [{ cell: 10, dot: false }], 'a character with no digit is a dark position');
});

test('screws_turn_round_robin', () => {
  assert.deepEqual([0, 1, 2, 3, 4, 5].map(screwFor), ['a', 'b', 'c', 'd', 'a', 'b']);
});

test('pointer_light_peak_follows_PointerLightLayout', () => {
  assert.equal(pointerPeakAlpha('dark'), 22 / 255);
  assert.equal(pointerPeakAlpha('light'), 64 / 255);
});

test('demo_level_follows_the_mockup_signal', () => {
  // docs/design/site-v2/hero-rack-stack.html level(): phrase * 0.82 + beat * 0.25 + noise * 0.08, capped at 1.05.
  assert.ok(Math.abs(levelAt(0, 0, 0) - 0.62 * 0.82) < 1e-9);
  const beatPeak = 0.125; // sin(2 * pi * 2 * t) = 1, so beat = 1
  const phrase = 0.62 + 0.25 * Math.sin(0.7 * beatPeak + 1) + 0.1 * Math.sin(2.3 * beatPeak + 2);
  assert.ok(Math.abs(levelAt(1, beatPeak, 0) - (phrase * 0.82 + 0.25)) < 1e-9);
  for (let t = 0; t < 20; t += 0.01) assert.ok(levelAt(0, t, 0.5) <= 1.05);
});

test('demo_meter_attacks_fast_releases_slow_and_holds_its_peak', () => {
  assert.equal(PEAK_HOLD_MS, 900);
  let s = { v: 0, peak: 0, peakT: 0 };
  s = meterStep(s, 1, 0, 32);
  assert.ok(Math.abs(s.v - 0.55) < 1e-9, 'attack 0.55');
  assert.equal(s.lit, 18);
  assert.equal(s.peak, 18);
  s = meterStep({ v: 1, peak: 32, peakT: 0 }, 0, 100, 32);
  assert.ok(Math.abs(s.v - 0.88) < 1e-9, 'release 0.12');
  assert.equal(s.lit, 28);
  assert.equal(s.peak, 32, 'peak held under 900 ms');
  s = meterStep({ v: 1, peak: 32, peakT: 0 }, 0, 901, 32);
  assert.equal(s.peak, 28, 'peak falls to the level after 900 ms');
  assert.equal(meterStep({ v: 2, peak: 0, peakT: 0 }, 2, 0, 32).lit, 32, 'clamped to the meter');
});
