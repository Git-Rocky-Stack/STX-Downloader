import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cpSync, mkdtempSync, readFileSync, writeFileSync, readdirSync, renameSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkSite } from './check-site.mjs';

const good = join(dirname(fileURLToPath(import.meta.url)), 'fixtures', 'good');
const SETUP_URL = 'https://github.com/Git-Rocky-Stack/STX-Downloader/releases/download/v1.2.0/STXDownloader-1.2.0-setup.exe';
const HASH = '1E067230272C7223E1A9430E0B9BFF8E3ADF8C8666477CB2323C2592E7590363';

// Copies the good fixture, lets `change` edit it, and returns the problems the check reports.
async function broken(change) {
  const root = mkdtempSync(join(tmpdir(), 'stx-site-'));
  try {
    cpSync(good, root, { recursive: true });
    const edit = (rel, fn) => writeFileSync(join(root, rel), fn(readFileSync(join(root, rel), 'utf8')));
    change({ root, edit });
    return await checkSite(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}
const mentions = (problems, re) => assert.ok(problems.some((p) => re.test(p)), `no problem matched ${re}; got:\n${problems.join('\n')}`);

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
  mentions(await broken(({ edit }) => edit('site/index.html', (h) => h.replace('"@type":"SoftwareApplication",', '"@type":"SoftwareApplication"'))), /JSON-LD/);
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
