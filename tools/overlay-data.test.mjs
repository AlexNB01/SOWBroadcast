// Checks HTML/sow-data.js against what the program used to write as files.
//
//   node tools/overlay-data.test.mjs <state.json> <goldenScoreboardDir> [fixturesDir]
//
// state.json is a GET /api/state document; goldenScoreboardDir holds the legacy files for the same state (src/SowBroadcast.Tests/Golden/*/Scoreboard).
// Every data file in that folder must come out of SOW.read()/readJSON() exactly as it was written (text byte for byte, JSON by value).
// Exits 0 when all match, 1 when something differs (and prints what), 2 on bad usage. Run by the test suite when Node is installed.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const [stateFile, goldenDir, fixturesDir] = process.argv.slice(2);
if (!stateFile || !goldenDir) {
    console.error('usage: node overlay-data.test.mjs <state.json> <goldenScoreboardDir> [fixturesDir]');
    process.exit(2);
}

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, '..', 'HTML', 'sow-data.js'), 'utf8');
const stateText = readFileSync(stateFile, 'utf8');

// A browser that only knows /api/state.
const window = {};
const context = vm.createContext({
    window,
    console,
    Promise,
    JSON,
    Date,
    Math,
    Object,
    String,
    Array,
    fetch: async url => {
        if (String(url).startsWith('/api/state')) return { ok: true, json: async () => JSON.parse(stateText), text: async () => stateText };
        return { ok: false, text: async () => '', json: async () => ({}) };
    },
});
vm.runInContext(source, context);
const SOW = window.SOW;

const detokenise = text => (fixturesDir ? text.replaceAll('{FIXTURES}', fixturesDir.replaceAll('\\', '/')) : text);

function* walk(dir) {
    for (const entry of readdirSync(dir)) {
        const full = join(dir, entry);
        if (statSync(full).isDirectory()) yield* walk(full);
        else yield full;
    }
}

const canonical = value => JSON.stringify(sorted(value));
const sorted = value => Array.isArray(value) ? value.map(sorted)
    : value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(k => [k, sorted(value[k])])) : value;

// The files the overlays read through the data layer (everything else under Scoreboard/ is a picture or a catalogue).
const DATA = /^(Match\/(T[12](Name|Score|Color|Abbr|P[1-8](Name|Role|Hero))|Map[1-7]|CurrentMap|matchtext|status)\.txt|Match\/match\.json|General\/(host|caster1|caster2|first_to|colors)\.txt|Waiting\/(text_starting|text_brb|text_end|timer_seconds|timer_running|ticker_override|ticker_use_override|videos|videos_dir)\.txt|Waiting\/socials\.json|Standings\/standings\.json|Bracket\/bracket\.json)$/;

let checked = 0;
const problems = [];
for (const file of walk(goldenDir)) {
    const rel = relative(goldenDir, file).replaceAll('\\', '/');
    if (!DATA.test(rel)) continue;
    const expected = detokenise(readFileSync(file, 'utf8'));
    const path = `../Scoreboard/${rel}`;
    checked++;

    if (rel.endsWith('.json')) {
        const actual = await SOW.readJSON(path);
        // match.json legacy copies carry local logo paths and the legacy-only "link" settings; the API state leaves paths out.
        const strip = v => { const o = JSON.parse(JSON.stringify(v)); for (const t of ['team1', 'team2']) if (o[t]) o[t].logo_path = null; if (o.general) { o.general.overlay_logo_path = null; o.general.transition_logo_path = null; delete o.general.short_status_text; } delete o.standings; delete o.bracket; return o; };
        const [a, e] = rel === 'Match/match.json' ? [strip(actual), strip(JSON.parse(expected))] : [actual, JSON.parse(expected)];
        if (rel === 'Match/match.json') for (const o of [a, e]) { delete o.draft_link_url; delete o.tournament_link_url; }
        if (canonical(a) !== canonical(e)) problems.push(`${rel}\n  expected ${canonical(e).slice(0, 300)}\n  actual   ${canonical(a).slice(0, 300)}`);
    } else {
        const actual = await SOW.read(path);
        if (actual !== expected) problems.push(`${rel}\n  expected ${JSON.stringify(expected)}\n  actual   ${JSON.stringify(actual)}`);
    }
}

// Files that exist for every map but may be empty in the sample still have to exist as data.
for (const i of [1, 2, 3, 4, 5, 6, 7]) {
    if (!(await SOW.ok(`../Scoreboard/Match/Map${i}.txt`))) problems.push(`Map${i}.txt should exist`);
}

if (checked < 40) problems.push(`only ${checked} data files were found in ${goldenDir}`);
if (problems.length) {
    console.error(`${problems.length} difference(s) in ${checked} files:\n` + problems.join('\n'));
    process.exit(1);
}
console.log(`${checked} data files match`);
