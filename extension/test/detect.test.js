import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import {
  addResponse, applyPage, canDownload, classify, emptyState, findEmbeds,
  batchMessage, captureMessage, cookieUrls, groupStreams, labelFor, saveAllUsesCookies, listFor, mayCapture, netscapeCookies, parseMaster, readTracker,
} from '../detect.js';

const fixtureChevrolet = JSON.parse(readFileSync(new URL('./fixtures/chevrolet.json', import.meta.url)));
const master = 'https://manifest.prod.boltdns.net/manifest/v1/hls/v4/clear/1/2/10s/master.m3u8?fastly_token=x';

function chevroletState() {
  return fixtureChevrolet.reduce((s, r) => addResponse(s, r.url, r.contentType), emptyState());
}

test('Brightcove master is HLS', () => assert.equal(classify(master, 'application/x-mpegURL'), 'hls'));
test('HLS by content type alone', () => assert.equal(classify('https://a.example/stream?id=1', 'application/vnd.apple.mpegurl; charset=utf-8'), 'hls'));
test('DASH by extension and by content type', () => {
  assert.equal(classify('https://a.example/v/manifest.mpd', ''), 'dash');
  assert.equal(classify('https://a.example/v/stream', 'application/dash+xml'), 'dash');
});
test('direct files', () => {
  assert.equal(classify('https://a.example/film.mp4', 'video/mp4'), 'file');
  assert.equal(classify('https://a.example/talk.mp3?x=1', ''), 'file');
  assert.equal(classify('https://a.example/get?id=1', 'audio/flac'), 'file');
});
test('segments and tracker URLs are not listed', () => {
  assert.equal(classify('https://metrics.brightcove.com/v2/tracker?media_url=x.m3u8', ''), null);
  assert.equal(classify('https://v.example/5x/segment0.ts', 'video/MP2T'), null);
  assert.equal(classify('https://v.example/a/chunk-12.m4s', 'video/iso.segment'), null);
  assert.equal(classify('https://v.example/a/seg-3.mp4', 'video/mp4'), null);
  assert.equal(classify('https://v.example/a/init.mp4', 'video/mp4'), null);
});
test('YouTube stream pieces are not listed as files', () =>
  assert.equal(classify('https://rr1---sn-abc.googlevideo.com/videoplayback?itag=18', 'video/mp4'), null));
test('blob and data URLs are not listed', () => {
  assert.equal(classify('blob:https://a.example/1', 'video/mp4'), null);
  assert.equal(classify('data:video/mp4;base64,AAAA', 'video/mp4'), null);
});
test('a bad URL is not listed and does not throw', () => assert.equal(classify('not a url', ''), null));
test('ordinary page requests are not listed', () => {
  assert.equal(classify('https://a.example/app.js', 'text/javascript'), null);
  assert.equal(classify('https://a.example/pic.gif', 'image/gif'), null);
});

test('renditions collapse under master', () =>
  assert.equal(groupStreams(chevroletState().items).filter(i => i.kind === 'hls').length, 2));
test('a rendition with no master seen stays listed', () => {
  const s = addResponse(emptyState(), 'https://v.example/a/720/rendition.m3u8', 'application/x-mpegURL');
  assert.equal(groupStreams(s.items).length, 1);
});
test('variants a master names collapse even when the path gives no hint', () => {
  const items = [
    { kind: 'hls', url: 'https://v.example/x/top.m3u8', variants: ['https://cdn.example/q/720.m3u8?t=1'] },
    { kind: 'hls', url: 'https://cdn.example/q/720.m3u8?t=2' },
  ];
  assert.deepEqual(groupStreams(items).map(i => i.url), ['https://v.example/x/top.m3u8']);
});
test('the same stream seen twice is one item holding the newest link', () => {
  let s = addResponse(emptyState(), 'https://v.example/a/master.m3u8?t=old', 'application/x-mpegURL');
  s = addResponse(s, 'https://v.example/a/master.m3u8?t=new', 'application/x-mpegURL');
  assert.deepEqual(s.items.map(i => i.url), ['https://v.example/a/master.m3u8?t=new']);
});

test('tracker gives name, duration and stream', () => {
  const t = readTracker('https://metrics.brightcove.com/v2/tracker?event=video_impression&video_name=Walk%20Around&video_duration=413&media_url=' + encodeURIComponent(master));
  assert.deepEqual(t, { streamUrl: master, title: 'Walk Around', durationSec: 413 });
});
test('tracker event without a video gives nothing', () =>
  assert.equal(readTracker('https://metrics.brightcove.com/v2/tracker?event=player_init'), null));
test('tracker labels reach the listed masters', () => {
  const titles = listFor(chevroletState()).map(i => i.title).sort();
  assert.deepEqual(titles, ['Corvette Owner Academy Driver Assistance', 'Corvette Owner Academy Walkaround']);
});
test('tracker seen before the master still labels it', () => {
  const t = 'https://metrics.brightcove.com/v2/tracker?video_name=N&video_duration=5&media_url=' + encodeURIComponent(master);
  let s = addResponse(emptyState(), t, 'image/gif');
  s = addResponse(s, master, 'application/x-mpegURL');
  assert.equal(listFor(s)[0].title, 'N');
});

test('master gives top resolution and absolute variants', () => {
  const m = parseMaster(`#EXTM3U
#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360
low/index.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080
/abs/high.m3u8?t=1
#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720
https://cdn.example/mid.m3u8
`, 'https://v.example/a/master.m3u8?t=9');
  assert.deepEqual([m.width, m.height], [1920, 1080]);
  assert.deepEqual(m.variants, ['https://v.example/a/low/index.m3u8', 'https://v.example/abs/high.m3u8?t=1', 'https://cdn.example/mid.m3u8']);
});
test('text that is not a master gives nothing', () =>
  assert.deepEqual(parseMaster('<html>denied</html>', 'https://v.example/a.m3u8'), { width: 0, height: 0, variants: [] }));

test('YouTube embed yields id', () =>
  assert.deepEqual(findEmbeds(['https://www.youtube.com/embed/QBwwj5p25AM?autoplay=0']), [{ kind: 'youtube', id: 'QBwwj5p25AM' }]));
test('nocookie embed, watch page and short link yield ids, once each', () =>
  assert.deepEqual(findEmbeds([
    'https://www.youtube-nocookie.com/embed/QBwwj5p25AM',
    'https://www.youtube.com/watch?v=QBwwj5p25AM',
    'https://youtu.be/abcdefghijk',
  ]), [{ kind: 'youtube', id: 'QBwwj5p25AM' }, { kind: 'youtube', id: 'abcdefghijk' }]));
test('other frames and look-alike hosts yield nothing', () =>
  assert.deepEqual(findEmbeds(['https://gm.demdex.net/dest5.html', 'https://youtube.com.evil.example/embed/QBwwj5p25AM', 'about:blank', '']), []));

test('label uses title, duration, resolution and kind', () =>
  assert.equal(labelFor({ kind: 'hls', url: master, title: 'Walkaround', durationSec: 413, width: 1920, height: 1080 }), 'Walkaround · 6:53 · 1920×1080 · HLS'));
test('label shows hours for long webinars', () =>
  assert.equal(labelFor({ kind: 'file', url: 'https://a.example/w.mp4', title: 'Webinar', durationSec: 3727 }), 'Webinar · 1:02:07 · File'));
test('label without a title names the host, never the token', () => {
  const label = labelFor({ kind: 'hls', url: master });
  assert.equal(label, 'manifest.prod.boltdns.net · HLS');
});
test('label for a YouTube item', () => assert.equal(labelFor({ kind: 'youtube', id: 'QBwwj5p25AM' }), 'YouTube video QBwwj5p25AM'));

test('DRM item offers no download', () => {
  const s = applyPage(addResponse(emptyState(), master, 'application/x-mpegURL'), { title: 'T', mediaKeys: true, frames: [], videos: [] });
  const [item] = listFor(s);
  assert.equal(canDownload(item), false);
  assert.match(labelFor(item), /DRM/);
});
test('clear item offers a download', () => {
  const [item] = listFor(addResponse(emptyState(), master, 'application/x-mpegURL'));
  assert.equal(canDownload(item), true);
});
test('DRM reported by one frame is not cleared by a later frame', () => {
  let s = applyPage(emptyState(), { mediaKeys: true, frames: [], videos: [] });
  s = applyPage(s, { mediaKeys: false, frames: [], videos: [] });
  assert.equal(s.drm, true);
});
test('page embeds are listed and page DRM does not mark another site\'s player', () => {
  const s = applyPage(emptyState(), { mediaKeys: true, frames: ['https://www.youtube.com/embed/QBwwj5p25AM'], videos: [] });
  const [item] = listFor(s);
  assert.deepEqual([item.kind, item.id, canDownload(item)], ['youtube', 'QBwwj5p25AM', true]);
});
test('a video element fills in a direct file by its exact source', () => {
  let s = addResponse(emptyState(), 'https://a.example/film.mp4', 'video/mp4');
  s = applyPage(s, { title: 'Page', frames: [], videos: [{ src: 'https://a.example/film.mp4', durationSec: 90.4, width: 1280, height: 720 }] });
  const [item] = listFor(s);
  assert.deepEqual([item.durationSec, item.width, item.height], [90, 1280, 720]);
});
test('a video element never gives an HLS stream its resolution, because it plays whichever rendition it chose', () => {
  let s = addResponse(emptyState(), master, 'application/x-mpegURL');
  s = applyPage(s, { title: 'Page', frames: [], videos: [{ src: 'blob:https://a.example/1', durationSec: 90, width: 480, height: 270 }] });
  assert.equal(listFor(s)[0].width, undefined);
});
test('the only stream on a page takes the only video duration and the page title', () => {
  let s = addResponse(emptyState(), master, 'application/x-mpegURL');
  s = applyPage(s, { title: 'Page title', frames: [], videos: [{ src: 'blob:https://a.example/1', durationSec: 90, width: 480, height: 270 }] });
  const [item] = listFor(s);
  assert.deepEqual([item.title, item.durationSec], ['Page title', 90]);
});
test('with many streams the page title is not guessed onto any of them', () => {
  let s = addResponse(emptyState(), 'https://v.example/a/master.m3u8', 'application/x-mpegURL');
  s = addResponse(s, 'https://v.example/b/master.m3u8', 'application/x-mpegURL');
  s = applyPage(s, { title: 'Page title', frames: [], videos: [] });
  assert.deepEqual(listFor(s).map(i => i.title), [undefined, undefined]);
});
test('a live video with infinite duration shows none', () => {
  let s = addResponse(emptyState(), master, 'application/x-mpegURL');
  s = applyPage(s, { frames: [], videos: [{ src: 'blob:x', durationSec: Infinity, width: 1, height: 1 }] });
  assert.equal(listFor(s)[0].durationSec, undefined);
});

test('manifest asks for what the plan names and pins the extension id', () => {
  const m = JSON.parse(readFileSync(new URL('../manifest.json', import.meta.url)));
  assert.equal(m.manifest_version, 3);
  for (const p of ['webRequest', 'tabs', 'cookies', 'nativeMessaging', 'storage']) assert.ok(m.permissions.includes(p), p);
  assert.deepEqual(m.host_permissions, ['<all_urls>']);
  const hash = createHash('sha256').update(Buffer.from(m.key, 'base64')).digest().subarray(0, 16);
  const id = [...hash].flatMap(b => [b >> 4, b & 15]).map(n => String.fromCharCode(97 + n)).join('');
  assert.equal(id, readFileSync(new URL('../extension-id.txt', import.meta.url), 'utf8').trim());
});

test('YouTube interface sounds are not listed', () =>
  assert.equal(classify('https://www.youtube.com/s/search/audio/open.mp3', 'audio/mpeg'), null));
test('streams told apart only by their query stay separate, as on Ferrari\'s Thron player', () => {
  const at = id => `https://ferrari-view.thron.com/api/xpublisher/resources/weebopublisher/getContentDescriptor.m3u8?clientId=ferrari&contentId=${id}`;
  let s = addResponse(emptyState(), at('a'), 'application/vnd.apple.mpegurl');
  s = addResponse(s, at('b'), 'application/vnd.apple.mpegurl');
  assert.equal(listFor(s).length, 2);
});
test('a changing token does not split one stream, whatever it is called', () => {
  for (const name of ['fastly_token', 'token', 'hdnts', 'Signature', 'Policy', 'Expires', 'Key-Pair-Id', 't']) {
    let s = addResponse(emptyState(), `https://v.example/a/master.m3u8?id=1&${name}=old`, 'application/x-mpegURL');
    s = addResponse(s, `https://v.example/a/master.m3u8?id=1&${name}=new`, 'application/x-mpegURL');
    assert.equal(s.items.length, 1, name);
  }
});
test('Thron masters with their own renditions collapse to one row each', () => {
  const master = 'https://ferrari-view.thron.com/api/getContentDescriptor.m3u8?contentId=a';
  const rendition = 'https://ferrari-cdn.thron.com/ios/PUPLGQ_idle_2.mp4/PUPLGQ_idle_2-4300.m3u8?v=8';
  const items = [{ kind: 'hls', url: master, variants: [rendition] }, { kind: 'hls', url: rendition }];
  assert.deepEqual(groupStreams(items).map(i => i.url), [master]);
});
test('label without a title names the file', () =>
  assert.equal(labelFor({ kind: 'file', url: 'https://ferrari-cdn.thron.com/static/2VKLXM_GT-Ferrari_Purosangue.mp4?xseo=' }), '2VKLXM_GT-Ferrari_Purosangue · File'));
test('a generic master takes its name from a rendition, without the bitrate', () =>
  assert.equal(labelFor({ kind: 'hls', url: 'https://ferrari-view.thron.com/api/getContentDescriptor.m3u8?contentId=a', variants: ['https://ferrari-cdn.thron.com/ios/x.mp4/PUPLGQ_idle_2-4300.m3u8?v=8'] }), 'PUPLGQ_idle_2 · HLS'));

const page = { url: 'https://www.chevrolet.com/performance/corvette/experience', title: 'Corvette', userAgent: 'UA' };
const cookie = { domain: '.chevrolet.com', hostOnly: false, path: '/', secure: true, httpOnly: false, session: false, expirationDate: 1800000000.5, name: 'a', value: 'b' };

test('cookies become a Netscape file', () => {
  const session = { domain: 'www.chevrolet.com', hostOnly: true, path: '/x', secure: false, httpOnly: true, session: true, name: 'c', value: 'd' };
  assert.equal(netscapeCookies([cookie, session]),
    '.chevrolet.com\tTRUE\t/\tTRUE\t1800000000\ta\tb\n#HttpOnly_www.chevrolet.com\tFALSE\t/x\tFALSE\t0\tc\td\n');
});
test('no cookies gives nothing to send', () => assert.equal(netscapeCookies([]), null));
test('a cookie with a line break is dropped, so it cannot add a line', () =>
  assert.equal(netscapeCookies([{ ...cookie, value: 'x\ny' }]), null));

test('a stream message carries what the app needs', () => {
  const m = captureMessage({ kind: 'hls', url: master, title: 'Walkaround', durationSec: 413, width: 1920, height: 1080 }, page, 'COOKIES');
  assert.deepEqual(m, {
    Kind: 'hls', PageUrl: page.url, Title: 'Walkaround', StreamUrl: master, VideoId: undefined,
    DurationSec: 413, Width: 1920, Height: 1080, IsDrm: false, Cookies: 'COOKIES',
    Headers: { Referer: page.url, Origin: 'https://www.chevrolet.com', 'User-Agent': 'UA' },
  });
});
test('a stream with no title is sent under its label name, so it is not saved as master', () =>
  assert.equal(captureMessage({ kind: 'hls', url: 'https://v.example/a/talk.m3u8' }, page, null).Title, 'talk'));
test('a YouTube message carries the id and no stream link', () => {
  const m = captureMessage({ kind: 'youtube', id: 'QBwwj5p25AM' }, page, null);
  assert.deepEqual([m.Kind, m.VideoId, m.StreamUrl, m.Cookies], ['youtube', 'QBwwj5p25AM', undefined, null]);
});
test('a batch shares one cookie jar across its items and leaves DRM items out', () => {
  const m = batchMessage([
    { kind: 'hls', url: master, title: 'One' },
    { kind: 'file', url: 'https://a.example/b.mp4', isDrm: true },
    { kind: 'youtube', id: 'QBwwj5p25AM' },
  ], page, 'COOKIES');
  assert.equal(m.Kind, 'batch');
  assert.equal(m.Cookies, 'COOKIES');
  assert.deepEqual(m.Items.map(i => [i.Kind, i.Title]), [['hls', 'One'], ['youtube', 'YouTube video QBwwj5p25AM']]);
});
test('cookies are looked up only for the sites this item touches', () =>
  assert.deepEqual(cookieUrls([{ kind: 'hls', url: master }, { kind: 'youtube', id: 'QBwwj5p25AM' }], page),
    ['https://www.chevrolet.com/performance/corvette/experience', master]));
test('only the extension\'s own pages may ask for a capture', () => {
  const origin = 'chrome-extension://ahbdmiddabpbfmoijlejkinopgppajfg/';
  assert.equal(mayCapture({ id: 'x', url: origin + 'popup.html' }, 'x', origin), true);
  assert.equal(mayCapture({ id: 'x', url: 'https://evil.example/', tab: { id: 1 } }, 'x', origin), false);
  assert.equal(mayCapture({ id: 'other', url: origin + 'popup.html' }, 'x', origin), false);
});

test('a generic master is sent under the same name its label shows, as on Ferrari\'s Thron player', () => {
  const item = { kind: 'hls', url: 'https://ferrari-view.thron.com/api/getContentDescriptor.m3u8?contentId=a', variants: ['https://ferrari-cdn.thron.com/ios/x.mp4/R9CF0A_aero-4300.m3u8?v=7'] };
  assert.equal(captureMessage(item, page, null).Title, 'R9CF0A_aero');
  assert.equal(batchMessage([item, { kind: 'file', url: 'https://a.example/b.mp4' }], page, null).Items[0].Title, 'R9CF0A_aero');
});
test('with no name anywhere, a stream is sent untitled and labeled by host', () => {
  const item = { kind: 'hls', url: 'https://v.example/a/master.m3u8' };
  assert.equal(captureMessage(item, page, null).Title, undefined);
  assert.equal(labelFor(item), 'v.example · HLS');
});

test('Save all sends cookies only when something other than YouTube is in it', () => {
  assert.equal(saveAllUsesCookies([{ kind: 'youtube', id: 'a' }, { kind: 'youtube', id: 'b' }]), false);
  assert.equal(saveAllUsesCookies([{ kind: 'youtube', id: 'a' }, { kind: 'hls', url: master }]), true);
});
