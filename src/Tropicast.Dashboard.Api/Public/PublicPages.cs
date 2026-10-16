using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tropicast.Dashboard.Domain;

namespace Tropicast.Dashboard.Api.Public;

/// <summary>
/// Server-rendered public HTML: the station page and the embeddable player, with inline CSS and a small player
/// script (no SPA bundle, no web fonts), so a page is a few kilobytes before audio. Every value from the station is
/// HTML-encoded; the data block is JSON with &lt; &gt; &amp; escaped, so it cannot close the script element.
/// </summary>
internal static class PublicPages
{
    private static readonly HtmlEncoder Html = HtmlEncoder.Default;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Default,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Dashboard design (#86): the values are copied from the Untitled UI theme in <c>web/src/styles/theme.css</c>
    /// (Tailwind neutral greys, teal brand, solid brand teal-700 for AA under white) so the public player matches the
    /// dashboard without loading its stylesheet. Change them together. Inter only when installed: no font download.
    /// The embed card is solid, not frosted: the host page behind it can be any colour and would break the contrast.
    /// </summary>
    private const string Css = """
        :root{color-scheme:light dark;--fg:#171717;--muted:#525252;--bg:#fafafa;--card:#fff;--glass:rgb(255 255 255/.72);
        --line:#e5e5e5;--track:#d4d4d4;--brand:#00786f;--link:#00786f;--tint:#f0fdfa;
        --focus:#00786f;--live:#e7000b;
        --shadow:0 12px 16px -4px rgb(0 0 0/.08),0 4px 6px -2px rgb(0 0 0/.03),0 2px 2px -1px rgb(0 0 0/.04)}
        @media (prefers-color-scheme:dark){:root{--fg:#fafafa;--muted:#a1a1a1;--bg:#0a0a0a;--card:#171717;
        --glass:rgb(23 23 23/.7);--line:#262626;--track:#404040;--link:#00d5be;--focus:#00d5be;--tint:rgb(0 95 90/.35)}}
        *{box-sizing:border-box}[hidden]{display:none!important}
        body{margin:0;min-height:100vh;font:16px/1.5 Inter,-apple-system,"Segoe UI",Roboto,Arial,sans-serif;color:var(--fg);
        background:radial-gradient(circle at 15% 0,var(--tint),transparent 55%),var(--bg)}
        main{max-width:560px;margin:0 auto;padding:32px 16px}a{color:var(--link)}
        .sr{position:absolute;width:1px;height:1px;overflow:hidden;clip-path:inset(50%);white-space:nowrap}
        .card{position:relative;padding:20px;border:1px solid var(--line);border-radius:16px;background:var(--card);box-shadow:var(--shadow)}
        @supports (backdrop-filter:blur(1px)){.card{background:var(--glass);backdrop-filter:blur(16px) saturate(1.4)}}
        @media (prefers-reduced-transparency:reduce){.card{background:var(--card);backdrop-filter:none}}
        body.embed .card{background:var(--card);backdrop-filter:none}
        .top{display:flex;gap:12px;align-items:flex-start}.titles{flex:1;min-width:0}
        .logo{flex:none;width:64px;height:64px;border-radius:12px;object-fit:cover;background:var(--line)}
        h1{font-size:1.375rem;line-height:1.3;font-weight:600;margin:0;overflow-wrap:anywhere}h1 a{color:inherit;text-decoration:none}h1 a:hover{text-decoration:underline}
        .meta,#note{margin:2px 0 0;color:var(--muted);font-size:.9375rem}#note:empty{margin:0}.titles:has(#note:not(:empty)) .meta{display:none}
        #note[data-busy]::before{content:"";display:inline-block;width:.8em;height:.8em;margin-right:.5em;vertical-align:-.05em;border:2px solid currentColor;
        border-right-color:transparent;border-radius:50%;animation:spin 1s linear infinite}
        @keyframes spin{to{transform:rotate(1turn)}}@keyframes pulse{50%{opacity:.55}}
        .bar{display:flex;gap:10px;align-items:center;margin:20px 0 12px;font-size:.8125rem;color:var(--muted);font-variant-numeric:tabular-nums}
        .badge{display:inline-flex;gap:6px;align-items:center;padding:0 8px;border-radius:99px;border:1px solid var(--line);font-weight:500;white-space:nowrap}
        .badge::before{content:"";width:6px;height:6px;border-radius:50%;background:var(--muted)}
        .badge.live{border-color:var(--live);background:var(--live);color:#fff}.badge.live::before{background:#fff}
        .track{flex:1;height:4px;border-radius:99px;background:var(--track);overflow:hidden}
        .track span{display:block;width:0;height:100%;background:var(--link);transition:width .4s}.on .track span{width:100%;animation:pulse 2s ease-in-out infinite}
        .controls{display:grid;grid-template-columns:minmax(0,1fr) auto minmax(0,1fr);align-items:center}.sound{display:flex;align-items:center;justify-content:flex-end;gap:4px}
        button,summary{display:inline-grid;place-items:center;font:inherit;color:inherit;border:0;background:none;cursor:pointer;-webkit-tap-highlight-color:transparent}
        .icon,summary{width:44px;height:44px;border-radius:10px}.icon:hover,summary:hover{background:var(--line)}
        svg{width:24px;height:24px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round}
        .play{width:64px;height:64px;border-radius:50%;background:var(--brand);color:#fff;box-shadow:var(--shadow)}.play svg{width:28px;height:28px;fill:currentColor;stroke:none}
        .play:hover{filter:brightness(1.1)}.play:disabled{opacity:.45;cursor:not-allowed;filter:none}
        .on .i-play,.i-stop,.muted .i-wave,.i-mute,.done .i-share,.i-done{display:none}.on .i-stop,.muted .i-mute,.done .i-done{display:block}
        :focus-visible{outline:2px solid var(--focus);outline-offset:2px}
        input[type=range]{width:96px;accent-color:var(--link)}@media (hover:none),(max-width:419px){input[type=range]{display:none}}
        details{position:relative}summary{list-style:none}summary::-webkit-details-marker{display:none}
        .menu{position:absolute;right:0;top:48px;z-index:1;min-width:200px;margin:0;padding:4px;list-style:none;border:1px solid var(--line);
        border-radius:12px;background:var(--card);box-shadow:var(--shadow)}
        .menu a{display:block;padding:10px 12px;border-radius:8px;color:var(--fg);text-decoration:none}.menu a:hover{background:var(--line)}
        .desc{white-space:pre-line;margin:24px 4px 0}footer{color:var(--muted);font-size:.8125rem;margin:32px 4px 0}footer a{color:inherit}
        body.embed{min-height:0;background:transparent}body.embed main{max-width:none;padding:6px}
        body.embed .card{display:grid;grid-template-columns:auto 1fr auto;grid-template-areas:"play top sound" "bar bar bar";gap:0 12px;padding:10px 12px;border-radius:12px}
        .embed .controls{display:contents}.embed .play{grid-area:play;width:48px;height:48px}.embed .play svg{width:22px;height:22px}
        .embed .top{grid-area:top;align-items:center;min-width:0}.embed .sound{grid-area:sound}.embed .controls>div:first-child,.embed input[type=range]{display:none}
        .embed .logo{width:44px;height:44px;border-radius:8px}.embed h1{font-size:1rem}.embed .meta,.embed #note{font-size:.8125rem}
        .embed .bar{grid-area:bar;margin:8px 0 0}.embed .titles{white-space:nowrap}.embed h1,.embed .meta{overflow:hidden;text-overflow:ellipsis}
        @media (max-width:359px){.embed .logo{display:none}}
        @media (prefers-reduced-motion:reduce){*,::before,::after{animation:none!important;transition:none!important}}
        """;

    /// <summary>
    /// Plays the first stream the browser supports (Opus, else MP3) straight from the listener host; reconnects with
    /// backoff (1 s doubling to 30 s) while the listener wants to play; refreshes the live state every 30 s. Shows the
    /// listening time, mute and volume, and shares the page: the share sheet, else the clipboard (a check mark and a
    /// status message of its own, so "Reconnecting…" stays), else a prompt with the address to copy by hand.
    /// </summary>
    private const string Player = """
        (()=>{const $=i=>document.getElementById(i),d=JSON.parse($('station-data').textContent);
        const card=$('card'),b=$('play'),label=$('play-label'),badge=$('status'),note=$('note'),clock=$('elapsed'),mute=$('mute'),vol=$('volume');
        const a=new Audio();a.preload='none';
        const s=d.streams.find(x=>a.canPlayType(x.format==='Opus'?'audio/ogg; codecs="opus"':x.contentType))||d.streams[d.streams.length-1];
        let want=false,delay=1000,timer=0,since=0,tick=0;
        const say=(t,busy)=>{note.textContent=t;note.toggleAttribute('data-busy',!!busy);};
        const ui=()=>{label.textContent=want?d.t.stop:d.t.play;card.classList.toggle('on',want);b.disabled=!want&&d.live===false&&!d.autoDj;};
        const live=(v,auto)=>{d.live=v;d.autoDj=!!auto;badge.textContent=v===true?d.t.live:auto?d.t.autoDj:v===false?d.t.offline:'';badge.hidden=v==null;badge.classList.toggle('live',!!v||!!auto);ui();};
        const two=n=>String(n).padStart(2,'0');
        const count=()=>{const n=Math.floor((Date.now()-since)/1000),h=Math.floor(n/3600),m=Math.floor(n/60)%60;
        clock.textContent=(h?h+':'+two(m):m)+':'+two(n%60);};
        const start=()=>{clearTimeout(timer);a.src=s.url+(s.url.includes('?')?'&':'?')+'t='+Date.now();a.play().catch(retry);};
        function retry(){if(!want)return;say(d.t.reconnecting,true);clearTimeout(timer);timer=setTimeout(start,delay);delay=Math.min(delay*2,30000);}
        const stop=()=>{clearTimeout(timer);clearInterval(tick);since=0;clock.textContent='0:00';a.pause();a.removeAttribute('src');a.load();say('');};
        a.addEventListener('playing',()=>{delay=1000;say('');if(!since){since=Date.now();tick=setInterval(count,1000);}});
        for(const e of['error','ended','stalled'])a.addEventListener(e,retry);
        b.addEventListener('click',()=>{want=!want;if(want)start();else stop();ui();});
        const sound=()=>{const m=a.muted||a.volume===0;mute.classList.toggle('muted',m);mute.setAttribute('aria-label',m?d.t.unmute:d.t.mute);};
        mute.addEventListener('click',()=>{if(a.muted||a.volume===0){a.muted=false;if(a.volume===0)vol.value=a.volume=1;}else a.muted=true;sound();});
        vol.addEventListener('input',()=>{a.volume=+vol.value;a.muted=false;sound();});
        const share=$('share'),shared=$('shared');share&&share.addEventListener('click',async()=>{
        if(navigator.share){try{await navigator.share({title:d.name,url:d.pageUrl});}catch{}return;}
        try{await navigator.clipboard.writeText(d.pageUrl);}catch{prompt(d.t.copy,d.pageUrl);return;}
        shared.textContent=d.t.copied;share.classList.add('done');setTimeout(()=>{shared.textContent='';share.classList.remove('done');},3000);});
        const menu=document.querySelector('details');menu&&menu.addEventListener('keydown',e=>{if(e.key==='Escape'&&menu.open){menu.open=false;menu.querySelector('summary').focus();}});
        document.addEventListener('click',e=>{if(menu&&menu.open&&!menu.contains(e.target))menu.open=false;});
        const poll=async()=>{try{const r=await fetch(d.statusUrl);if(r.ok){const j=await r.json();live(j.live,j.autoDj);}}catch{}setTimeout(poll,30000);};
        live(d.live,d.autoDj);sound();setTimeout(poll,30000);})();
        """;

    /// <summary>Inline icons (Untitled UI line style, 24 px, stroke 2), so the page loads no image for its controls.</summary>
    private const string PlayIcon = """<svg class="i-play" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 4.5v15a1 1 0 0 0 1.5.86l12.5-7.5a1 1 0 0 0 0-1.72L8.5 3.64A1 1 0 0 0 7 4.5Z"/></svg>""";

    private const string StopIcon = """<svg class="i-stop" viewBox="0 0 24 24" aria-hidden="true"><rect x="6" y="6" width="12" height="12" rx="2"/></svg>""";

    private const string VolumeIcon = """<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M11 5 6 9H3v6h3l5 4V5Z"/><path class="i-wave" d="M15.5 8.5a5 5 0 0 1 0 7M18.5 5.5a9 9 0 0 1 0 13"/><path class="i-mute" d="m16 9 6 6m0-6-6 6"/></svg>""";

    private const string ShareIcon = """<svg class="i-share" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 12v7a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-7M16 6l-4-4-4 4M12 2v13"/></svg><svg class="i-done" viewBox="0 0 24 24" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg>""";

    private const string MoreIcon = """<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/></svg>""";

    /// <summary>
    /// <c>&lt;script src="…/embed.js" data-station="tenant/station" async&gt;</c> puts the player iframe after itself.
    /// </summary>
    internal const string EmbedScript = """
        (()=>{const s=document.currentScript;if(!s)return;const t=s.dataset.station||'';
        if(!/^[a-z0-9-]{3,64}\/[a-z0-9-]{3,64}$/.test(t))return;
        const f=document.createElement('iframe');f.src=new URL(s.src).origin+'/embed/'+t;f.title=s.dataset.title||'Radio player';
        f.width='100%';f.height='120';f.loading='lazy';f.allow='autoplay';f.style.cssText='border:0;max-width:480px;display:block';
        s.after(f);})();
        """;

    private static readonly Dictionary<string, string> English = new()
    {
        ["play"] = "Play", ["stop"] = "Stop", ["live"] = "Live", ["autoDj"] = "AutoDJ", ["offline"] = "Offline", ["reconnecting"] = "Reconnecting…",
        ["listen"] = "Listen live", ["website"] = "Website", ["mute"] = "Mute", ["unmute"] = "Unmute", ["volume"] = "Volume",
        ["share"] = "Share", ["copied"] = "Link copied", ["copy"] = "Copy this link", ["more"] = "More options", ["stream"] = "Open the {0} stream",
        ["privacy"] = "Privacy", ["terms"] = "Terms",
    };

    private static readonly Dictionary<string, string> French = new()
    {
        ["play"] = "Écouter", ["stop"] = "Arrêter", ["live"] = "En direct", ["autoDj"] = "AutoDJ", ["offline"] = "Hors antenne", ["reconnecting"] = "Reconnexion…",
        ["listen"] = "Écouter en direct", ["website"] = "Site web", ["mute"] = "Couper le son", ["unmute"] = "Rétablir le son",
        ["volume"] = "Volume", ["share"] = "Partager", ["copied"] = "Lien copié", ["copy"] = "Copiez ce lien", ["more"] = "Plus d’options",
        ["stream"] = "Ouvrir le flux {0}", ["privacy"] = "Confidentialité", ["terms"] = "Conditions",
    };

    internal static string Station(PublicStationResponse station, string statusUrl, string nonce, bool embed)
    {
        var lang = station.Language is { Length: >= 2 } tag ? tag : "en";
        var t = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? French : English;
        var name = Html.Encode(station.Name);
        var summary = station.Description.Length > 0 ? station.Description : $"{station.Name}: {t["listen"]}";
        var description = Html.Encode(summary.Length > 300 ? summary[..300] : summary);
        var meta = string.Join(" · ", new[] { station.Genre, station.Country ?? "" }.Where(v => v.Length > 0));
        var pageUrl = station.PageUrl.AbsoluteUri;
        var data = JsonSerializer.Serialize(new { name = station.Name, pageUrl, streams = station.Streams, live = station.Live, autoDj = station.AutoDj, statusUrl, t }, Json);
        var logo = station.LogoUrl is { } url
            ? $"""<img class="logo" src="{Html.Encode(url.AbsoluteUri)}" alt="" width="64" height="64" decoding="async">"""
            : "";
        var head = embed
            ? """<meta name="robots" content="noindex">"""
            : $"""
              <meta name="description" content="{description}">
              <link rel="canonical" href="{Html.Encode(station.PageUrl.AbsoluteUri)}">
              <meta property="og:type" content="music.radio_station">
              <meta property="og:site_name" content="Tropicast">
              <meta property="og:title" content="{name}">
              <meta property="og:description" content="{description}">
              <meta property="og:url" content="{Html.Encode(station.PageUrl.AbsoluteUri)}">
              {(station.LogoUrl is { } image ? $"""<meta property="og:image" content="{Html.Encode(image.AbsoluteUri)}">""" : "")}
              <meta name="twitter:card" content="summary">
              """;
        var title = embed ? $"""<a href="{Html.Encode(station.PageUrl.AbsoluteUri)}" target="_blank" rel="noopener">{name}</a>""" : name;
        var body = embed ? "" : $"""
            {(station.Description.Length > 0 ? $"""<p class="desc">{Html.Encode(station.Description)}</p>""" : "")}
            {(station.Website is { } site ? $"""<p><a href="{Html.Encode(site.AbsoluteUri)}" rel="noopener nofollow ugc">{t["website"]}</a></p>""" : "")}
            <footer>Tropicast · <a href="/privacy">{t["privacy"]}</a> · <a href="/terms">{t["terms"]}</a></footer>
            """;
        return $"""
            <!doctype html>
            <html lang="{Html.Encode(lang)}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <link rel="icon" href="data:,">
            <title>{name} · {t["listen"]}</title>
            {head}
            <style nonce="{nonce}">{Css}</style>
            </head>
            <body{(embed ? " class=\"embed\"" : "")}>
            <main>
            <section id="card" class="card" aria-labelledby="name">
            <div class="top">{logo}<div class="titles"><h1 id="name">{title}</h1>{(meta.Length > 0 ? $"""<p class="meta">{Html.Encode(meta)}</p>""" : "")}<p id="note" role="status" aria-live="polite"></p></div>{More(station, t, embed)}</div>
            <div class="bar"><span id="status" class="badge" hidden></span><span class="track"><span></span></span><span id="elapsed">0:00</span></div>
            <div class="controls"><div><button id="share" class="icon" type="button" aria-label="{t["share"]}">{ShareIcon}</button><span id="shared" class="sr" role="status"></span></div>
            <button id="play" class="play" type="button">{PlayIcon}{StopIcon}<span id="play-label" class="sr">{t["play"]}</span></button>
            <div class="sound"><button id="mute" class="icon" type="button" aria-label="{t["mute"]}">{VolumeIcon}</button><input id="volume" type="range" min="0" max="1" step="0.05" value="1" aria-label="{t["volume"]}"></div></div>
            </section>
            {body}
            </main>
            <script id="station-data" type="application/json">{data}</script>
            <script nonce="{nonce}">{Player}</script>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// The "more" menu with the stream addresses, on the station page only: in the embed, a dropdown cannot fit the
    /// 120 px iframe, and the title already links the station page.
    /// </summary>
    private static string More(PublicStationResponse station, Dictionary<string, string> t, bool embed)
    {
        if (embed)
        {
            return "";
        }

        var items = station.Streams.Select(stream =>
            $"""<li><a href="{Html.Encode(stream.Url.AbsoluteUri)}" target="_blank" rel="noopener">{Html.Encode(string.Format(CultureInfo.InvariantCulture, t["stream"], stream.Format == AudioFormat.Opus ? "Opus" : "MP3"))}</a></li>""");
        var list = string.Concat(items);
        return list.Length == 0 ? "" : $"""<details><summary aria-label="{t["more"]}">{MoreIcon}</summary><ul class="menu">{list}</ul></details>""";
    }

    internal static string EmbedNotIncluded(PublicStationResponse station, string nonce) => Message(nonce,
        $"""<a href="{Html.Encode(station.PageUrl.AbsoluteUri)}" target="_blank" rel="noopener">{Html.Encode(station.Name)}</a>: """
        + "embedding is not included in this station's plan.");

    internal static string NotFound(string nonce) => Message(nonce, "This station does not exist or is not public.");

    private static string Message(string nonce, string html) => $"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="robots" content="noindex"><link rel="icon" href="data:,"><title>Tropicast</title><style nonce="{nonce}">{Css}</style></head>
        <body><main><p>{html}</p></main></body></html>
        """;
}
