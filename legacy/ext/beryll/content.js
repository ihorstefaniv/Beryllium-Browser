// Runs at document_start in the isolated world. The page code below must run in the page's own JS world
// (that is where YouTube and other players ask which codecs the browser supports), and it must run BEFORE the
// page's scripts — so it is injected inline and synchronously, not as <script src> (that loads asynchronously).
(function () {
  var code = '(' + function () {
    if (window.__beryll) return;
    window.__beryll = true;
    var root = document.documentElement;
    function on() { return !root || root.getAttribute('data-beryll-h264') !== '0'; }   // read at call time
    // Beryll: report VP8/VP9/AV1 as unsupported so video sites fall back to H.264. H.264 is decoded by almost any
    // GPU since ~2008 and is far cheaper in software than VP9/AV1: on an Atom / Core 2 the difference between
    // smooth and stuttering video.
    var bad = /vp0?8|vp0?9|av01|av1/i;
    function wrap(obj, name, no) {
      if (!obj || !obj[name]) return;
      var orig = obj[name];
      obj[name] = function (type) { return on() && bad.test(String(type)) ? no : orig.apply(this, arguments); };
    }
    if (window.MediaSource) wrap(MediaSource, 'isTypeSupported', false);
    if (window.ManagedMediaSource) wrap(ManagedMediaSource, 'isTypeSupported', false);
    if (window.HTMLMediaElement) wrap(HTMLMediaElement.prototype, 'canPlayType', '');
    if (navigator.mediaCapabilities && navigator.mediaCapabilities.decodingInfo) {
      var mc = navigator.mediaCapabilities, dec = mc.decodingInfo.bind(mc);
      mc.decodingInfo = function (cfg) {
        var t = cfg && (cfg.video && cfg.video.contentType || cfg.audio && cfg.audio.contentType) || '';
        return on() && bad.test(t) ? Promise.resolve({ supported: false, smooth: false, powerEfficient: false }) : dec(cfg);
      };
    }
    // Global Privacy Control, JS side (the Sec-GPC header is added by background.js)
    try { Object.defineProperty(Navigator.prototype, 'globalPrivacyControl', { get: function () { return true; }, configurable: true }); } catch (e) { }
  } + ')();';
  var s = document.createElement('script');
  s.textContent = code;
  (document.head || document.documentElement).appendChild(s);
  s.remove();
  // the user's switch (new tab page): applied before video players initialise
  chrome.storage.local.get({ h264: true }, function (o) {
    if (!o.h264 && document.documentElement) document.documentElement.setAttribute('data-beryll-h264', '0');
  });
})();
