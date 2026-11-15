// The page script's decisions, tested without a browser. site/js/stx.js only touches the DOM when a document exists,
// so these pure helpers import cleanly into Node. Wiring (pads, observers, swaps) is checked in a browser per task.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chooseFinish, readStoredFinish, storeFinish, litSegments, digitCells, screwFor, pointerPeakAlpha, FINISH_KEY } from '../../site/js/stx.js';

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
