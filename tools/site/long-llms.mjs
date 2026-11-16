// site/long-llms.txt: the notes of each released version, newest first, as CHANGELOG.md gives them, for language models.
// Rocky's SEO/GEO diligence keeps long-llms.txt for an archive of dated entries; STX has no blog, so its archive is its
// release history. It is made from the CHANGELOG, never typed, and the publish (pages.yml) and the tests check it.
//
//   node tools/site/long-llms.mjs [repoRoot]           writes site/long-llms.txt
//   node tools/site/long-llms.mjs --check [repoRoot]   exits 1 when it differs from what the CHANGELOG makes
//
// Released versions are the "## 1.2.0 (2026-11-15)" sections; Unreleased is left out until it is released.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { PAGE_URL, isEntry } from './check-site.mjs';

const REPO = 'https://github.com/Git-Rocky-Stack/STX-Downloader';
const RELEASE = /^## (\d+)\.(\d+)\.(\d+) \((\d{4}-\d{2}-\d{2})\)$/;

/** The released versions in a CHANGELOG, newest first by version number, each with its notes. */
export function releasesIn(changelog) {
  const text = changelog.replace(/\r\n/g, '\n');
  const heads = [...text.matchAll(/^## .*$/gm)];
  const releases = [];
  heads.forEach((head, i) => {
    const m = RELEASE.exec(head[0]);
    if (!m) return;
    const end = heads[i + 1]?.index ?? text.length;
    releases.push({ version: `${m[1]}.${m[2]}.${m[3]}`, parts: [+m[1], +m[2], +m[3]], date: m[4], notes: text.slice(head.index + head[0].length, end).trim() });
  });
  return releases.sort((a, b) => b.parts[0] - a.parts[0] || b.parts[1] - a.parts[1] || b.parts[2] - a.parts[2]);
}

// A link relative to the repository (docs/THIRD_PARTY.md) would break on the page's host, so it points at GitHub.
const absolute = (notes) => notes.replace(/\]\((?![a-z][a-z0-9+.-]*:|#|\/\/)\.?\/?([^)\s]+)\)/gi, (_, path) => `](${REPO}/blob/main/${path})`);

/** The text of site/long-llms.txt for this CHANGELOG. */
export function archiveText(changelog) {
  const releases = releasesIn(changelog);
  if (!releases.length) throw new Error('CHANGELOG.md has no released version ("## 1.2.0 (2026-11-15)")');
  const count = releases.length === 1 ? '1 release' : `${releases.length} releases`;
  return [
    '# STX Downloader: release history',
    '',
    `> The release notes of STX Downloader, newest first, as its CHANGELOG gives them. The project page is ${PAGE_URL} and its short index is ${PAGE_URL}llms.txt.`,
    '',
    `Notes for ${count}; the newest is ${releases[0].version} (${releases[0].date}). Source: ${REPO}/blob/main/CHANGELOG.md`,
    '',
    ...releases.flatMap((r) => [`## ${r.version} (${r.date})`, '', absolute(r.notes), '']),
  ].join('\n');
}

const archiveOf = (root) => join(root, 'site', 'long-llms.txt');
const made = (root) => archiveText(readFileSync(join(root, 'CHANGELOG.md'), 'utf8'));

/** What is wrong with site/long-llms.txt; empty when it is what the CHANGELOG makes. Line endings don't count. */
export function checkArchive(root) {
  const path = archiveOf(root);
  if (!existsSync(path)) return ['site/long-llms.txt: missing (run node tools/site/long-llms.mjs)'];
  return readFileSync(path, 'utf8').replace(/\r\n/g, '\n') === made(root)
    ? []
    : ['site/long-llms.txt: differs from what CHANGELOG.md makes (run node tools/site/long-llms.mjs)'];
}

if (isEntry(process.argv[1], import.meta.url, import.meta.main)) {
  const args = process.argv.slice(2);
  const given = args.find((a) => a !== '--check');
  const root = given ? resolve(given) : join(dirname(fileURLToPath(import.meta.url)), '..', '..');
  if (args.includes('--check')) {
    const problems = checkArchive(root);
    if (problems.length) {
      for (const p of problems) console.error(p);
      process.exit(1);
    }
    console.log('release archive: current');
  } else {
    writeFileSync(archiveOf(root), made(root));
    console.log(`wrote site/long-llms.txt: ${releasesIn(readFileSync(join(root, 'CHANGELOG.md'), 'utf8')).map((r) => r.version).join(', ')}`);
  }
}
