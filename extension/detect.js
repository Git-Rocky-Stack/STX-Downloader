// Pure detection logic. No browser APIs here, so all of it runs under node --test.

const SEGMENT_TYPES = /^(video\/mp2t|video\/iso\.segment|audio\/iso\.segment)$/;
const SEGMENT_EXTENSIONS = /\.(ts|m4s|m4f|cmfv|cmfa)$/;
const SEGMENT_NAMES = /(^|[/_.-])(seg|segment|chunk|frag|fragment|init)[-_]?\d*\.[a-z0-9]+$/;
const FILE_EXTENSIONS = /\.(mp4|m4v|webm|mov|mkv|mp3|m4a|aac|ogg|oga|opus|flac|wav)$/;
const HLS_TYPES = /^(application\/x-mpegurl|application\/vnd\.apple\.mpegurl|audio\/mpegurl|audio\/x-mpegurl)$/;
const MASTER_NAMES = /(^|[/_-])(master|main|manifest|playlist)[^/]*\.m3u8$/;
const VOLATILE_QUERY = /token|sig|policy|expire|^exp$|^e$|^t$|^st$|key-pair-id|hdnts|hdntl|auth|session/i;
const GENERIC_NAMES = /^(master|main|index|playlist|manifest|rendition|chunklist|media|stream|video|getcontentdescriptor)$/i;
const MAX_ITEMS = 200;

function parse(url) {
  try {
    const u = new URL(url);
    return u.protocol === 'http:' || u.protocol === 'https:' ? u : null;
  } catch {
    return null;
  }
}

/** Which kind of media a response is, or null when it isn't something to list. */
export function classify(url, contentType) {
  const u = parse(url);
  if (!u) return null;
  const path = u.pathname.toLowerCase();
  const type = (contentType || '').split(';')[0].trim().toLowerCase();

  // YouTube's own requests are stream pieces and interface sounds. Its videos are handed over by ID instead.
  if (u.hostname.endsWith('.googlevideo.com') || /(^|\.)youtube\.com$/.test(u.hostname)) return null;
  if (SEGMENT_TYPES.test(type) || SEGMENT_EXTENSIONS.test(path) || SEGMENT_NAMES.test(path)) return null;
  if (path.endsWith('.m3u8') || HLS_TYPES.test(type)) return 'hls';
  if (path.endsWith('.mpd') || type === 'application/dash+xml') return 'dash';
  if (FILE_EXTENSIONS.test(path) || type.startsWith('video/') || type.startsWith('audio/')) return 'file';
  return null;
}

/** Brightcove's player reports each video's name, length and stream in its tracker requests. */
export function readTracker(url) {
  const u = parse(url);
  if (!u) return null;
  const streamUrl = u.searchParams.get('media_url');
  const title = u.searchParams.get('video_name');
  if (!streamUrl || !title) return null;
  const seconds = Number(u.searchParams.get('video_duration'));
  return { streamUrl, title, durationSec: seconds > 0 ? seconds : undefined };
}

/** The largest resolution a master playlist offers, and the renditions it names. */
export function parseMaster(text, baseUrl) {
  const result = { width: 0, height: 0, variants: [] };
  if (!text.startsWith('#EXTM3U')) return result;
  const lines = text.split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    if (!lines[i].startsWith('#EXT-X-STREAM-INF')) continue;
    const size = /RESOLUTION=(\d+)x(\d+)/.exec(lines[i]);
    if (size && Number(size[1]) * Number(size[2]) > result.width * result.height) {
      result.width = Number(size[1]);
      result.height = Number(size[2]);
    }
    const next = lines[i + 1]?.trim();
    if (!next || next.startsWith('#')) continue;
    try {
      result.variants.push(new URL(next, baseUrl).href);
    } catch {
      // A line that can't be a URL names no rendition.
    }
  }
  return result;
}

/** What identifies a stream: its link without the tokens that change between requests. Some players identify videos in the query. */
function streamKey(url) {
  const u = parse(url);
  if (!u) return url;
  const kept = [...u.searchParams].filter(([name]) => !VOLATILE_QUERY.test(name)).map(([name, value]) => `${name}=${value}`).sort();
  return u.origin + u.pathname + (kept.length ? `?${kept.join('&')}` : '');
}

function nameIn(url) {
  const last = parse(url)?.pathname.split('/').pop() || '';
  let stem = last.replace(/\.[^.]*$/, '');
  try { stem = decodeURIComponent(stem); } catch { /* keep it as written */ }
  stem = stem.replace(/[-_]\d+$/, '');
  return stem.length >= 3 && !GENERIC_NAMES.test(stem) ? stem : null;
}

export function emptyState() {
  return { title: '', drm: false, items: [], meta: {}, embeds: [], videos: [] };
}

/** Records one response seen on the tab. Returns the new state. */
export function addResponse(state, url, contentType) {
  const tracker = readTracker(url);
  if (tracker) {
    return { ...state, meta: { ...state.meta, [streamKey(tracker.streamUrl)]: { title: tracker.title, durationSec: tracker.durationSec } } };
  }
  const kind = classify(url, contentType);
  if (!kind) return state;
  const key = streamKey(url);
  const existing = state.items.find(i => streamKey(i.url) === key);
  if (existing) {
    return { ...state, items: state.items.map(i => (i === existing ? { ...i, url } : i)) };
  }
  if (state.items.length >= MAX_ITEMS) return state;
  return { ...state, items: [...state.items, { kind, url, contentType: contentType || '' }] };
}

/** Records what the page's own script reported. DRM seen in any frame stays seen. */
export function applyPage(state, page) {
  const known = new Set(state.embeds.map(e => e.id));
  const embeds = [...state.embeds, ...findEmbeds(page.frames || []).filter(e => !known.has(e.id))];
  return {
    ...state,
    title: page.title || state.title,
    drm: state.drm || Boolean(page.mediaKeys),
    embeds,
    videos: mergeVideos(state.videos, page.videos || []),
  };
}

function mergeVideos(old, incoming) {
  const merged = [...old];
  for (const v of incoming) {
    const at = merged.findIndex(m => m.src === v.src);
    if (at >= 0) merged[at] = v;
    else merged.push(v);
  }
  return merged;
}

/** Drops renditions that belong to a master also on the list. */
export function groupStreams(items) {
  const masters = items.filter(i => i.kind === 'hls' && MASTER_NAMES.test(streamKey(i.url).toLowerCase()));
  const named = new Set(items.flatMap(i => i.variants || []).map(streamKey));
  const folders = masters.map(m => parse(m.url).origin + parse(m.url).pathname.replace(/(\/\d+s)?\/[^/]*$/, '/'));
  return items.filter(i => {
    if (i.kind !== 'hls') return true;
    const key = streamKey(i.url);
    if (named.has(key)) return false;
    if (masters.includes(i)) return true;
    return !folders.some(folder => key.startsWith(folder));
  });
}

/** The YouTube videos found among frame sources or the page's own address. */
export function findEmbeds(sources) {
  const seen = new Set();
  const out = [];
  for (const source of sources) {
    const id = youTubeId(source);
    if (id && !seen.has(id)) {
      seen.add(id);
      out.push({ kind: 'youtube', id });
    }
  }
  return out;
}

function youTubeId(source) {
  const u = parse(source);
  if (!u) return null;
  const host = u.hostname.replace(/^(www|m)\./, '');
  let id = null;
  if (host === 'youtu.be') id = u.pathname.slice(1);
  else if (host === 'youtube.com' || host === 'youtube-nocookie.com') {
    if (u.pathname === '/watch') id = u.searchParams.get('v');
    else id = /^\/(embed|shorts|live)\/([^/]+)/.exec(u.pathname)?.[2];
  }
  return id && /^[\w-]{11}$/.test(id) ? id : null;
}

/** Everything to show for a tab: grouped streams with what's known about each, then embedded players. */
export function listFor(state) {
  const streams = groupStreams(state.items).map(item => ({ ...item, ...state.meta[streamKey(item.url)] }));
  const onlyOne = streams.length === 1 && state.videos.length === 1;
  const filled = streams.map(item => fill(item, state, onlyOne));
  return [...filled, ...state.embeds];
}

function fill(item, state, onlyOne) {
  const out = { ...item };
  if (state.drm) out.isDrm = true;
  const exact = state.videos.find(v => v.src === item.url);
  if (exact) {
    if (exact.width > 0) { out.width = exact.width; out.height = exact.height; }
    setDuration(out, exact.durationSec);
  } else if (onlyOne) {
    // A blob-backed player plays whichever rendition it picked, so its size says nothing about the stream.
    setDuration(out, state.videos[0].durationSec);
  }
  if (onlyOne && !out.title && state.title) out.title = state.title;
  return out;
}

function setDuration(item, seconds) {
  if (item.durationSec === undefined && Number.isFinite(seconds) && seconds > 0) item.durationSec = Math.round(seconds);
}

export function canDownload(item) {
  return !item.isDrm;
}

function clock(total) {
  const s = Math.round(total);
  const two = n => String(n).padStart(2, '0');
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return h > 0 ? `${h}:${two(m)}:${two(s % 60)}` : `${m}:${two(s % 60)}`;
}

/** One line telling this item apart from the others on the page. */
export function labelFor(item) {
  if (item.kind === 'youtube') return `YouTube video ${item.id}`;
  const parts = [item.title || nameIn(item.url) || (item.variants?.[0] && nameIn(item.variants[0])) || parse(item.url)?.hostname || 'Unknown source'];
  if (item.durationSec > 0) parts.push(clock(item.durationSec));
  if (item.width > 0) parts.push(`${item.width}×${item.height}`);
  parts.push({ hls: 'HLS', dash: 'DASH', file: 'File' }[item.kind]);
  if (item.isDrm) parts.push('DRM, no download');
  return parts.join(' · ');
}
