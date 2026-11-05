(function () {
  var q = document.getElementById('q');
  document.getElementById('f').addEventListener('submit', function (e) {
    var v = q.value.trim();
    if (!v) { e.preventDefault(); return; }
    // looks like an address -> open it directly
    if (!/\s/.test(v) && (/^[a-z]+:\/\//i.test(v) || /^[^\/]+\.[a-z]{2,}(\/|:|$)/i.test(v) || /^localhost(:|\/|$)/i.test(v))) {
      e.preventDefault();
      location.href = /^[a-z]+:\/\//i.test(v) ? v : 'https://' + v;
    }
  });

  var box = document.getElementById('sites');
  chrome.topSites.get(function (list) {
    list.filter(function (s) { return /^https?:/.test(s.url); }).slice(0, 8).forEach(function (s) {
      var a = document.createElement('a'); a.href = s.url; a.title = s.url;
      var ic = document.createElement('span'); ic.className = 'ic';
      var img = document.createElement('img'); img.src = 'chrome://favicon/size/16@2x/' + s.url; img.alt = '';
      ic.appendChild(img);
      var t = document.createElement('span'); t.className = 't'; t.textContent = s.title || new URL(s.url).hostname;
      a.appendChild(ic); a.appendChild(t); box.appendChild(a);
    });
  });

  var cb = document.getElementById('h264');
  chrome.storage.local.get({ h264: true }, function (o) { cb.checked = o.h264; });
  cb.addEventListener('change', function () { chrome.storage.local.set({ h264: cb.checked }); });
})();
