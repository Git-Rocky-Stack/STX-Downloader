import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cpSync, mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkSite } from './check-site.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const good = join(here, 'fixtures', 'good');
const SETUP_URL = 'https://github.com/Git-Rocky-Stack/STX-Downloader/releases/download/v1.2.0/STXDownloader-1.2.0-setup.exe';
const ZIP_URL = 'https://github.com/Git-Rocky-Stack/STX-Downloader/releases/download/v1.2.0/STXDownloader-1.2.0-win-x64.zip';
const HASH = '1E067230272C7223E1A9430E0B9BFF8E3ADF8C8666477CB2323C2592E7590363';
const PAGE = 'https://git-rocky-stack.github.io/STX-Downloader/';

// Copies the good fixture to a temp folder, lets `change` edit it, then hands the folder to `use`.
async function inCopy(change, use) {
  const root = mkdtempSync(join(tmpdir(), 'stx-site-'));
  try {
    cpSync(good, root, { recursive: true });
    const edit = (rel, fn) => writeFileSync(join(root, rel), fn(readFileSync(join(root, rel), 'utf8')));
    change({ root, edit });
    return await use(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}
// Returns the problems the check reports for the good fixture after `change`.
const broken = (change) => inCopy(change, (root) => checkSite(root));
const mentions = (problems, re) => assert.ok(problems.some((p) => re.test(p)), `no problem matched ${re}; got:\n${problems.join('\n')}`);
// The one rule under test fired, and nothing else did: the break isolates that rule.
const only = (problems, re) => {
  assert.equal(problems.length, 1, `want exactly one problem matching ${re}; got:\n${problems.join('\n')}`);
  assert.match(problems[0], re);
};

test('passes_the_good_fixture', async () => {
  assert.deepEqual(await checkSite(good), []);
});

test('fails_when_version_differs_from_readme', async () => {
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replaceAll('data-version="1.2.0"', 'data-version="1.1.0"'))), /data-version/);
});

test('fails_when_hash_differs_from_readme', async () => {
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(HASH, 'A'.repeat(64)))), /data-sha256/);
});

test('fails_when_size_differs_from_readme', async () => {
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replaceAll('data-size="254 MB"', 'data-size="300 MB"'))), /data-size/);
});

test('fails_when_download_url_differs_from_readme', async () => {
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(`href='${SETUP_URL}'`, `href='${SETUP_URL}x'`))), /href/);
});

test('fails_without_a_setup_element', async () => {
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replaceAll('data-release-file="setup"', 'data-other="setup"'))), /data-release-file="setup"/);
});

test('fails_without_a_zip_element', async () => {
  const p = await broken(({ edit }) => edit('site/index.html', (h) => h.replace('data-release-file="zip"', 'data-other="zip"')));
  only(p, /data-release-file="zip"/);
});

test('zip_link_is_a_plain_anchor_even_without_a_zip_element', async () => {
  // The whole zip block gone: both the element rule and the anchor rule must fire.
  const p = await broken(({ edit }) => edit('site/index.html', (h) => h.replace(/<div data-release-file="zip"[\s\S]*?<\/div>\n/, '')));
  mentions(p, /data-release-file="zip"/);
  mentions(p, new RegExp(`zip download ${ZIP_URL.replaceAll('.', '\\.')} is not a plain <a href>`));
  assert.equal(p.length, 2, p.join('\n'));
});

test('download_links_are_plain_anchors', async () => {
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replaceAll(`href="${SETUP_URL}"`, `data-go="${SETUP_URL}"`).replaceAll(`href='${SETUP_URL}'`, `data-go='${SETUP_URL}'`))), /plain <a href>|anchor/i);
});

test('readme_anchors_into_page_exist', async () => {
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('id="screens"', 'id="gallery"'))), /#screens/);
});

test('fails_on_em_dash', async () => {
  for (const file of ['site/index.html', 'site/robots.txt', 'site/sitemap.xml', 'site/site.webmanifest', 'README.md']) {
    const p = await broken(({ root }) => writeFileSync(join(root, file), 'a \u2014 b\n', { flag: 'a' }));
    mentions(p, new RegExp(`${file.replace('.', '\\.')}.*em dash`, 'i'));
  }
});

test('fails_on_emoji', async () => {
  for (const file of ['site/index.html', 'site/robots.txt', 'site/sitemap.xml', 'site/site.webmanifest', 'README.md']) {
    const p = await broken(({ root }) => writeFileSync(join(root, file), 'ok \u{1F680}\n', { flag: 'a' }));
    mentions(p, new RegExp(`${file.replace('.', '\\.')}.*emoji`, 'i'));
  }
});

test('fails_on_emoji_outside_the_main_emoji_blocks', async () => {
  // U+2B50 star and U+231A watch are pictographs outside U+1F000-1FAFF and U+2600-27BF; a flag is two regional indicators.
  for (const ch of ['⭐', '⌚', '\u{1F1FA}\u{1F1F8}']) {
    only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('<body>', `<body>${ch}`))), /site\/index\.html:\d+: contains an emoji/);
  }
});

test('fails_on_img_without_alt_or_size', async () => {
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(' alt="The STX console"', ''))), /alt/);
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(' width="1" height="1"', ''))), /width and height/);
});

test('fails_on_missing_local_ref', async () => {
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('src="img/shot.png"', 'src="img/nope.png"'))), /img\/nope\.png/);
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('<img ', '<img srcset="img/gone.png 2x" '))), /img\/gone\.png/);
  mentions(await broken(({ edit }) => edit('site/css/site.css', (c) => c.replace('url("../img/shot.png")', 'url("../img/lost.png")'))), /img\/lost\.png/);
  mentions(await broken(({ edit }) => edit('site/css/site.css', (c) => c.replace('image-set("../img/shot.png" 1x', 'image-set("../img/away.png" 1x'))), /img\/away\.png/);
});

test('fails_on_broken_json_ld', async () => {
  // A second, unparseable block beside the good one: only the parse rule can fire.
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('</head>', '<script type="application/ld+json">{"@context":</script>\n</head>'))), /JSON-LD block 2 is not valid JSON/);
});

test('fails_on_json_ld_context_other_than_schema_org', async () => {
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('"@context":"https://schema.org"', '"@context":"http://schema.org"'))), /@context/);
});

test('fails_without_SoftwareApplication', async () => {
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('SoftwareApplication', 'Thing'))), /SoftwareApplication/);
});

test('fails_when_sitemap_lacks_lastmod', async () => {
  mentions(await broken(({ edit }) => edit('site/sitemap.xml', (x) => x.replace(/<lastmod>.*<\/lastmod>/, ''))), /lastmod/);
});

test('fails_when_robots_lacks_sitemap', async () => {
  mentions(await broken(({ edit }) => edit('site/robots.txt', (r) => r.replace(/Sitemap:.*/, ''))), /robots\.txt.*Sitemap/i);
});

test('fails_without_one_indexnow_key_file', async () => {
  mentions(await broken(({ root }) => rmSync(join(root, 'site', '0123456789abcdef0123456789abcdef.txt'))), /IndexNow/);
  mentions(await broken(({ root }) => writeFileSync(join(root, 'site', 'fedcba9876543210fedcba9876543210.txt'), 'fedcba9876543210fedcba9876543210')), /IndexNow/);
});

test('fails_on_title_or_description_length', async () => {
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(/<title>.*<\/title>/, '<title>Short</title>'))), /title/i);
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(/<title>.*<\/title>/, `<title>${'x'.repeat(66)}</title>`))), /title/i);
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(/(name="description" content=")[^"]*/, '$1too short'))), /description/i);
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(/(name="description" content=")[^"]*/, `$1${'y'.repeat(166)}`))), /description/i);
});

test('root_absolute_refs_resolve_only_under_the_base_path', async () => {
  // Pages serves /img/shot.png from the github.io root, outside the project: a problem even though site/img/shot.png exists.
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('src="img/shot.png"', 'src="/img/shot.png"'))), /\/img\/shot\.png.*outside \/STX-Downloader\//);
  assert.deepEqual(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('src="img/shot.png"', 'src="/STX-Downloader/img/shot.png"'))), []);
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('src="img/shot.png"', 'src="/STX-Downloader/img/nope.png"'))), /\/STX-Downloader\/img\/nope\.png/);
});

test('self_urls_resolve_to_files_in_site', async () => {
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(`content="${PAGE}img/shot.png"`, `content="${PAGE}img/card.png"`))), /img\/card\.png/);
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('src="img/shot.png"', `src="${PAGE}img/gone.png"`))), /img\/gone\.png/);
});

test('fails_on_in_page_link_without_target', async () => {
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('href="#downloads"', 'href="#nowhere"'))), /#nowhere/);
});

test('fails_when_canonical_is_not_the_page_url', async () => {
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(`rel="canonical" href="${PAGE}"`, `rel="canonical" href="${PAGE}index.html"`))), /canonical/);
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(/<link rel="canonical"[^>]*>\n/, ''))), /canonical/);
});

test('fails_without_open_graph_or_twitter_card', async () => {
  for (const tag of ['og:title', 'og:description', 'og:image', 'twitter:card']) {
    only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(new RegExp(`<meta (property|name)="${tag}"[^>]*>\\n`), ''))), new RegExp(tag));
  }
});

test('fails_without_html_lang', async () => {
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('<html lang="en">', '<html>'))), /lang/);
});

test('fails_when_sitemap_loc_is_not_a_page_of_the_site', async () => {
  // A second url that names a missing file, then a sitemap that no longer lists the page itself.
  only(await broken(({ edit }) => edit('site/sitemap.xml', (x) => x.replace('</urlset>', `  <url>\n    <loc>${PAGE}gone.html</loc>\n    <lastmod>2026-11-15</lastmod>\n  </url>\n</urlset>`))), /gone\.html/);
  only(await broken(({ edit }) => edit('site/sitemap.xml', (x) => x.replace(`<loc>${PAGE}</loc>`, `<loc>${PAGE}img/shot.png</loc>`))), /sitemap\.xml.*does not list/);
});

test('cli_exits_0_when_clean_and_1_with_problems', async () => {
  const cli = join(here, 'check-site.mjs');
  const clean = spawnSync(process.execPath, [cli, good], { encoding: 'utf8' });
  assert.equal(clean.status, 0, clean.stderr);
  assert.match(clean.stdout, /site check: clean/);
  await inCopy(({ edit }) => edit('site/index.html', (h) => h.replace(HASH, 'A'.repeat(64))), (root) => {
    const bad = spawnSync(process.execPath, [cli, root], { encoding: 'utf8' });
    assert.equal(bad.status, 1, bad.stdout);
    assert.match(bad.stderr, /data-sha256/);
  });
});

test('isEntry_falls_back_to_real_paths_when_node_has_no_import_meta_main', async () => {
  const { isEntry } = await import('./check-site.mjs');
  const cliUrl = new URL('./check-site.mjs', import.meta.url).href;
  const cliPath = fileURLToPath(cliUrl);
  assert.equal(isEntry(cliPath, cliUrl, true), true);
  assert.equal(isEntry(cliPath, cliUrl, false), false);
  assert.equal(isEntry(cliPath, cliUrl, undefined), true);
  assert.equal(isEntry(join(here, 'check-site.test.mjs'), cliUrl, undefined), false);
  assert.equal(isEntry(undefined, cliUrl, undefined), false);
  assert.equal(isEntry(join(here, 'no-such-file.mjs'), cliUrl, undefined), false);
  if (process.platform === 'win32') assert.equal(isEntry(cliPath.toUpperCase(), cliUrl, undefined, 'win32'), true);
});

test('fails_when_json_ld_version_or_download_differs_from_readme', async () => {
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('"softwareVersion":"1.2.0"', '"softwareVersion":"1.1.0"'))), /softwareVersion/);
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(`"downloadUrl":"${SETUP_URL}"`, `"downloadUrl":"${SETUP_URL.replace('1.2.0', '1.1.0')}"`))), /downloadUrl/);
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(/,"softwareVersion":"1\.2\.0"/, ''))), /softwareVersion/);
});

test('fails_when_version_readout_differs_from_readme', async () => {
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('data-release="version" data-value="1.2.0"', 'data-release="version" data-value="1.1.0"'))), /data-release="version".*1\.1\.0/);
});

test('fails_when_visible_release_text_is_stale', async () => {
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('Setup, 254 MB', 'Setup, 250 MB'))), /250 MB/);
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace(`<code>${HASH}</code>`, `<code>${'B'.repeat(64)}</code>`))), /B{64}/);
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('<p>STXDownloader-1.2.0-setup.exe, 254 MB</p>', '<p>STXDownloader-1.1.0-setup.exe, 254 MB</p>'))), /STXDownloader-1\.1\.0-setup\.exe/);
  only(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('<p>171 MB</p>', '<p>170 MB</p>'))), /zip.*170 MB/);
});

test('text_outside_release_elements_is_not_held_to_the_table', async () => {
  // "Captured from STX Downloader 1.2.0" is history, not the current release: other versions may appear outside them.
  assert.deepEqual(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('Captured from STX Downloader 1.2.0', 'Captured from STX Downloader 1.1.0'))), []);
});

test('release_text_reaches_past_nested_elements_of_the_same_name', async () => {
  // The zip block's anchor sits in its own <div>; the stale size after that inner </div> is still the zip block's.
  only(await broken(({ edit }) => edit('site/index.html', (h) => h
    .replace(/(<div data-release-file="zip"[^>]*>\n)(<a [^\n]*<\/a>)/, '$1<div>$2</div>')
    .replace('<p>171 MB</p>', '<p>170 MB</p>'))), /zip.*170 MB/);
});
