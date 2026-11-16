// Each sitemap URL's <lastmod>, taken from git rather than typed: the date of the last commit that changed the files
// behind the URL, or today when one of them has changes not yet committed. Rocky's SEO/GEO diligence asks for lastmod
// to be generated, with a --check the publish runs (pages.yml), so a hand-set date can't go stale unnoticed.
//
//   node tools/site/lastmod.mjs [repoRoot]           writes the dates into site/sitemap.xml
//   node tools/site/lastmod.mjs --check [repoRoot]   exits 1, naming each URL whose date git disagrees with
//
// A file URL (llms.txt, say) is dated by that file. The page is dated by everything else in site/, except the files
// that only describe the site to crawlers (sitemap.xml, robots.txt, the IndexNow key), so a new sitemap doesn't move it.
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { PAGE_URL, isEntry } from './check-site.mjs';

const KEY_FILE = /^[0-9a-f]{32}\.txt$/;
const URL_BLOCK = /<url>([\s\S]*?)<\/url>/g;
const LOC = /<loc>\s*([^<\s]+)\s*<\/loc>/;

/** Today on this computer's calendar, as git prints a commit's date (YYYY-MM-DD). */
export function today() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

const git = (root, args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
const sitemapOf = (root) => join(root, 'site', 'sitemap.xml');
const locsIn = (xml) => [...xml.matchAll(URL_BLOCK)].map((m) => LOC.exec(m[1])?.[1]).filter((loc) => loc?.startsWith(PAGE_URL));

// The pathspec of the files behind one URL, or null when there is no file to date it by.
function filesBehind(root, loc, locs) {
  const rel = loc.slice(PAGE_URL.length);
  if (rel) return existsSync(join(root, 'site', rel)) ? [`site/${rel}`] : null;
  if (!existsSync(join(root, 'site', 'index.html'))) return null;
  const crawlerFiles = ['sitemap.xml', 'robots.txt', ...readdirSync(join(root, 'site')).filter((n) => KEY_FILE.test(n))];
  const otherUrls = locs.map((l) => l.slice(PAGE_URL.length)).filter(Boolean);
  return ['site', ...[...otherUrls, ...crawlerFiles].map((n) => `:(exclude)site/${n}`)];
}

function dateOf(root, paths) {
  if (!paths) return null;
  if (git(root, ['status', '--porcelain', '--', ...paths])) return today();
  return git(root, ['log', '-1', '--format=%cs', '--', ...paths]) || null;
}

/** Each sitemap URL under the page, with the date git gives the files behind it (null: no file behind it). */
export function siteLastmods(root) {
  const locs = locsIn(readFileSync(sitemapOf(root), 'utf8'));
  return new Map(locs.map((loc) => [loc, dateOf(root, filesBehind(root, loc, locs))]));
}

/** Writes git's date into each URL's <lastmod>, changing nothing else in the sitemap. Returns the dates. */
export function writeLastmods(root) {
  const path = sitemapOf(root);
  const xml = readFileSync(path, 'utf8');
  const dates = siteLastmods(root);
  const next = xml.replace(URL_BLOCK, (block, inner) => {
    const date = dates.get(LOC.exec(inner)?.[1]);
    if (!date) return block;
    return /<lastmod>[^<]*<\/lastmod>/.test(block)
      ? block.replace(/<lastmod>[^<]*<\/lastmod>/, `<lastmod>${date}</lastmod>`)
      : block.replace('</loc>', `</loc>\n    <lastmod>${date}</lastmod>`);
  });
  if (next !== xml) writeFileSync(path, next);
  return dates;
}

/** What is wrong with the sitemap's dates, as git sees them; empty when every one is current. */
export function checkLastmods(root) {
  // A shallow clone holds only the newest commit, which would date every file by it and pass a stale sitemap.
  if (git(root, ['rev-parse', '--is-shallow-repository']) === 'true')
    return ["site/sitemap.xml: this clone is shallow, so git can't date the files behind each URL (check out with fetch-depth: 0)"];
  const xml = readFileSync(sitemapOf(root), 'utf8');
  const dates = siteLastmods(root);
  const problems = [];
  for (const [, inner] of xml.matchAll(URL_BLOCK)) {
    const loc = LOC.exec(inner)?.[1];
    if (!loc?.startsWith(PAGE_URL)) continue;
    const stated = /<lastmod>\s*([^<\s]*)\s*<\/lastmod>/.exec(inner)?.[1] || 'no date';
    const date = dates.get(loc);
    if (!date) problems.push(`site/sitemap.xml: ${loc} has no file behind it to date`);
    else if (stated !== date) problems.push(`site/sitemap.xml: ${loc} says ${stated}; git says ${date} (run node tools/site/lastmod.mjs, then commit the sitemap)`);
  }
  return problems;
}

if (isEntry(process.argv[1], import.meta.url, import.meta.main)) {
  const args = process.argv.slice(2);
  const given = args.find((a) => a !== '--check');
  const root = given ? resolve(given) : join(dirname(fileURLToPath(import.meta.url)), '..', '..');
  if (args.includes('--check')) {
    const problems = checkLastmods(root);
    if (problems.length) {
      for (const p of problems) console.error(p);
      process.exit(1);
    }
    console.log('sitemap dates: current');
  } else {
    for (const [loc, date] of writeLastmods(root)) console.log(`${date ?? 'no file'}  ${loc}`);
  }
}
