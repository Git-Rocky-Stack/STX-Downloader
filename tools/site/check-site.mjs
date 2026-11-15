// Checks the project page in site/ before it's published (.github/workflows/pages.yml runs it, and so can you:
// `node tools/site/check-site.mjs`). It exits 1 with every problem listed, so a page with a missing image, a file
// the sitemap doesn't list, broken structured data, an em dash or an emoji never reaches GitHub Pages.
import { readFileSync, readdirSync, existsSync, statSync } from 'node:fs';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const site = join(root, 'site');
const base = 'https://git-rocky-stack.github.io/STX-Downloader/';
const problems = [];
const fail = (message) => problems.push(message);

const html = readFileSync(join(site, 'index.html'), 'utf8');

// 1. Every local file the page points at exists.
const refs = new Set();
for (const m of html.matchAll(/\s(?:src|href)="([^"]+)"/g)) refs.add(m[1]);
for (const m of html.matchAll(/\ssrcset="([^"]+)"/g)) for (const part of m[1].split(',')) refs.add(part.trim().split(/\s+/)[0]);
for (const ref of refs) {
  if (/^(https?:|mailto:|#)/.test(ref)) continue;
  if (!existsSync(join(site, ref))) fail(`index.html points at ${ref}, which isn't in site/`);
}
for (const m of html.matchAll(/href="#([^"]+)"/g)) {
  if (!html.includes(`id="${m[1]}"`)) fail(`index.html links to #${m[1]}, which no element has as its id`);
}

// 2. Every image has alt text, and every content image has its size, so nothing shifts as it loads.
for (const m of html.matchAll(/<img\b[^>]*>/g)) {
  const tag = m[0];
  if (!/\salt="/.test(tag)) fail(`an <img> has no alt: ${tag.slice(0, 80)}`);
  if (!/\swidth="\d+"/.test(tag) || !/\sheight="\d+"/.test(tag)) fail(`an <img> has no width and height: ${tag.slice(0, 80)}`);
}

// 3. The head carries what search and social cards need.
const required = [
  /<html lang="en">/, /<title>[^<]+<\/title>/, /<meta name="description" content="[^"]+">/,
  /<meta name="keywords" content="[^"]+">/, /<meta name="robots" content="[^"]+">/,
  new RegExp(`<link rel="canonical" href="${base}">`),
  /<meta property="og:title"/, /<meta property="og:description"/, /<meta property="og:image" content="https:/,
  /<meta property="og:url"/, /<meta property="og:type"/,
  /<meta name="twitter:card" content="summary_large_image">/, /<meta name="twitter:title"/,
  /<meta name="twitter:description"/, /<meta name="twitter:image"/,
];
for (const pattern of required) if (!pattern.test(html)) fail(`index.html's head lacks ${pattern}`);
const title = html.match(/<title>([^<]+)<\/title>/)?.[1] ?? '';
const description = html.match(/<meta name="description" content="([^"]+)">/)?.[1] ?? '';
if (title.length < 30 || title.length > 65) fail(`the title is ${title.length} characters; keep it 30 to 65`);
if (description.length < 120 || description.length > 165) fail(`the description is ${description.length} characters; keep it 120 to 165`);

// 4. The structured data parses, and holds the types the page is about.
const blocks = [...html.matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)];
if (blocks.length === 0) fail('index.html has no JSON-LD');
const types = new Set();
for (const block of blocks) {
  try {
    const data = JSON.parse(block[1]);
    if (data['@context'] !== 'https://schema.org') fail('a JSON-LD block\'s @context isn\'t https://schema.org');
    for (const node of data['@graph'] ?? [data]) types.add(node['@type']);
  } catch (error) {
    fail(`a JSON-LD block doesn't parse: ${error.message}`);
  }
}
for (const type of ['Organization', 'WebSite', 'WebPage', 'SoftwareApplication']) {
  if (!types.has(type)) fail(`the JSON-LD has no ${type}`);
}

// 5. The sitemap lists the page and the llms files, each with a lastmod, and nothing that isn't there.
const sitemap = readFileSync(join(site, 'sitemap.xml'), 'utf8');
const locs = [...sitemap.matchAll(/<url><loc>([^<]+)<\/loc><lastmod>(\d{4}-\d{2}-\d{2})<\/lastmod><\/url>/g)];
const listed = new Set(locs.map((m) => m[1]));
for (const page of [base, `${base}llms.txt`, `${base}llms-full.txt`]) {
  if (!listed.has(page)) fail(`sitemap.xml doesn't list ${page} with a lastmod`);
}
for (const [, loc] of locs) {
  const path = loc.slice(base.length) || 'index.html';
  if (!loc.startsWith(base) || !existsSync(join(site, path))) fail(`sitemap.xml lists ${loc}, which isn't in site/`);
}
if (!/Sitemap: https:\/\/git-rocky-stack\.github\.io\/STX-Downloader\/sitemap\.xml/.test(readFileSync(join(site, 'robots.txt'), 'utf8'))) {
  fail('robots.txt doesn\'t point at the sitemap');
}

// 6. The IndexNow key file holds its own name, as the protocol asks.
const keys = readdirSync(site).filter((f) => /^[0-9a-f]{32}\.txt$/.test(f));
if (keys.length !== 1) fail(`site/ should hold one IndexNow key file; it holds ${keys.length}`);
for (const key of keys) {
  if (readFileSync(join(site, key), 'utf8').trim() !== key.slice(0, -4)) fail(`${key} doesn't hold its own key`);
}

// 7. No em dashes and no emojis, in the page, its text files, or the README that links to it.
const banned = /[—]|\p{Extended_Pictographic}/u;
const textFiles = [];
const walk = (dir) => {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path);
    else if (/\.(html|txt|xml|webmanifest|md)$/.test(name)) textFiles.push(path);
  }
};
walk(site);
textFiles.push(join(root, 'README.md'));
for (const file of textFiles) {
  readFileSync(file, 'utf8').split('\n').forEach((line, i) => {
    const hit = line.match(banned);
    if (hit) fail(`${relative(root, file)}:${i + 1} has "${hit[0]}" (U+${hit[0].codePointAt(0).toString(16).toUpperCase()})`);
  });
}

if (problems.length) {
  console.error(`site check: ${problems.length} problem(s)`);
  for (const p of problems) console.error(`  - ${p}`);
  process.exit(1);
}
console.log(`site check: ok (${refs.size} references, ${blocks.length} JSON-LD block, ${locs.length} sitemap URLs, ${textFiles.length} text files, title ${title.length} chars, description ${description.length} chars)`);
