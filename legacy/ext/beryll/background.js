// Global Privacy Control: tell every site "do not sell / share my data" (Sec-GPC header, https://globalprivacycontrol.org).
chrome.webRequest.onBeforeSendHeaders.addListener(function (d) {
  d.requestHeaders.push({ name: 'Sec-GPC', value: '1' });
  return { requestHeaders: d.requestHeaders };
}, { urls: ['<all_urls>'] }, ['blocking', 'requestHeaders']);

// defaults: H.264 forcing on (best for old CPUs / GPUs without VP9/AV1 decoding)
chrome.runtime.onInstalled.addListener(function () {
  chrome.storage.local.get({ h264: null }, function (s) { if (s.h264 === null) chrome.storage.local.set({ h264: true }); });
});
