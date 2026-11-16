import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { execFileSync, spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { siteLastmods, writeLastmods, checkLastmods, today } from './lastmod.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const PAGE = 'https://git-rocky-stack.github.io/STX-Downloader/';
const KEY = 'site/0123456789abcdef0123456789abcdef.txt';

const sitemap = ({ page, llms }) => `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>${PAGE}</loc>
    <lastmod>${page}</lastmod>
  </url>
  <url>
    <loc>${PAGE}llms.txt</loc>
    <lastmod>${llms}</lastmod>
  </url>
</urlset>
`;

// A throwaway repository. Its commits carry the dates given and a placeholder identity, and never sign.
function repo() {
  const root = mkdtempSync(join(tmpdir(), 'stx-lastmod-'));
  const env = (date) => ({
    ...process.env,
    GIT_AUTHOR_NAME: 'site test', GIT_AUTHOR_EMAIL: 'site-test@example.invalid',
    GIT_COMMITTER_NAME: 'site test', GIT_COMMITTER_EMAIL: 'site-test@example.invalid',
    ...(date && { GIT_AUTHOR_DATE: `${date}T12:00:00+00:00`, GIT_COMMITTER_DATE: `${date}T12:00:00+00:00` }),
  });
  const git = (args, date, cwd = root) =>
    execFileSync('git', ['-c', 'commit.gpgsign=false', '-c', 'core.autocrlf=false', ...args], { cwd, env: env(date), stdio: 'pipe' }).toString();
  git(['init', '-q']);
  const write = (rel, text) => {
    mkdirSync(dirname(join(root, rel)), { recursive: true });
    writeFileSync(join(root, rel), text);
  };
  const commit = (date, files) => {
    for (const [rel, text] of Object.entries(files)) write(rel, text);
    git(['add', '-A']);
    git(['commit', '-q', '-m', `on ${date}`], date);
  };
  const read = (rel) => readFileSync(join(root, rel), 'utf8');
  const done = () => rmSync(root, { recursive: true, force: true, maxRetries: 5 });
  return { root, git, write, commit, read, done };
}

const startingSite = (page = 'x', llms = 'x') => ({
  'site/index.html': 'page',
  'site/css/stx.css': 'css',
  'site/llms.txt': 'llms one',
  'site/sitemap.xml': sitemap({ page, llms }),
});

test('dates_each_url_by_the_last_commit_behind_it', () => {
  const r = repo();
  try {
    r.commit('2026-01-02', startingSite());
    r.commit('2026-03-04', { 'site/llms.txt': 'llms two' });
    r.commit('2026-05-06', { 'site/css/stx.css': 'css two' });
    assert.deepEqual(siteLastmods(r.root), new Map([[PAGE, '2026-05-06'], [PAGE + 'llms.txt', '2026-03-04']]));
  } finally {
    r.done();
  }
});

test('the_sitemap_robots_and_indexnow_key_do_not_date_the_page', () => {
  const r = repo();
  try {
    r.commit('2026-01-02', startingSite());
    r.commit('2026-07-08', { 'site/sitemap.xml': sitemap({ page: '2026-01-02', llms: '2026-01-02' }), 'site/robots.txt': 'User-agent: *', [KEY]: 'key' });
    assert.equal(siteLastmods(r.root).get(PAGE), '2026-01-02');
  } finally {
    r.done();
  }
});

test('an_uncommitted_change_dates_its_url_today', () => {
  const r = repo();
  try {
    r.commit('2026-01-02', startingSite());
    r.write('site/index.html', 'edited, not committed');
    const dates = siteLastmods(r.root);
    assert.equal(dates.get(PAGE), today());
    assert.equal(dates.get(PAGE + 'llms.txt'), '2026-01-02');
  } finally {
    r.done();
  }
});

test('writing_sets_only_the_dates_and_the_check_then_passes', () => {
  const r = repo();
  try {
    r.commit('2026-01-02', startingSite('2025-12-31', '2025-12-31'));
    writeLastmods(r.root);
    assert.equal(r.read('site/sitemap.xml'), sitemap({ page: '2026-01-02', llms: '2026-01-02' }));
    r.commit('2026-01-02', {});
    assert.deepEqual(checkLastmods(r.root), []);
  } finally {
    r.done();
  }
});

test('the_check_names_a_stale_url_with_both_dates', () => {
  const r = repo();
  try {
    r.commit('2026-01-02', startingSite('2025-12-31', '2026-01-02'));
    const problems = checkLastmods(r.root);
    assert.equal(problems.length, 1);
    assert.match(problems[0], /STX-Downloader\/ says 2025-12-31; git says 2026-01-02/);
    assert.match(problems[0], /node tools\/site\/lastmod\.mjs/);
  } finally {
    r.done();
  }
});

test('the_check_refuses_a_shallow_clone', () => {
  const r = repo();
  const clone = mkdtempSync(join(tmpdir(), 'stx-lastmod-shallow-'));
  try {
    r.commit('2026-01-02', startingSite('2026-01-02', '2026-03-04'));
    r.commit('2026-03-04', { 'site/llms.txt': 'llms two' });
    r.git(['clone', '-q', '--depth', '1', pathToFileURL(r.root).href, clone]);
    const problems = checkLastmods(clone);
    assert.equal(problems.length, 1);
    assert.match(problems[0], /shallow/);
    assert.match(problems[0], /fetch-depth: 0/);
  } finally {
    r.done();
    rmSync(clone, { recursive: true, force: true, maxRetries: 5 });
  }
});

test('a_url_with_no_file_behind_it_is_a_problem', () => {
  const r = repo();
  try {
    r.commit('2026-01-02', startingSite('2026-01-02', '2026-01-02'));
    r.git(['rm', '-q', 'site/llms.txt']);
    r.git(['commit', '-q', '-m', 'gone'], '2026-01-03');
    assert.match(checkLastmods(r.root).join('\n'), /llms\.txt has no file behind it/);
  } finally {
    r.done();
  }
});

test('the_command_line_check_exits_1_on_a_stale_date_and_0_when_current', () => {
  const r = repo();
  try {
    r.commit('2026-01-02', startingSite('2025-12-31', '2026-01-02'));
    const script = join(here, 'lastmod.mjs');
    const stale = spawnSync(process.execPath, [script, '--check', r.root], { encoding: 'utf8' });
    assert.equal(stale.status, 1);
    assert.match(stale.stderr, /says 2025-12-31; git says 2026-01-02/);
    assert.equal(spawnSync(process.execPath, [script, r.root], { encoding: 'utf8' }).status, 0);
    r.commit('2026-01-02', {});
    const current = spawnSync(process.execPath, [script, '--check', r.root], { encoding: 'utf8' });
    assert.equal(current.status, 0, current.stderr);
    assert.match(current.stdout, /sitemap dates: current/);
  } finally {
    r.done();
  }
});
