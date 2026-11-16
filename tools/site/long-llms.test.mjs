import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { archiveText, checkArchive } from './long-llms.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(here, '..', '..');

const CHANGELOG = `# Changelog

What changed in each release.

## Unreleased

### Not out yet (2026-12-01)

- **Something unreleased** (\`src/x.cs\`).

## 1.2.0 (2026-11-15)

The summary of 1.2.0, with [third-party software](docs/THIRD_PARTY.md) and [a page](https://example.com/page).

### The release (2026-11-15)

- **The setup is signed.**

## 1.1.0 (2026-11-09)

The first release since 1.0.0 (2026-11-01).
`;

// A folder holding just what the archive is made from and where it goes.
function folder(changelog, archive) {
  const root = mkdtempSync(join(tmpdir(), 'stx-long-llms-'));
  writeFileSync(join(root, 'CHANGELOG.md'), changelog);
  mkdirSync(join(root, 'site'));
  if (archive !== undefined) writeFileSync(join(root, 'site', 'long-llms.txt'), archive);
  return { root, done: () => rmSync(root, { recursive: true, force: true }) };
}

test('holds_each_released_version_newest_first_and_leaves_out_unreleased', () => {
  const text = archiveText(CHANGELOG);
  assert.ok(text.indexOf('## 1.2.0 (2026-11-15)') < text.indexOf('## 1.1.0 (2026-11-09)'));
  assert.ok(text.includes('### The release (2026-11-15)\n\n- **The setup is signed.**'));
  assert.ok(text.includes('The first release since 1.0.0 (2026-11-01).'));
  assert.ok(!text.includes('Unreleased') && !text.includes('Not out yet') && !text.includes('Something unreleased'));
});

test('says_how_many_releases_it_has_notes_for_and_never_claims_more', () => {
  const text = archiveText(CHANGELOG);
  assert.match(text, /Notes for 2 releases; the newest is 1\.2\.0 \(2026-11-15\)\./);
  assert.doesNotMatch(text, /\bevery release\b/i);
});

test('relative_links_point_at_the_repository_and_others_are_kept', () => {
  const text = archiveText(CHANGELOG);
  assert.ok(text.includes('[third-party software](https://github.com/Git-Rocky-Stack/STX-Downloader/blob/main/docs/THIRD_PARTY.md)'));
  assert.ok(text.includes('[a page](https://example.com/page)'));
});

test('the_order_comes_from_the_version_numbers_not_the_file', () => {
  const swapped = CHANGELOG.replace(/## 1\.2\.0[\s\S]*?(?=## 1\.1\.0)/, '').replace(/$/, '\n' + CHANGELOG.match(/## 1\.2\.0[\s\S]*?(?=## 1\.1\.0)/)[0]);
  const text = archiveText(swapped);
  assert.ok(text.indexOf('## 1.2.0') < text.indexOf('## 1.1.0'));
  assert.match(text, /the newest is 1\.2\.0/);
});

test('the_check_passes_when_current_whatever_the_line_endings', () => {
  const f = folder(CHANGELOG, archiveText(CHANGELOG).replace(/\n/g, '\r\n'));
  try {
    assert.deepEqual(checkArchive(f.root), []);
  } finally {
    f.done();
  }
});

test('the_check_fails_when_the_archive_is_stale_or_missing', () => {
  const stale = folder(CHANGELOG.replace('The setup is signed.', 'The setup is signed and smaller.'), archiveText(CHANGELOG));
  const missing = folder(CHANGELOG, undefined);
  try {
    assert.match(checkArchive(stale.root).join('\n'), /site\/long-llms\.txt: differs from what CHANGELOG\.md makes \(run node tools\/site\/long-llms\.mjs\)/);
    assert.match(checkArchive(missing.root).join('\n'), /site\/long-llms\.txt: missing/);
  } finally {
    stale.done();
    missing.done();
  }
});

// The archive in the repository is held to the CHANGELOG on every test run, here and in CI.
test('the_committed_archive_matches_the_changelog', () => {
  assert.deepEqual(checkArchive(repoRoot), []);
});

test('the_command_line_writes_the_archive_and_its_check_exits_1_when_stale', () => {
  const f = folder(CHANGELOG, 'stale');
  try {
    const script = join(here, 'long-llms.mjs');
    const stale = spawnSync(process.execPath, [script, '--check', f.root], { encoding: 'utf8' });
    assert.equal(stale.status, 1);
    assert.match(stale.stderr, /differs from what CHANGELOG\.md makes/);
    assert.equal(spawnSync(process.execPath, [script, f.root], { encoding: 'utf8' }).status, 0);
    assert.equal(readFileSync(join(f.root, 'site', 'long-llms.txt'), 'utf8'), archiveText(CHANGELOG));
    const current = spawnSync(process.execPath, [script, '--check', f.root], { encoding: 'utf8' });
    assert.equal(current.status, 0, current.stderr);
    assert.match(current.stdout, /release archive: current/);
  } finally {
    f.done();
  }
});
