/*
  sow-data.js - the one data layer of every SOW Broadcast overlay.

  What the program gives an overlay: GET /api/state (one JSON document with the whole published state) and the
  SSE stream /events, which says which parts changed ({"changed":[...],"version":n}). This module loads the state, keeps
  it fresh, and answers the overlays' old read()/readJSON()/ok() calls for the data paths ("Scoreboard/Match/T1Name.txt",
  "Scoreboard/Waiting/timer_seconds.txt", ...) from that state. Pictures, fonts, indexes and videos are still plain files
  and are fetched as before.

    SOW.read(path) / SOW.readJSON(path) / SOW.ok(path)   same results as the old files had
    SOW.events()                                         like new EventSource("/events"), but onmessage runs after the state is fresh
    SOW.timerSeconds()                                   whole seconds left on the waiting-screen countdown (counted from its end time)
    SOW.state()                                          the raw state, when it is loaded

  The path -> value rules are in virtualFile() below; tools/overlay-data.test.mjs checks them against the files the program used to write.
*/
(function () {
  "use strict";

  var DEFAULT_TEXT = { text_starting: "STARTING SOON!", text_brb: "BE RIGHT BACK!", text_end: "THANK YOU FOR WATCHING" };

  var state = null;       // the last /api/state
  var clockOffset = 0;    // server time minus this browser's time, for the countdown
  var loading = null;     // the first load
  var source = null;      // our own connection to the program (WebSocket, or SSE when that is not available)
  var lastEventId = -1;   // the newest event this page has seen (a reconnect asks for what it missed)
  var subscribers = [];   // SOW.events() consumers

  // ---- loading -----------------------------------------------------------------------------------------

  function refresh() {
    return fetch("/api/state?_=" + Date.now(), { cache: "no-store" })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (json) {
        if (json) {
          state = json;
          clockOffset = (json.server_time || Date.now()) - Date.now();
        }
      })
      .catch(function () { /* keep what we have */ });
  }

  function ensure() {
    if (!loading) loading = refresh().then(startEvents);
    return loading;
  }

  // Events are handled one at a time, in order: refresh the state, then tell the page.
  var chain = Promise.resolve();

  function handle(data) {
    chain = chain.then(function () {
      var version = -1;
      try { version = (JSON.parse(data || "{}").version) | 0; } catch (e) { /* not ours */ }
      if (version > lastEventId) lastEventId = version;
      var stale = !state || version > (state.version | 0) || version < 0;
      return (stale ? refresh() : Promise.resolve()).then(function () {
        subscribers.forEach(function (s) { if (s.onmessage) { try { return s.onmessage({ data: data }); } catch (e) { /* the page's problem */ } } });
      });
    });
  }

  // The connection to the program is a WebSocket: browsers (and OBS) allow only six plain HTTP connections per address, and an
  // open /events stream per overlay page used them all up, so the pages' other requests waited for ever. WebSockets are
  // outside that limit. If a WebSocket cannot be opened at all, the old SSE stream (/events) is used instead.
  function startEvents() {
    if (source) return;
    if (typeof WebSocket !== "undefined" && typeof location !== "undefined" && /^https?:$/.test(location.protocol)) connectSocket(0);
    else connectStream();
  }

  function connectStream() {
    if (typeof EventSource === "undefined") return;
    source = new EventSource("/events");
    source.onmessage = function (ev) { handle(ev.data); };
  }

  function connectSocket(failures) {
    var opened = false;
    var url = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/ws" + (lastEventId >= 0 ? "?lastEventId=" + lastEventId : "");
    var socket;
    try { socket = new WebSocket(url); } catch (e) { connectStream(); return; }
    source = socket;
    socket.onopen = function () { opened = true; failures = 0; };
    socket.onmessage = function (ev) { handle(ev.data); };
    socket.onclose = function () {
      if (source === socket) source = null;
      // Never opened twice in a row: this program (or something in between) has no WebSocket, use the SSE stream.
      if (!opened && failures >= 1) { connectStream(); return; }
      setTimeout(function () { if (!source) connectSocket(opened ? 0 : failures + 1); }, opened ? 1000 : 2000);
    };
  }

  // ---- the data paths ----------------------------------------------------------------------------------

  function team(n) { return (state && state.match && state.match["team" + n]) || {}; }
  function general() { return (state && state.match && state.match.general) || {}; }
  function waiting() { return (state && state.match && state.match.waiting) || {}; }
  function maps() { return (state && state.match && state.match.maps) || []; }
  function str(v) { return v === null || v === undefined ? "" : String(v); }

  function timerSeconds() {
    var t = (state && state.timer) || {};
    if (!t.running) return Math.max(0, t.seconds | 0);
    var msLeft = (t.ends_at || 0) - (Date.now() + clockOffset);
    return Math.max(0, Math.ceil(msLeft / 1000));
  }

  function mapText(i) {
    var m = maps().filter(function (x) { return x.index === i; })[0];
    if (!m) return undefined;
    return "Name=" + str(m.map).replace(/\n/g, " ").trim() + "\r\n" +
      "T1=" + (m.t1 | 0) + "\r\n" +
      "T2=" + (m.t2 | 0) + "\r\n" +
      "Completed=" + (m.completed ? 1 : 0) + "\r\n" +
      "Pick=" + str(m.pick) + "\r\n" +
      "T1Ban=" + str(m.t1_ban) + "\r\n" +
      "T2Ban=" + str(m.t2_ban) + "\r\n";
  }

  function colorsText() {
    var c = general().colors || {};
    return Object.keys(c).map(function (k) { return k + "=" + c[k] + "\r\n"; }).join("");
  }

  function useTickerOverride() {
    var w = waiting();
    return w.ticker_override_enabled && str(w.ticker_override).trim().length > 0 ? "1" : "0";
  }

  // The old file for a path under Scoreboard/ (without Scoreboard/): text, an object for the .json files, or undefined
  // when the path is not one of the data files (then it is an ordinary file).
  function virtualFile(rel) {
    var m;
    if ((m = /^Match\/T([12])(Name|Score|Color|Abbr)\.txt$/.exec(rel))) {
      var t = team(m[1]);
      switch (m[2]) {
        case "Name": return str(t.name);
        case "Score": return str(t.score | 0);
        case "Color": return str(t.color_hex);
        case "Abbr": return str(t.abbr);
      }
    }
    if ((m = /^Match\/T([12])P([1-8])(Name|Role|Hero)\.txt$/.exec(rel))) {
      var p = (team(m[1]).players || [])[m[2] - 1] || {};
      return str(m[3] === "Name" ? p.name : m[3] === "Role" ? p.role : p.hero).trim();
    }
    if ((m = /^Match\/Map([1-7])\.txt$/.exec(rel))) return mapText(+m[1]);
    switch (rel) {
      case "Match/CurrentMap.txt": return state && state.match && state.match.current_map ? String(state.match.current_map) : "";
      case "Match/matchtext.txt": return str(state && state.matchtext);
      case "Match/status.txt": return str(general().status_text).trim();
      case "Match/match.json": return (state && state.match) || {};
      case "General/host.txt": return str(general().host);
      case "General/caster1.txt": return str(general().caster1);
      case "General/caster2.txt": return str(general().caster2);
      case "General/first_to.txt": return str(general().maps_count | 0);
      case "General/colors.txt": return colorsText();
      case "Waiting/text_starting.txt": return str(waiting().text_starting) || DEFAULT_TEXT.text_starting;
      case "Waiting/text_brb.txt": return str(waiting().text_brb) || DEFAULT_TEXT.text_brb;
      case "Waiting/text_end.txt": return str(waiting().text_end) || DEFAULT_TEXT.text_end;
      case "Waiting/timer_seconds.txt": return String(timerSeconds());
      case "Waiting/timer_running.txt": return state && state.timer && state.timer.running ? "1" : "0";
      case "Waiting/ticker_override.txt": return str(waiting().ticker_override);
      case "Waiting/ticker_use_override.txt": return useTickerOverride();
      case "Waiting/socials.json": return waiting().socials || {};
      case "Waiting/videos.txt": {
        var v = (state && state.videos) || [];
        return v.length ? v.join("\r\n") + "\r\n" : "";
      }
      case "Waiting/videos_dir.txt": return "";
      case "Standings/standings.json": return (state && state.standings) || {};
      case "Bracket/bracket.json": return (state && state.bracket) || {};
    }
    return undefined;
  }

  // "../Scoreboard/Match/T1Name.txt?_=123" -> "Match/T1Name.txt"; null for anything that is not under Scoreboard/.
  function relative(path) {
    var p = String(path).split("?")[0].replace(/^(\.\.\/|\/)+/, "");
    var at = p.indexOf("Scoreboard/");
    return at === 0 ? p.slice("Scoreboard/".length) : null;
  }

  function fetchText(path) {
    return fetch(path + (path.indexOf("?") < 0 ? "?" : "&") + "_=" + Date.now())
      .then(function (r) { return r.ok ? r.text() : ""; })
      .catch(function () { return ""; });
  }

  function lookup(path) {
    var rel = relative(path);
    if (rel === null) return Promise.resolve(undefined);
    return ensure().then(function () { return virtualFile(rel); });
  }

  // ---- heroes: the full-body picture comes from the manifest (Heroes/index.json), not from a naming rule here ---

  var heroes = null;      // the Heroes/index.json list, as a promise
  var heroesAt = 0;

  function heroList() {
    var now = Date.now();
    if (heroes && now - heroesAt < 5000) return heroes;
    heroesAt = now;
    heroes = fetch("/Scoreboard/Heroes/index.json?_=" + now, { cache: "no-store" })
      .then(function (r) { return r.ok ? r.json() : {}; })
      .then(function (j) { return (j && j.heroes) || []; })
      .catch(function () { return []; });
    return heroes;
  }

  function squeeze(text) { return String(text || "").trim().replace(/\s+/g, " ").toLowerCase(); }

  // "HeroesBody/Ana.png" for a hero (by name or alias), or null when the catalogue does not know it.
  function heroBodyPath(name) {
    var key = squeeze(name);
    if (!key) return Promise.resolve(null);
    return heroList().then(function (list) {
      for (var i = 0; i < list.length; i++) if (squeeze(list[i].name) === key) return list[i].body_image || null;
      for (var j = 0; j < list.length; j++) {
        var aliases = list[j].aliases || [];
        for (var k = 0; k < aliases.length; k++) if (squeeze(aliases[k]) === key) return list[j].body_image || null;
      }
      return null;
    });
  }

  var SOW = {
    state: function () { return state; },
    heroBodyPath: heroBodyPath,
    timerSeconds: timerSeconds,

    read: function (path) {
      return lookup(path).then(function (v) {
        if (v === undefined) return fetchText(path);
        return typeof v === "string" ? v : JSON.stringify(v);
      });
    },

    readJSON: function (path) {
      return lookup(path).then(function (v) {
        if (v !== undefined) return typeof v === "string" ? safeParse(v) : v;
        return fetch(path + (path.indexOf("?") < 0 ? "?" : "&") + "_=" + Date.now())
          .then(function (r) { return r.ok ? r.json() : {}; })
          .catch(function () { return {}; });
      });
    },

    // true when the data exists (the data files always did); otherwise a HEAD request for the real file
    ok: function (path) {
      return lookup(path).then(function (v) {
        if (v !== undefined) return true;
        return fetch(path + (path.indexOf("?") < 0 ? "?" : "&") + "_=" + Date.now(), { method: "HEAD" })
          .then(function (r) { return r.ok; })
          .catch(function () { return false; });
      });
    },

    events: function () {
      ensure();
      var s = { onmessage: null, close: function () { subscribers = subscribers.filter(function (x) { return x !== s; }); } };
      subscribers.push(s);
      return s;
    },
  };

  function safeParse(text) {
    try { return JSON.parse(text); } catch (e) { return {}; }
  }

  window.SOW = SOW;

  // ---- ?preview: one overlay on a checkerboard, scaled to the browser window (the program's Preview menu) ------
  // Only for looking at an overlay outside OBS; OBS never adds ?preview, so nothing changes there.
  if (typeof location !== "undefined" && typeof document !== "undefined" && /[?&]preview(=|&|$)/.test(location.search)) {
    var style = document.createElement("style");
    style.textContent =
      "html{overflow:hidden!important;width:100%!important;height:100%!important;" +
      "background:conic-gradient(#3a3f4d 25%,#2b2f3b 0 50%,#3a3f4d 0 75%,#2b2f3b 0) 0 0/24px 24px!important}" +
      "body{position:absolute!important;left:0!important;top:0!important;transform-origin:0 0!important}";
    (document.head || document.documentElement).appendChild(style);

    var fit = function () {
      if (!document.body) return;
      var scale = Math.min(window.innerWidth / 1920, window.innerHeight / 1080);
      var x = (window.innerWidth - 1920 * scale) / 2, y = (window.innerHeight - 1080 * scale) / 2;
      document.body.style.transform = "translate(" + x + "px," + y + "px) scale(" + scale + ")";
    };
    window.addEventListener("resize", fit);
    document.addEventListener("DOMContentLoaded", fit);
  }
})();
