// Reports what only the page can see: frames, video elements and DRM use. Runs in every frame.
(() => {
  let mediaKeys = false;
  let timer = 0;

  function report() {
    timer = 0;
    const videos = [...document.querySelectorAll('video')];
    if (videos.some(v => v.mediaKeys)) mediaKeys = true;
    chrome.runtime.sendMessage({
      type: 'page',
      title: window === window.top ? document.title : '',
      mediaKeys,
      frames: [location.href, ...[...document.querySelectorAll('iframe')].map(f => f.src)],
      videos: videos.map(v => ({
        src: v.currentSrc,
        // Infinity (a live stream) doesn't survive messaging, so it travels as 0.
        durationSec: Number.isFinite(v.duration) ? v.duration : 0,
        width: v.videoWidth,
        height: v.videoHeight,
      })),
    }).catch(() => {});
  }

  function soon() {
    if (!timer) timer = setTimeout(report, 500);
  }

  // These events don't bubble, but they do pass through the capture phase on their way to the video.
  document.addEventListener('encrypted', () => { mediaKeys = true; soon(); }, true);
  document.addEventListener('loadedmetadata', soon, true);
  window.addEventListener('load', soon);
  document.addEventListener('DOMContentLoaded', soon);
  chrome.runtime.onMessage.addListener(message => { if (message?.type === 'report') report(); });
})();
