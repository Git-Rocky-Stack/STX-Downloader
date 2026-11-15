// Pre-publish check for the project page in site/. .github/workflows/pages.yml runs it, and so can you:
// `node tools/site/check-site.mjs`. It exits 1 and lists every problem, so a page that disagrees with the README's
// release table, links to a missing file, or carries an em dash or an emoji never reaches GitHub Pages.
import { readFileSync, readdirSync, existsSync, statSync } from 'node:fs';
import { join, dirname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const PAGE_URL = 'https://git-rocky-stack.github.io/STX-Downloader/';
const EM_DASH = /\u2014/;
const EMOJI = /[\u{1F000}-\u{1FAFF}\u{2600}-\u{27BF}\u{FE0F}\u{200D}]/u;
const TITLE_LEN = [30, 65];
const DESC_LEN = [120, 165];

const read = (path) => readFileSync(path, 'utf8');
const lineOf = (text, index) => text.slice(0, index).split('\n').length;

// ---- HTML helpers (regex parsing is enough for this one controlled page) ----

function attrsOf(source) {
  const attrs = {};
  for (const m of source.matchAll(/([^\s=\/"'>]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+)))?/g)) {
    attrs[m[1].toLowerCase()] = m[2] ?? m[3] ?? m[4] ?? '';
  }
  return attrs;
}

function tagsOf(html) {
  const bare = html.replace(/<!--[\s\S]*?-->/g, '').replace(/<(script|style)\b[^>]*>[\s\S]*?<\/\1>/gi, (m) => m.slice(0, m.indexOf('>') + 1));
  const tags = [];
  for (const m of bare.matchAll(/<([a-zA-Z][\w-]*)((?:[^>"']|"[^"]*"|'[^']*')*)>/g)) tags.push({ name: m[1].toLowerCase(), attrs: attrsOf(m[2]) });
  return tags;
}

// ---- README release table ----

function readmeRows(readme) {
  const lines = readme.split(/\r?\n/);
  const head = lines.findIndex((l) => /^\|\s*File\s*\|\s*Size\s*\|\s*SHA-256\s*\|\s*$/.test(l));
  if (head < 0) return null;
  const rows = {};
  for (const line of lines.slice(head + 2)) {
    if (!line.startsWith('|')) break;
    const cells = line.split('|').slice(1, -1).map((c) => c.trim());
    const link = /\[`?([^\]`]+)`?\]\(([^)]+)\)/.exec(cells[0] ?? '');
    if (!link) continue;
    const [, file, url] = link;
    const kind = file.endsWith('-setup.exe') ? 'setup' : file.endsWith('-win-x64.zip') ? 'zip' : null;
    if (kind) rows[kind] = { file, url, size: cells[1], sha256: cells[2], version: /-(\d+\.\d+\.\d+)-/.exec(file)?.[1] };
  }
  return rows;
}

function checkParity(rows, tags, problems) {
  for (const kind of ['setup', 'zip']) {
    const row = rows[kind];
    if (!row) { problems.push(`README.md: the download table has no ${kind} row`); continue; }
    const releaseVersion = /\/releases\/download\/v([^/]+)\//.exec(row.url)?.[1];
    if (releaseVersion !== row.version) problems.push(`README.md: ${row.file} has version ${row.version} but its release link says ${releaseVersion}`);
    const els = tags.filter((t) => t.attrs['data-release-file'] === kind);
    if (!els.length) { if (kind === 'setup') problems.push('site/index.html: no element has data-release-file="setup"'); continue; }
    for (const { attrs } of els) {
      const where = `site/index.html: data-release-file="${kind}"`;
      if (attrs['data-version'] !== row.version) problems.push(`${where} has data-version "${attrs['data-version']}", README says "${row.version}"`);
      if ((attrs['data-sha256'] ?? '').toLowerCase() !== (row.sha256 ?? '').toLowerCase()) problems.push(`${where} has data-sha256 "${attrs['data-sha256']}", README says "${row.sha256}"`);
      if (attrs['data-size'] !== row.size) problems.push(`${where} has data-size "${attrs['data-size']}", README says "${row.size}"`);
      if ('href' in attrs && attrs.href !== row.url) problems.push(`${where} has href "${attrs.href}", README says "${row.url}"`);
    }
    if (!tags.some((t) => t.name === 'a' && t.attrs.href === row.url)) problems.push(`site/index.html: the ${kind} download ${row.url} is not a plain <a href> in the HTML source`);
  }
}

// ---- local references ----

const isRemote = (ref) => /^([a-z][a-z0-9+.-]*:|\/\/|#)/i.test(ref);

function checkRef(ref, baseDir, siteDir, owner, problems) {
  if (!ref || isRemote(ref)) return;
  const clean = decodeURI(ref.split('#')[0].split('?')[0]);
  if (!clean) return;
  const target = clean.startsWith('/') ? join(siteDir, clean) : resolve(baseDir, clean);
  const inside = target === siteDir || target.startsWith(siteDir + sep);
  if (!inside || !existsSync(target)) problems.push(`${owner}: points at ${ref}, which isn't in site/`);
}

function cssRefs(css) {
  const refs = [];
  for (const m of css.matchAll(/url\(\s*(?:"([^"]*)"|'([^']*)'|([^)\s]*))\s*\)/gi)) refs.push(m[1] ?? m[2] ?? m[3]);
  for (const m of css.matchAll(/image-set\(([^)]*)\)/gi)) {
    for (const s of m[1].matchAll(/(?:^|,)\s*(?:"([^"]*)"|'([^']*)')/g)) refs.push(s[1] ?? s[2]);
  }
  return refs;
}

// ---- the check ----

export async function checkSite(repoRoot) {
  const problems = [];
  const siteDir = join(repoRoot, 'site');
  const pagePath = join(siteDir, 'index.html');
  const readmePath = join(repoRoot, 'README.md');
  if (!existsSync(pagePath)) return ['site/index.html: missing'];
  const html = read(pagePath);
  const readme = existsSync(readmePath) ? read(readmePath) : null;
  if (readme === null) problems.push('README.md: missing');
  const tags = tagsOf(html);

  // README parity
  if (readme !== null) {
    const rows = readmeRows(readme);
    if (!rows) problems.push('README.md: no "| File | Size | SHA-256 |" download table');
    else checkParity(rows, tags, problems);
    const ids = new Set(tags.map((t) => t.attrs.id).filter(Boolean));
    for (const m of readme.matchAll(/\]\(https:\/\/git-rocky-stack\.github\.io\/STX-Downloader\/#([^)\s]+)\)/g)) {
      if (!ids.has(m[1])) problems.push(`README.md:${lineOf(readme, m.index)}: links to ${PAGE_URL}#${m[1]}, but site/index.html has no element with id="${m[1]}"`);
    }
  }

  // em dashes and emoji
  const texts = [['site/index.html', html]];
  for (const name of readdirSync(siteDir)) {
    if (/\.(txt|xml|webmanifest)$/.test(name)) texts.push([`site/${name}`, read(join(siteDir, name))]);
  }
  if (readme !== null) texts.push(['README.md', readme]);
  for (const [name, text] of texts) {
    const dash = EM_DASH.exec(text);
    if (dash) problems.push(`${name}:${lineOf(text, dash.index)}: contains an em dash`);
    const emoji = EMOJI.exec(text);
    if (emoji) problems.push(`${name}:${lineOf(text, emoji.index)}: contains an emoji`);
  }

  // images
  for (const t of tags.filter((x) => x.name === 'img')) {
    const src = t.attrs.src ?? '(no src)';
    if (t.attrs.alt === undefined) problems.push(`site/index.html: <img ${src}> has no alt attribute`);
    if (!t.attrs.width || !t.attrs.height) problems.push(`site/index.html: <img ${src}> needs both width and height`);
  }

  // local references: html src, href, srcset; css url() and image-set()
  for (const { attrs } of tags) {
    for (const key of ['src', 'href']) if (attrs[key] !== undefined) checkRef(attrs[key], siteDir, siteDir, 'site/index.html', problems);
    if (attrs.srcset) for (const part of attrs.srcset.split(',')) checkRef(part.trim().split(/\s+/)[0], siteDir, siteDir, 'site/index.html', problems);
  }
  const cssDir = join(siteDir, 'css');
  if (existsSync(cssDir)) {
    for (const name of readdirSync(cssDir).filter((n) => n.endsWith('.css'))) {
      for (const ref of cssRefs(read(join(cssDir, name)))) checkRef(ref, cssDir, siteDir, `site/css/${name}`, problems);
    }
  }

  // JSON-LD
  const blocks = [...html.matchAll(/<script\b[^>]*type\s*=\s*["']application\/ld\+json["'][^>]*>([\s\S]*?)<\/script>/gi)].map((m) => m[1]);
  let hasApp = false;
  const hasType = (node) => Array.isArray(node) ? node.some(hasType) : node && typeof node === 'object'
    && ([].concat(node['@type'] ?? []).includes('SoftwareApplication') || hasType(node['@graph'] ?? []));
  for (const [i, block] of blocks.entries()) {
    try { hasApp ||= hasType(JSON.parse(block)); } catch (e) { problems.push(`site/index.html: JSON-LD block ${i + 1} is not valid JSON (${e.message})`); }
  }
  if (!hasApp) problems.push('site/index.html: no JSON-LD block of @type SoftwareApplication');

  // sitemap, robots, IndexNow
  const sitemapPath = join(siteDir, 'sitemap.xml');
  if (!existsSync(sitemapPath)) problems.push('site/sitemap.xml: missing');
  else {
    const urls = [...read(sitemapPath).matchAll(/<url>([\s\S]*?)<\/url>/g)];
    if (!urls.length) problems.push('site/sitemap.xml: lists no <url>');
    for (const u of urls) if (!/<lastmod>\s*\S[^<]*<\/lastmod>/.test(u[1])) problems.push(`site/sitemap.xml: a <url> has no lastmod (${/<loc>([^<]*)/.exec(u[1])?.[1] ?? 'no loc'})`);
  }
  const robotsPath = join(siteDir, 'robots.txt');
  if (!existsSync(robotsPath)) problems.push('site/robots.txt: missing');
  else if (!/^\s*Sitemap:\s*https?:\/\/\S+/im.test(read(robotsPath))) problems.push('site/robots.txt: has no Sitemap: line');
  const keys = readdirSync(siteDir).filter((n) => /^[0-9a-f]{32}\.txt$/.test(n) && statSync(join(siteDir, n)).isFile());
  if (keys.length !== 1) problems.push(`site/: expected exactly one IndexNow key file (32 hex characters, .txt), found ${keys.length}`);
  else if (read(join(siteDir, keys[0])).trim() !== keys[0].slice(0, 32)) problems.push(`site/${keys[0]}: IndexNow key file must contain its own key`);

  // title and description
  const title = /<title[^>]*>([\s\S]*?)<\/title>/i.exec(html)?.[1].trim();
  if (title === undefined) problems.push('site/index.html: no <title>');
  else if (title.length < TITLE_LEN[0] || title.length > TITLE_LEN[1]) problems.push(`site/index.html: title is ${title.length} characters, want ${TITLE_LEN[0]} to ${TITLE_LEN[1]}`);
  const desc = tags.find((t) => t.name === 'meta' && t.attrs.name?.toLowerCase() === 'description')?.attrs.content;
  if (desc === undefined) problems.push('site/index.html: no meta description');
  else if (desc.length < DESC_LEN[0] || desc.length > DESC_LEN[1]) problems.push(`site/index.html: description is ${desc.length} characters, want ${DESC_LEN[0]} to ${DESC_LEN[1]}`);

  return problems;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
  const problems = await checkSite(root);
  if (problems.length) {
    console.error(`${problems.length} problem(s):\n` + problems.map((p) => `  - ${p}`).join('\n'));
    process.exit(1);
  }
  console.log('site check: clean');
}
