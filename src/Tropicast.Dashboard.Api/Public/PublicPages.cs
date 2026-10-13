using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    private const string Css = """
        :root{color-scheme:light dark;--fg:#16202a;--bg:#fff;--muted:#5b6670;--accent:#0b6e4f;--card:#f3f5f7}
        @media (prefers-color-scheme:dark){:root{--fg:#e8edf1;--bg:#111619;--muted:#9aa6af;--accent:#3fc28f;--card:#1b2227}}
        *{box-sizing:border-box}body{margin:0;font:16px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;color:var(--fg);background:var(--bg)}
        main{max-width:640px;margin:0 auto;padding:24px 16px}
        .head{display:flex;gap:16px;align-items:center}.logo{width:88px;height:88px;border-radius:12px;object-fit:cover;background:var(--card)}
        h1{font-size:1.6rem;margin:0}.meta{color:var(--muted);margin:2px 0 0}
        .badge{display:inline-block;margin-top:6px;padding:2px 10px;border-radius:99px;background:var(--card);font-size:.85rem;color:var(--muted)}
        .badge.live{background:#c62828;color:#fff}
        .player{display:flex;gap:12px;align-items:center;margin:20px 0;padding:16px;border-radius:12px;background:var(--card)}
        button{font:inherit;font-weight:600;min-width:112px;min-height:48px;border:0;border-radius:99px;background:var(--accent);color:#fff;cursor:pointer}
        button:disabled{opacity:.5;cursor:not-allowed}button:focus-visible{outline:3px solid var(--fg);outline-offset:2px}
        #note{color:var(--muted);margin:0}.desc{white-space:pre-line}a{color:var(--accent)}footer{color:var(--muted);font-size:.85rem;margin-top:32px}
        body.embed main{padding:8px}body.embed .logo{width:56px;height:56px}body.embed h1{font-size:1.1rem}body.embed .player{margin:8px 0 0;padding:8px 12px}
        """;

    /// <summary>
    /// Plays the first stream the browser supports (Opus, else MP3) straight from the listener host; reconnects with
    /// backoff (1 s doubling to 30 s) while the listener wants to play; refreshes the live state every 30 s.
    /// </summary>
    private const string Player = """
        (()=>{const d=JSON.parse(document.getElementById('station-data').textContent);
        const b=document.getElementById('play'),badge=document.getElementById('status'),note=document.getElementById('note');
        const a=new Audio();a.preload='none';
        const s=d.streams.find(x=>a.canPlayType(x.format==='Opus'?'audio/ogg; codecs="opus"':x.contentType))||d.streams[d.streams.length-1];
        let want=false,delay=1000,timer=0;
        const ui=()=>{b.textContent=want?d.t.stop:d.t.play;b.disabled=!want&&d.live===false;};
        const live=v=>{d.live=v;badge.textContent=v===true?d.t.live:v===false?d.t.offline:'';badge.hidden=v==null;badge.className='badge'+(v?' live':'');ui();};
        const start=()=>{clearTimeout(timer);a.src=s.url+(s.url.includes('?')?'&':'?')+'t='+Date.now();a.play().catch(retry);};
        function retry(){if(!want)return;note.textContent=d.t.reconnecting;clearTimeout(timer);timer=setTimeout(start,delay);delay=Math.min(delay*2,30000);}
        const stop=()=>{clearTimeout(timer);a.pause();a.removeAttribute('src');a.load();note.textContent='';};
        a.addEventListener('playing',()=>{delay=1000;note.textContent='';});
        for(const e of['error','ended','stalled'])a.addEventListener(e,retry);
        b.addEventListener('click',()=>{want=!want;if(want)start();else stop();ui();});
        const poll=async()=>{try{const r=await fetch(d.statusUrl);if(r.ok)live((await r.json()).live);}catch{}setTimeout(poll,30000);};
        live(d.live);setTimeout(poll,30000);})();
        """;

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
        ["play"] = "Play", ["stop"] = "Stop", ["live"] = "Live", ["offline"] = "Offline", ["reconnecting"] = "Reconnecting…",
        ["listen"] = "Listen live", ["website"] = "Website",
    };

    private static readonly Dictionary<string, string> French = new()
    {
        ["play"] = "Écouter", ["stop"] = "Arrêter", ["live"] = "En direct", ["offline"] = "Hors antenne", ["reconnecting"] = "Reconnexion…",
        ["listen"] = "Écouter en direct", ["website"] = "Site web",
    };

    internal static string Station(PublicStationResponse station, string statusUrl, string nonce, bool embed)
    {
        var lang = station.Language is { Length: >= 2 } tag ? tag : "en";
        var t = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? French : English;
        var name = Html.Encode(station.Name);
        var summary = station.Description.Length > 0 ? station.Description : $"{station.Name}: {t["listen"]}";
        var description = Html.Encode(summary.Length > 300 ? summary[..300] : summary);
        var meta = string.Join(" · ", new[] { station.Genre, station.Country ?? "" }.Where(v => v.Length > 0));
        var data = JsonSerializer.Serialize(new { streams = station.Streams, live = station.Live, statusUrl, t }, Json);
        var logo = station.LogoUrl is { } url
            ? $"""<img class="logo" src="{Html.Encode(url.AbsoluteUri)}" alt="" width="88" height="88" decoding="async">"""
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
            <footer>Tropicast</footer>
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
            <div class="head">{logo}<div><h1>{title}</h1>{(meta.Length > 0 ? $"""<p class="meta">{Html.Encode(meta)}</p>""" : "")}<span id="status" class="badge" hidden></span></div></div>
            <div class="player"><button id="play" type="button">{t["play"]}</button><p id="note" role="status" aria-live="polite"></p></div>
            {body}
            </main>
            <script id="station-data" type="application/json">{data}</script>
            <script nonce="{nonce}">{Player}</script>
            </body>
            </html>
            """;
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
