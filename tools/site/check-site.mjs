// Pre-publish check for the project page in site/. .github/workflows/pages.yml runs it, and so can you:
// `node tools/site/check-site.mjs`. It exits 1 and lists every problem, so a page that disagrees with the README's
// release table, links to a missing file, or carries an em dash or an emoji never reaches GitHub Pages.
import { readFileSync, readdirSync, existsSync, statSync, realpathSync } from 'node:fs';
import { join, dirname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

export const PAGE_URL = 'https://git-rocky-stack.github.io/STX-Downloader/';
// GitHub Pages serves site/ at this path, so a root-absolute reference anywhere else leaves the project.
const BASE_PATH = '/STX-Downloader/';
const EM_DASH = /\u2014/;
// Any pictograph, plus the parts that build emoji out of plain characters: flag letters, the keycap and VS16.
const EMOJI = /\p{Extended_Pictographic}|[\u{1F1E6}-\u{1F1FF}\u{20E3}\u{FE0F}]/u;
const OPEN_GRAPH = ['og:title', 'og:description', 'og:image', 'twitter:card'];
const LLMS_FILES = ['llms.txt', 'llms-full.txt'];
// The release archive for language models, made from the CHANGELOG by tools/site/long-llms.mjs.
const ARCHIVE = 'long-llms.txt';
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

// The page without comments, and with script and style bodies emptied, so neither is read as markup.
const bareOf = (html) => html.replace(/<!--[\s\S]*?-->/g, '').replace(/<(script|style)\b[^>]*>[\s\S]*?<\/\1>/gi, (m) => m.slice(0, m.indexOf('>') + 1));
const OPEN_TAG = /<([a-zA-Z][\w-]*)((?:[^>"']|"[^"]*"|'[^']*')*)>/g;

function tagsOf(html) {
  const tags = [];
  for (const m of bareOf(html).matchAll(OPEN_TAG)) tags.push({ name: m[1].toLowerCase(), attrs: attrsOf(m[2]) });
  return tags;
}

// The text people read inside each element that carries data-release-file, or is a data-release="version"
// readout (kind "version"), up to its own closing tag.
function releaseTexts(html) {
  const bare = bareOf(html);
  const out = [];
  for (const m of bare.matchAll(OPEN_TAG)) {
    const attrs = attrsOf(m[2]);
    const kind = attrs['data-release-file'] ?? (attrs['data-release'] === 'version' ? 'version' : undefined);
    if (!kind) continue;
    const start = m.index + m[0].length;
    const tag = new RegExp(`<(/?)${m[1]}\\b[^>]*>`, 'gi');
    tag.lastIndex = start;
    let depth = 1, end = bare.length, t;
    while ((t = tag.exec(bare))) { depth += t[1] ? -1 : 1; if (depth === 0) { end = t.index; break; } }
    out.push({ kind, text: bare.slice(start, end).replace(/<[^>]*>/g, ' ').replace(/&nbsp;/g, ' ').replace(/\s+/g, ' ').trim() });
  }
  return out;
}

// Release facts written in that text: each must be the README row's own (ruling R24).
function checkReleaseText(rows, html, problems) {
  for (const { kind, text } of releaseTexts(html)) {
    if (kind === 'version') {
      for (const [version] of text.matchAll(/\b\d+\.\d+\.\d+\b/g)) {
        if (version !== rows.setup?.version) problems.push(`site/index.html: data-release="version" reads ${version}, README says ${rows.setup?.version}`);
      }
      continue;
    }
    const row = rows[kind];
    if (!row) continue;
    const where = `site/index.html: the ${kind} download's text`;
    let rest = text;
    for (const [file] of text.matchAll(/STXDownloader-[\w.-]+?\.(?:exe|zip)\b/g)) {
      if (file !== row.file) problems.push(`${where} names ${file}, README says ${row.file}`);
      rest = rest.replaceAll(file, ' ');
    }
    for (const [hash] of rest.matchAll(/\b[0-9a-fA-F]{64}\b/g)) {
      if (hash.toLowerCase() !== row.sha256.toLowerCase()) problems.push(`${where} shows SHA-256 ${hash}, README says ${row.sha256}`);
    }
    for (const [size] of rest.matchAll(/\b\d+(?:\.\d+)?\s?[KMG]B\b/g)) {
      if (size.replace(/\s+/, ' ') !== row.size) problems.push(`${where} shows ${size}, README says ${row.size}`);
    }
    for (const [version] of rest.matchAll(/\b\d+\.\d+\.\d+\b/g)) {
      if (version !== row.version) problems.push(`${where} shows version ${version}, README says ${row.version}`);
    }
  }
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
    if (!els.length) problems.push(`site/index.html: no element has data-release-file="${kind}"`);
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

// Release facts in a text file that names no particular row: each file name, hash and release download link must
// be one of the README's, a size on a line that names a release file must be that file's size, and a
// "version x.y.z" must be the README's version.
function checkTextFacts(owner, text, rows, problems) {
  const known = [rows.setup, rows.zip].filter(Boolean);
  for (const [url] of text.matchAll(/https:\/\/github\.com\/Git-Rocky-Stack\/STX-Downloader\/releases\/download\/[^\s)"'<>]+/g)) {
    if (!known.some((r) => r.url === url)) problems.push(`${owner}: links ${url}, which isn't in the README's download table`);
  }
  for (const line of text.split(/\r?\n/)) {
    if (line.includes('/releases/download/')) continue;
    const row = known.find((r) => line.includes(r.file));
    if (!row) continue;
    for (const [size] of line.matchAll(/\b\d+(?:\.\d+)?\s?[KMG]B\b/g)) {
      if (size.replace(/\s+/, ' ') !== row.size) problems.push(`${owner}: says ${size} for ${row.file}, README says ${row.size}`);
    }
  }
  let rest = text;
  for (const [file] of text.matchAll(/STXDownloader-[\w.-]+?\.(?:exe|zip)\b/g)) {
    if (!known.some((r) => r.file === file)) problems.push(`${owner}: names ${file}, which isn't in the README's download table`);
    rest = rest.replaceAll(file, ' ');
  }
  for (const [hash] of rest.matchAll(/\b[0-9a-fA-F]{64}\b/g)) {
    if (!known.some((r) => r.sha256.toLowerCase() === hash.toLowerCase())) problems.push(`${owner}: shows SHA-256 ${hash}, which isn't in the README's download table`);
  }
  for (const m of rest.matchAll(/\bversion\s+(\d+\.\d+\.\d+)\b/gi)) {
    if (m[1] !== rows.setup?.version) problems.push(`${owner}: says version ${m[1]}, README says ${rows.setup?.version}`);
  }
}

// ---- local references ----

const isRemote = (ref) => /^([a-z][a-z0-9+.-]*:|\/\/|#)/i.test(ref);

// A reference must land on a file in site/. Relative refs resolve from `baseDir`; the page's own URL and
// /STX-Downloader/ paths resolve from site/; any other root-absolute path is outside what Pages serves for us.
// A path ending in / means its index.html.
function checkRef(ref, baseDir, siteDir, owner, problems) {
  if (!ref) return;
  const path = ref.split('#')[0].split('?')[0];
  let target;
  if (ref.startsWith(PAGE_URL)) target = join(siteDir, decodeURI(path.slice(PAGE_URL.length)));
  else if (path.startsWith('/') && !path.startsWith('//')) {
    if (path !== BASE_PATH.slice(0, -1) && !path.startsWith(BASE_PATH)) {
      problems.push(`${owner}: points at ${ref}, which is outside ${BASE_PATH} (Pages serves site/ there)`);
      return;
    }
    target = join(siteDir, decodeURI(path.slice(BASE_PATH.length - 1)));
  } else if (isRemote(ref) || !path) return;
  else target = resolve(baseDir, decodeURI(path));
  if (path.endsWith('/') || target === siteDir || (existsSync(target) && statSync(target).isDirectory())) target = join(target, 'index.html');
  const inside = target.startsWith(siteDir + sep);
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
  const ids = new Set(tags.map((t) => t.attrs.id).filter(Boolean));

  // README parity
  const rows = readme === null ? null : readmeRows(readme);
  if (readme !== null) {
    if (!rows) problems.push('README.md: no "| File | Size | SHA-256 |" download table');
    else {
      checkParity(rows, tags, problems);
      checkReleaseText(rows, html, problems);
      for (const { attrs } of tags.filter((t) => t.attrs['data-release'] === 'version')) {
        if (attrs['data-value'] !== rows.setup?.version) problems.push(`site/index.html: data-release="version" shows ${attrs['data-value']}, README says ${rows.setup?.version}`);
      }
    }
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

  // local references: html src, href, srcset, og:image and twitter:image; css url() and image-set()
  for (const { name, attrs } of tags) {
    for (const key of ['src', 'href']) if (attrs[key] !== undefined) checkRef(attrs[key], siteDir, siteDir, 'site/index.html', problems);
    if (attrs.srcset) for (const part of attrs.srcset.split(',')) checkRef(part.trim().split(/\s+/)[0], siteDir, siteDir, 'site/index.html', problems);
    if (name === 'meta' && ['og:image', 'twitter:image'].includes(attrs.property ?? attrs.name)) checkRef(attrs.content, siteDir, siteDir, 'site/index.html', problems);
  }

  // in-page links: every href="#id" has its element
  for (const { attrs } of tags) {
    if (!attrs.href?.startsWith('#') || attrs.href.length < 2) continue;
    const id = decodeURIComponent(attrs.href.slice(1));
    if (!ids.has(id)) problems.push(`site/index.html: links to #${id}, but no element has id="${id}"`);
  }

  // language, canonical, Open Graph and Twitter card
  if (!tags.find((t) => t.name === 'html')?.attrs.lang) problems.push('site/index.html: <html> has no lang attribute');
  const canonicals = tags.filter((t) => t.name === 'link' && (t.attrs.rel ?? '').toLowerCase().split(/\s+/).includes('canonical'));
  if (canonicals.length !== 1) problems.push(`site/index.html: want one <link rel="canonical">, found ${canonicals.length}`);
  else if (canonicals[0].attrs.href !== PAGE_URL) problems.push(`site/index.html: canonical is "${canonicals[0].attrs.href}", want "${PAGE_URL}"`);
  const metaContent = (key) => tags.find((t) => t.name === 'meta' && (t.attrs.property === key || t.attrs.name === key))?.attrs.content;
  for (const key of OPEN_GRAPH) if (!metaContent(key)) problems.push(`site/index.html: no ${key} meta tag with content`);
  const ogImage = metaContent('og:image');
  if (ogImage && !ogImage.startsWith('https://')) problems.push(`site/index.html: og:image "${ogImage}" must be an absolute https URL`);
  const cssDir = join(siteDir, 'css');
  if (existsSync(cssDir)) {
    for (const name of readdirSync(cssDir).filter((n) => n.endsWith('.css'))) {
      for (const ref of cssRefs(read(join(cssDir, name)))) checkRef(ref, cssDir, siteDir, `site/css/${name}`, problems);
    }
  }

  // JSON-LD
  const blocks = [...html.matchAll(/<script\b[^>]*type\s*=\s*["']application\/ld\+json["'][^>]*>([\s\S]*?)<\/script>/gi)].map((m) => m[1]);
  let hasApp = false;
  const isApp = (node) => [].concat(node?.['@type'] ?? []).includes('SoftwareApplication');
  // Every SoftwareApplication node, at the top level, in an array or inside @graph.
  const appsIn = (node) => Array.isArray(node) ? node.flatMap(appsIn)
    : node && typeof node === 'object' ? [...(isApp(node) ? [node] : []), ...appsIn(node['@graph'] ?? [])] : [];
  const hasType = (node) => appsIn(node).length > 0;
  // The structured data names the release too, so it is held to the README table like the download links.
  const checkAppRelease = (app, i) => {
    const setup = rows?.setup;
    if (!setup) return;
    if (app.softwareVersion !== setup.version) problems.push(`site/index.html: JSON-LD block ${i + 1} has softwareVersion ${JSON.stringify(app.softwareVersion)}, README says "${setup.version}"`);
    if (app.downloadUrl !== setup.url) problems.push(`site/index.html: JSON-LD block ${i + 1} has downloadUrl ${JSON.stringify(app.downloadUrl)}, README says "${setup.url}"`);
  };
  // Every block is parsed and checked: none is skipped once the SoftwareApplication block has been seen.
  for (const [i, block] of blocks.entries()) {
    let data;
    try { data = JSON.parse(block); } catch (e) { problems.push(`site/index.html: JSON-LD block ${i + 1} is not valid JSON (${e.message})`); continue; }
    for (const node of [].concat(data)) {
      if (node?.['@context'] !== 'https://schema.org') problems.push(`site/index.html: JSON-LD block ${i + 1} has @context ${JSON.stringify(node?.['@context'])}, want "https://schema.org"`);
    }
    for (const app of appsIn(data)) checkAppRelease(app, i);
    if (hasType(data)) hasApp = true;
  }
  if (!hasApp) problems.push('site/index.html: no JSON-LD block of @type SoftwareApplication');

  // sitemap, robots, IndexNow
  const sitemapPath = join(siteDir, 'sitemap.xml');
  let sitemapLocs = null;
  if (!existsSync(sitemapPath)) problems.push('site/sitemap.xml: missing');
  else {
    const urls = [...read(sitemapPath).matchAll(/<url>([\s\S]*?)<\/url>/g)];
    if (!urls.length) problems.push('site/sitemap.xml: lists no <url>');
    for (const u of urls) if (!/<lastmod>\s*\S[^<]*<\/lastmod>/.test(u[1])) problems.push(`site/sitemap.xml: a <url> has no lastmod (${/<loc>([^<]*)/.exec(u[1])?.[1] ?? 'no loc'})`);
    const locs = urls.map((u) => /<loc>\s*([^<\s]+)\s*<\/loc>/.exec(u[1])?.[1]);
    for (const loc of locs) {
      if (!loc) problems.push('site/sitemap.xml: a <url> has no <loc>');
      else if (!loc.startsWith(PAGE_URL)) problems.push(`site/sitemap.xml: ${loc} is not under ${PAGE_URL}`);
      else checkRef(loc, siteDir, siteDir, 'site/sitemap.xml', problems);
    }
    if (urls.length && !locs.includes(PAGE_URL)) problems.push(`site/sitemap.xml: does not list the page itself, ${PAGE_URL}`);
    sitemapLocs = locs;
  }

  // The LLM-facing files: both must exist and be in the sitemap (Rocky's SEO/GEO diligence, step 7), and any
  // release fact written in them is held to the README table, like the page's.
  for (const name of LLMS_FILES) {
    const path = join(siteDir, name);
    if (!existsSync(path)) { problems.push(`site/${name}: missing`); continue; }
    if (sitemapLocs && !sitemapLocs.includes(PAGE_URL + name)) problems.push(`site/sitemap.xml: does not list ${PAGE_URL}${name}`);
    if (rows) checkTextFacts(`site/${name}`, read(path), rows, problems);
  }
  // The archive must exist and be in the sitemap too, but its facts are held to the CHANGELOG by long-llms.mjs --check,
  // not to the README's download table: it names older releases' files by design.
  if (!existsSync(join(siteDir, ARCHIVE))) problems.push(`site/${ARCHIVE}: missing`);
  if (sitemapLocs && !sitemapLocs.includes(PAGE_URL + ARCHIVE)) problems.push(`site/sitemap.xml: does not list ${PAGE_URL}${ARCHIVE}`);
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

// True when `argv1` (process.argv[1]) is this module run as the entry script. Node 22.18+/24.2+ answer through
// `main` (import.meta.main); older Node compares real paths, so a symlinked checkout or a drive letter in another
// case can't make the check skip itself and exit 0 unchecked.
export function isEntry(argv1, moduleUrl, main, platform = process.platform) {
  if (typeof main === 'boolean') return main;
  if (!argv1) return false;
  try {
    const a = realpathSync(resolve(argv1));
    const b = realpathSync(fileURLToPath(moduleUrl));
    return platform === 'win32' ? a.toLowerCase() === b.toLowerCase() : a === b;
  } catch {
    return false;
  }
}

// `node tools/site/check-site.mjs [repoRoot]`: checks the repo it lives in unless a root is given.
if (isEntry(process.argv[1], import.meta.url, import.meta.main)) {
  const root = process.argv[2] ? resolve(process.argv[2]) : join(dirname(fileURLToPath(import.meta.url)), '..', '..');
  const problems = await checkSite(root);
  if (problems.length) {
    console.error(`${problems.length} problem(s):\n` + problems.map((p) => `  - ${p}`).join('\n'));
    process.exit(1);
  }
  console.log('site check: clean');
}
