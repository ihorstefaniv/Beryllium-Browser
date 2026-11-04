using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Beryll
{
    /// <summary>DuckDuckGo-style protections:
    ///  * third-party tracker blocking (own curated list + the engine's strict tracking prevention);
    ///    first-party / same-company requests are allowed (like DDG, to avoid breaking sites);
    ///  * HTTPS-first upgrade with safe fallback;
    ///  * removal of tracking parameters from links (utm_*, fbclid, gclid…);
    ///  * Global Privacy Control (Sec-GPC header + navigator.globalPrivacyControl);
    ///  * automatic "reject" on cookie pop-ups;
    ///  * Fire button with Fireproof sites (in BrowserForm).</summary>
    static class Privacy
    {
        // Curated list of advertising / analytics / session-replay domains (facts, maintained by the project).
        // Users can extend it with blocklist.txt in the data folder (one domain per line).
        const string BuiltIn = @"
google-analytics.com googletagmanager.com googletagservices.com doubleclick.net googlesyndication.com googleadservices.com
adservice.google.com app-measurement.com imasdk.googleapis.com
connect.facebook.net
bat.bing.com clarity.ms
ads-twitter.com analytics.twitter.com static.ads-twitter.com
px.ads.linkedin.com snap.licdn.com
analytics.tiktok.com ads.tiktok.com analytics-sg.tiktok.com
tr.snapchat.com sc-static.net ct.pinterest.com alb.reddit.com events.redditmedia.com
mc.yandex.ru mc.yandex.com mc.yandex.ua an.yandex.ru yandexadexchange.net adfox.ru adfox.yandex.ru mc.webvisor.org mc.webvisor.com
top-fwz1.mail.ru top.mail.ru ad.mail.ru rs.mail.ru ads.vk.com
c.bigmir.net counter.bigmir.net counter.yadro.ru hit.gemius.pl gemius.com admixer.net adriver.ru
mgid.com marketgid.com adnow.com propellerads.com popads.net onclickads.net exoclick.com adsterra.com redtram.com directadvert.ru
adnxs.com rubiconproject.com pubmatic.com openx.net casalemedia.com criteo.com criteo.net taboola.com outbrain.com revcontent.com zemanta.com
smartadserver.com adform.net teads.tv yieldmo.com sharethrough.com triplelift.com 3lift.com bidswitch.net adsrvr.org
demdex.net everesttech.net omtrdc.net 2o7.net scorecardresearch.com quantserve.com quantcount.com moatads.com doubleverify.com
adsafeprotected.com serving-sys.com flashtalking.com mathtag.com rlcdn.com krxd.net bluekai.com exelator.com agkn.com
crwdcntrl.net tapad.com liadm.com id5-sync.com 33across.com sonobi.com gumgum.com media.net contextweb.com lijit.com sovrn.com
yieldlab.net adition.com amazon-adsystem.com advertising.com analytics.yahoo.com ads.yahoo.com adtech.de
hotjar.com hotjar.io mouseflow.com fullstory.com crazyegg.com luckyorange.com inspectlet.com smartlook.com
mixpanel.com amplitude.com heapanalytics.com segment.io cdn.segment.com chartbeat.com chartbeat.net parsely.com nr-data.net
statcounter.com histats.com getclicky.com addthis.com sharethis.com disqusads.com appsflyer.com kissmetrics.com
outbrainimg.com taboolasyndication.com trc.taboola.com adskeeper.com adskeeper.co.uk
";

        static HashSet<string> trackers;
        public static HashSet<string> Trackers
        {
            get
            {
                if (trackers == null)
                {
                    var h = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string d in BuiltIn.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)) h.Add(d);
                    try
                    {
                        string user = Paths.File("blocklist.txt");
                        if (File.Exists(user))
                            foreach (string line in File.ReadAllLines(user))
                            {
                                string d = line.Trim().ToLowerInvariant();
                                if (d.Length > 3 && !d.StartsWith("#") && d.IndexOf(' ') < 0 && d.IndexOf('/') < 0) h.Add(d);
                            }
                    }
                    catch { }
                    trackers = h;
                }
                return trackers;
            }
        }

        /// <summary>Returns the matching list entry if the host is (a subdomain of) a tracker domain.</summary>
        public static string MatchTracker(string host)
        {
            if (string.IsNullOrEmpty(host)) return null;
            string h = host.ToLowerInvariant();
            while (true)
            {
                if (Trackers.Contains(h)) return h;
                int dot = h.IndexOf('.');
                if (dot < 0 || h.IndexOf('.', dot + 1) < 0) return null;
                h = h.Substring(dot + 1);
            }
        }

        // ---------------------------------------------------------------- "same company" (first party)

        static readonly string[][] Entities = {
            new[] { "google.", "gstatic.com", "googleapis.com", "youtube.com", "ytimg.com", "googlevideo.com", "googletagmanager.com", "google-analytics.com",
                    "doubleclick.net", "googlesyndication.com", "googleadservices.com", "googleusercontent.com", "youtube-nocookie.com", "app-measurement.com" },
            new[] { "facebook.com", "facebook.net", "fbcdn.net", "instagram.com", "cdninstagram.com", "whatsapp.com", "whatsapp.net", "messenger.com" },
            new[] { "microsoft.com", "bing.com", "live.com", "clarity.ms", "msn.com", "office.com", "microsoftonline.com", "linkedin.com", "licdn.com", "skype.com" },
            new[] { "yandex.", "yastatic.net", "ya.ru", "adfox.ru", "webvisor.org", "webvisor.com", "yandexadexchange.net" },
            new[] { "amazon.", "amazon-adsystem.com", "media-amazon.com", "ssl-images-amazon.com" },
            new[] { "twitter.com", "x.com", "twimg.com", "ads-twitter.com", "t.co" },
            new[] { "tiktok.com", "tiktokcdn.com", "byteoversea.com" },
            new[] { "mail.ru", "vk.com", "vk.ru", "userapi.com", "ok.ru" },
            new[] { "yahoo.com", "yimg.com", "aol.com" },
            new[] { "pinterest.com", "pinimg.com" },
            new[] { "reddit.com", "redditmedia.com", "redditstatic.com", "redd.it" },
        };

        static readonly HashSet<string> MultiTld = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "com.ua", "net.ua", "org.ua", "gov.ua", "edu.ua", "in.ua", "kiev.ua", "kyiv.ua", "lviv.ua", "od.ua", "dp.ua", "kh.ua", "zp.ua",
            "co.uk", "org.uk", "ac.uk", "gov.uk", "com.au", "net.au", "co.jp", "com.br", "com.pl", "co.il", "com.tr", "co.za", "com.cn", "com.ru", "msk.ru", "spb.ru" };

        public static string RegistrableDomain(string host)
        {
            if (string.IsNullOrEmpty(host)) return "";
            string[] p = host.ToLowerInvariant().TrimEnd('.').Split('.');
            if (p.Length <= 2) return string.Join(".", p);
            string last2 = p[p.Length - 2] + "." + p[p.Length - 1];
            if (MultiTld.Contains(last2) && p.Length >= 3) return p[p.Length - 3] + "." + last2;
            return last2;
        }

        static int EntityOf(string host)
        {
            string h = host.ToLowerInvariant();
            for (int i = 0; i < Entities.Length; i++)
                foreach (string d in Entities[i])
                {
                    if (d.EndsWith("."))
                    {
                        string rd = RegistrableDomain(h);
                        if (rd.StartsWith(d, StringComparison.Ordinal)) return i;
                    }
                    else if (h == d || h.EndsWith("." + d, StringComparison.Ordinal)) return i;
                }
            return -1;
        }

        /// <summary>True when the request belongs to the same site or the same company as the page (DDG: not blocked).</summary>
        public static bool SameParty(string pageHost, string requestHost)
        {
            if (string.IsNullOrEmpty(pageHost) || string.IsNullOrEmpty(requestHost)) return false;
            if (RegistrableDomain(pageHost) == RegistrableDomain(requestHost)) return true;
            int a = EntityOf(pageHost);
            return a >= 0 && a == EntityOf(requestHost);
        }

        // ---------------------------------------------------------------- links

        static readonly HashSet<string> TrackingParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id", "utm_name", "utm_reader", "utm_brand", "utm_social",
            "fbclid", "gclid", "gclsrc", "dclid", "gbraid", "wbraid", "msclkid", "yclid", "ysclid", "mc_cid", "mc_eid", "_hsenc", "_hsmi",
            "igshid", "twclid", "ttclid", "li_fat_id", "oly_anon_id", "oly_enc_id", "vero_id", "_openstat", "wickedid", "rb_clickid", "s_cid" };

        /// <summary>Removes tracking parameters. Returns null when nothing changed.</summary>
        public static string StripTracking(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || string.IsNullOrEmpty(u.Query) || u.Query.Length < 2) return null;
            string[] parts = u.Query.Substring(1).Split('&');
            var keep = new List<string>();
            bool removed = false;
            foreach (string part in parts)
            {
                string name = part.Split('=')[0];
                try { name = Uri.UnescapeDataString(name); } catch { }
                if (TrackingParams.Contains(name)) removed = true; else if (part.Length > 0) keep.Add(part);
            }
            if (!removed) return null;
            var b = new UriBuilder(u) { Query = string.Join("&", keep) };
            string r = b.Uri.AbsoluteUri;
            if (keep.Count == 0 && r.EndsWith("?")) r = r.TrimEnd('?');
            return r;
        }

        public static bool IsLocalHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return true;
            if (host == "localhost" || host.EndsWith(".local") || host.EndsWith(".lan") || host.EndsWith(".internal") || host.IndexOf('.') < 0) return true;
            System.Net.IPAddress ip;
            return System.Net.IPAddress.TryParse(host, out ip);
        }

        // ---------------------------------------------------------------- injected page scripts

        public static string PageScript(bool gpc, bool cookies, bool h264)
        {
            var sb = new StringBuilder("(function(){if(window.__beryll)return;window.__beryll=1;");
            // unsaved-input tracking: the memory governor never puts such a tab to sleep
            sb.Append("document.addEventListener('input',function(e){var t=e.target;if(t&&(t.isContentEditable||t.tagName==='TEXTAREA'||(t.tagName==='INPUT'&&/^(text|email|search|tel|url|number|password|)$/.test(t.type||''))))window.__beryllDirty=true;},true);");
            sb.Append("document.addEventListener('submit',function(){window.__beryllDirty=false;},true);");
            if (gpc)
                sb.Append("try{Object.defineProperty(Navigator.prototype,'globalPrivacyControl',{get:function(){return true;},configurable:true});}catch(e){}");
            if (h264)
                sb.Append(@"var bad=/vp0?9|av01|av1/i;try{if(window.MediaSource&&MediaSource.isTypeSupported){var o=MediaSource.isTypeSupported.bind(MediaSource);MediaSource.isTypeSupported=function(t){return bad.test(t)?false:o(t);};}
var c=HTMLMediaElement.prototype.canPlayType;HTMLMediaElement.prototype.canPlayType=function(t){return bad.test(t)?'':c.call(this,t);};
if(navigator.mediaCapabilities&&navigator.mediaCapabilities.decodingInfo){var d=navigator.mediaCapabilities.decodingInfo.bind(navigator.mediaCapabilities);navigator.mediaCapabilities.decodingInfo=function(cfg){var ct=cfg&&cfg.video&&cfg.video.contentType||'';if(bad.test(ct))return Promise.resolve({supported:false,smooth:false,powerEfficient:false});return d(cfg);};}}catch(e){}");
            if (cookies)
                sb.Append(@"(function(){var sel=['#onetrust-reject-all-handler','#CybotCookiebotDialogBodyButtonDecline','#CybotCookiebotDialogBodyLevelButtonLevelOptinDeclineAll','#didomi-notice-disagree-button','.fc-cta-do-not-consent',""[data-testid='uc-deny-all-button']"",'button.sp_choice_type_REJECT_ALL','.cmpboxbtnno','#cmpwelcomebtnno',""button[data-cookiebanner='accept_only_essential_button']"",'.cky-btn-reject','#cookie_action_close_header_reject','.cc-deny','#truste-consent-required'];
var words=/^(відхилити( все| всі)?|відмовитися|лише необхідні|тільки необхідні|reject( all)?|decline( all)?|deny( all)?|only necessary|necessary only|use necessary cookies only|reject optional cookies|отклонить( все)?)$/i;
function vis(b){return b&&b.offsetParent!==null;}
function go(){for(var i=0;i<sel.length;i++){var b=document.querySelector(sel[i]);if(vis(b)){b.click();return true;}}
var boxes=document.querySelectorAll('[id*=cookie i],[class*=cookie i],[id*=consent i],[class*=consent i],[id*=gdpr i],[class*=gdpr i],[aria-label*=cookie i]');
for(var j=0;j<boxes.length&&j<30;j++){var bs=boxes[j].querySelectorAll('button,a[role=button],[role=button]');for(var k=0;k<bs.length;k++){var t=(bs[k].innerText||bs[k].textContent||'').trim();if(t.length<40&&words.test(t)&&vis(bs[k])){bs[k].click();return true;}}}return false;}
var n=0;function loop(){if(go()||++n>8)return;setTimeout(loop,n<3?400:1200);}
if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',loop);else loop();})();");
            sb.Append("})();");
            return sb.ToString();
        }
    }
}
