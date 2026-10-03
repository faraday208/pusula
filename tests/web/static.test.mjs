// Static guards for src/Pusula/wwwroot: the page must work under the server's CSP
// (no inline script or style, same-origin only) and must not let markup in through
// anything but markdown-it's output.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { RELEASES_URL } from '../../src/Pusula/wwwroot/js/core.js';

const wwwroot = fileURLToPath(new URL('../../src/Pusula/wwwroot/', import.meta.url));
const read = (path) => readFileSync(`${wwwroot}${path}`, 'utf8');

const html = read('index.html');
const css = read('app.css');
const scripts = Object.fromEntries(
  readdirSync(`${wwwroot}js`).filter((name) => name.endsWith('.js')).map((name) => [name, read(`js/${name}`)]),
);

describe('index.html under the CSP', () => {
  it('has no inline style: no <style> element and no style attribute', () => {
    assert.ok(!/<style[\s>]/i.test(html));
    assert.ok(!/\sstyle\s*=/i.test(html));
  });

  it('has no inline script and no inline event handler', () => {
    const tags = [...html.matchAll(/<script\b[^>]*>/gi)].map((match) => match[0]);
    assert.equal(tags.length, 2);
    for (const tag of tags) assert.match(tag, /\ssrc="[^"]+"/, tag);
    assert.ok(!/\son[a-z]+\s*=/i.test(html));
  });

  it('loads the classic markdown-it script before the module', () => {
    const vendor = html.indexOf('src="vendor/markdown-it/markdown-it.umd.min.js"');
    const module = html.indexOf('type="module" src="js/app.js"');
    assert.ok(vendor > 0 && module > vendor);
    assert.ok(!/<script[^>]*src="vendor[^>]*type="module"/.test(html));
  });

  it('references only files that exist and only same-origin addresses', () => {
    assert.ok(!/https?:\/\//i.test(html));
    for (const match of html.matchAll(/(?:src|href)="((?!#)[^"]+)"/g)) {
      assert.ok(existsSync(`${wwwroot}${match[1]}`), `${match[1]} does not exist`);
    }
  });
});

describe('same-origin only', () => {
  // The one address outside the server that the page names: where the release notes are. It is the target of a link that the reader
  // follows, never a request (see "the version of pusula" at the end of this file); nothing else may name a remote address.
  const RELEASES_DECLARATION = `export const RELEASES_URL = '${RELEASES_URL}';`;

  it('css and scripts name no remote address and import no remote module', () => {
    assert.ok(!/https?:\/\//i.test(css), 'app.css');
    assert.ok(!/@import/i.test(css));
    for (const [name, source] of Object.entries(scripts)) {
      assert.ok(!/https?:\/\//i.test(source.replace(RELEASES_DECLARATION, '')), name);
      assert.ok(!/import\s*\(/.test(source), `${name} uses dynamic import`);
    }
  });

  it('the only network calls are the API endpoints: the list of sources, what a source has (always through sourceUrl), the two reads of the folder picker (the folders the server found, and a folder through browseUrl), and the writes that change the list (always through editSources)', () => {
    const app = code('app.js');
    const literal = [...app.matchAll(/\b(?:getJson|fetch|EventSource)\(\s*['`]([^'`]+)/g)].map((match) => match[1]);
    assert.deepEqual(literal, ['/api/sources', '/api/browse/found'], 'a literal address is the list of sources, or the folders the picker lists first, and nothing else');
    const viaSource = [...app.matchAll(/\b(?:getJson|EventSource)\(sourceUrl\(([^)]*)\)/g)].map((match) => match[1]);
    assert.equal(viaSource.length, 4, viaSource.join(' | '));
    assert.deepEqual(viaSource.map((argument) => argument.replace(/[^a-z]/g, '').slice(0, 8)), ['tree', 'overview', 'filepath', 'events'].map((name) => name.slice(0, 8)));
    const viaBrowse = [...app.matchAll(/\bgetJson\(browseUrl\(([^)]*)\)/g)].map((match) => match[1]);
    assert.equal(viaBrowse.length, 1, 'the picker reads a folder in one place');
    const every = [...app.matchAll(/\b(?:getJson|fetch|EventSource)\(/g)].length;
    assert.equal(every, literal.length + viaSource.length + viaBrowse.length + 3, 'every call is one of those (and the definitions of getJson and editSources, each with its one fetch)');
    assert.match(functionSource('sourceUrl'), /`\/api\/sources\/\$\{encodeURIComponent\(state\.source\.id\)\}\/\$\{resource\}`/);
    // The writes: one function sends them, and there are three callers, which add the folder that is typed, add the folder the picker chose, and remove a source.
    assert.equal([...app.matchAll(/\beditSources\(/g)].length, 4, 'the definition and the three callers');
    assert.match(app, /source = await editSources\('POST', '\/api\/sources', addSourceBody\(edit\.path, edit\.name\)\);/);
    assert.match(app, /source = await editSources\('POST', '\/api\/sources', addSourceBody\(path\)\);/);
    assert.match(app, /await editSources\('DELETE', sourceItemUrl\(id\)\);/);
    assert.match(functionSource('sourceItemUrl'), /`\/api\/sources\/\$\{encodeURIComponent\(id\)\}`/);
  });
});

describe('markup only from markdown-it', () => {
  it('app.js assigns innerHTML exactly once, from markdown.render', () => {
    const assignments = [...scripts['app.js'].matchAll(/\.innerHTML\s*=[^=][^;]*;/g)].map((match) => match[0]);
    assert.equal(assignments.length, 1, assignments.join('\n'));
    assert.match(assignments[0], /markdown\.render\(/);
  });

  it('no script writes markup or evaluates code some other way', () => {
    const banned = [/outerHTML/, /insertAdjacentHTML/, /document\.write/, /\beval\s*\(/, /new\s+Function\b/, /setAttribute\(\s*['"]style['"]/, /\.cssText/, /srcdoc/];
    for (const [name, source] of Object.entries(scripts)) {
      for (const pattern of banned) assert.ok(!pattern.test(source), `${name} matches ${pattern}`);
    }
  });

  it('core.js and i18n.js never touch the DOM or the network', () => {
    for (const name of ['core.js', 'i18n.js']) {
      const source = scripts[name].replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '');
      for (const pattern of [/\bdocument\b/, /\bwindow\b/, /\bfetch\b/, /\bEventSource\b/, /\bNode\./]) {
        assert.ok(!pattern.test(source), `${name} matches ${pattern}`);
      }
    }
  });
});

describe('overview layout hooks (app.js <-> app.css)', () => {
  it('the overview hides the info panel: app.js sets body.overview-view, app.css acts on it', () => {
    assert.match(scripts['app.js'], /classList\.toggle\('overview-view'/);
    assert.match(css, /body\.overview-view\s*\{[^}]*--info-w:\s*0px/);
    assert.match(css, /body\.overview-view \.info,\s*body\.overview-view \.info-toggle\s*\{[^}]*display:\s*none/);
  });

  it('the one-line path pieces and the fixed table columns exist on both sides', () => {
    const names = ['path-link', 'path-head', 'path-parent', 'path-name', 'path-tail', 'issue-line', 'issue-meta', 'issue-raw', 'issue-detail', 'tail', 'col-tokens', 'col-files', 'col-total', 'fixed'];
    for (const name of names) {
      assert.match(scripts['app.js'], new RegExp(`['\`" ]${name}['\`" ]`), `${name} is not used in app.js`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
  });

  it('the budget band, the issue chips and the other overview pieces exist on both sides', () => {
    const names = [
      'ov-head', 'ov-budget', 'ov-issues', 'ov-layers', 'ov-memory', 'budget-note', 'fold-btn', 'memory-link', 'per-project',
      'band', 'band-seg', 'band-labels', 'band-label', 'band-name', 'band-meta', 'band-files', 'band-files-title', 'band-file-list', 'band-file',
      'ov-errors', 'ov-review', 'issues-head', 'issue-chips', 'issue-chip', 'is-zero', 'tone-error', 'is-error', 'issue-help', 'issue-clear',
      'is-active', 'more-btn', 'row-zero', 'memory-index', 'legend-block', 'legend-title',
    ];
    for (const name of names) {
      assert.match(scripts['app.js'], new RegExp(`['\`" ]${name}['\`" ]`), `${name} is not used in app.js`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
  });

  it('the overview is one column in reading order, and two columns where #content is 1100px wide, the issues beside the rest', () => {
    assert.match(css, /\.page\.overview\s*\{[^}]*max-width:\s*1200px/);
    const wide = css.match(/@container content \(min-width: 1100px\)\s*\{\s*\.overview\s*\{([^}]*)\}/);
    assert.ok(wide, 'no .overview rule in a container query of 1100px');
    assert.match(wide[1], /grid-template-columns:\s*minmax\(0,\s*1fr\)\s+minmax\(360px,\s*420px\)/, 'the issues column is 360 to 420px');
    const rows = [...wide[1].match(/grid-template-areas:([^;]*);/)[1].matchAll(/"([^"]*)"/g)].map((match) => match[1].trim().split(/\s+/));
    assert.deepEqual(rows, [
      ['head', 'head'], ['budget', 'issues'], ['layers', 'issues'], ['memory', 'issues'],
    ], 'the issues column spans every row beside the budget, the layers and the memory');
    for (const [cls, area] of [['ov-head', 'head'], ['ov-budget', 'budget'], ['ov-issues', 'issues'], ['ov-layers', 'layers'], ['ov-memory', 'memory']]) {
      assert.match(css, new RegExp(`\\.${cls}\\s*\\{\\s*grid-area:\\s*${area};`), cls);
    }
    // The sections are appended in DOM order (the two issue sections come in one wrapper, `issuesBlock`); a single column puts them in the
    // order of the page with `order` (below).
    const order = [...scripts['app.js'].matchAll(/page\.append\(overviewHead\(data\),([^;]*)\);/g)].flatMap((match) => match[1].match(/\w+(?:Section|Block)/g));
    assert.deepEqual(order, ['budgetSection', 'issuesBlock', 'layersSection', 'memorySection']);
  });

  it('one column: head, budget, errors, layers, what is to be reviewed, memory; the wrapper of the two issue sections is not a box there and is the second column where it is wide', () => {
    const orderOf = (cls) => Number(new RegExp(`(?:^|\\n)\\.${cls}\\s*\\{\\s*order:\\s*(\\d+);`).exec(css)?.[1]);
    const numbers = ['ov-head', 'ov-budget', 'ov-errors', 'ov-layers', 'ov-review', 'ov-memory'].map(orderOf);
    assert.ok(numbers.every(Number.isFinite), numbers.join(','));
    assert.deepEqual([...numbers].sort((a, b) => a - b), numbers, 'the order values are the reading order');
    assert.equal(new Set(numbers).size, numbers.length);
    assert.match(ruleBody('.ov-issues') ?? '', /display: contents/, 'in one column the wrapper takes no box: its sections are sorted among the others');
    const wide = css.match(/@container content \(min-width: 1100px\)\s*\{[\s\S]*?\.ov-issues\s*\{([^}]*)\}/)?.[1] ?? '';
    assert.match(wide, /grid-area: issues/);
    assert.match(wide, /display: block/, 'in two columns it is a box again: the errors above what is to be reviewed');
    const body = functionSource('issuesBlock');
    assert.ok(body.indexOf('errorsSection(') < body.indexOf('reviewSection('), 'errors before what is to be reviewed inside the wrapper');
    assert.match(body, /el\('div', 'ov-issues'\)/);
    assert.match(body, /if \(reviewCount > 0\) block\.append\(reviewSection/, 'nothing to review leaves that section out');
  });

  it('the page asks #content, not the window: it is a size container, and no wide-window media query lays the overview out', () => {
    const body = css.match(/(?:^|\n)\.content\s*\{([^}]*)\}/)?.[1] ?? '';
    assert.match(body, /container-type:\s*inline-size/);
    assert.match(body, /container-name:\s*content/);
    assert.ok(!/@media \(min-width: 1[0-9]{3}px\)/.test(css), 'a wide-window media query is left');
  });

  it('the issues heading and counts are pinned beside the rest (two columns), not in one column', () => {
    const wide = css.match(/@container content \(min-width: 1100px\)\s*\{\s*\.issues-head\s*\{([^}]*)\}/);
    assert.ok(wide, 'no .issues-head rule in a container query of 1100px');
    assert.match(wide[1], /position:\s*sticky/);
    assert.match(wide[1], /top:\s*0/);
  });

  it('hidden rows are hidden even where a class sets display (the "show more" lists rely on it)', () => {
    assert.match(css, /\[hidden\]\s*\{\s*display:\s*none\s*!important;/);
  });
});

describe('vendored markdown-it', () => {
  const readme = read('vendor/README.md');

  it('ships the bundle and its license', () => {
    assert.ok(existsSync(`${wwwroot}vendor/markdown-it/markdown-it.umd.min.js`));
    assert.match(read('vendor/markdown-it/LICENSE'), /Permission is hereby granted/);
  });

  it('the bundle matches the hash recorded in vendor/README.md', () => {
    const bytes = readFileSync(`${wwwroot}vendor/markdown-it/markdown-it.umd.min.js`);
    const sha256 = createHash('sha256').update(bytes).digest('hex');
    assert.ok(readme.includes(sha256), `vendor/README.md does not list sha256 ${sha256}`);
  });

  it('README records the version, the source and a sha512 integrity', () => {
    assert.match(readme, /15\.0\.2/);
    assert.match(readme, /registry\.npmjs\.org\/markdown-it\/-\/markdown-it-15\.0\.2\.tgz/);
    assert.match(readme, /sha512-[A-Za-z0-9+/]{86}==/);
  });
});

// ---------------------------------------------------------------------------------------------
// The page never scrolls (the scroll trap)
// ---------------------------------------------------------------------------------------------

/** The declarations of the first rule whose selector is exactly `selector` (whitespace-insensitive). */
function ruleBody(selector) {
  const pattern = selector.split(/\s*,\s*/).map((part) => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('\\s*,\\s*');
  const match = new RegExp(`(?:^|[}\\s])${pattern}\\s*\\{([^}]*)\\}`).exec(css);
  return match ? match[1].replace(/\s+/g, ' ').trim() : null;
}

const code = (name) => scripts[name].replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '');

describe('the document never scrolls', () => {
  it('no script calls scrollIntoView: it moves every scrollable ancestor, the document included', () => {
    for (const name of Object.keys(scripts)) {
      assert.ok(!/scrollIntoView/.test(code(name)), `${name} calls scrollIntoView`);
    }
  });

  it('the page scrolls #content and #sidebar through scrollWithin, which uses scrollTargetTop', () => {
    const app = code('app.js');
    assert.match(app, /function scrollWithin\(/);
    assert.match(app, /scrollTargetTop\(/);
    assert.match(app, /scrollWithin\(refs\.sidebar,/);
    assert.match(app, /scrollWithin\(refs\.content,/);
  });

  it('every scroll container and every tree row is positioned, so an absolutely positioned child (the .sr-only text of a badge) stays inside its container', () => {
    assert.match(ruleBody('.sidebar, .content, .info') ?? '', /position: relative/);
    assert.match(ruleBody('.tree-row') ?? '', /position: relative/);
    assert.match(ruleBody('.sr-only') ?? '', /position: absolute/, 'the cause this guards against');
  });

  it('the narrow-screen drawers override the positioning with fixed, after the base rule', () => {
    const base = css.indexOf('.sidebar,\n.content,\n.info {');
    assert.ok(base > 0);
    const drawers = [...css.matchAll(/position:\s*fixed;/g)].map((match) => match.index);
    assert.ok(drawers.length >= 2);
    assert.ok(drawers.every((index) => index > base), 'a fixed drawer rule comes before the base rule and would lose');
  });

  it('html and body clip their overflow, with hidden as the fallback for a browser without clip', () => {
    assert.match(ruleBody('html, body') ?? '', /overflow: hidden; overflow: clip;/);
  });

  it('a stray document scroll is put back', () => {
    const app = code('app.js');
    assert.match(app, /window\.addEventListener\('scroll'/);
    assert.match(app, /document\.scrollingElement/);
    assert.match(app, /root\.scrollTo\(0, 0\)/);
  });
});

// ---------------------------------------------------------------------------------------------
// Colour: link text, the danger badge and the load markers (WCAG contrast of the real tokens)
// ---------------------------------------------------------------------------------------------

function parseTokens(block) {
  const tokens = {};
  for (const match of block.matchAll(/(--[\w-]+):\s*([^;]+);/g)) tokens[match[1]] = match[2].trim();
  return tokens;
}

function parseColor(text) {
  const hex = /^#([0-9a-f]{6})$/i.exec(text);
  if (hex) return [0, 2, 4].map((i) => parseInt(hex[1].slice(i, i + 2), 16)).concat(1);
  const rgba = /^rgba?\(([^)]+)\)$/.exec(text);
  assert.ok(rgba, `cannot read the colour ${text}`);
  const [r, g, b, a = 1] = rgba[1].split(',').map(Number);
  return [r, g, b, a];
}

function over(top, bottom) {
  const a = top[3];
  return [0, 1, 2].map((i) => top[i] * a + bottom[i] * (1 - a)).concat(1);
}

function luminance([r, g, b]) {
  const channel = (value) => {
    const c = value / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

function contrast(foreground, background) {
  const [hi, lo] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  return (hi + 0.05) / (lo + 0.05);
}

const lightTokens = parseTokens(css.match(/:root\s*\{([^}]*)\}/)[1]);
const darkTokens = { ...lightTokens, ...parseTokens(css.match(/@media \(prefers-color-scheme: dark\)\s*\{\s*:root\s*\{([^}]*)\}/)[1]) };
const calloutColors = [...css.matchAll(/--cc:\s*(#[0-9a-f]{6});/gi)].map((match) => match[1]);

describe('colour contrast of the tokens', () => {
  it('reads the tokens (guards the scan itself)', () => {
    assert.equal(lightTokens['--accent-text'], '#714fcd');
    assert.equal(darkTokens['--accent-text'].length, 7);
    assert.notEqual(lightTokens['--accent-text'], darkTokens['--accent-text']);
    assert.ok(calloutColors.length >= 7, `${calloutColors.length} callout colours`);
  });

  for (const [theme, tokens] of [['light', lightTokens], ['dark', darkTokens]]) {
    const color = (name) => parseColor(tokens[name]);
    const bg = color('--bg');
    const side = color('--bg-side');

    it(`${theme}: link text (--accent-text) is at least 4.5:1 on every surface a link sits on`, () => {
      const surfaces = {
        '--bg': bg,
        '--bg-side': side,
        '--code-bg': color('--code-bg'),
        '--warn-bg over --bg': over(color('--warn-bg'), bg),
      };
      for (const callout of calloutColors) surfaces[`callout ${callout} over --bg`] = over([...parseColor(callout).slice(0, 3), 0.1], bg);
      for (const [name, surface] of Object.entries(surfaces)) {
        const ratio = contrast(color('--accent-text'), surface);
        assert.ok(ratio >= 4.5, `${tokens['--accent-text']} on ${name}: ${ratio.toFixed(2)}`);
      }
    });

    it(`${theme}: --accent is only for fills, borders and outlines (3:1 against the page is enough)`, () => {
      assert.ok(contrast(color('--accent'), bg) >= 3);
      assert.ok(contrast(color('--accent'), side) >= 3);
    });

    it(`${theme}: text on the danger fill (--on-danger on --danger) is at least 4.5:1`, () => {
      assert.ok(contrast(color('--on-danger'), color('--danger')) >= 4.5, `${tokens['--on-danger']} on ${tokens['--danger']}`);
    });

    it(`${theme}: the load markers are at least 3:1 on the page and the side panel; all but the pale on-demand dot also on a hovered or current tree row`, () => {
      const tinted = {
        'hover row': over(color('--hover'), side),
        'current row': over(color('--accent-soft'), side),
      };
      for (const name of ['--lm-every', '--lm-description', '--lm-project', '--lm-conditional', '--lm-ondemand', '--lm-user']) {
        for (const [label, surface] of [['--bg', bg], ['--bg-side', side]]) {
          const ratio = contrast(color(name), surface);
          assert.ok(ratio >= 3, `${name} ${tokens[name]} on ${label}: ${ratio.toFixed(2)}`);
        }
        if (name === '--lm-ondemand') continue;
        for (const [label, surface] of Object.entries(tinted)) {
          const ratio = contrast(color(name), surface);
          assert.ok(ratio >= 3, `${name} ${tokens[name]} on a ${label}: ${ratio.toFixed(2)}`);
        }
      }
    });
  }

  for (const [theme, tokens] of [['light', lightTokens], ['dark', darkTokens]]) {
    const color = (name) => parseColor(tokens[name]);
    const bg = color('--bg');
    const side = color('--bg-side');

    it(`${theme}: the quiet grey (--muted) is at least 4.5:1 on the page and the side panel`, () => {
      for (const [label, surface] of [['--bg', bg], ['--bg-side', side]]) {
        const ratio = contrast(color('--muted'), surface);
        assert.ok(ratio >= 4.5, `${tokens['--muted']} on ${label}: ${ratio.toFixed(2)}`);
      }
    });

    it(`${theme}: on a hovered or the open tree row (a darker tint) the quiet figures use the text colour, which is at least 4.5:1 there`, () => {
      for (const tint of ['--hover', '--accent-soft']) {
        const row = over(color(tint), side);
        const ratio = contrast(color('--text'), row);
        assert.ok(ratio >= 4.5, `--text on ${tint} row: ${ratio.toFixed(2)}`);
      }
      for (const selector of ['.tree-row:hover .tokens', '.tree-row.current .tokens', '.tree-row:hover .tokens-total', '.tree-row.current .tokens-total']) {
        assert.ok(css.includes(selector), selector);
      }
      assert.match(css, /\.tree-row\.current \.tokens-total\s*\{\s*color: var\(--text\)/);
    });

    it(`${theme}: on the tint of the place a route moved to (--hit-bg) links, the danger colour and text are at least 4.5:1`, () => {
      const tint = over(color('--hit-bg'), bg);
      for (const name of ['--accent-text', '--danger', '--text', '--muted']) {
        const ratio = contrast(color(name), tint);
        assert.ok(ratio >= 4.5, `${name} ${tokens[name]} on --hit-bg: ${ratio.toFixed(2)}`);
      }
      assert.ok(contrast(color('--accent'), bg) >= 3, 'the frame (--accent) is 3:1 against the page');
    });
  }

  it('links, and the danger badge, use those tokens', () => {
    assert.match(ruleBody('a') ?? '', /color: var\(--accent-text\)/);
    assert.match(ruleBody('.link-resolved') ?? '', /color: var\(--accent-text\)/);
    assert.match(ruleBody('.badge-broken') ?? '', /color: var\(--on-danger\)/);
    assert.ok(!/\.badge-broken[^}]*#[0-9a-f]{6}/i.test(css), 'the badge text colour is a token, not a literal');
    // No text takes the fill colour: --accent appears only as an outline, a border or a fill.
    for (const match of css.matchAll(/([^{}]+)\{([^}]*)\}/g)) {
      for (const declaration of match[2].split(';')) {
        if (/^\s*color:\s*var\(--accent\)\s*$/.test(declaration)) assert.fail(`${match[1].trim()} sets text to --accent`);
      }
    }
  });
});

// ---------------------------------------------------------------------------------------------
// Load markers: colour and shape
// ---------------------------------------------------------------------------------------------

const MODES = ['EverySession', 'DescriptionEverySession', 'ProjectSession', 'Conditional', 'OnDemand', 'UserInvoked', 'Inactive'];

describe('load markers', () => {
  it('every load mode has a colour in both themes, and the light ones are not the dark ones', () => {
    for (const name of ['--lm-every', '--lm-description', '--lm-project', '--lm-conditional', '--lm-ondemand', '--lm-user']) {
      assert.match(lightTokens[name] ?? '', /^#[0-9a-f]{6}$/i, `light ${name}`);
      assert.match(darkTokens[name] ?? '', /^#[0-9a-f]{6}$/i, `dark ${name}`);
    }
    for (const name of ['--lm-every', '--lm-description', '--lm-project', '--lm-conditional', '--lm-user']) {
      assert.notEqual(lightTokens[name], darkTokens[name], `${name} must be tuned per theme`);
    }
  });

  it('every mode picks its colour through --lm', () => {
    const expected = {
      EverySession: '--lm-every', DescriptionEverySession: '--lm-description', ProjectSession: '--lm-project',
      Conditional: '--lm-conditional', OnDemand: '--lm-ondemand', UserInvoked: '--lm-user', Inactive: '--lm-ondemand',
    };
    for (const mode of MODES) {
      const body = css.match(new RegExp(`(?:^|[}\\s,])\\.lm-${mode}\\s*(?:,[^{]*)?\\{([^}]*)\\}`))?.[1] ?? '';
      assert.match(body, new RegExp(`--lm:\\s*var\\(${expected[mode]}\\)`), mode);
    }
  });

  it('every mode but EverySession (the plain filled circle) has a shape of its own, and no two shapes are alike', () => {
    const signatures = { EverySession: ruleBody('.dot') ?? '' };
    for (const mode of MODES.slice(1)) {
      const body = ruleBody(`.dot.lm-${mode}`);
      assert.ok(body, `.dot.lm-${mode} has no rule`);
      signatures[mode] = body;
    }
    const shapeOf = (body) => (body.match(/(?:clip-path|border-radius|background-clip|background):[^;]+;/g) ?? []).sort().join(' ');
    const shapes = Object.entries(signatures).map(([mode, body]) => [mode, mode === 'EverySession' ? 'border-radius: 50%;' : shapeOf(body)]);
    assert.equal(new Set(shapes.map(([, shape]) => shape)).size, MODES.length, JSON.stringify(shapes));
    // The shapes the spec names: half-filled, square, diamond, triangle, empty circle, small circle.
    assert.match(signatures.DescriptionEverySession, /linear-gradient\(90deg, var\(--lm\) 50%, transparent 50%\)/);
    assert.match(signatures.ProjectSession, /border-radius: 2px/);
    assert.match(signatures.Conditional, /clip-path: polygon\(50% 0, 100% 50%, 50% 100%, 0 50%\)/);
    assert.match(signatures.UserInvoked, /clip-path: polygon\(8% 0, 100% 50%, 8% 100%\)/);
    assert.match(signatures.Inactive, /background: transparent/);
    assert.match(signatures.OnDemand, /background-clip: padding-box/);
  });

  it('a marker is about 10px and stays visible in forced-colors mode', () => {
    assert.match(ruleBody('.dot') ?? '', /width: 10px; height: 10px/);
    assert.match(css, /@media \(forced-colors: active\)\s*\{\s*\.dot\s*\{\s*forced-color-adjust:\s*none;/);
  });

  it('every marker in the page comes from loadDot(), so the tree, the legend, the panel and the tables agree', () => {
    const app = code('app.js');
    assert.equal([...app.matchAll(/`dot lm-\$\{/g)].length, 1, 'markers are built in one place');
    assert.match(app, /function loadDot\(/);
    assert.ok(!/'dot'/.test(app) && !/"dot"/.test(app), 'no other code builds a .dot');
    for (const place of ['buildTreeItem', 'legendList', 'renderInfo', 'layerRow', 'budgetBand', 'bandFiles', 'loadLine']) {
      const body = app.slice(app.indexOf(`function ${place}(`));
      assert.match(body.slice(0, body.indexOf('\nfunction ', 10) > 0 ? body.indexOf('\nfunction ', 10) : undefined), /loadDot\(/, place);
    }
  });

  it('keeps the accessible name: the marker is an image with a label', () => {
    const app = code('app.js');
    const body = app.slice(app.indexOf('function loadDot('), app.indexOf('function tokensText('));
    assert.match(body, /setAttribute\('role', 'img'\)/);
    assert.match(body, /setAttribute\('aria-label'/);
    assert.match(body, /\.title = /);
  });
});


// ---------------------------------------------------------------------------------------------
// F2: from a problem to its line
// ---------------------------------------------------------------------------------------------

/** The source of the function `name` in app.js: from its declaration to the next one at the left margin. */
function functionSource(name) {
  const app = code('app.js');
  const start = app.indexOf(`function ${name}(`);
  assert.ok(start >= 0, `no function ${name}`);
  const next = app.indexOf('\nfunction ', start + 10);
  return app.slice(start, next > 0 ? next : undefined);
}

describe('a ?l=<line> route moves #content and nothing else', () => {
  it('scrollToLine finds the block through blockIndexForLine and scrolls only #content (scrollWithin), never the document', () => {
    const body = functionSource('scrollToLine');
    assert.match(body, /querySelectorAll\('\[data-line\]'\)/);
    assert.match(body, /blockIndexForLine\(/);
    assert.match(body, /scrollWithin\(refs\.content, block,/);
    assert.ok(!/window\.scroll|scrollTo\(/.test(body), 'no document scrolling');
    assert.match(body, /preventScroll:\s*true/, 'focusing the block must not scroll');
  });

  it('the route reaches it on a fresh load (renderContent) and on a change within the open file (onRoute), both through the arrival target', () => {
    assert.match(functionSource('renderContent'), /if \(target\) moveTo\(target\)/);
    assert.match(functionSource('moveTo'), /case 'line':\s*scrollToLine\(target\.line\)/);
    assert.match(functionSource('onRoute'), /moveTo\(target\)/);
    assert.match(functionSource('showFile'), /const arrival = keepScroll \? null : /, 'a live refresh must not scroll or flash again');
    assert.match(functionSource('showFile'), /renderContent\(\{ keepScroll, target: arrival \}\)/);
  });

  it('the page asks markdown-it for the file lines: the render gets bodyStartLine', () => {
    assert.match(code('app.js'), /markdown\.render\(file\.body \?\? '', \{ bodyStartLine: file\.bodyStartLine \?\? 1 \}\)/);
    assert.match(code('core.js'), /md\.use\(lineNumbers\)/);
  });

  it('a fence and a callout keep their line when the DOM is rewritten', () => {
    assert.match(functionSource('liftFenceLines'), /pre > code\[data-line\]/);
    assert.match(functionSource('enhance'), /liftFenceLines\(root\)/);
    assert.match(functionSource('convertCallouts'), /callout\.dataset\.line = quote\.dataset\.line/);
  });

  it('the mark is a still frame first and fades only when motion is allowed, and app.js removes it after HIT_MS', () => {
    const base = ruleBody('.hit-flash') ?? '';
    assert.match(base, /outline: 2px solid var\(--accent\)/);
    assert.match(base, /background-color: var\(--hit-bg\)/);
    assert.ok(!/animation/.test(base), 'the base rule is the still frame');
    const motion = css.match(/@media \(prefers-reduced-motion: no-preference\)\s*\{\s*\.hit-flash\s*\{([^}]*)\}/);
    assert.ok(motion, 'the fade is inside a no-preference query');
    assert.match(motion[1], /animation: hit-flash 1\.5s/);
    assert.match(css, /@keyframes hit-flash/);
    assert.match(css, /@media \(prefers-reduced-motion: reduce\)\s*\{[^}]*animation: none !important/);
    assert.match(code('app.js'), /const HIT_MS = 1500;/);
    assert.match(functionSource('flash'), /classList\.add\('hit-flash'\)[\s\S]*setTimeout\(\(\) => node\.classList\.remove\('hit-flash'\), HIT_MS\)/);
  });

  it('a link to the address the page already has re-applies the route (it fires no hashchange), for the page, the panel and the top bar but not the tree', () => {
    const app = code('app.js');
    assert.match(app, /closest\('a\[href\^="#\/"\]'\)/);
    assert.match(app, /refs\.content\.contains\(link\) \|\| refs\.info\.contains\(link\) \|\| link\.closest\('\.topbar'\)/);
    assert.match(app, /link\.getAttribute\('href'\) !== window\.location\.hash/);
  });
});

describe('the side panel (F2)', () => {
  it('lists who links here first, then what the file links to', () => {
    assert.match(functionSource('renderInfo'), /sections\.push\(backlinkList\(file\.backlinks \?\? \[\]\), outgoingLinks\(file\)\)/);
  });

  it('a backlink goes to its source at its line and shows the excerpt as text under it, only when there is one', () => {
    const body = functionSource('backlinkList');
    assert.match(body, /pathLink\(backlink\.source, \{ line: backlink\.line/);
    assert.match(body, /if \(backlink\.excerpt\)/);
    assert.match(body, /const words = readableWikilinks\(backlink\.excerpt\);\s*const excerpt = el\('div', 'link-excerpt', words\);\s*excerpt\.title = words;/, 'a wikilink in it is its words; textContent through el(): the excerpt is raw Markdown and may hold "<"');
    assert.ok(!/innerHTML/.test(body));
  });

  it('outgoing links merge by target, one row each, and every line number is a link to that line of this file', () => {
    assert.match(functionSource('outgoingLinks'), /mergeLinks\(group\)/);
    const row = functionSource('outgoingRow');
    assert.match(row, /pathLink\(link\.target, \{ heading: link\.heading, skillFirst: true \}\)/);
    assert.match(row, /lineNote\(lines, \(line\) => fileHash\(sid\(\), path, null, line\)\)/);
    assert.match(row, /lines\.length > 0/, 'a row with no usable line shows no note');
  });

  it('the excerpt is one pale line that is cut with an ellipsis, not wrapped', () => {
    const body = ruleBody('.link-excerpt') ?? '';
    assert.match(body, /color: var\(--muted\)/);
    assert.match(body, /white-space: nowrap/);
    assert.match(body, /text-overflow: ellipsis/);
    assert.match(body, /overflow: hidden/);
  });

  it('a folder link is shown as "folder": the group, the tooltip and the status class exist', () => {
    assert.match(code('app.js'), /STATUS_ORDER = \['Broken', 'Pending', 'Resolved', 'Folder', 'NonMarkdown', 'External'\]/);
    assert.match(functionSource('makeLink'), /displayStatus\(link, state\.folders\)/);
    assert.match(css, /\.link-nonmarkdown,\s*\.link-folder\s*\{/);
  });
});

// ---------------------------------------------------------------------------------------------
// F2: tablet
// ---------------------------------------------------------------------------------------------

describe('touch and tablet (F2)', () => {
  const coarse = css.match(/@media \(pointer: coarse\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';

  it('--touch is 40px and the touch query uses it for the tree rows, the buttons and the language buttons', () => {
    assert.match(lightTokens['--touch'] ?? '', /^40px$/);
    assert.ok(coarse.length > 0, 'no @media (pointer: coarse) block');
    const rule = (selector) => new RegExp(`${selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}[^{]*\\{([^}]*)\\}`).exec(coarse)?.[1] ?? '';
    assert.match(coarse.match(/([^{}]*)\{\s*min-height: var\(--touch\);\s*\}/)?.[1] ?? '', /\.tree-row/);
    assert.match(rule('.icon-btn'), /width: var\(--touch\);\s*height: var\(--touch\)/);
    assert.match(rule('.lang-btn'), /min-height: var\(--touch\)/);
    for (const name of ['.band-label', '.issue-chip', '.more-btn', '.button', '.crumb-dir', '.path-link']) {
      assert.ok(coarse.includes(name), `${name} is not made touch-sized`);
    }
  });

  it('the tree is a drawer from 900px down, and the narrower rules (700px, 480px) come after it', () => {
    const drawer = css.match(/@media \(max-width: 900px\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    assert.match(drawer, /\.sidebar\s*\{[^}]*position: fixed/);
    assert.match(drawer, /\.layout\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\)/);
    assert.match(drawer, /\.tree-toggle\s*\{[^}]*display: inline-flex/);
    assert.ok(css.indexOf('@media (max-width: 900px)') < css.indexOf('@media (max-width: 700px)'));
    assert.ok(!/@media \(max-width: 700px\)\s*\{[^@]*\.sidebar\s*\{/.test(css), 'the drawer is no longer a 700px rule');
  });

  it('under 700px the overview link is its icon, and it keeps its name', () => {
    assert.match(html, /<a id="overview-link"[^>]*data-i18n-aria-label="nav\.overview"/);
    assert.match(html, /class="top-link-icon"[^>]*aria-hidden="true"/);
    const narrow = css.match(/@media \(max-width: 700px\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    assert.match(narrow, /\.top-link-text\s*\{[^}]*display: none|\.top-link-text\s*\{\s*display: none/);
    assert.match(narrow, /\.top-link-icon\s*\{[^}]*display: block/);
    assert.ok(!/\.top-link\s*\{[^}]*display: none/.test(narrow), 'the link itself must stay');
    assert.match(ruleBody('.top-link-icon') ?? '', /display: none/, 'the icon is hidden until the narrow rule shows it');
    assert.match(html, /<a class="brand"[^>]*aria-label="pusula"/, 'the brand link has a name when its text is hidden (480px)');
  });

  it('a file shows its load mode and tokens under its path, in words, from loadLine()', () => {
    const body = functionSource('loadLine');
    assert.match(body, /loadDot\(file\.loadMode, \{ decorative: true \}\)/);
    assert.match(body, /loadedTokens\(file\.loadMode, file\.tokens\)/);
    assert.match(body, /t\('file\.loadLine'/);
    assert.match(functionSource('filePage'), /head\.append\(breadcrumb\(file\.path\), \.\.\.\(isNotes\(\) \? tagList\(file\.tags\) : \[loadLine\(file\)\]\)\)/, 'a note has its tags under its name, not how it loads');
  });

  it('the legend is one tap away in every view: a <details> at the bottom of the tree, built from the same list as the overview', () => {
    assert.match(functionSource('treeFooter'), /el\('details', 'legend-pop'\)/);
    assert.match(functionSource('treeFooter'), /legendList\(\)/);
    assert.match(functionSource('legendBlock'), /legendList\(\)/);
    assert.match(functionSource('renderTree'), /buildTreeList\(state\.tree\.nodes\), \.\.\.\(isNotes\(\) \? \[\] : \[treeFooter\(\)\]\)/, 'a note has no load markers to explain');
    assert.match(ruleBody('.tree-foot') ?? '', /position: sticky/);
  });

  it('the narrow tables drop columns instead of scrolling: the classes exist on both sides (500px: room for a classic scrollbar)', () => {
    const narrow = css.match(/@media \(max-width: 500px\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    for (const name of ['.col-files', '.col-total', '.share .bar']) assert.ok(narrow.includes(name), name);
    assert.match(narrow, /table\.data\.fixed\s*\{\s*min-width: 0/);
  });
});

// ---------------------------------------------------------------------------------------------
// F2: clarify and polish
// ---------------------------------------------------------------------------------------------

describe('the tree writes tokens as every-session first, total quiet (F2)', () => {
  it('both rows (file and folder) use treeTokens, which reads tokenParts and says it in words', () => {
    assert.equal([...code('app.js').matchAll(/(?<!function )treeTokens\(node\)/g)].length, 2);
    const body = functionSource('treeTokens');
    assert.match(body, /tokenParts\(node\.everySessionTokens, node\.tokens\)/);
    assert.match(body, /setAttribute\('aria-hidden', 'true'\)/);
    assert.match(body, /el\('span', 'sr-only', words\)/);
  });

  it('the lead is in text colour and the total is small and muted', () => {
    assert.match(ruleBody('.tokens-lead') ?? '', /color: var\(--text\)/);
    assert.match(ruleBody('.tokens-total') ?? '', /color: var\(--muted\)/);
    assert.match(ruleBody('.tokens-total') ?? '', /font-size: 11px/);
  });

  it('a sentence under the band says what is loaded', () => {
    assert.match(functionSource('budgetSection'), /el\('p', 'budget-note muted', t\('budget\.explain'\)\)/);
  });

  it('a frontmatter error of a file is one notice (frontmatterNotice), and of the overview a row with the line and the parser\'s words', () => {
    assert.match(functionSource('filePage'), /frontmatterProblem\(file\)/);
    assert.match(functionSource('filePage'), /frontmatterNotice\(problem\)/);
    assert.match(functionSource('issueRows'), /frontmatter: \(item\) => \{[\s\S]*positiveLine\(item\.line\)[\s\S]*pathLink\(item\.path, \{ line \}\)[\s\S]*el\('div', 'muted issue-detail', item\.error\)/);
    assert.ok(!/file\.frontmatterError/.test(code('app.js')), 'the old one-string wording is gone');
  });
});

describe('polish: one link language, one stop per budget piece, nothing that looks clickable and is not (F2)', () => {
  it('a plain link is accent text with no underline at rest, and underlines under the pointer through a zero-specificity rule', () => {
    assert.match(ruleBody('a') ?? '', /text-decoration: none/);
    assert.match(css, /:where\(a:hover\)\s*\{\s*text-decoration: underline/);
  });

  it('the second link style is gone: no .file-ref anywhere, backlinks are path links', () => {
    assert.ok(!/file-ref|fileLink/.test(code('app.js')));
    assert.ok(!/file-ref/.test(css));
  });

  it('a control that is also a link settles its own decoration (class rule outweighs the hover rule)', () => {
    for (const selector of ['.tree-row', '.brand', '.top-link', '.issue-chip', '.band-label', '.path-link']) {
      assert.match(ruleBody(selector) ?? '', /text-decoration: none/, selector);
    }
  });

  it('a budget piece has one stop: the label is a link or a button, the band is hidden from the accessibility tree, the pieces are plain spans that forward the click', () => {
    const band = functionSource('budgetBand');
    assert.match(band, /band\.setAttribute\('aria-hidden', 'true'\)/);
    assert.match(band, /labels\.setAttribute\('aria-label', t\('budget\.label'\)\)/);
    const segment = functionSource('budgetSegment');
    assert.match(segment, /el\('span', `band-seg lm-\$\{part\.loadMode\}`\)/);
    assert.match(segment, /addEventListener\('click', \(\) => label\.click\(\)\)/);
    assert.ok(!/tabIndex|tabindex/i.test(segment));
    assert.match(functionSource('budgetLabel'), /el\('a', 'band-label'\)[\s\S]*el\('button', 'band-label'\)/);
  });

  it('the group heading marks in the side panel are labels, not link-coloured text', () => {
    assert.match(ruleBody('.status-mark.link-resolved') ?? '', /color: var\(--text\)/);
    assert.match(ruleBody('.status-mark') ?? '', /cursor: default/);
  });

  it('the folders of a file path are real controls that show themselves in the tree; the file itself is the current page', () => {
    const body = functionSource('breadcrumb');
    assert.match(body, /el\('button', 'crumb crumb-dir', part\)/);
    assert.match(body, /addEventListener\('click', \(\) => revealDir\(dir\)\)/);
    assert.match(body, /setAttribute\('aria-current', 'page'\)/);
    const reveal = functionSource('revealDir');
    assert.match(reveal, /scrollWithin\(refs\.sidebar, row,/);
    assert.match(reveal, /getComputedStyle\(refs\.sidebar\)\.position === 'fixed'/);
  });

  it('focus: a navigation that removed the focused element puts the focus on the content; a skip button leads past the tree', () => {
    assert.match(functionSource('settleFocus'), /if \(previous && previous !== document\.body && document\.activeElement === document\.body\) \{\s*refs\.content\.focus\(\{ preventScroll: true \}\);/);
    assert.match(functionSource('showFile'), /settleFocus\(active\)/);
    assert.match(functionSource('showOverview'), /settleFocus\(active\)/);
    assert.match(html, /<button id="skip-link"[^>]*data-i18n="nav\.skip"/);
    assert.ok(html.indexOf('id="skip-link"') < html.indexOf('class="topbar"'), 'the first stop of the page');
    assert.match(code('app.js'), /refs\.skip\.addEventListener\('click', \(\) => refs\.content\.focus\(\{ preventScroll: true \}\)\)/);
  });

  it('the new pieces exist on both sides', () => {
    for (const name of ['file-head', 'load-line', 'crumb-dir', 'tree-foot', 'legend-pop', 'hit-flash', 'line-note', 'line-link', 'link-excerpt', 'tokens-lead', 'tokens-total']) {
      assert.match(code('app.js'), new RegExp(`['\`" ]${name}['\`" ]`), `${name} is not used in app.js`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
    for (const name of ['top-link-icon', 'top-link-text', 'skip-link']) {
      assert.match(html, new RegExp(`class="[^"]*\\b${name}\\b`), `${name} is not in index.html`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
  });
});

// ---------------------------------------------------------------------------------------------
// P1: a project is named by its name, not by the slug of its folder
// ---------------------------------------------------------------------------------------------

describe('project folders are shown by their project (P1)', () => {
  /** The declarations of the rule whose selector starts a line and is exactly `selector`: not a longer selector that ends the same way. */
  const ownRule = (selector) => {
    const pattern = selector.split(/\s*,\s*/).map((part) => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join(',\\s*');
    const match = new RegExp(`(?:^|\\n)${pattern}\\s*\\{([^}]*)\\}`).exec(css);
    return match ? match[1].replace(/\s+/g, ' ').trim() : null;
  };
  /** The `flex` shorthand of a rule as `[grow, shrink, basis]`. */
  const flexOf = (selector) => {
    const match = /flex: (\d+) (\d+) (\w+);/.exec(ownRule(selector) ?? '');
    assert.ok(match, `${selector} has no flex: <grow> <shrink> <basis>`);
    return [Number(match[1]), Number(match[2]), match[3]];
  };
  const NAME_ONE_LINE = '.path-link.is-project > .proj-name, .tree-project > .proj-name';
  const PLACE_ONE_LINE = '.path-link.is-project > .proj-context, .tree-project > .proj-context';

  it('the labels are made again whenever the tree loads, from its project folders and this machine\'s home slug', () => {
    assert.match(functionSource('loadTree'), /state\.labels = isNotes\(\) \? new Map\(\) : projectLabels\(projectFolders\(tree\.nodes\), homeSlug\(tree\.root\)\);/, 'projects are a Claude configuration\'s');
    assert.match(code('app.js'), /labels: new Map\(\)/);
    assert.ok(functionSource('loadTree').indexOf('state.labels =') < functionSource('loadTree').indexOf('renderTree()'), 'the tree is drawn with the new labels');
  });

  it('every path link asks displayPath: a memory file reads "project · file", every other path is drawn as it was', () => {
    const body = functionSource('pathLink');
    assert.match(body, /displayPath\(path, state\.labels\)/);
    assert.match(body, /shown\.kind === 'project'/);
    assert.match(body, /link\.classList\.add\('is-project'\)/);
    assert.match(body, /projectName\(shown\.project\), el\('span', 'path-tail', ' · '\), \.\.\.fileName\(shown\.file\)/);
    assert.match(body, /skillFile\(path\)/, 'the skill-first form is still there');
    for (const part of ['path-head', 'path-parent', 'path-name']) assert.match(body, new RegExp(`el\\('span', '${part}'`), part);
    assert.match(body, /link\.title = heading \? `\$\{path\}#\$\{heading\}` : path/, 'the whole path, slug and all, is the tooltip');
    assert.match(body, /if \(shown\.kind === 'project'\) link\.setAttribute\('aria-label', link\.textContent\)/, 'a cut file name is two flex items, which would be read with a space inside it');
    assert.ok(!/splitPath/.test(code('app.js')), 'the split of a path is displayPath\'s now');
  });

  it('the name is plain text in its own span, the place only when the label asks for it', () => {
    const body = functionSource('projectName');
    assert.match(body, /el\('span', 'proj-name'\)/);
    assert.match(body, /el\('span', 'proj-text', label\.name\)/, 'textContent, never markup: a folder name is not ours');
    assert.match(body, /context === 'shared' && !label\.shared/, 'a name no other project has needs no place in a list');
    assert.match(body, /label\.shared \? 'proj-context is-shared' : 'proj-context'/);
    assert.ok(!/innerHTML/.test(body));
    const file = functionSource('fileName');
    assert.match(file, /fileEnds\(file\)/);
    assert.match(file, /el\('span', 'file-start', start\)/);
    assert.match(file, /el\('span', 'file-end', end\)/);
    assert.match(file, /start \?/, 'a name that is all end has no start to cut');
  });

  it('the tree draws a project folder (and only that) by its name; the slug stays in the tooltip', () => {
    assert.match(functionSource('projectLabelOf'), /path === `projects\/\$\{node\.name\}` \? state\.labels\.get\(node\.name\)/);
    const name = functionSource('treeName');
    assert.match(name, /projectLabelOf\(node, path\)/);
    assert.match(name, /el\('span', 'tree-name tree-project'\)/);
    assert.match(name, /projectName\(label\)/, 'the default policy: the place only for a shared name');
    assert.match(name, /el\('span', 'tree-name', node\.name\)/, 'any other folder is named as before');
    const item = functionSource('buildTreeItem');
    assert.match(item, /button\.append\(chevron, treeName\(node, path\), \.\.\.\(isNotes\(\) \? \[\] : \[treeTokens\(node\)\]\)\)/);
    assert.match(item, /button\.title = `\$\{whole\}\$\{path\}\\n\$\{t\('tree\.files'/, 'the whole name, then the path with its slug, are the folder row\'s tooltip');
    assert.match(item, /label\.name\}\$\{label\.context \? ` \\u00b7 \$\{label\.context\}` : ''\}\\n/, 'the first line is the project\'s whole name and its place');
  });

  it('the breadcrumb names the project folder of the path, place always (it has the room)', () => {
    const body = functionSource('breadcrumb');
    assert.match(body, /index === 1 && parts\[0\] === 'projects' \? state\.labels\.get\(part\)/);
    assert.match(body, /projectName\(label, \{ context: 'always' \}\)/);
    assert.match(body, /el\('span', 'crumb-label'\)/);
    assert.match(body, /el\('button', 'crumb crumb-dir', part\)/, 'every other folder is still its own name');
    assert.match(body, /t\('file\.revealDir', \{ path: dir \}\)/, 'and the tooltip is still the real folder');
  });

  it('the memory table uses the same labels: name and place, wrapped, the slug as tooltip', () => {
    const body = functionSource('projectLink');
    assert.match(body, /el\('a', 'path-link is-wrap'\)/);
    assert.match(body, /projectName\(label, \{ context: 'always' \}\)/);
    assert.match(body, /link\.title = slug/);
    assert.match(functionSource('memorySection'), /cell\(projectLink\(entry\.path, entry\.project\)\)/);
    assert.ok(!/splitSlug|slugLink/.test(code('app.js')), 'the old slug split is gone');
  });

  it('the name does not shrink while there is anything else to give way: it is clamped to the line, and cut at its END with an ellipsis, like every name in the tree', () => {
    const rule = ownRule(NAME_ONE_LINE) ?? '';
    assert.match(rule, /flex: 0 0 auto/, 'no shrinking: the others give way first');
    assert.match(rule, /max-width: 100%/);
    assert.match(rule, /min-width: 0/);
    assert.match(rule, /overflow: hidden/);
    assert.match(rule, /text-overflow: ellipsis/);
    assert.ok(!/direction/.test(rule) && !/text-align/.test(rule), 'no right-to-left trick: the front of the name is never the part that is cut');
    assert.ok(!/direction:\s*rtl/.test(css), 'no right-to-left box anywhere in the page');
    assert.match(ownRule('.proj-text') ?? '', /^unicode-bidi: isolate;?$/, 'a name in another script keeps its own order, and nothing else is said of it');
    assert.match(ownRule('.proj-name') ?? '', /font-weight: 600/);
  });

  it('every name of the tree is cut at its end: file, folder and project, the whole name being the row\'s tooltip', () => {
    const name = ownRule('.tree-name') ?? '';
    assert.match(name, /overflow: hidden/);
    assert.match(name, /text-overflow: ellipsis/);
    assert.match(name, /white-space: nowrap/);
    assert.match(functionSource('buildTreeItem'), /link\.title = `\$\{node\.path\}/, 'a file row has its whole path in its tooltip');
  });

  it('what gives way, in order: the place (the huge shrink factor), then the middle of the file name, the name never', () => {
    const [, nameShrink] = flexOf(NAME_ONE_LINE);
    const [, placeShrink] = flexOf(PLACE_ONE_LINE);
    const [, startShrink] = flexOf('.file-start');
    const [, headShrink] = flexOf('.path-head');
    assert.equal(nameShrink, 0);
    assert.ok(startShrink >= 1 && placeShrink > startShrink * 1000, `place ${placeShrink} against file ${startShrink}`);
    assert.equal(placeShrink, headShrink, 'the same factor the upper folders of an ordinary path have');
    assert.match(ownRule(PLACE_ONE_LINE) ?? '', /min-width: 0/);
    assert.match(ownRule(PLACE_ONE_LINE) ?? '', /text-overflow: ellipsis/);
    assert.match(ownRule('.file-start') ?? '', /min-width: 0/);
    assert.match(ownRule('.file-start') ?? '', /text-overflow: ellipsis/);
    assert.match(ownRule('.file-end') ?? '', /flex: none/, 'the end of the file name is never cut');
  });

  it('the place of a name that more than one project has keeps a few characters: that is all that tells them apart', () => {
    const rule = ownRule('.path-link.is-project > .proj-context.is-shared, .tree-project > .proj-context.is-shared') ?? '';
    assert.match(rule, /min-width: 7ch/);
    assert.ok(css.indexOf('.proj-context.is-shared') > css.indexOf(PLACE_ONE_LINE), 'after the rule it outweighs, with the higher specificity as well');
  });

  it('the place is quiet and smaller; the name and the file are not', () => {
    const place = ownRule('.proj-context') ?? '';
    assert.match(place, /color: var\(--muted\)/);
    assert.match(place, /font-size: 12px/);
    assert.ok(!/font-weight: 6/.test(ownRule('.file-start') ?? '') && !/font-weight: 6/.test(ownRule('.file-end') ?? ''));
  });

  it('in a table the label wraps instead of being cut: the wrapping variant lets name and place shrink, and breaks anywhere', () => {
    const link = ownRule('.path-link.is-wrap') ?? '';
    assert.match(link, /flex-wrap: wrap/);
    assert.match(link, /white-space: normal/);
    const parts = ownRule('.path-link.is-wrap > .proj-name, .path-link.is-wrap > .proj-context') ?? '';
    assert.match(parts, /flex: 0 1 auto/);
    assert.match(parts, /overflow-wrap: anywhere/);
  });

  it('a project folder crumb wraps too, and its name is in text colour (the path around it is quiet)', () => {
    const crumb = ownRule('.crumb-dir') ?? '';
    assert.match(crumb, /max-width: 100%/);
    assert.match(crumb, /overflow-wrap: anywhere/);
    assert.match(ownRule('.breadcrumb .proj-name') ?? '', /color: var\(--text\)/);
  });

  it('on a hovered tree row the quiet place takes the text colour (the muted grey is under 4.5:1 on that tint, like the figures)', () => {
    assert.match(ownRule('.tree-row:hover .proj-context') ?? '', /color: var\(--text\)/);
    assert.match(ownRule('.tree-name.tree-project') ?? '', /display: flex/);
  });

  it('under touch a project link is centred in its 40px like every other path link', () => {
    const coarse = css.match(/@media \(pointer: coarse\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    const centred = coarse.match(/([^{}]*)\{\s*align-items: center;\s*\}/)?.[1] ?? '';
    for (const name of ['.path-link', '.path-link.is-project', '.path-link.is-wrap']) assert.ok(centred.includes(name), `${name} is not centred under touch`);
    assert.match(ownRule('.path-link.is-project') ?? '', /align-items: baseline/, 'baseline-aligned elsewhere: the place is smaller than the name');
  });

  it('the new pieces exist on both sides', () => {
    for (const name of ['proj-name', 'proj-text', 'proj-context', 'is-shared', 'is-project', 'is-wrap', 'tree-project', 'file-start', 'file-end', 'crumb-label']) {
      assert.match(code('app.js'), new RegExp(`['\`" ]${name}['\`" ]`), `${name} is not used in app.js`);
    }
    for (const name of ['proj-name', 'proj-text', 'proj-context', 'is-shared', 'is-project', 'is-wrap', 'tree-project', 'file-start', 'file-end']) {
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
  });
});

// ---------------------------------------------------------------------------------------------
// R4-1: the reading place of a history entry (Back, Forward, reload)
// ---------------------------------------------------------------------------------------------

describe('the reading place is kept in the history entry (R4-1)', () => {
  it('the browser\'s own scroll restoration is off: #content scrolls, not the document', () => {
    assert.match(functionSource('boot'), /history\.scrollRestoration = 'manual'/);
  });

  it('the place of #content and of the tree is written with replaceState into the current entry, keeping what it carries', () => {
    const body = functionSource('savePosition');
    assert.match(body, /withPosition\(history\.state, refs\.content\.scrollTop, refs\.sidebar\.scrollTop, refs\.info\.scrollTop\)/);
    assert.match(body, /history\.replaceState\(next, ''\)/);
    assert.match(body, /catch \{/, 'a refused replaceState must not break the page');
    assert.match(body, /if \(!positionTracked\) return/, 'nothing is written while a navigation is on its way');
  });

  it('it is written after the scroll settles (200 ms, never later than 1 s into a long scroll), from #content and the tree', () => {
    assert.match(code('app.js'), /const POSITION_SETTLE_MS = 200;/);
    assert.match(code('app.js'), /const POSITION_MAX_MS = 1000;/);
    assert.match(functionSource('schedulePositionSave'), /Math\.min\(POSITION_SETTLE_MS, Math\.max\(0, positionSince \+ POSITION_MAX_MS - now\)\)/);
    const bind = functionSource('bindEvents');
    assert.match(bind, /refs\.content\.addEventListener\('scroll', schedulePositionSave, \{ passive: true \}\)/);
    assert.match(bind, /refs\.sidebar\.addEventListener\('scroll', schedulePositionSave, \{ passive: true \}\)/);
  });

  it('it is also written before a link is followed (a capture listener on a[href^="#"]), on pagehide and when the page is hidden', () => {
    const bind = functionSource('bindEvents');
    assert.match(bind, /closest\('a\[href\^="#"\]'\)\) savePosition\(\);\s*\}, true\)/);
    assert.match(bind, /window\.addEventListener\('pagehide', savePosition\)/);
    assert.match(bind, /visibilityState === 'hidden'\) \{\s*savePosition\(\);/);
  });

  it('a navigation stops the tracking, and every page that is shown starts it again (and writes its place at once)', () => {
    assert.match(functionSource('onRoute'), /untrackPosition\(\);/);
    for (const name of ['showFile', 'showOverview']) assert.match(functionSource(name), /settlePosition\(\);\s*\}\s*$/, name);
    assert.match(functionSource('settlePosition'), /positionTracked = true;[\s\S]*savePosition\(\)/);
  });

  it('an entry that comes back is shown where the reader left it: onRoute and boot read savedPosition(history.state) into the arrival target', () => {
    assert.match(functionSource('onRoute'), /arrivalTarget\(route, fresh \? null : savedPosition\(history\.state\)\)/);
    assert.match(functionSource('loadFresh'), /arrivalTarget\(route, savedPosition\(history\.state\)\)/);
    assert.match(functionSource('loadFresh'), /openRoute\(route, \{ target \}\)/);
    assert.match(functionSource('boot'), /await loadFresh\(\)/, 'a reload loads the page the way a page that has an error is tried again');
  });

  it('a link to the address the page already has is followed, not restored (it has an entry with a place of its own)', () => {
    assert.match(functionSource('bindEvents'), /onRoute\(\{ fresh: true \}\)/);
    assert.match(functionSource('bindEvents'), /window\.addEventListener\('hashchange', \(\) => \{\s*onRoute\(\);/);
  });

  it('the tree comes back after the folders above the open file are opened, never before', () => {
    for (const name of ['showFile', 'showOverview']) {
      const body = functionSource(name);
      assert.ok(body.indexOf('updateChrome(') < body.indexOf('restoreTree(arrival)'), name);
    }
    assert.match(functionSource('restoreTree'), /target\?\.kind === 'restore'\) refs\.sidebar\.scrollTop = target\.treeScroll/);
  });

  it('the places that change what is open or folded remember it for the tab (a reload then shows the page the place was measured on)', () => {
    for (const name of ['treeFooter', 'toggleBandGroup', 'limitedList', 'issueGroup', 'applyOverviewRoute', 'memorySection', 'propertiesBlock']) {
      assert.match(functionSource(name), /saveUi\(\)/, name);
    }
    assert.match(functionSource('saveUi'), /sessionStorage\.setItem\(sourceKey\(UI_KEY, state\.source\.id\), json\)/, 'what is open is kept per source');
    assert.match(functionSource('saveUi'), /catch \{/, 'blocked storage is not an error');
    assert.match(functionSource('loadSourceState'), /const ui = readUi\(id\);/, 'read back when a source is opened');
  });
});

// ---------------------------------------------------------------------------------------------
// R4-3: the distilled overview
// ---------------------------------------------------------------------------------------------

describe('the overview says each thing once (R4-3)', () => {
  it('the title is the budget sentence, with the estimate-and-upper-limit tooltip; no "Overview" title, no version, no folder', () => {
    const body = functionSource('overviewHead');
    assert.match(body, /el\('h1', null, t\('budget\.title', \{ tokens: formatTokens\(data\.everySessionTokens\) \}\)\)/);
    assert.match(body, /title\.title = t\('budget\.hint'\)/);
    assert.ok(!/overview\.(title|version|root)/.test(body));
    assert.match(body, /t\('overview\.indexed', \{ time: indexedTime\(builtAt\) \}\)/);
    assert.match(functionSource('indexedTime'), /trLocative\(date\.getHours\(\), date\.getMinutes\(\)\)/);
  });

  it('the budget section has the band, the sentence under it and the memory note: its headline moved up into the title', () => {
    const body = functionSource('budgetSection');
    assert.ok(!/el\('h2'/.test(body), 'no second heading with the same sentence');
    assert.match(body, /el\('p', 'budget-note muted', t\('budget\.explain'\)\)/);
    assert.match(body, /memoryNote\(data\)/);
  });

  it('the "10 heaviest files" table is gone from the page, its styles and its strings', () => {
    for (const [name, source] of [['app.js', code('app.js')], ['app.css', css], ['i18n.js', code('i18n.js')]]) {
      assert.ok(!/heaviest|file-cell/i.test(source), `${name} still mentions the heaviest files`);
    }
    assert.ok(!/col-layer/.test(css) && !/col-layer/.test(code('app.js')));
  });

  it('the layers are two: the table of what loads every session and a fold of the rest (splitLayers), the fold a button that controls a hidden <tbody>', () => {
    const body = functionSource('layersSection');
    assert.match(body, /splitLayers\(data\.layers\)/);
    assert.match(body, /body\.hidden = !state\.layersRestOpen/);
    assert.match(body, /setAttribute\('aria-controls', body\.id\)/);
    assert.match(body, /setAttribute\('aria-expanded', String\(state\.layersRestOpen\)\)/);
    assert.match(body, /tn\('layers\.rest', rest\.length, \{ tokens: formatTokens\(restTokens\) \}\)/);
    assert.match(body, /saveUi\(\)/, 'a reload keeps the fold as it was');
    assert.match(body, /if \(rest\.length === 0\) return section/, 'no fold when there is nothing to fold');
    assert.match(body, /layerRow\(entry, data, modes, \{ rest: true \}\)/);
  });

  it('the memory index stays a row of its own, its tokens a link to the table of project memory indexes, its share "in each project"', () => {
    const row = functionSource('layerRow');
    assert.match(row, /enumName\(entry\.layer\) === 'MemoryIndex' && !\(entry\.everySessionTokens > 0\)/);
    assert.match(row, /memoryLink\(entry\.tokens\)/);
    assert.match(row, /t\('layers\.perProject'\)/);
    assert.match(functionSource('memoryLink'), /link\.href = overviewHash\(sid\(\), \{ memory: true \}\)/);
    assert.match(row, /if \(rest\) row\.className = 'row-zero'/, 'only a folded layer is dimmed');
  });

  it('the legend is in the overview once (the layers); the tree\'s own is hidden there and comes back, with its height, in a file view', () => {
    assert.match(functionSource('layersSection'), /legendBlock\(\)/);
    assert.match(functionSource('legendBlock'), /legendList\(\), el\('p', 'legend-note muted', t\('legend\.tokens'\)\)/, 'the sentence that explains the numbers beside the tree rows goes with it');
    assert.equal([...code('app.js').matchAll(/legendBlock\(\)/g)].length, 2, 'declared once, used once');
    assert.match(ruleBody('body.overview-view .tree-foot') ?? '', /display: none/);
    assert.match(functionSource('updateLayoutMode'), /classList\.toggle\('overview-view'[\s\S]*syncFooterSpace\(\)/);
  });

  it('the layers table keeps its headers on one line and its share bar its room where its section has the 580px for it; below that it is as it was and the narrow rules drop columns', () => {
    const section = [...css.matchAll(/\n\.ov-layers\s*\{([^}]*)\}/g)].map((match) => match[1]).find((body) => /container-type/.test(body)) ?? '';
    assert.match(section, /container-type: inline-size/);
    assert.match(section, /container-name: layers/);
    const wide = css.match(/@container layers \(min-width: 580px\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    assert.match(wide, /\.ov-layers table\.data th,\s*\.ov-layers \.per-project\s*\{\s*white-space: nowrap/);
    assert.match(wide, /\.ov-layers \.share\s*\{\s*min-width: 10\.5rem/);
    // Nothing of that is unconditional: a table squeezed under 580px must be able to shrink to the 445px it was.
    assert.ok(!/white-space: nowrap/.test(ruleBody('table.data th') ?? ''));
    assert.ok(!/min-width/.test(ruleBody('.share') ?? ''));
    assert.ok(!/\n\.per-project\s*\{/.test(css), 'its nowrap is the wide rule\'s alone');
    const narrowest = css.match(/@media \(max-width: 360px\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    assert.match(narrowest, /\.fold-btn\s*\{[^}]*margin-inline: -4px/, 'the fold button reaches into the cell padding and no further');
  });

  it('under touch the fold and the memory link are 40px controls', () => {
    const coarse = css.match(/@media \(pointer: coarse\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    const sized = coarse.match(/([^{}]*)\{\s*min-height: var\(--touch\);\s*\}/)?.[1] ?? '';
    for (const name of ['.fold-btn', '.memory-link']) assert.ok(sized.includes(name), `${name} is not made touch-sized`);
    assert.match(coarse.match(/\.memory-link\s*\{\s*justify-content: flex-end;\s*min-width: var\(--touch\);/)?.[0] ?? '', /min-width: var\(--touch\)/, 'a figure is narrower than a finger: its link is 40px wide too');
  });
});

// ---------------------------------------------------------------------------------------------
// R4-2: showing what changed while the page is open
// ---------------------------------------------------------------------------------------------

describe('live changes are shown without taking anything from the reader (R4-2)', () => {
  const liveParts = ['noteChange', 'applyFresh', 'sweepFresh', 'remarkUnder', 'collectBlocks', 'markBlocks', 'showFileChange', 'showOverviewDiff', 'renderLastChange', 'announce', 'hidePill', 'positionPill', 'offerPill'];

  it('the three new pieces are in index.html: the top bar link, the pill (with a drawn arrow) and a polite live region', () => {
    assert.match(html, /<a id="last-change" class="last-change" href="#\/" hidden><\/a>/);
    assert.ok(html.indexOf('id="live"') < html.indexOf('id="last-change"') && html.indexOf('id="last-change"') < html.indexOf('id="info-toggle"'), 'beside the live indicator');
    assert.match(html, /<button id="change-pill" class="change-pill" type="button" hidden>\s*<svg [^>]*aria-hidden="true"/, 'an SVG, not a glyph, for the arrow');
    assert.match(html, /<div id="announce" class="announce" role="status" aria-live="polite" aria-atomic="true"><\/div>/);
    assert.ok(!/[↑↓▲▼]/.test(html + css + code('app.js')), 'no arrow glyph stands in for an icon');
  });

  it('a screen reader is told through a region that is never replaced (a live region that arrives with its text is not announced)', () => {
    const body = functionSource('announce');
    assert.match(body, /refs\.announce\.textContent = ''/);
    assert.match(body, /setTimeout/);
    assert.ok(!/replaceChildren|innerHTML|createElement/.test(body));
    assert.match(code('app.js'), /refs\.announce = document\.getElementById\('announce'\)/);
    assert.match(code('app.js'), /const ANNOUNCE_MS = 10000;/);
    assert.match(body, /ANNOUNCE_MS/, 'what was said is cleared again, not left behind in the page');
  });

  it('the event is noted before the tree is drawn again, so the new rows wear the marks; the tree ends by applying them', () => {
    const body = functionSource('onChanged');
    assert.ok(body.indexOf('noteChange(') < body.indexOf('loadTree()'));
    assert.match(functionSource('renderTree'), /restoreTreeFocus\(focused\);\s*applyFresh\(\);\s*\}/);
    assert.match(body, /showFile\(state\.route, \{ keepScroll: !added\.includes\(path\), live: added\.includes\(path\) \|\| changed\.includes\(path\) \}\)/, 'the open file is "updated" only when it is the file that changed');
    assert.match(body, /showOverview\(\{ keepScroll: true, live: true \}\)/);
  });

  it('showing a change never moves the focus and never scrolls #content', () => {
    for (const name of liveParts) {
      const body = functionSource(name);
      assert.ok(!/\.focus\(/.test(body), `${name} takes the focus`);
      assert.ok(!/scrollTop\s*[+-]?=|scrollTo\(|scrollWithin\(|scrollBy\(/.test(body), `${name} scrolls`);
    }
    // The one place a change leads to a scroll is the reader's own click on the pill, and the keyboard goes with them.
    const use = functionSource('usePill');
    assert.match(use, /scrollWithin\(refs\.content, node, \{ block: 'start', margin: LINE_MARGIN \}\)/);
    assert.match(use, /node\.focus\(\{ preventScroll: true \}\)/);
  });

  it('the marks are classes that app.js removes after their time, the badges are removed after theirs', () => {
    assert.match(code('app.js'), /const FRESH_MS = 3000;/);
    assert.match(code('app.js'), /const FRESH_DOT_MS = 10000;/);
    assert.match(code('app.js'), /const BLOCK_MS = 3000;/);
    assert.match(code('app.js'), /const BADGE_MS = 4000;/);
    assert.match(code('app.js'), /const PILL_MS = 8000;/);
    assert.match(code('app.js'), /const AGE_TICK_MS = 15000;/);
    assert.match(functionSource('markBlocks'), /classList\.add\('live-new'\)[\s\S]*setTimeout\(clearBlockMarks, BLOCK_MS\)/);
    assert.match(functionSource('sweepFresh'), /FRESH_MS - age : FRESH_DOT_MS - age/);
    assert.match(functionSource('showOverviewDiff'), /setTimeout\(\(\) => \{\s*for \(const badge of badges\) badge\.remove\(\);\s*\}, BADGE_MS\)/);
    assert.match(functionSource('offerPill'), /setTimeout\(hidePill, PILL_MS\)/);
    assert.match(functionSource('boot'), /setInterval\(renderLastChange, AGE_TICK_MS\)/);
  });

  it('a tree that is drawn again carries the mark on from how far it had faded (animation-delay), and a closed folder gets the dot', () => {
    const body = functionSource('applyFresh');
    assert.match(body, /row\.style\.animationDelay = `\$\{-age\}ms`/);
    assert.match(body, /closedAncestor\(path, state\.openDirs\)/);
    assert.match(body, /classList\.add\('has-fresh'\)/);
    assert.match(functionSource('setDirOpen'), /if \(open\) item\.firstElementChild\.classList\.remove\('has-fresh'\)/);
    assert.match(functionSource('toggleDir'), /if \(open\) remarkUnder\(path\)/);
  });

  it('the open file: the blocks are compared before and after (collectBlocks, changedBlocks), the stamp is in the load line, and the pill is hidden when the page is replaced', () => {
    const show = functionSource('showFile');
    assert.match(show, /const before = live && state\.file !== null \? collectBlocks\(refs\.content\) : null/, 'the old page is read before it is replaced');
    assert.ok(show.indexOf('collectBlocks(refs.content)') < show.indexOf('renderContent('));
    assert.match(show, /state\.stamp = \{ path: route\.path, at: new Date\(\) \}/);
    assert.match(show, /showFileChange\(before\)/);
    assert.match(show, /announce\(t\('announce\.updated'/);
    assert.match(functionSource('showFileChange'), /changedBlocks\(before\.map\(\(block\) => block\.text\), blocks\.map\(\(block\) => block\.text\)\)/);
    assert.match(functionSource('loadLine'), /state\.stamp\?\.path === file\.path\) line\.append\(el\('span', 'updated-at', t\('file\.updated'/);
    assert.match(functionSource('renderContent'), /hidePill\(\)/);
  });

  it('the pill goes when the reader scrolls (more than a few pixels: the page\'s own place-keeping is not a scroll of theirs), after PILL_MS, and it follows the window', () => {
    const bind = functionSource('bindEvents');
    assert.match(bind, /Math\.abs\(refs\.content\.scrollTop - pillState\.scroll\) > 4\) hidePill\(\)/);
    assert.match(bind, /refs\.pill\.addEventListener\('click', usePill\)/);
    assert.match(bind, /window\.addEventListener\('resize'/);
    assert.match(functionSource('positionPill'), /refs\.content\.getBoundingClientRect\(\)/, 'centred over #content, not over the window');
  });

  it('the overview: the numbers that changed get a badge (aria-hidden, no room taken) and the words go to the live region', () => {
    const body = functionSource('showOverviewDiff');
    assert.match(body, /overviewDiff\(previous, next\)/);
    assert.match(body, /formatDelta\(to - from, \{ tokens: budget \}\)/);
    assert.match(body, /t\('announce\.count', \{ label: t\(`issues\.\$\{key\}`\), from, to \}\)/);
    assert.match(body, /t\('announce\.budget'/);
    assert.match(body, /announce\(words\.join\('\. '\)\)/);
    assert.match(functionSource('deltaBadge'), /setAttribute\('aria-hidden', 'true'\)/);
    assert.match(functionSource('issueChips'), /chip\.dataset\.group = key/);
    assert.match(functionSource('showOverview'), /if \(live && previous && state\.overview && !state\.error\) showOverviewDiff\(previous, state\.overview\)/);
  });

  it('the top bar line: a link to the file (none for a deleted one), a name that says all of it to a screen reader, rewritten with the language', () => {
    const body = functionSource('renderLastChange');
    assert.match(body, /setAttribute\('aria-label'/);
    assert.match(body, /if \(change\.kind === 'removed'\) link\.removeAttribute\('href'\)/);
    assert.match(body, /else link\.href = fileHash\(sid\(\), change\.path\)/);
    assert.match(body, /changeName\(change\.path, state\.labels, \{ notes: isNotes\(\) \}\)/);
    assert.match(functionSource('changeLanguage'), /renderLastChange\(\)/);
  });
});

describe('the marks are calm, readable and still without motion (R4-2)', () => {
  for (const [theme, tokens] of [['light', lightTokens], ['dark', darkTokens]]) {
    it(`${theme}: on the tint of a file that changed (--fresh-bg), over the page and the side panel, text, the quiet grey, links and the danger colour are at least 4.5:1`, () => {
      const color = (name) => parseColor(tokens[name]);
      for (const [surfaceName, surface] of [['--bg', color('--bg')], ['--bg-side', color('--bg-side')]]) {
        const row = over(color('--fresh-bg'), surface);
        for (const name of ['--text', '--muted', '--accent-text', '--danger']) {
          const ratio = contrast(color(name), row);
          assert.ok(ratio >= 4.5, `${name} ${tokens[name]} on --fresh-bg over ${surfaceName}: ${ratio.toFixed(2)}`);
        }
      }
    });

    it(`${theme}: it is a tenth of the accent: calmer than the frame of a line that was moved to (--hit-bg)`, () => {
      assert.ok(parseColor(tokens['--fresh-bg'])[3] <= 0.1 + 1e-9);
      assert.ok(parseColor(tokens['--fresh-bg'])[3] < parseColor(tokens['--hit-bg'])[3]);
    });
  }

  it('the base rules are the still marks; the fade is only in a no-preference query (and the global reduce rule removes any other)', () => {
    for (const selector of ['.tree-row.fresh', '.live-new']) {
      const body = ruleBody(selector) ?? '';
      assert.match(body, /box-shadow:[^;]*var\(--fresh-bg\)/, selector);
      assert.ok(!/animation/.test(body), `${selector} has an animation outside the no-preference query`);
    }
    const motion = css.match(/@media \(prefers-reduced-motion: no-preference\)\s*\{\s*\.tree-row\.fresh,\s*tr\.live-new > \*\s*\{([^}]*)\}\s*\.live-new\s*\{([^}]*)\}/);
    assert.ok(motion, 'the fade is in a no-preference query');
    assert.match(motion[1], /animation: fresh-row 2\.8s/);
    assert.match(motion[2], /animation: fresh-block 2\.8s/);
    assert.match(css, /@keyframes fresh-row/);
    assert.match(css, /@keyframes fresh-block/);
    assert.match(css, /@media \(prefers-reduced-motion: reduce\)\s*\{[^}]*animation: none !important/);
  });

  it('a tint is an inset shadow, never a background or an outline: the open row and the hovered row keep their own, and nothing moves', () => {
    for (const selector of ['.tree-row.fresh', '.live-new', 'tr.live-new > *']) {
      const body = ruleBody(selector) ?? '';
      assert.ok(!/background|outline|border|margin|padding|width|height/.test(body.replace(/box-shadow:[^;]*;/, '').replace(/border-radius:[^;]*;/, '')), selector);
    }
    for (const frame of ['fresh-row', 'fresh-block']) {
      const body = css.match(new RegExp(`@keyframes ${frame}\\s*\\{([\\s\\S]*?)\\n\\}\\n`))?.[1] ?? '';
      assert.ok(body.includes('box-shadow') && !/background|outline|transform/.test(body), frame);
    }
  });

  it('the dot of a closed folder sits between the name and the numbers, round, in the accent (3:1 on both surfaces, tested above)', () => {
    const dot = ruleBody('.tree-row.has-fresh::after') ?? '';
    assert.match(dot, /order: 1/);
    assert.match(dot, /width: 7px; height: 7px/);
    assert.match(dot, /border-radius: 50%/);
    assert.match(dot, /background: var\(--accent\)/);
    assert.match(ruleBody('.tree-row.has-fresh > .tokens') ?? '', /order: 2/);
    assert.match(css, /@media \(forced-colors: active\)\s*\{\s*\.tree-row\.has-fresh::after\s*\{\s*forced-color-adjust: none/);
  });

  it('the pill is fixed (app.js centres it over #content), above the page and under the scrim, and an SVG arrow turns up', () => {
    const body = ruleBody('.change-pill') ?? '';
    assert.match(body, /position: fixed/);
    assert.match(body, /z-index: 15/);
    assert.ok(15 < 20, 'the scrim of a drawer (20) is over it');
    assert.match(ruleBody('.change-pill.is-up .pill-arrow') ?? '', /transform: rotate\(180deg\)/);
  });

  it('a badge floats over the corner of a chip and takes no room; after the title it sits in the line; neither takes a click', () => {
    assert.match(ruleBody('.issue-chip > .delta') ?? '', /position: absolute/);
    assert.match(ruleBody('.issue-chip') ?? '', /position: relative/);
    assert.match(ruleBody('.delta') ?? '', /pointer-events: none/);
    assert.match(ruleBody('.delta') ?? '', /color: var\(--text\)/);
  });

  it('the live region is fixed and clipped, not .sr-only (whose absolute position once stretched the document)', () => {
    const body = ruleBody('.announce') ?? '';
    assert.match(body, /position: fixed/);
    assert.match(body, /clip: rect\(0, 0, 0, 0\)/);
  });

  it('the top bar line is not shown below 760px (the bar has no room), and is a 40px control under touch', () => {
    assert.match(css, /@media \(max-width: 760px\)\s*\{\s*\.last-change\s*\{\s*display: none/);
    const coarse = css.match(/@media \(pointer: coarse\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    const sized = coarse.match(/([^{}]*)\{\s*min-height: var\(--touch\);\s*\}/)?.[1] ?? '';
    for (const name of ['.last-change', '.change-pill']) assert.ok(sized.includes(name), `${name} is not made touch-sized`);
  });
});

// ---------------------------------------------------------------------------------------------
// R5-A: the strip above the tree, the order of the tree, the info panel's place
// ---------------------------------------------------------------------------------------------

/** The declarations of the rule whose selector starts a line and is exactly `selector`: not a longer selector that ends the same way. */
const ownRule = (selector) => {
  const pattern = selector.split(/\s*,\s*/).map((part) => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join(',\\s*');
  const match = new RegExp(`(?:^|\\n)${pattern}\\s*\\{([^}]*)\\}`).exec(css);
  return match ? match[1].replace(/\s+/g, ' ').trim() : null;
};

describe('the strip above the tree and the order of the tree (R5-A)', () => {
  const template = html.match(/<template id="tree-head-template">([\s\S]*?)<\/template>/)?.[1] ?? '';

  it('the strip is a template of index.html: the name, and two buttons that are drawn icons (their names come from app.js)', () => {
    assert.ok(template.length > 0, 'no <template id="tree-head-template">');
    assert.match(template, /<div class="tree-head">/);
    assert.match(template, /class="tree-head-title"/);
    const buttons = [...template.matchAll(/<button class="tree-tool" type="button" data-action="(\w+)">([\s\S]*?)<\/button>/g)];
    assert.deepEqual(buttons.map((match) => match[1]), ['collapse', 'reveal']);
    for (const [, action, inner] of buttons) {
      assert.match(inner, /^\s*<svg [^>]*aria-hidden="true"[^>]*>[\s\S]*<\/svg>\s*$/, `${action}: an SVG and nothing else`);
      assert.ok(!/[←-⇿■-◿✀-➿]/.test(inner), `${action}: no glyph stands in for an icon`);
    }
    assert.ok(!/\sstyle\s*=/.test(template) && !/\son[a-z]+\s*=/i.test(template));
  });

  it('treeHead clones it, names the buttons (title and aria-label, one text) and the tree is drawn with it first', () => {
    const body = functionSource('treeHead');
    assert.match(body, /refs\.treeHeadTemplate\.content\.firstElementChild\.cloneNode\(true\)/);
    assert.match(body, /textContent = t\('nav\.tree'\)/);
    assert.match(body, /collapse: t\('tree\.collapseAll'\), reveal: t\('tree\.revealOpen'\)/);
    assert.match(body, /button\.title = label;\s*button\.setAttribute\('aria-label', label\)/);
    assert.match(functionSource('renderTree'), /nav\.replaceChildren\(treeHead\(\), buildTreeList\(state\.tree\.nodes\), \.\.\.\(isNotes\(\) \? \[\] : \[treeFooter\(\)\]\)\)/);
    assert.match(code('app.js'), /refs\.treeHeadTemplate = document\.getElementById\('tree-head-template'\)/);
  });

  it('"collapse all" closes every folder, the ones the page opened for the open file too, and remembers it', () => {
    const body = functionSource('collapseAll');
    assert.match(body, /state\.openDirs\.clear\(\)/);
    assert.match(body, /querySelectorAll\('li\.tree-dir\.open'\)\) setDirOpen\(item, false\)/);
    assert.match(body, /saveOpenDirs\(\)/);
    assert.match(body, /applyFresh\(\)/, 'a folder that closes wears the dot for a change inside it');
  });

  it('"show open file" opens the folders above it, brings the row into view clear of the strip, marks it and takes the focus to it', () => {
    const body = functionSource('revealOpenFile');
    assert.match(body, /a\.tree-row\.current/);
    assert.match(body, /updateTreeSelection\(true\)/);
    assert.match(body, /flash\(row\)/);
    assert.match(body, /row\.focus\(\{ preventScroll: true \}\)/);
    assert.match(functionSource('updateTreeSelection'), /margin: 4 \+ treeHeadHeight\(\), bottomMargin: 4 \+ covered/, 'the strip covers the rows under it, like the legend');
    assert.match(functionSource('revealDir'), /margin: 8 \+ treeHeadHeight\(\)/);
  });

  it('it has nothing to show on the overview or for a file the tree does not have: the button is disabled there, from updateTreeSelection', () => {
    const body = functionSource('syncTreeTools');
    assert.match(body, /\.tree-tool\[data-action="reveal"\]/);
    assert.match(body, /reveal\.disabled = refs\.sidebar\.querySelector\('a\.tree-row\.current'\) === null/);
    assert.match(functionSource('updateTreeSelection'), /syncTreeTools\(\);\s*if \(path === null \|\| !reveal\) return;/);
  });

  it('the tools are used from the tree\'s one click listener, and the focus on a tool survives the tree being drawn again', () => {
    assert.match(functionSource('bindEvents'), /closest\('button\.tree-tool'\);\s*if \(tool\) \(tool\.dataset\.action === 'collapse' \? collapseAll : revealOpenFile\)\(\)/);
    assert.match(functionSource('focusedTreeKey'), /button\.tree-tool'\)\) return \{ tool: active\.dataset\.action \}/);
    assert.match(functionSource('restoreTreeFocus'), /key\.tool\) target = refs\.sidebar\.querySelector\(`\.tree-tool\[data-action="\$\{key\.tool\}"\]:not\(:disabled\)`\)/);
  });

  it('the strip sticks to the top of the tree, and the tree scrolls a row clear of it (--head-h, set where --foot-h is)', () => {
    const rule = ownRule('.tree-head') ?? '';
    assert.match(rule, /position: sticky/);
    assert.match(rule, /top: -6px/, 'the tree\'s own padding: it sticks to the very edge');
    assert.match(rule, /background: var\(--bg-side\)/);
    assert.match(ownRule('.sidebar') ?? '', /scroll-padding-top: var\(--head-h, 34px\)/);
    assert.match(functionSource('syncFooterSpace'), /setProperty\('--head-h', `\$\{treeHeadHeight\(\)\}px`\)/);
    assert.match(functionSource('treeHeadHeight'), /querySelector\('\.tree-head'\)\?\.offsetHeight \?\? 0/);
  });

  it('the tools are 30px with a mouse and 40px under touch; a disabled one is faded and shows no pointer', () => {
    assert.match(ownRule('.tree-tool') ?? '', /width: 30px; height: 30px/);
    const coarse = css.match(/@media \(pointer: coarse\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    assert.match(coarse, /\.tree-tool\s*\{\s*width: var\(--touch\);\s*height: var\(--touch\);/);
    assert.match(coarse, /\.tree-head\s*\{\s*min-height: var\(--touch\);/);
    assert.match(ownRule('.tree-tool:disabled') ?? '', /opacity: 0\.4; cursor: default/);
  });

  it('the folders under projects/ are listed by the name of their project (orderProjectFolders), every other folder as the server sent it', () => {
    const body = functionSource('treeChildren');
    assert.match(body, /path === 'projects' && !isNotes\(\) \? orderProjectFolders\(children, state\.labels\) : children/, 'a folder of notes called projects is a folder');
    assert.match(functionSource('buildTreeItem'), /buildTreeList\(treeChildren\(node, path\)\)/);
    assert.match(functionSource('renderTree'), /buildTreeList\(state\.tree\.nodes\)/, 'the top level is not reordered');
  });
});

describe('the info panel keeps its place for the same file and starts at its top for another (R5-A)', () => {
  it('renderInfo remembers which file the panel shows and keeps the scroll only for that one', () => {
    const body = functionSource('renderInfo');
    assert.match(body, /const scroll = refs\.info\.scrollTop;/);
    assert.match(body, /refs\.info\.scrollTop = infoPath === file\.path \? scroll : 0;\s*infoPath = file\.path;/);
    assert.equal([...body.matchAll(/infoPath = null/g)].length, 2, 'the overview, a page that failed and a file that is gone have no file in the panel');
    assert.match(code('app.js'), /let infoPath = null;/);
  });

  it('nothing is said where a page failed to load: "open a file" would contradict the page', () => {
    assert.match(functionSource('renderInfo'), /if \(state\.route\.view !== 'file' \|\| state\.error\) \{\s*refs\.info\.replaceChildren\(\);/);
  });

  it('Back, Forward and a reload put the panel back from the history entry: it is written with the other places, and read where they are', () => {
    assert.match(functionSource('restoreInfo'), /target\?\.kind === 'restore'\) refs\.info\.scrollTop = target\.infoScroll/);
    assert.match(functionSource('showFile'), /restoreTree\(arrival\);\s*restoreInfo\(arrival\);/);
    assert.match(functionSource('onRoute'), /restoreTree\(target\);\s*restoreInfo\(target\);/);
    assert.match(functionSource('bindEvents'), /refs\.info\.addEventListener\('scroll', schedulePositionSave, \{ passive: true \}\)/);
    assert.match(functionSource('savePosition'), /\$\{next\.scroll\}\/\$\{next\.treeScroll\}\/\$\{next\.infoScroll\}/);
  });
});

describe('the reading place is written only at its own address (R5: Forward and Back at once)', () => {
  it('savePosition asks pageIsAtAddress(state.route, location.hash) after the tracking check and before it writes into the entry', () => {
    const body = functionSource('savePosition');
    assert.match(body, /if \(!pageIsAtAddress\(state\.route, window\.location\.hash\)\) return;/);
    assert.ok(body.indexOf('if (!positionTracked) return') < body.indexOf('pageIsAtAddress('));
    assert.ok(body.indexOf('pageIsAtAddress(') < body.indexOf('history.replaceState('), 'asked before anything is written');
  });

  it('every write of the place goes through savePosition (settlePosition, the scroll timer, a followed link, pagehide), so the guard covers them all', () => {
    const writes = [...code('app.js').matchAll(/history\.replaceState\(([^)]*)\)/g)].map((match) => match[1]);
    assert.deepEqual([...writes].sort(), ["history.state, '', hash", "next, ''"], 'savePosition writes the place; replaceAddress only the address, and keeps the place the entry carries');
    assert.match(functionSource('replaceAddress'), /history\.replaceState\(history\.state, '', hash\)/);
    assert.match(functionSource('settlePosition'), /savePosition\(\)/);
    assert.match(functionSource('schedulePositionSave'), /setTimeout\(savePosition,/);
    assert.match(functionSource('goToFile'), /savePosition\(\)/);
  });
});

// ---------------------------------------------------------------------------------------------
// R5-B: errors, and what is only to be reviewed
// ---------------------------------------------------------------------------------------------

describe('errors and what is to be reviewed (R5-B)', () => {
  it('an errors group starts open when it has items, a group to review starts folded: the default is issueStartsOpen, an override is kept as before', () => {
    const body = functionSource('issueGroup');
    assert.match(body, /const startsOpen = issueStartsOpen\(key, items\.length\)/);
    assert.match(body, /details\.open = state\.issueOpen\.has\(key\) \? state\.issueOpen\.get\(key\) : startsOpen/);
    assert.match(body, /if \(details\.open === startsOpen\) state\.issueOpen\.delete\(key\);\s*else state\.issueOpen\.set\(key, details\.open\)/);
    assert.match(body, /saveUi\(\)/);
  });

  it('a group to review says in a sentence under its name what it is; an errors group needs none', () => {
    const body = functionSource('issueGroup');
    assert.match(body, /if \(review\) summary\.append\(el\('span', 'issue-help', t\(`issues\.help\.\$\{key\}`\)\)\)/);
    assert.match(body, /el\('details', review \? 'issues' : 'issues is-error'\)/);
    const help = ownRule('.issue-help') ?? '';
    assert.match(help, /display: block/);
    assert.match(help, /color: var\(--muted\)/);
    assert.match(help, /font-weight: 400/);
  });

  it('only groups with something in them are listed; nothing wrong is a calm line; nothing to review leaves its section out', () => {
    assert.match(functionSource('errorsSection'), /group\.items\.length > 0\)\.map\(issueGroup\)/);
    assert.match(functionSource('errorsSection'), /el\('p', 'issue-clear muted', t\('issues\.errorsNone'\)\)/);
    assert.match(functionSource('reviewSection'), /group\.items\.length > 0\)\.map\(issueGroup\)/);
    assert.match(functionSource('issuesBlock'), /if \(reviewCount > 0\) block\.append\(reviewSection\(withRows\(review\)\)\)/);
  });

  it('each section has its own heading and counts, and the counts lead to their groups through the same route as before', () => {
    assert.match(functionSource('errorsSection'), /issuesHead\(t\('issues\.errors'\), groups, \{ hideZero: isNotes\(\) \}\)/);
    assert.match(functionSource('reviewSection'), /issuesHead\(t\('issues\.review'\), groups, \{ hideZero: isNotes\(\) \}\)/);
    const chips = functionSource('issueChips');
    assert.match(chips, /chip\.href = overviewHash\(sid\(\), \{ issues: key \}\)/);
    assert.match(chips, /chip\.dataset\.group = key/);
    assert.match(chips, /if \(issueSection\(key\) === 'errors'\) chip\.classList\.add\('tone-error'\)/, 'red only for the errors');
    assert.match(functionSource('applyOverviewRoute'), /target\.closest\('\.section'\)\?\.querySelector\('\.issues-head'\)/, 'the heading that is pinned is the one of the section the group is in');
  });

  it('red is for the errors alone: no rule of the review section, and not the plain chip or group, uses the danger colour', () => {
    const rules = [...css.matchAll(/([^{}]+)\{([^}]*)\}/g)].map((match) => [match[1].trim().replace(/\s+/g, ' '), match[2]]);
    for (const [selector, body] of rules) {
      if (!/--danger/.test(body)) continue;
      assert.ok(!/ov-review|is-review/.test(selector), `${selector} makes something to review red`);
    }
    assert.ok(!/--danger/.test(ownRule('.issue-chip') ?? ''), 'a chip is neutral until it is an error chip');
    assert.ok(!/--danger/.test(ownRule('.issues') ?? '') && !/--danger/.test(ownRule('.count') ?? ''), 'a group and its count are neutral until they are an errors group');
    assert.match(ownRule('.issue-chip.tone-error') ?? '', /border-color: var\(--danger\); color: var\(--danger\)/);
    assert.match(ownRule('.issues.is-error > summary .count') ?? '', /background: var\(--danger\); color: var\(--on-danger\)/, 'white on the red fill, which is 4.5:1 (tested above)');
  });

  it('the wrapper of the two sections takes the sticky heading with it: each section pins its own', () => {
    const wide = css.match(/@container content \(min-width: 1100px\)\s*\{\s*\.issues-head\s*\{([^}]*)\}/);
    assert.ok(wide);
    assert.match(wide[1], /position: sticky/);
  });
});

// ---------------------------------------------------------------------------------------------
// R5-C: errors in words, stale content, the frontmatter notice
// ---------------------------------------------------------------------------------------------

describe('a page that could not be loaded says why in words (R5-C)', () => {
  it('no message of an error is ever shown: the words come from loadFailure and the dictionary', () => {
    assert.ok(!/\.message\b/.test(code('app.js')), 'a message of an error reaches the page');
    const body = functionSource('errorPage');
    assert.match(body, /const failure = loadFailure\(error\)/);
    assert.match(body, /el\('p', null, failureWords\(failure\)\)/);
    assert.match(functionSource('failureWords'), /t\(`error\.\$\{failure\.kind\}`, \{ status: failure\.status \}\)/, 'the words of the kind, and no sentence of the server\'s');
    assert.match(body, /box\.setAttribute\('role', 'alert'\)/);
    assert.match(body, /t\('error\.attempt', \{ time: clockText\(state\.errorAt\) \}\)/);
    assert.match(body, /t\('error\.retry'\)/);
  });

  it('every place that sets an error notes when, and a retry is a new try at the page, not a refresh', () => {
    assert.equal([...code('app.js').matchAll(/state\.error = error;/g)].length, 1, 'only setError writes it');
    assert.match(functionSource('setError'), /state\.errorAt = new Date\(\)/);
    assert.match(functionSource('errorPage'), /retry\.addEventListener\('click', \(\) => \{\s*refreshAll\(\);/);
    assert.match(functionSource('refreshAll'), /if \(state\.error\) \{\s*await loadFresh\(\);\s*return;/);
  });

  it('a refresh that fails leaves the page that is there: only a page that was asked for can be an error page', () => {
    assert.match(functionSource('showFile'), /if \(keepScroll && !gone && state\.file !== null && state\.file\.path === route\.path && state\.error === null\) return;/);
    assert.match(functionSource('showOverview'), /if \(keepScroll && state\.overview !== null && state\.error === null\) return;/);
    assert.match(functionSource('refreshAll'), /if \(state\.source !== null\) \{\s*try \{\s*if \(!\(await loadTree\(\)\)\) return;\s*\} catch \{\s*return;\s*\}/);
  });

  it('the page loads the way it opens, again, when a page that is an error is tried: the reader arrives where the route says', () => {
    const body = functionSource('loadFresh');
    assert.match(body, /state\.error = null;/);
    assert.match(body, /openRoute\(route, \{ target \}\)/);
  });
});

describe('stale content (R5-C)', () => {
  const strip = html.match(/<div id="stale"[\s\S]*?<\/div>/)?.[0] ?? '';

  it('the strip is in index.html between the top bar and the layout, hidden, with a drawn icon', () => {
    assert.match(strip, /<div id="stale" class="stale" hidden>/);
    assert.match(strip, /<svg class="stale-icon"[^>]*aria-hidden="true"/);
    assert.match(strip, /<span id="stale-text"><\/span>/);
    assert.ok(html.indexOf('</header>') < html.indexOf('id="stale"') && html.indexOf('id="stale"') < html.indexOf('<div class="layout">'));
  });

  it('it is for a page that is there and a connection that is lost: not for an error page, and not before there was a page', () => {
    assert.match(functionSource('updateStale'), /const show = state\.offline && state\.tree !== null && !state\.error;/);
    assert.match(functionSource('updateStale'), /t\('stale\.text', \{ time: clockText\(state\.contentAt\) \}\)/);
    assert.match(functionSource('renderContent'), /updateStale\(\)/);
    assert.match(functionSource('applyStaticI18n'), /updateStale\(\)/, 'the other language is written at once');
  });

  it('the time is the last moment the page was told what the folder has: a load, or the moment the live connection went', () => {
    assert.match(functionSource('getJson'), /state\.contentAt = new Date\(\)/);
    const body = functionSource('markOffline');
    assert.match(body, /if \(state\.offline\) return;/);
    assert.match(body, /if \(state\.live === 'live'\) state\.contentAt = new Date\(\)/);
    assert.match(body, /announce\(refs\.staleText\.textContent\)/, 'a screen reader is told once, in the polite region');
  });

  it('it goes when the live connection is back and the page has been told again, and it comes after the connection is lost, before the indicator changes', () => {
    const connect = functionSource('connectEvents');
    assert.ok(connect.indexOf('markOffline()') < connect.indexOf("setLive('offline')"), 'markOffline asks whether the connection was live');
    assert.match(functionSource('onReady'), /await refreshAll\(\);\s*markOnline\(\);/);
    assert.match(functionSource('markOnline'), /state\.offline = false;\s*updateStale\(\)/);
  });

  it('it is one thin line of the layout, not a cover: flex none between the top bar and the layout, in the warning colours, text-wrapping', () => {
    const rule = ownRule('.stale') ?? '';
    assert.match(rule, /flex: none/);
    assert.match(rule, /background: var\(--warn-bg\)/);
    assert.match(rule, /border-bottom: 1px solid var\(--warn-border\)/);
    assert.match(rule, /overflow-wrap: anywhere/);
    assert.ok(!/position/.test(rule), 'it takes its own height and covers nothing');
  });
});

describe('the frontmatter notice (R5-C)', () => {
  it('"partly read" or "could not be read", the line, the line as code (text), the hint, and the parser\'s words folded and quiet', () => {
    const body = functionSource('frontmatterNotice');
    assert.match(body, /partial \? t\('fm\.partial'\) : t\('fm\.failed'\)/);
    assert.match(body, /t\('fm\.line', \{ line \}\)/);
    assert.match(body, /el\('code', 'fm-code', text\)/, 'textContent through el(): the raw line may hold anything');
    assert.match(body, /t\('fm\.hint'\)/);
    assert.match(body, /el\('details', 'fm-detail'\)/);
    assert.match(body, /el\('p', 'muted fm-raw', message\)/);
    assert.ok(!/\.open\s*=\s*true/.test(body), 'the details are folded');
    assert.ok(!/innerHTML/.test(body));
    assert.match(body, /box\.setAttribute\('role', 'alert'\)/);
  });

  it('the notice comes before the properties it is about; a line that is not known leaves the line row out', () => {
    const page = functionSource('filePage');
    assert.ok(page.indexOf('frontmatterNotice(problem)') < page.indexOf('propertiesBlock(file.frontmatter)'));
    assert.match(functionSource('frontmatterNotice'), /if \(line !== null \|\| text !== null\)/);
    assert.match(functionSource('frontmatterNotice'), /if \(line !== null\) where\.append/);
    assert.match(functionSource('frontmatterNotice'), /if \(text !== null\) where\.append/);
  });

  it('a link to a line in the frontmatter takes the reader to the top, where the notice marks itself for a moment', () => {
    const body = functionSource('scrollToLine');
    assert.match(body, /if \(index < 0\) \{\s*refs\.content\.scrollTop = 0;[\s\S]*querySelector\('\.fm-error'\);\s*if \(notice\) flash\(notice\);/);
  });

  it('the line of the code wraps and never scrolls the page; the folded detail keeps its triangle', () => {
    const rule = ownRule('.fm-code') ?? '';
    assert.match(rule, /overflow-wrap: anywhere/);
    assert.match(rule, /white-space: pre-wrap/);
    assert.match(rule, /font: 12\.5px var\(--mono\)/);
    assert.ok(!/display/.test(ownRule('.fm-detail > summary') ?? ''), 'a summary that is not a list item has no marker');
  });
});

// ---------------------------------------------------------------------------------------------
// R5-D: the quick opener
// ---------------------------------------------------------------------------------------------

describe('the quick opener (R5-D)', () => {
  const palette = html.match(/<div id="palette"[\s\S]*?<\/ul>[\s\S]*?<\/div>\s*<\/div>/)?.[0] ?? '';

  it('the search button is in the top bar: a drawn icon, a name from app.js, and it says it opens a dialog', () => {
    assert.match(html, /<button id="quick-open" class="icon-btn search-btn" type="button" aria-haspopup="dialog">\s*<svg [^>]*aria-hidden="true"/);
    assert.ok(html.indexOf('class="spacer"') < html.indexOf('id="quick-open"') && html.indexOf('id="quick-open"') < html.indexOf('id="overview-link"'));
    assert.match(functionSource('applyStaticI18n'), /t\('quick\.open', \{ key: quickShortcutLabel\(\) \}\)/);
    assert.match(functionSource('applyStaticI18n'), /refs\.quickOpen\.title = openLabel;\s*refs\.quickOpen\.setAttribute\('aria-label', openLabel\)/);
    assert.match(functionSource('quickShortcutLabel'), /isApplePlatform\(navigator\.userAgentData\?\.platform \?\? navigator\.platform\) \? 'Cmd\+K' : 'Ctrl\+K'/);
  });

  it('the dialog is a modal dialog with a name, a combobox field and a listbox that the head names (WAI-ARIA)', () => {
    assert.match(palette, /<div id="palette" class="palette" hidden>/);
    assert.match(palette, /role="dialog" aria-modal="true" data-i18n-aria-label="quick\.title"/);
    assert.match(palette, /<input id="palette-input"[^>]*role="combobox"[^>]*aria-expanded="false"[^>]*aria-controls="palette-list"[^>]*aria-autocomplete="list"[^>]*data-i18n-aria-label="quick\.label"/);
    assert.match(palette, /<ul id="palette-list" class="palette-list" role="listbox" aria-labelledby="palette-head">/);
    assert.match(palette, /<p id="palette-head" class="palette-head" role="status">/);
    assert.match(palette, /<button id="palette-close"[^>]*data-i18n-title="quick\.close" data-i18n-aria-label="quick\.close">/);
    assert.match(palette, /autocomplete="off"[^>]*spellcheck="false"/);
  });

  it('a row is an option; the one that is selected is named by the field (aria-activedescendant), the rows are never focused', () => {
    const option = functionSource('quickOption');
    assert.match(option, /setAttribute\('role', 'option'\)/);
    assert.match(option, /setAttribute\('aria-selected', 'false'\)/);
    assert.match(option, /setAttribute\('aria-label', entry\.context \? `\$\{entry\.name\}, \$\{entry\.context\}` : entry\.name\)/);
    assert.ok(!/tabIndex|tabindex|\.focus\(/.test(option));
    const active = functionSource('setPaletteActive');
    assert.match(active, /aria-activedescendant/);
    assert.match(active, /\(index \+ options\.length\) % options\.length/, 'the ends wrap round');
    assert.match(active, /scrollWithin\(refs\.paletteList, options\[next\], \{ block: 'nearest', margin: 4 \}\)/, 'the list scrolls, never the page');
  });

  it('the keys that open it are decided by opensQuickOpen, the browser\'s own use of them is taken away, and a text field is told from the page', () => {
    const bind = functionSource('bindEvents');
    assert.match(bind, /const editable = event\.target instanceof Element && event\.target\.closest\('input, textarea, select, \[contenteditable\]:not\(\[contenteditable="false"\]\)'\) !== null;\s*if \(!opensQuickOpen\(event, \{ editable \}\)\) return;\s*event\.preventDefault\(\);\s*openPalette\(\);/);
    assert.match(bind, /refs\.quickOpen\.addEventListener\('click', openPalette\)/);
  });

  it('inside the dialog: Escape closes it (the drawers\' own Escape is not also run), Tab goes round, arrows move, Enter opens, an input method is left alone', () => {
    const body = functionSource('onPaletteKey');
    assert.match(body, /if \(event\.isComposing\) return;/);
    assert.match(body, /event\.key === 'Escape'\) \{\s*event\.preventDefault\(\);\s*event\.stopPropagation\(\);\s*closePalette\(\);/);
    assert.match(body, /event\.key === 'Tab'\) \{\s*trapPaletteFocus\(event\)/);
    assert.match(body, /inField && \(event\.key === 'ArrowDown' \|\| event\.key === 'ArrowUp'\)/);
    assert.match(body, /inField && event\.key === 'Enter'[\s\S]*goToFile\(item\.entry\.path\)/);
    const trap = functionSource('trapPaletteFocus');
    assert.match(trap, /\[refs\.paletteInput, refs\.paletteClose\]/);
    assert.match(trap, /event\.preventDefault\(\)/);
  });

  it('while it is open the rest of the page is inert (focus, pointer, screen reader); the focus is given back only after that is lifted', () => {
    assert.match(functionSource('setPageInert'), /\[refs\.skip, refs\.topbar, refs\.stale, refs\.layout\]\) node\.inert = on/);
    const open = functionSource('openPalette');
    assert.match(open, /paletteOpener = active instanceof HTMLElement && active !== document\.body \? active : null/);
    assert.match(open, /setOverlay\(null\)/, 'no drawer under it');
    assert.match(open, /setPageInert\(true\)/);
    assert.match(open, /refs\.paletteInput\.focus\(\)/);
    const close = functionSource('closePalette');
    assert.ok(close.indexOf('setPageInert(false)') < close.indexOf('opener.focus('), 'an inert element cannot take the focus back');
    assert.match(close, /focus === 'opener' && opener !== null && opener\.isConnected && !opener\.disabled\) opener\.focus\(\{ preventScroll: true \}\);\s*else refs\.content\.focus\(\{ preventScroll: true \}\)/);
  });

  it('opening a file saves the place of the page being left (Back returns to it), closes the dialog into the content, and follows the route', () => {
    const body = functionSource('goToFile');
    assert.ok(body.includes('savePosition()') && body.indexOf('savePosition()') < body.indexOf('window.location.hash = hash'), 'saved before the route changes');
    assert.match(body, /closePalette\(\{ focus: 'content' \}\)/);
    assert.match(body, /if \(window\.location\.hash === hash\) onRoute\(\{ fresh: true \}\);\s*else window\.location\.hash = hash/);
    assert.match(functionSource('onRoute'), /if \(!refs\.palette\.hidden\) closePalette\(\{ focus: 'content' \}\)/, 'Back or Forward while it is open closes it');
  });

  it('it searches the tree the page has: nothing is asked of the server, and a search is not a search of what the files say', () => {
    for (const name of ['quickIndex', 'quickResults', 'renderPalette', 'quickOption', 'markedText', 'goToFile']) {
      assert.ok(!/getJson\(|fetch\(|EventSource/.test(functionSource(name)), `${name} asks the server`);
    }
    assert.match(functionSource('quickIndex'), /quickEntries\(flattenFiles\(state\.tree\.nodes\), state\.labels, \{ notes: isNotes\(\) \}\)/);
    assert.match(functionSource('quickIndex'), /if \(paletteIndex\.tree !== state\.tree\)/, 'made again when the tree is');
    assert.match(functionSource('quickResults'), /recentResults\(entries, state\.recent\)/);
    assert.match(functionSource('quickResults'), /quickSearch\(entries, query, \{ recent: state\.recent \}\)/);
    assert.match(functionSource('loadTree'), /if \(!refs\.palette\.hidden\) renderPalette\(\{ keepActive: true \}\)/, 'a file that came or went while it is open');
  });

  it('how many files matched, or that none did, is said in the head, which is the live region and the name of the list; the notes under it are for what is not a result', () => {
    const body = functionSource('renderPalette');
    assert.match(body, /refs\.paletteHead\.textContent = total > 0 \? tn\('quick\.count', total\) : t\('quick\.none'\)/);
    assert.match(body, /if \(recent && items\.length === 0\) note = t\('quick\.empty'\)/);
    assert.match(body, /else if \(!recent && total > items\.length\) note = t\('quick\.more', \{ n: total - items\.length \}\)/);
    assert.match(body, /aria-expanded', String\(items\.length > 0\)/);
  });

  it('a name is text: the letters that matched are <mark> text nodes, nothing else is built from it', () => {
    const body = functionSource('markedText');
    assert.match(body, /part\.hit \? el\('mark', null, part\.text\) : document\.createTextNode\(part\.text\)/);
    assert.ok(!/innerHTML/.test(body));
  });

  it('the files opened in this tab are remembered in sessionStorage, newest first, and blocked storage is not an error', () => {
    assert.match(code('app.js'), /const RECENT_KEY = 'pusula\.recent';/);
    assert.match(functionSource('readRecent'), /parseRecent\(sessionStorage\.getItem\(sourceKey\(RECENT_KEY, id\)\)\)[\s\S]*catch \{\s*return \[\];/, 'the files of each source are its own');
    const remember = functionSource('rememberRecent');
    assert.match(remember, /state\.recent = pushRecent\(state\.recent, path\)/);
    assert.match(remember, /sessionStorage\.setItem\(sourceKey\(RECENT_KEY, state\.source\.id\), recentJson\(state\.recent\)\)[\s\S]*catch \{/);
    assert.match(functionSource('showFile'), /if \(state\.file !== null\) rememberRecent\(route\.path\)/);
  });

  it('the dialog is over everything (above the skip link, 60), fixed, and its field is 16px so a phone does not zoom into it', () => {
    const rule = ownRule('.palette') ?? '';
    assert.match(rule, /position: fixed/);
    const z = Number(/z-index: (\d+)/.exec(rule)?.[1]);
    assert.ok(z > 60, `z-index ${z}`);
    assert.match(ownRule('.palette-input') ?? '', /font: 16px var\(--font\)/);
    assert.match(css, /\[hidden\]\s*\{\s*display:\s*none\s*!important;/, 'hidden wins over the display the dialog sets');
  });

  it('the letters that matched are heavier and underlined in the line\'s own colour, never a tint (it would be lost on the selected row); the selected row is the accent tint', () => {
    const mark = ownRule('.palette-option mark') ?? '';
    assert.match(mark, /background: none/);
    assert.match(mark, /color: var\(--text\)/);
    assert.match(mark, /font-weight: 700/);
    assert.match(mark, /text-decoration: underline/);
    assert.match(ownRule('.palette-option[aria-selected="true"]') ?? '', /background: var\(--accent-soft\)/);
    assert.match(ownRule('.palette-option[aria-selected="true"] .palette-context') ?? '', /color: var\(--text\)/, 'the quiet place takes the text colour there');
  });

  it('a name is cut at its end inside a row, the place giving way first; under touch a row and the close button are 40px', () => {
    assert.match(ownRule('.palette-name') ?? '', /text-overflow: ellipsis/);
    assert.match(ownRule('.palette-context') ?? '', /flex: 0 100000 auto/);
    const coarse = css.match(/@media \(pointer: coarse\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    assert.match(coarse, /\.palette-option\s*\{\s*min-height: var\(--touch\);/);
    assert.match(coarse, /\.icon-btn\s*\{\s*width: var\(--touch\);\s*height: var\(--touch\);/, 'the close button and the search button are .icon-btn');
    assert.match(ownRule('.search-btn') ?? '', /display: inline-flex/);
    assert.match(ownRule('.palette-close') ?? '', /display: inline-flex/);
    assert.ok(css.indexOf('.search-btn {') > css.indexOf('.icon-btn {'), 'it overrides the display none of the drawers\' handles');
  });

  it('the box is lifted by its shadow alone (a hairline as well would be both at once); in forced colours, which remove shadows and backgrounds, it has an edge and the selected row an outline', () => {
    const box = ownRule('.palette-box') ?? '';
    assert.match(box, /box-shadow: var\(--shadow\)/);
    assert.ok(!/border/.test(box.replace(/border-radius:[^;]*;/, '')), 'no border next to the shadow');
    const forced = css.match(/@media \(forced-colors: active\)\s*\{\s*\.palette-box\s*\{([^}]*)\}\s*\.palette-option\[aria-selected="true"\]\s*\{([^}]*)\}/);
    assert.ok(forced, 'no forced-colors rules for the dialog');
    assert.match(forced[1], /outline: 1px solid CanvasText/);
    assert.match(forced[2], /outline: 2px solid Highlight/);
  });

  it('the opening is a small move that motion preferences remove', () => {
    assert.match(css, /@media \(prefers-reduced-motion: no-preference\)\s*\{\s*\.palette:not\(\[hidden\]\) \.palette-box\s*\{\s*animation: palette-in 0\.14s ease-out;/);
    assert.match(css, /@keyframes palette-in/);
    assert.match(css, /@media \(prefers-reduced-motion: reduce\)\s*\{[^}]*animation: none !important/);
  });
});

describe('the top bar keeps its controls the size they are (R5)', () => {
  it('the controls do not shrink: it is the name of the source and the last change that give way', () => {
    assert.match(ruleBody('.brand, .icon-btn, .top-link, .lang, .live') ?? '', /flex: none/);
    assert.match(ownRule('.source-switch') ?? '', /flex: 0 1 auto/);
    assert.match(ownRule('.source-switch') ?? '', /min-width: 0/);
    assert.match(ownRule('.last-change') ?? '', /min-width: 0/);
  });

  it('seven controls and the source switch fit in 400px: the gaps and the sides of the bar give, then the brand (the overview link says the same) and the folder icon of the switch; its arrow stays, since it says there is a menu; the name is cut at its end', () => {
    const narrow = css.match(/@media \(max-width: 480px\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    assert.match(narrow, /\.topbar\s*\{\s*gap: 4px;\s*padding: 0 8px;/);
    assert.match(narrow, /\.brand\s*\{\s*display: none;/);
    assert.match(narrow, /\.source-icon\s*\{\s*display: none;/);
    assert.ok(!/\.source-chevron\s*\{/.test(narrow), 'the arrow of the switch is what says it opens a menu: it stays');
    assert.ok(!/@media \(max-width: 380px\)/.test(css), 'the brand has already gone');
    const name = ownRule('.source-name') ?? '';
    assert.match(name, /min-width: 0/);
    assert.match(name, /overflow: hidden/);
    assert.match(name, /text-overflow: ellipsis/);
  });

  it('a language button is as wide as it is high under touch, even in 400px', () => {
    assert.match(css, /@media \(pointer: coarse\) and \(max-width: 480px\)\s*\{\s*\.lang-btn\s*\{\s*min-width: var\(--touch\);/);
  });
});

describe('colour of the R5 pieces (WCAG contrast of the real tokens)', () => {
  for (const [theme, tokens] of [['light', lightTokens], ['dark', darkTokens]]) {
    const color = (name) => parseColor(tokens[name]);
    const bg = color('--bg');
    const side = color('--bg-side');

    it(`${theme}: the red of the errors is text on the page (chips, broken links), and white on the red fill of an errors group's count`, () => {
      assert.ok(contrast(color('--danger'), bg) >= 4.5, `--danger on --bg: ${contrast(color('--danger'), bg).toFixed(2)}`);
      assert.ok(contrast(color('--danger'), side) >= 4.5);
      assert.ok(contrast(color('--on-danger'), color('--danger')) >= 4.5);
    });

    it(`${theme}: the quiet grey stays 4.5:1 on the notice of a frontmatter error (the warning tint) and on the error page (the danger tint), over the page`, () => {
      for (const tint of ['--warn-bg', '--danger-bg']) {
        const ratio = contrast(color('--muted'), over(color(tint), bg));
        assert.ok(ratio >= 4.5, `--muted on ${tint}: ${ratio.toFixed(2)}`);
      }
      for (const name of ['--text']) assert.ok(contrast(color(name), over(color('--warn-bg'), bg)) >= 4.5, `${name} on the stale strip`);
      assert.ok(contrast(color('--text'), color('--code-bg')) >= 4.5, 'the raw line is code');
    });

    it(`${theme}: the selected row of the quick opener (the accent tint over the page): text 4.5:1, and the place in the text colour there; the quiet grey on the page for its field and notes`, () => {
      const row = over(color('--accent-soft'), bg);
      assert.ok(contrast(color('--text'), row) >= 4.5, `--text on the selected row: ${contrast(color('--text'), row).toFixed(2)}`);
      assert.ok(contrast(color('--muted'), bg) >= 4.5, 'placeholder, head and notes');
      assert.ok(contrast(color('--accent'), bg) >= 3, 'the line under the field and under a mark');
    });

    it(`${theme}: the check of "no errors" and the tree tools are drawn marks (3:1 is enough)`, () => {
      for (const surface of [bg, side]) {
        assert.ok(contrast(color('--live'), surface) >= 3, `--live: ${contrast(color('--live'), surface).toFixed(2)}`);
        assert.ok(contrast(color('--muted'), surface) >= 3);
      }
      assert.ok(contrast(color('--text'), over(color('--hover'), side)) >= 3, 'a tool under the pointer');
    });
  }
});

// ---------------------------------------------------------------------------------------------
// Sources: the switch, the sources page, a source's own state, and the view of notes
// ---------------------------------------------------------------------------------------------

describe('every address the page writes names the source (S1)', () => {
  it('fileHash, overviewHash and linkHref are always given the open source (sid()) or the id of a source of the list', () => {
    const app = code('app.js');
    const calls = [...app.matchAll(/\b(fileHash|overviewHash|linkHref)\(([^)]*)/g)].map((match) => [match[1], match[2]]);
    assert.ok(calls.length >= 15, `found ${calls.length} calls`);
    for (const [name, args] of calls) assert.match(args, /^(?:sid\(|source\.id\b)/, `${name}(${args}`);
  });

  it('no address is written by hand in app.js: every href and every hash comes from core.js (fileHash, overviewHash, SOURCES_HASH)', () => {
    const app = code('app.js');
    assert.ok(!/['"`]#\/f\//.test(app) && !/['"`]#\/\?/.test(app) && !/['"`]#\/s\//.test(app));
    assert.ok(!/(?:href|hash)\s*=\s*['"`]#/.test(app), 'an address is assigned as a literal');
    assert.match(app, /sources\.href = SOURCES_HASH/);
  });

  it('the address is read against the list of sources before anything is shown: the old addresses are written over, never looped through', () => {
    const body = functionSource('resolveAddress');
    assert.match(body, /resolveRoute\(parseRoute\(window\.location\.hash\), state\.sources, readLastSource\(\)\)/);
    assert.match(body, /if \(resolved\.redirect !== null\) replaceAddress\(resolved\.redirect\)/);
    assert.match(body, /state\.missing = resolved\.missing/);
    assert.match(functionSource('onRoute'), /const route = resolveAddress\(\)/);
    assert.match(functionSource('loadFresh'), /const route = resolveAddress\(\)/);
    assert.ok(!/parseRoute\(window\.location\.hash\)/.test(functionSource('onRoute')), 'the raw address is not what onRoute shows');
  });

  it('the source opened last is remembered when a source that can be read is opened, and read back by the address that names no source', () => {
    assert.match(code('app.js'), /const LAST_SOURCE_KEY = 'pusula\.lastSource';/);
    assert.match(functionSource('readLastSource'), /localStorage\.getItem\(LAST_SOURCE_KEY\)[\s\S]*catch \{\s*return null;/);
    assert.match(functionSource('rememberSource'), /localStorage\.setItem\(LAST_SOURCE_KEY, id\)[\s\S]*catch \{/);
    assert.match(functionSource('openSource'), /if \(entry\.available !== false\) rememberSource\(entry\.id\)/);
  });
});

describe('a source has its own state (S1)', () => {
  it('what is remembered is keyed by the source: open folders, the files opened in this tab, and what is open or folded', () => {
    const app = code('app.js');
    for (const key of ['OPEN_DIRS_KEY', 'UI_KEY', 'RECENT_KEY']) {
      assert.ok(!new RegExp(`(?:getItem|setItem)\\(${key}\\b`).test(app), `${key} is stored without the source`);
      assert.match(app, new RegExp(`(?:getItem|setItem)\\(sourceKey\\(${key}, `), `${key} is stored through sourceKey`);
    }
  });

  it('opening a source drops what belonged to the one before and reads back its own; an answer still on its way is dropped', () => {
    const open = functionSource('openSource');
    assert.match(open, /stopEvents\(\);\s*state\.source = entry;/);
    assert.match(open, /setVariant\(isNotes\(\) \? 'note' : ''\)/);
    assert.match(open, /loadSourceState\(entry\.id\);\s*resetSourceView\(\)/);
    const reset = functionSource('resetSourceView');
    for (const field of ['state.tree = null', 'state.overview = null', 'state.file = null', 'state.lastChange = null', 'state.fresh = new Map()', 'paletteIndex = { tree: null, entries: [] }', 'renderTree()', 'renderLastChange()']) {
      assert.ok(reset.includes(field), field);
    }
    assert.match(reset, /navSeq \+= 1/);
  });

  it('the live connection is for one source: the old one is closed, and an event that is still queued when the reader has gone is dropped', () => {
    const stop = functionSource('stopEvents');
    assert.match(stop, /eventSource\.close\(\)/);
    assert.match(stop, /refs\.live\.hidden = true/);
    const connect = functionSource('connectEvents');
    assert.match(connect, /new EventSource\(sourceUrl\('events'\)\)/);
    assert.match(connect, /state\.source\.id === id \? task\(parseEvent\(event\)\) : undefined/);
    assert.match(connect, /addEventListener\('ready', forThisSource\(onReady\)\)/);
    assert.match(connect, /addEventListener\('changed', forThisSource\(onChanged\)\)/);
    assert.match(functionSource('openRoute'), /await loadRoute\(options\);\s*\}\s*ensureEvents\(\);/, 'made once there is a page, and also when the page is an error (it is what brings the page back)');
    assert.ok(!/connectEvents\(\)/.test(functionSource('boot')), 'the page opened by the address makes it');
  });

  it('a tree that arrives after the reader has gone to another source is not the page\'s', () => {
    const body = functionSource('loadTree');
    assert.match(body, /const id = state\.source\.id;/);
    assert.match(body, /if \(state\.source === null \|\| state\.source\.id !== id\) return false;/);
    for (const name of ['onChanged']) assert.match(functionSource(name), /if \(!\(await loadTree\(\)\)\) return;/, name);
  });

  it('a source that the server no longer lists is closed, and the address that names it is answered by the sources page', () => {
    const body = functionSource('refreshAll');
    assert.match(body, /!state\.sources\.some\(\(source\) => source\.id === state\.source\.id\)\) \{\s*closeSource\(\);\s*await onRoute\(\{ fresh: true \}\);/);
  });

  it('with no source there is no live connection to bring the page back: the sources are asked for again, quietly', () => {
    assert.match(functionSource('loadFresh'), /if \(state\.source === null\) scheduleRetry\(\)/);
    assert.match(functionSource('loadFresh'), /window\.clearTimeout\(retryTimer\)/);
    const retry = functionSource('scheduleRetry');
    assert.match(retry, /RECONNECT_MS/);
    assert.match(retry, /catch \{\s*scheduleRetry\(\);\s*return;/);
  });

  it('a source that cannot be read: the 503 carries a code (ProblemDetails), the page says it in words in the error box, and the sources are one link away', () => {
    assert.match(functionSource('getJson'), /const problem = await readProblem\(response\);\s*throw new ApiError\(response\.status, problem\.detail, problem\.code\)/);
    assert.ok(!/problemDetail\(/.test(code('app.js')), 'one reader of the ProblemDetails of an answer');
    const page = functionSource('errorPage');
    assert.match(page, /if \(failure\.kind === 'unavailable'\) \{/);
    assert.match(page, /sources\.href = SOURCES_HASH/);
    assert.match(page, /el\('a', 'error-link', t\('error\.toSources'\)\)/);
    const words = functionSource('failureWords');
    assert.match(words, /if \(failure\.kind !== 'unavailable'\) return t\(`error\.\$\{failure\.kind\}`, \{ status: failure\.status \}\)/);
    assert.match(words, /const code = failure\.code !== '' \? failure\.code : sourceErrorCode\(state\.source\?\.errorCode\)/, 'the code of the 503, else the list\'s');
    assert.match(words, /code === '' \? t\('error\.unavailable'\) : t\(`source\.why\.\$\{code\}`, \{ path: state\.source\?\.path \?\? '' \}\)/, 'plain when there is no code; otherwise why, with the folder in it');
  });
});

describe('the source switch and the sources page (S1)', () => {
  const switchHtml = html.match(/<button id="source-switch"[\s\S]*?<\/button>/)?.[0] ?? '';

  it('the top bar has the switch in the place of the folder\'s path: a button that opens a menu, with a drawn icon, the name and an arrow', () => {
    assert.ok(!/root-path/.test(html), 'the path of the folder is gone');
    assert.match(switchHtml, /<button id="source-switch" class="source-switch" type="button" aria-haspopup="menu" aria-expanded="false" aria-controls="source-menu">/);
    assert.match(switchHtml, /<svg class="source-icon"[^>]*aria-hidden="true"/);
    assert.match(switchHtml, /<span id="source-name" class="source-name"><\/span>/);
    assert.match(switchHtml, /<svg class="source-chevron"[^>]*aria-hidden="true"/);
    assert.ok(!/<a id="source-switch"/.test(html), 'it is no link to a page any more');
    assert.ok(html.indexOf('class="brand"') < html.indexOf('id="source-switch"') && html.indexOf('id="source-switch"') < html.indexOf('class="spacer"'));
    assert.ok(!/[▾▼⌄˅]/.test(html + css + code('app.js')), 'no glyph stands in for the arrow');
  });

  it('it is named "Kaynak: <ad> — değiştir", its tooltip is the whole path, and the brand and the overview link lead to the open source', () => {
    const body = functionSource('updateSourceSwitch');
    assert.match(body, /setAttribute\('aria-label', source === null \? t\('source\.pick'\) : t\('source\.switch', \{ name: source\.name \}\)\)/);
    assert.match(body, /refs\.sourceSwitch\.title = source === null \? t\('source\.pick'\) : source\.path/);
    assert.match(body, /const home = overviewHash\(sid\(\)\)/);
    assert.match(body, /refs\.brand\.href = home/);
    assert.match(body, /refs\.overviewLink\.href = home/);
    assert.match(body, /refs\.overviewLink\.hidden = source === null/);
    assert.match(functionSource('applyStaticI18n'), /updateSourceSwitch\(\)/, 'the other language is written at once');
    assert.match(functionSource('loadSources'), /updateSourceSwitch\(\)/);
  });

  it('the sources page is marked as the current page in the menu (the switch is no page), and the live indicator is hidden until a source is open', () => {
    assert.match(functionSource('updateChrome'), /if \(view === 'sources'\) refs\.sourceManage\.setAttribute\('aria-current', 'page'\)/);
    assert.match(html, /<a id="source-manage" class="menu-item" role="menuitem" tabindex="-1" href="#\/sources" data-i18n="source\.manage"><\/a>/);
    assert.match(html, /<span id="live" class="live live-connecting" role="status" hidden>/);
    assert.match(ownRule('.source-switch[aria-expanded="true"]') ?? '', /background: var\(--accent-soft\)/, 'the switch is marked while its menu is open');
  });

  it('the sources page is drawn when the route says so, from the list the server gave; a card is a box that holds a link, the card of a source that cannot be read holds none', () => {
    assert.match(functionSource('renderContent'), /else if \(state\.route\.view === 'sources'\) page = sourcesPage\(\)/);
    const card = functionSource('sourceCard');
    assert.match(card, /const card = el\('div', 'source-card'\)/);
    assert.match(card, /if \(!readable\) card\.classList\.add\('is-unavailable'\)/);
    assert.match(card, /const body = el\(readable \? 'a' : 'div', 'source-body'\)/);
    assert.match(card, /if \(readable\) \{\s*body\.href = overviewHash\(source\.id\);/);
    assert.match(card, /if \(open\) body\.setAttribute\('aria-current', 'true'\)/);
    assert.match(card, /el\('strong', 'source-title', source\.name\)/);
    assert.match(card, /el\('span', 'source-path', source\.path\)/);
    assert.match(card, /t\(`source\.profile\.\$\{profileName\(source\.profile\)\}`\)/);
    assert.match(card, /tn\(`source\.count\.\$\{isNoteProfile\(source\.profile\) \? 'notes' : 'files'\}`, source\.fileCount \?\? 0\)/);
    assert.match(card, /t\('source\.open'\)/);
    assert.match(card, /t\('source\.last'\)/);
    assert.match(card, /body\.append\(el\('span', 'source-error', unavailableShort\(source\.errorCode\)\)\)/, 'why a folder cannot be read comes from its code, in the page\'s words');
    assert.ok(!/\bsource\.error\b/.test(code('app.js')), 'the server\'s own sentence is English and never shown');
    assert.ok(!/innerHTML/.test(card), 'a name and a path are text');
  });

  it('an address that names a source that is not listed says so on the page', () => {
    assert.match(functionSource('sourcesPage'), /if \(state\.missing !== null\)[\s\S]*t\('sources\.missing', \{ id: state\.missing \}\)/);
  });

  it('the help is folded under "other ways" and is text and code: the file of the server when there is one (with the example, and the one sentence about the server), the command line and the vault', () => {
    const body = functionSource('sourcesHelp');
    assert.match(body, /el\('details', 'sources-help'\)/);
    assert.match(body, /help\.append\(el\('summary', null, t\('sources\.more'\)\)\)/);
    assert.match(body, /el\('code', 'sources-file', state\.sourcesFile\)/);
    assert.match(body, /help\.append\(sourcesExample\(\), el\('p', null, t\('sources\.help\.refresh'\)\)\)/);
    assert.equal([...code('app.js').matchAll(/t\('sources\.help\.refresh'\)/g)].length, 1, 'said once, in one place');
    assert.match(functionSource('sourcesExample'), /example\.append\(el\('code', null, t\('sources\.example'\)\)\)/);
    assert.match(body, /t\('sources\.help\.auto'\)/);
    assert.ok(!/\.open = true/.test(body), 'it starts folded');
    assert.ok(!/innerHTML/.test(body));
  });

  it('opening the sources page asks the server again and says nothing of any file: it clears the error that was a source\'s', () => {
    const body = functionSource('showSources');
    assert.match(body, /await loadSources\(\);\s*if \(seq !== navSeq\) return;\s*state\.error = null;/);
    assert.match(functionSource('loadRoute'), /if \(state\.route\.view === 'sources'\) return showSources\(options\)/);
    assert.match(functionSource('onChanged'), /else if \(state\.route\.view === 'sources'\) \{\s*await showSources\(\{ keepScroll: true \}\)/);
    assert.match(functionSource('pageTitle'), /t\('sources\.title'\)/);
  });

  it('the new pieces exist on both sides', () => {
    for (const name of ['sources-page', 'source-list', 'source-card', 'is-unavailable', 'is-open', 'source-title', 'source-path', 'source-meta', 'source-tag', 'source-error', 'sources-help', 'sources-example', 'error-link']) {
      assert.match(code('app.js'), new RegExp(`['\`" ]${name}['\`" ]`), `${name} is not used in app.js`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
    for (const name of ['source-switch', 'source-name', 'source-icon', 'source-chevron']) {
      assert.match(html, new RegExp(`class="${name}"`), `${name} is not in index.html`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
  });

  it('the cards and the controls of the new pages are 40px under touch', () => {
    const coarse = css.match(/@media \(pointer: coarse\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    const sized = coarse.match(/([^{}]*)\{\s*min-height: var\(--touch\);\s*\}/)?.[1] ?? '';
    for (const name of ['.source-switch', '.tag-chips .tag-chip', '.tagged-close', '.error-link', 'a.embed-card']) assert.ok(sized.includes(name), `${name} is not made touch-sized`);
  });

  it('a card is a card of the page\'s own kind: the border and the radius of the other cards, the page\'s two background tokens, no coloured side stripe', () => {
    const card = ownRule('.source-card') ?? '';
    assert.match(card, /border: 1px solid var\(--border\)/);
    assert.match(card, /border-radius: 8px/);
    assert.match(card, /background: var\(--bg-side\)/);
    assert.ok(!/border-(?:left|right)/.test(card));
    assert.match(ownRule('.source-card:not(.is-unavailable):not(.is-asking):hover, .source-card.is-open') ?? '', /border-color: var\(--accent\)/, 'the card is marked under the pointer, except one that is not a link or is asking');
  });
});

describe('a source of notes is read as notes (S1)', () => {
  it('there is one place that knows whether the page shows notes, and it asks the profile of the open source', () => {
    assert.match(functionSource('isNotes'), /state\.source !== null && isNoteProfile\(state\.source\.profile\)/);
    assert.match(functionSource('shownName'), /isNotes\(\) \? noteName\(name\) : name/);
  });

  it('the tree has no load marker, no tokens, no legend and no .md; its tooltip is the path alone', () => {
    const item = functionSource('buildTreeItem');
    assert.match(item, /if \(!isNotes\(\)\) link\.append\(loadDot\(node\.loadMode\)\)/);
    assert.match(item, /link\.append\(el\('span', 'tree-name', shownName\(node\.name\)\)\)/);
    assert.match(item, /if \(!isNotes\(\)\) link\.append\(treeTokens\(node\)\)/);
    assert.match(item, /link\.title = `\$\{node\.path\}\$\{isNotes\(\) \? '' : /);
  });

  it('the overview is its own page: the name of the source, the notes of a tag, the errors, the tags, what is to be reviewed; no budget, layers or memory', () => {
    assert.match(functionSource('overviewPage'), /if \(isNotes\(\)\) return notesOverviewPage\(data\)/);
    const body = functionSource('notesOverviewPage');
    assert.match(body, /page\.append\(notesHead\(data\)\)/);
    assert.match(body, /if \(state\.route\.tag\) page\.append\(taggedSection\(state\.route\.tag\)\)/);
    assert.match(body, /page\.append\(issuesBlock\(data\), tagsSection\(data\)\)/);
    assert.ok(body.indexOf('taggedSection') < body.indexOf('issuesBlock'), 'the notes of a tag are where the reader arrives');
    for (const gone of ['budgetSection', 'layersSection', 'memorySection', 'legendBlock']) assert.ok(!body.includes(gone), gone);
    const head = functionSource('notesHead');
    assert.match(head, /el\('h1', null, state\.source\.name\)/);
    assert.match(head, /tn\('overview\.files', data\.fileCount\), tn\('overview\.folders', folderPaths\(state\.tree\?\.nodes\)\.size\)/, 'the folders are counted from the tree');
    assert.match(head, /t\('overview\.indexed', \{ time: indexedTime\(builtAt\) \}\)/);
  });

  it('the tags: sorted by the notes they have, the first 30 and then "show more"; a chip leads to the tag\'s notes; the tag the page is filtered by is marked', () => {
    assert.match(code('app.js'), /const TAG_ROWS = 30;/);
    const section = functionSource('tagsSection');
    assert.match(section, /sortTags\(data\.tags\)/);
    assert.match(section, /limitedList\('tags', 'tag-chips', rows, TAG_ROWS\)/);
    const chip = functionSource('tagChip');
    assert.match(chip, /chip\.href = overviewHash\(sid\(\), \{ tag: name \}\)/);
    assert.match(chip, /tagKey\(state\.route\.tag\) === tagKey\(name\)/);
    assert.match(chip, /chip\.setAttribute\('aria-current', 'true'\)/);
    assert.match(chip, /el\('span', 'tag-count', String\(count\)\)/);
  });

  it('the notes of a tag come from the tree (case does not matter), under "#etiket · N not", with a link that closes the list', () => {
    const body = functionSource('taggedSection');
    assert.match(body, /filesWithTag\(flattenFiles\(state\.tree\?\.nodes\), tag\)/);
    assert.match(body, /`#\$\{tag\} · \$\{tn\('tagged\.count', notes\.length\)\}`/);
    assert.match(body, /close\.href = overviewHash\(sid\(\)\)/);
    assert.match(body, /limitedList\(`tag:\$\{tagKey\(tag\)\}`, 'tagged-list', rows\)/);
  });

  it('a tag is another list, not another place on the page: onRoute draws the overview again when the tag changes', () => {
    assert.match(functionSource('onRoute'), /if \(\(previous\.tag \?\? null\) !== \(route\.tag \?\? null\)\) renderContent\(\{ target \}\);\s*else moveTo\(target\);/);
  });

  it('a file has its tags under its name instead of how it loads; its info panel has no layer and no tokens; its title is the note\'s name', () => {
    assert.match(functionSource('filePage'), /\.\.\.\(isNotes\(\) \? tagList\(file\.tags\) : \[loadLine\(file\)\]\)/);
    assert.match(functionSource('renderInfo'), /if \(!isNotes\(\)\) \{\s*const details = infoSection\(\);/);
    assert.match(functionSource('breadcrumb'), /el\('span', 'crumb crumb-last', shownName\(part\)\)/);
    assert.match(functionSource('pageTitle'), /shownName\(baseName\(route\.path\)\)/);
    assert.match(functionSource('tagList'), /el\('ul', 'tag-chips file-tags'\)/);
  });

  it('the #tags in the text of a note that are its tags are chips: in text nodes only, never in code, links or chips', () => {
    assert.match(functionSource('enhance'), /if \(isNotes\(\)\) linkInlineTags\(root, file\.tags\);/);
    const body = functionSource('linkInlineTags');
    assert.match(body, /closest\('pre, code, a, \.md-link, \.tag-chip'\) === null/);
    assert.match(body, /findInlineTags\(text, tags\)/);
    assert.match(body, /tagChip\(match\.tag, \{ label: match\.raw \}\)/);
    assert.ok(!/innerHTML/.test(body), 'a tag is text');
  });

  it('![[x]] is a card alone in its paragraph (it keeps the line the paragraph had), a link in the line otherwise, to the note; what is not created yet or is not Markdown says so', () => {
    assert.match(functionSource('linkWikilinks'), /if \(anchor\.hasAttribute\('data-embed'\)\) \{\s*placeEmbed\(anchor, raw, links\);\s*continue;/);
    const body = functionSource('placeEmbed');
    assert.match(body, /findWikiLink\(links, raw, 'Embed'\)/);
    assert.match(body, /document\.createTextNode\(`!\[\[\$\{raw\}\]\]`\)/, 'what the server did not report stays as it was written');
    assert.match(body, /enumName\(link\.status\) === 'NonMarkdown'/);
    assert.match(body, /embedAssetKind\(link\.target \?\? name\) === 'image' \? t\('md\.image', \{ alt: name \}\) : t\('md\.file', \{ name \}\)/);
    assert.match(body, /el\('span', 'md-image'/, 'the placeholder of an image');
    assert.match(body, /paragraph\.tagName === 'P'/);
    assert.match(body, /makeLink\(link, \[icon, name\], 'embed-link'\)/);
    assert.match(body, /makeLink\(link, \[icon, el\('span', 'embed-name', name\), kind\], 'embed-card'\)/);
    assert.match(body, /status === 'Resolved' \? t\('embed\.kind'\) : t\(`status\.\$\{status\}`\)/, 'not created yet reads "henüz oluşturulmamış not" in a source of notes');
    assert.match(body, /card\.dataset\.line = paragraph\.dataset\.line/);
    assert.match(body, /paragraph\.replaceWith\(card\)/);
    assert.ok(!/fetch|getJson|innerHTML/.test(body), 'nothing is asked for and nothing is made of markup: the folder is not served');
    assert.match(code('app.js'), /refs\.embedIcon = document\.getElementById\('embed-icon-template'\)/);
    const template = html.match(/<template id="embed-icon-template">([\s\S]*?)<\/template>/)?.[1] ?? '';
    assert.match(template, /<svg class="embed-icon"[^>]*aria-hidden="true"/, 'a drawn icon');
  });

  it('a project is a Claude configuration\'s: the labels, the order of projects/ and the skill-first path are left out for notes', () => {
    assert.match(functionSource('pathLink'), /skillFirst && !isNotes\(\) \? skillFile\(path\) : null/);
    assert.match(functionSource('treeChildren'), /path === 'projects' && !isNotes\(\)/);
    assert.match(functionSource('loadTree'), /isNotes\(\) \? new Map\(\)/);
  });

  it('the quick opener of notes: no load marker in a row, the names without .md', () => {
    assert.match(functionSource('quickOption'), /if \(!isNotes\(\)\) option\.append\(loadDot\(entry\.loadMode, \{ decorative: true \}\)\)/);
    assert.match(functionSource('quickIndex'), /\{ notes: isNotes\(\) \}/);
  });

  it('the new pieces exist on both sides', () => {
    for (const name of ['ov-tags', 'ov-tagged', 'tag-chips', 'tag-chip', 'tag-count', 'is-current', 'file-tags', 'tagged-head', 'tagged-close', 'tagged-list', 'embed-card', 'embed-link', 'embed-name', 'embed-kind']) {
      assert.match(code('app.js'), new RegExp(`['\`" ]${name}['\`" ]`), `${name} is not used in app.js`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
    assert.match(css, /\.embed-icon\s*\{/);
  });

  it('the notes overview sits in the same grid as the other: the notes of a tag, where to start and the tags in the places of the budget, the layers and the memory, in one column in reading order', () => {
    const wide = css.match(/@container content \(min-width: 1100px\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    assert.match(wide, /\.ov-tagged\s*\{\s*grid-area: budget;/);
    assert.match(wide, /\.ov-front\s*\{\s*grid-area: layers;/);
    assert.match(wide, /\.ov-tags\s*\{\s*grid-area: memory;/);
    const orderOf = (cls) => Number(new RegExp(`(?:^|\\n)\\.${cls}\\s*\\{\\s*order:\\s*(\\d+);`).exec(css)?.[1]);
    const [head, tagged, front, errors, tags, review] = ['ov-head', 'ov-tagged', 'ov-front', 'ov-errors', 'ov-tags', 'ov-review'].map(orderOf);
    assert.ok(head < tagged && tagged < front && front < errors && errors < tags && tags < review, 'head, the notes of a tag, where to start, errors, tags, what is to be reviewed');
  });

  it('the chips and the cards are drawn with the tokens of the page: the text colour on a chip, no accent text on the accent tint, no red', () => {
    for (const selector of ['.tag-chip', '.embed-card']) {
      const body = ownRule(selector) ?? '';
      assert.match(body, /color: var\(--text\)/, selector);
      assert.ok(!/--danger/.test(body), selector);
    }
    assert.match(ownRule('.tag-chip.is-current .tag-count') ?? '', /color: var\(--text\)/, 'the quiet grey is under 4.5:1 on the accent tint in the dark theme');
    assert.ok(!/border-(?:left|right)/.test(ownRule('.embed-card') ?? ''), 'no coloured side stripe');
  });
});

describe('colour of the source pieces (WCAG contrast of the real tokens) (S1)', () => {
  for (const [theme, tokens] of [['light', lightTokens], ['dark', darkTokens]]) {
    const color = (name) => parseColor(tokens[name]);
    const bg = color('--bg');
    const side = color('--bg-side');

    it(`${theme}: a tag chip: its quiet count is 4.5:1 on the page and under the pointer; on the filtered chip (the accent tint) the text colour is`, () => {
      assert.ok(contrast(color('--muted'), bg) >= 4.5, 'count on the page');
      assert.ok(contrast(color('--muted'), over(color('--hover'), bg)) >= 4.5, 'count under the pointer');
      assert.ok(contrast(color('--text'), over(color('--hover'), bg)) >= 4.5, 'name under the pointer');
      assert.ok(contrast(color('--text'), over(color('--accent-soft'), bg)) >= 4.5, 'the filtered chip, and "open" on a card');
      assert.ok(contrast(color('--text'), over(color('--accent-soft'), side)) >= 4.5, 'the switch of the top bar on the sources page');
    });

    it(`${theme}: a source card: its path, its profile and its count (the quiet grey) and its red line are 4.5:1 on the card; its edge in the accent is 3:1`, () => {
      assert.ok(contrast(color('--muted'), side) >= 4.5, 'the quiet grey on a card');
      assert.ok(contrast(color('--danger'), side) >= 4.5, 'why a folder cannot be read');
      assert.ok(contrast(color('--accent'), side) >= 3, 'the open card\'s edge');
    });

    it(`${theme}: an embedded note's kind (the quiet grey) is 4.5:1 on its card, and the example of the help is text on code`, () => {
      assert.ok(contrast(color('--muted'), side) >= 4.5);
      assert.ok(contrast(color('--text'), color('--code-bg')) >= 4.5);
      assert.ok(contrast(color('--muted'), bg) >= 4.5, 'the help text on the page');
    });
  }
});

// ---------------------------------------------------------------------------------------------
// Sources: adding and removing them from the page (S2)
// ---------------------------------------------------------------------------------------------

/** The whole of the function `name` of app.js, async or not: from its declaration to the closing brace at the left margin (not up to the next plain function, as `functionSource` does). */
function wholeFunction(name) {
  const app = code('app.js');
  const start = app.indexOf(`function ${name}(`);
  assert.ok(start >= 0, `no function ${name}`);
  const end = app.indexOf('\n}\n', start);
  assert.ok(end > start, `no end of function ${name}`);
  return app.slice(start, end + 2);
}

describe('what the server says about editing, and the list asked for again (S2)', () => {
  it('it comes with the list: loadSources reads it with sourcesAccess, and the page is closed until the server says otherwise', () => {
    assert.match(wholeFunction('loadSources'), /state\.sourcesAccess = sourcesAccess\(data\);/);
    assert.match(code('app.js'), /sourcesAccess: \{ canEdit: false, closed: null, fileError: null \}/);
  });

  it('every opening of the sources page asks the server for the list again, and a question or a refusal that was on screen does not outlive the visit', () => {
    const show = wholeFunction('showSources');
    assert.match(show, /await loadSources\(\);/);
    assert.match(show, /if \(!keepScroll\) \{\s*state\.sourceEdit\.asking = null;\s*state\.sourceEdit\.addError = null;\s*state\.sourceEdit\.removeError = null;\s*\}/);
    assert.match(wholeFunction('openRoute'), /if \(route\.view === 'sources'\) \{\s*await showSources\(options\);/);
    assert.match(wholeFunction('loadRoute'), /if \(state\.route\.view === 'sources'\) return showSources\(options\)/);
    assert.match(wholeFunction('getJson'), /cache: 'no-store'/, 'the answer is not the browser\'s old one');
  });

  it('a card that asks for the removal to be confirmed, when the list no longer has its source, stops asking', () => {
    assert.match(wholeFunction('loadSources'), /if \(asking !== null && !state\.sources\.some\(\(source\) => source\.id === asking\)\) state\.sourceEdit\.asking = null;/);
  });
});

describe('the cards and the removal of a source (S2)', () => {
  it('a card is a box that holds the link and, beside it, a button: never a button inside a link', () => {
    const card = wholeFunction('sourceCard');
    assert.match(card, /const editing = state\.sourcesAccess\.canEdit;/);
    assert.match(card, /const asking = editing && state\.sourceEdit\.asking === source\.id;/);
    assert.match(card, /body\.append\(main, meta\);/);
    assert.match(card, /card\.append\(body\);\s*if \(editing\) card\.append\(asking \? removeConfirm\(source\) : removeButton\(source\)\);/);
    assert.ok(!/body\.append\([^;]*(?:removeButton|removeConfirm)/.test(card), 'the controls are the card\'s, not the link\'s');
    assert.match(card, /if \(asking\) card\.classList\.add\('is-asking'\)/);
    assert.match(card, /body\.dataset\.keep = `open:\$\{source\.id\}`/, 'the keyboard keeps its place on a card when the page is drawn again');
  });

  it('the way to remove is a quiet button named by its source (every card has one), and it asks nothing yet', () => {
    const body = wholeFunction('removeButton');
    assert.match(body, /el\('button', 'source-remove', t\('sources\.remove'\)\)/);
    assert.match(body, /button\.type = 'button'/);
    assert.match(body, /setAttribute\('aria-label', t\('sources\.remove\.named', \{ name: source\.name \}\)\)/);
    assert.match(body, /addEventListener\('click', \(\) => askRemove\(source\.id\)\)/);
  });

  it('the question is inside the card (the browser\'s own dialog is not used): a group named by its question, the two answers in the order Kaldır, Vazgeç, and why when it was refused', () => {
    const app = code('app.js');
    assert.ok(!/\bconfirm\(/.test(app) && !/\balert\(/.test(app) && !/\bprompt\(/.test(app), 'a dialog of the browser');
    const body = wholeFunction('removeConfirm');
    assert.match(body, /group\.setAttribute\('role', 'group'\)/);
    assert.match(body, /group\.setAttribute\('aria-labelledby', `source-ask-\$\{source\.id\}`\)/);
    assert.match(body, /ask\.id = `source-ask-\$\{source\.id\}`/);
    assert.match(body, /el\('p', 'source-ask', t\('sources\.remove\.ask'\)\)/);
    assert.match(body, /el\('button', 'button is-danger', edit\.removing \? t\('sources\.remove\.busy'\) : t\('sources\.remove'\)\)/);
    assert.match(body, /el\('button', 'button is-quiet', t\('sources\.remove\.cancel'\)\)/);
    assert.ok(body.indexOf("'button is-danger'") < body.indexOf("'button is-quiet'"));
    assert.match(body, /yes\.disabled = edit\.removing;/);
    assert.match(body, /no\.disabled = edit\.removing;/);
    assert.match(body, /el\('p', 'source-problem', failureText\(edit\.removeError\)\);\s*problem\.setAttribute\('role', 'alert'\)/);
    assert.match(body, /event\.key !== 'Escape' \|\| edit\.removing\) return;\s*event\.preventDefault\(\);\s*cancelRemove\(source\.id\)/, 'Escape cancels');
  });

  it('asking puts the keyboard on the answer that does nothing; cancelling gives it back to the button that asked; one card asks at a time, and none while a request is on its way', () => {
    const ask = wholeFunction('askRemove');
    assert.match(ask, /if \(edit\.removing\) return;\s*edit\.asking = id;\s*edit\.removeError = null;\s*redrawSources\(\);\s*focusKept\(`no:\$\{id\}`\)/);
    const cancel = wholeFunction('cancelRemove');
    assert.match(cancel, /if \(edit\.removing\) return;\s*edit\.asking = null;\s*edit\.removeError = null;\s*redrawSources\(\);\s*focusKept\(`remove:\$\{id\}`\)/);
  });

  it('removing sends a DELETE and reads the list again; a 404 is what was asked for; a refusal is said in the card, which keeps asking', () => {
    const body = wholeFunction('removeSource');
    assert.match(body, /if \(edit\.removing\) return;/);
    assert.match(body, /await editSources\('DELETE', sourceItemUrl\(id\)\);/);
    assert.match(body, /if \(!\(error instanceof ApiError && error\.status === 404\)\) \{/);
    assert.match(body, /edit\.removeError = sourceFailure\(error, \{ file: state\.sourcesFile \}\);\s*redrawSources\(\);\s*focusKept\(`yes:\$\{id\}`\);\s*return;/);
    assert.match(body, /await loadSources\(\);\s*\} catch \{\s*state\.sources = state\.sources\.filter\(\(source\) => source\.id !== id\);/, 'the server has removed it, whatever it says now');
  });

  it('what the page kept of a removed source goes: it is not the one opened last, and when it was the open one its tree, pages and live connection are dropped before the list is read; the reader stays', () => {
    const body = wholeFunction('removeSource');
    assert.match(body, /forgetSource\(id\);/);
    assert.match(body, /if \(state\.source !== null && state\.source\.id === id\) closeSource\(\);/);
    assert.ok(body.indexOf('closeSource()') < body.indexOf('await loadSources()'), 'closed before the server cuts its connection: no "offline" flash');
    assert.ok(!/location\.hash|overviewHash|onRoute|history\./.test(body), 'it stays where it is');
    assert.match(body, /if \(state\.route\.view !== 'sources'\) return;\s*redrawSources\(\);\s*showToast\(t\('sources\.removed', \{ name \}\)\);/, 'seen, and heard: the region of the toasts is a live one');
    assert.ok(!/announce\(t\('sources\.removed'/.test(body), 'said once, not twice to a screen reader');
    assert.match(body, /querySelector\('a\.source-body, button\.source-remove'\)/, 'the keyboard goes to the card that took its place, or to the form when there is none');
    assert.match(body, /\[data-keep="path"\]/);
    const closing = wholeFunction('closeSource');
    assert.match(closing, /stopEvents\(\);\s*state\.source = null;/);
    assert.match(closing, /resetSourceView\(\)/);
  });

  it('the source opened last is forgotten only when it is the one that was removed, and blocked storage is no error', () => {
    const body = wholeFunction('forgetSource');
    assert.match(body, /if \(localStorage\.getItem\(LAST_SOURCE_KEY\) === id\) localStorage\.removeItem\(LAST_SOURCE_KEY\)/);
    assert.match(body, /catch \{/);
  });
});

describe('the form that adds a source (S2)', () => {
  it('the form: the path (in the mono face), an optional name and the button; the boxes keep what is typed and the button is locked, and says so, while the request is on its way', () => {
    const form = wholeFunction('addSourceForm');
    assert.match(form, /el\('form', lock === null \? 'source-add' : 'source-add is-locked'\)/);
    assert.match(form, /form\.noValidate = true/);
    assert.match(form, /sourceField\('path', \{ label: t\('sources\.add\.path'\), placeholder: t\('sources\.add\.pathPlaceholder'\), mono: true \}\)/);
    assert.match(form, /sourceField\('name', \{ label: t\('sources\.add\.name'\), hint: t\('sources\.add\.optional'\) \}\)/);
    assert.match(form, /el\('button', 'button source-submit', edit\.adding \? t\('sources\.add\.busy'\) : t\('sources\.add\.submit'\)\)/);
    assert.match(form, /submit\.type = 'submit';\s*submit\.disabled = edit\.adding;/);
    assert.match(form, /if \(edit\.adding\) form\.setAttribute\('aria-busy', 'true'\)/);
    assert.match(form, /form\.addEventListener\('submit', \(event\) => \{\s*event\.preventDefault\(\);\s*addSource\(\);/, 'Enter in a box sends it');
    assert.ok(form.indexOf("sourceField('path'") < form.indexOf("sourceField('name'") && form.indexOf("sourceField('name'") < form.indexOf('submit,'));
  });

  it('a refusal is a line under the boxes that is an alert, and it describes both boxes; the box it is about is marked invalid', () => {
    const form = wholeFunction('addSourceForm');
    assert.match(form, /el\('p', 'source-add-error', failure === null \? '' : failureText\(failure\)\);\s*error\.id = 'source-add-error';\s*error\.setAttribute\('role', 'alert'\)/);
    const field = wholeFunction('sourceField');
    assert.match(field, /if \(failure !== null\) input\.setAttribute\('aria-describedby', 'source-add-error'\)/);
    assert.match(field, /if \(failure !== null && failure\.field === name\) input\.setAttribute\('aria-invalid', 'true'\)/);
  });

  it('the boxes: no autocomplete, no capitalisation, no correction, no spell-check; a label for each; what is typed is kept as it is typed; the browser judges nothing', () => {
    const field = wholeFunction('sourceField');
    assert.match(field, /input\.autocomplete = 'off'/);
    assert.match(field, /input\.spellcheck = false/);
    assert.match(field, /setAttribute\('autocapitalize', 'off'\)/);
    assert.match(field, /setAttribute\('autocorrect', 'off'\)/);
    assert.match(field, /mono \? 'source-input is-mono' : 'source-input'/);
    assert.match(field, /input\.type = 'text'/);
    assert.match(field, /caption\.htmlFor = `source-add-\$\{name\}`;/);
    assert.match(field, /input\.id = `source-add-\$\{name\}`;/);
    assert.match(field, /input\.value = state\.sourceEdit\[name\]/);
    assert.match(field, /addEventListener\('input', \(\) => \{\s*state\.sourceEdit\[name\] = input\.value;/);
    assert.match(field, /input\.dataset\.keep = name/);
    assert.ok(!/required|maxLength|minLength|pattern/.test(wholeFunction('addSourceForm') + field), 'a path is the server\'s to judge');
  });

  it('no id the page builds is an id of index.html: a label for a box must not find the top bar (the name of the source has one)', () => {
    const fixed = new Set([...html.matchAll(/\sid="([^"]+)"/g)].map((match) => match[1]));
    assert.ok(fixed.has('source-name'), 'the top bar has it');
    const built = [...code('app.js').matchAll(/\b\w+\.id = ([^;]+);/g)].map((match) => match[1].replace(/^[`']|[`']$/g, ''));
    assert.ok(built.some((id) => id.startsWith('source-add-')), built.join(' | '));
    for (const expression of built) {
      const ids = expression.includes('${name}') ? ['path', 'name'].map((name) => expression.replace('${name}', name)) : [expression];
      for (const id of ids) assert.ok(!fixed.has(id), `${id} is also an id of index.html`);
    }
  });

  it('adding sends a POST of what is typed, locked; the list is read again, the new source is the one opened last, and the reader is taken to it', () => {
    const body = wholeFunction('addSource');
    assert.match(body, /if \(edit\.adding\) return;\s*edit\.adding = true;\s*edit\.addError = null;\s*redrawSources\(\);/);
    assert.match(body, /source = await editSources\('POST', '\/api\/sources', addSourceBody\(edit\.path, edit\.name\)\);/);
    assert.match(body, /typeof source\?\.id !== 'string' \|\| source\.id === ''\) throw/, 'an answer that names no source is not one');
    assert.match(body, /edit\.path = '';\s*edit\.name = '';/);
    assert.match(body, /await loadSources\(\);/);
    assert.match(body, /rememberSource\(source\.id\);/);
    assert.match(body, /window\.location\.hash = overviewHash\(source\.id\);/);
    assert.ok(body.indexOf('await loadSources()') < body.indexOf('rememberSource(') && body.indexOf('rememberSource(') < body.indexOf('window.location.hash'), 'the list, then the last source, then the page');
    assert.match(body, /if \(state\.route\.view !== 'sources'\) return;/, 'a reader who has gone elsewhere is not taken anywhere');
    assert.match(body, /savePosition\(\);[^\n]*\n\s*window\.location\.hash/, 'Back returns to the place on the sources page');
  });

  it('a refusal to add is said under the boxes and the keyboard goes back to the path box, whichever box it is about', () => {
    const body = wholeFunction('addSource');
    assert.match(body, /edit\.adding = false;\s*edit\.addError = sourceFailure\(error, \{ file: state\.sourcesFile \}\);\s*redrawSources\(\);\s*focusKept\('path'\);\s*return;/);
  });

  it('a list that cannot be read after the server took the new source is no reason to lose it: the answer of the server is used', () => {
    assert.match(wholeFunction('addSource'), /await loadSources\(\);\s*\} catch \{\s*state\.sources = \[\.\.\.state\.sources\.filter\(\(entry\) => entry\.id !== source\.id\), source\];/);
  });
});

describe('the writes to the server (S2)', () => {
  it('one function sends them: JSON in, the code of a ProblemDetails out, and a 204 has no body to read', () => {
    const body = wholeFunction('editSources');
    assert.match(body, /headers\['Content-Type'\] = 'application\/json'/);
    assert.match(body, /fetch\(url, \{ method, headers, body: body === undefined \? undefined : JSON\.stringify\(body\) \}\)/);
    assert.match(body, /throw new ApiError\(response\.status, problem\.detail, problem\.code\)/);
    assert.match(body, /response\.status === 204 \? null : response\.json\(\)/);
    const problem = wholeFunction('readProblem');
    assert.match(problem, /typeof problem\?\.code === 'string' \? problem\.code : ''/);
    assert.match(problem, /catch \{\s*return \{ detail: '', code: '' \};/);
    assert.match(code('app.js'), /constructor\(status, detail = '', code = ''\) \{[\s\S]*?this\.code = code;/);
  });

  it('a refusal is said in the words of its kind: the line of a closed page, its own words, or the words of a page that did not load', () => {
    const body = wholeFunction('failureText');
    assert.match(body, /if \(kind === 'Remote' \|\| kind === 'CommandLine'\) return lockedText\(\{ kind, machine: state\.host\.machine \}\)/);
    assert.match(body, /SOURCE_REFUSALS\.includes\(kind\) \? t\(`sources\.error\.\$\{kind\}`, params\) : t\(`error\.\$\{kind\}`, params\)/);
  });
});

describe('the box of a page that cannot change the list, and the warning of a damaged file (S2)', () => {
  it('the form is under the cards in every state, and where the list cannot be changed it has no remove button; a server that says nothing is locked too', () => {
    assert.match(wholeFunction('sourcesPage'), /page\.append\(sourceList\(\), addSourceForm\(\), sourcesHelp\(\)\)/, 'the form, in its place, under the cards; the help under that');
    assert.ok(!/sourceEditing|closedNote/.test(code('app.js')), 'no state draws something else in its place');
    assert.match(wholeFunction('sourceCard'), /if \(editing\) card\.append/, 'no button on a page that cannot change the list');
    assert.match(wholeFunction('currentLock'), /return sourceLock\(state\.sourcesAccess, state\.host, state\.sourcesFile\)/);
  });

  it('a damaged sources file is a warning above the list, below the one that says a source is not listed', () => {
    const body = wholeFunction('sourcesPage');
    assert.match(body, /if \(state\.sourcesAccess\.fileError !== null\) \{[\s\S]*?t\('sources\.fileError', \{ error: state\.sourcesAccess\.fileError \}\)/);
    assert.match(body, /notice\.setAttribute\('role', 'status'\)/);
    assert.ok(body.indexOf('state.missing !== null') < body.indexOf('state.sourcesAccess.fileError') && body.indexOf('state.sourcesAccess.fileError') < body.indexOf('sourceList()'));
  });
});

describe('a page that is drawn again keeps the keyboard where it was (S2)', () => {
  it('the field or button of the sources page that has the focus gets it back, with its caret, once the new page is there; a locked one cannot', () => {
    const render = wholeFunction('renderContent');
    assert.match(render, /const kept = keepScroll \? keptFocus\(\) : null;/);
    assert.match(render, /if \(kept\) focusKept\(kept\.key, kept\);/);
    assert.ok(render.indexOf('replaceChildren') < render.indexOf('focusKept(kept.key, kept)'), 'after the new page is there');
    assert.match(wholeFunction('keptFocus'), /!node\.dataset\.keep\) return null/);
    assert.match(wholeFunction('keptFocus'), /node\.selectionStart \?\? null/);
    const focus = wholeFunction('focusKept');
    assert.match(focus, /node\.disabled\) return;/);
    assert.match(focus, /node\.focus\(\{ preventScroll: true \}\)/);
    assert.match(focus, /node\.setSelectionRange\(caret\.start, caret\.end\)/);
    const app = code('app.js');
    for (const key of ['`open:${source.id}`', '`remove:${source.id}`', '`yes:${source.id}`', '`no:${source.id}`', "'add'", 'name']) assert.ok(app.includes(`dataset.keep = ${key}`), key);
  });

  it('a request that ends elsewhere leaves the page the reader has gone to alone', () => {
    assert.match(wholeFunction('redrawSources'), /if \(state\.route\.view === 'sources' && !state\.error\) renderContent\(\{ keepScroll: true \}\)/);
  });
});

describe('a source that the server took out of the list is left (S2)', () => {
  it('when the live connection is refused the list is asked before it is tried again; a source that is gone is left for the sources page, which names it', () => {
    assert.match(wholeFunction('connectEvents'), /reconnectTimer = window\.setTimeout\(reconnectEvents, RECONNECT_MS\)/);
    const body = wholeFunction('reconnectEvents');
    assert.match(body, /const id = eventsFor;\s*try \{\s*await loadSources\(\);\s*\} catch \{/);
    assert.match(body, /if \(state\.source === null \|\| state\.source\.id !== id\) return;/, 'a reader who has gone elsewhere');
    assert.match(body, /if \(!state\.sources\.some\(\(source\) => source\.id === id\)\) \{\s*await refreshAll\(\);\s*return;\s*\}\s*connectEvents\(\);/);
    assert.match(wholeFunction('refreshAll'), /closeSource\(\);\s*await onRoute\(\{ fresh: true \}\);/);
    assert.match(wholeFunction('stopEvents'), /window\.clearTimeout\(reconnectTimer\)/, 'a source that was left does not leave a timer behind');
  });
});

describe('the cards and the form: style (S2)', () => {
  it('the new pieces exist on both sides', () => {
    const names = [
      'source-body', 'source-actions', 'source-remove', 'source-confirm', 'source-ask', 'source-confirm-actions', 'source-problem', 'is-asking',
      'source-add', 'source-fields', 'field-hint', 'source-input', 'is-mono', 'source-submit', 'source-add-error',
      'source-file-error', 'is-danger', 'is-quiet',
    ];
    for (const name of names) {
      assert.match(code('app.js'), new RegExp(`['\`" ]${name}['\`" ]`), `${name} is not used in app.js`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
    assert.match(code('app.js'), /`field field-\$\{name\}`/);
    for (const name of ['field', 'field-path', 'field-name']) assert.match(css, new RegExp(`\\.${name}\\b`), name);
  });

  it('the whole card is still what is clicked: the link\'s ::after is as big as the card, the keyboard\'s mark goes round the card, and what has to be clicked on its own is above it', () => {
    assert.match(ownRule('.source-card') ?? '', /position: relative/);
    assert.match(ownRule('a.source-body::after') ?? '', /content: ""; position: absolute; inset: -1px; border-radius: 8px/);
    assert.match(ownRule('a.source-body:focus-visible') ?? '', /outline: none/);
    assert.match(ownRule('a.source-body:focus-visible::after') ?? '', /outline: 2px solid var\(--accent\)/);
    for (const selector of ['.source-actions', '.source-confirm']) assert.match(ownRule(selector) ?? '', /position: relative; z-index: 1/, `${selector} sits above the link`);
    assert.match(ownRule('.source-body') ?? '', /text-decoration: none/);
    assert.ok(!/border-(?:left|right)/.test(ownRule('.source-confirm') ?? ''), 'no coloured side stripe');
  });

  it('the card keeps its look where the list cannot be changed: one column, so nothing narrows its text', () => {
    const card = ownRule('.source-card') ?? '';
    assert.match(card, /grid-template-columns: minmax\(0, 1fr\) auto/);
    assert.match(card, /gap: 4px 0/, 'no gap between columns: an empty one takes no room');
    assert.match(ownRule('.source-actions') ?? '', /margin-left: 12px/);
  });

  it('the quiet button is as quiet as the rest of the card, and the two answers are the page\'s own buttons: the danger colour for the one that removes', () => {
    const quiet = ownRule('.source-remove') ?? '';
    assert.match(quiet, /background: none/);
    assert.match(quiet, /color: var\(--muted\)/);
    assert.match(ownRule('.source-remove:hover') ?? '', /color: var\(--text\)/, 'the quiet grey is under 4.5:1 on the hover tint in the dark theme');
    assert.match(ownRule('.button.is-danger') ?? '', /border-color: var\(--danger\); background: var\(--danger-bg\)/);
    assert.match(ownRule('.button.is-quiet') ?? '', /background: none/);
    assert.match(ownRule('.button:disabled') ?? '', /cursor: default/);
  });

  it('the boxes: an edge of the quiet grey (3:1), the page\'s background, the page\'s text; the placeholder and the hint in the quiet grey; the refusals in the danger colour', () => {
    const input = ownRule('.source-input') ?? '';
    assert.match(input, /border: 1px solid var\(--muted\)/);
    assert.match(input, /background: var\(--bg\)/);
    assert.match(input, /color: var\(--text\)/);
    assert.match(ownRule('.source-input::placeholder') ?? '', /color: var\(--muted\)/);
    assert.match(ownRule('.source-input.is-mono') ?? '', /font-family: var\(--mono\)/);
    assert.match(ownRule('.source-input[aria-invalid="true"]') ?? '', /border-color: var\(--danger\)/);
    assert.match(ownRule('.field-hint') ?? '', /color: var\(--muted\)/);
    for (const selector of ['.source-add-error', '.source-problem']) assert.match(ownRule(selector) ?? '', /color: var\(--danger\)/, selector);
    assert.match(ownRule('.source-add-error:not(:empty)') ?? '', /margin-top: 8px/, 'no room is taken until there is something to say');
  });

  it('under touch the new controls are 40px: the quiet button, the buttons (the answers and the form\'s), the boxes; the boxes are 16px so a phone does not zoom into them', () => {
    const coarse = css.match(/@media \(pointer: coarse\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    const sized = coarse.match(/([^{}]*)\{\s*min-height: var\(--touch\);\s*\}/)?.[1] ?? '';
    for (const name of ['.button', '.source-remove']) assert.ok(sized.includes(name), `${name} is not made touch-sized`);
    assert.match(coarse, /\.source-input\s*\{\s*min-height: var\(--touch\);\s*font-size: 16px;\s*\}/);
  });

  it('the button of the form is as wide as its longest word, so the boxes beside it do not move when it says it is busy', () => {
    assert.match(ownRule('.source-submit') ?? '', /min-width: 6\.5rem/);
  });
});

describe('colour of the controls of the sources page (WCAG contrast of the real tokens) (S2)', () => {
  for (const [theme, tokens] of [['light', lightTokens], ['dark', darkTokens]]) {
    const color = (name) => parseColor(tokens[name]);
    const bg = color('--bg');
    const side = color('--bg-side');

    it(`${theme}: "Kaldır" is the quiet grey on a card, and the text colour under the pointer: 4.5:1 on both`, () => {
      assert.ok(contrast(color('--muted'), side) >= 4.5, 'the quiet grey on a card');
      assert.ok(contrast(color('--text'), over(color('--hover'), side)) >= 4.5, 'the text colour on the hover tint');
    });

    it(`${theme}: the answers: the text colour on the tint of the danger fill, on the hover tint and on the card, 4.5:1; their edges (the danger colour, the quiet grey) are 3:1 on the card`, () => {
      assert.ok(contrast(color('--text'), over(color('--danger-bg'), side)) >= 4.5, 'Kaldır');
      assert.ok(contrast(color('--text'), side) >= 4.5, 'Vazgeç, and the question');
      assert.ok(contrast(color('--danger'), side) >= 3, 'the edge of Kaldır');
      assert.ok(contrast(color('--muted'), side) >= 3, 'the edge of Vazgeç');
    });

    it(`${theme}: a box: its edge is 3:1 on the card and on the page, its text and its placeholder 4.5:1 on its own background; the edge of a box that is refused is 3:1`, () => {
      assert.ok(contrast(color('--muted'), side) >= 3 && contrast(color('--muted'), bg) >= 3, 'the edge');
      assert.ok(contrast(color('--text'), bg) >= 4.5, 'what is typed');
      assert.ok(contrast(color('--muted'), bg) >= 4.5, 'the placeholder');
      assert.ok(contrast(color('--danger'), bg) >= 3 && contrast(color('--danger'), side) >= 3, 'the edge of a box that is refused');
    });

    it(`${theme}: the form on the card's background: the hint (quiet grey) and the refusal (danger colour) are 4.5:1 on it, and the button's text is on the accent tint`, () => {
      assert.ok(contrast(color('--muted'), side) >= 4.5, 'optional');
      assert.ok(contrast(color('--danger'), side) >= 4.5, 'the line under the boxes');
      assert.ok(contrast(color('--text'), over(color('--accent-soft'), side)) >= 4.5, 'Ekle');
    });

    it(`${theme}: the note of a page that cannot change the list is the text colour on the page, and its code is the text colour on the code background`, () => {
      assert.ok(contrast(color('--text'), bg) >= 4.5);
      assert.ok(contrast(color('--text'), color('--code-bg')) >= 4.5);
    });
  }
});

// ---------------------------------------------------------------------------------------------
// Critique round: the sources page, the menu of sources, a folder that cannot be read, where to start in a vault, polish
// ---------------------------------------------------------------------------------------------

describe('the "add a folder" box is the same in every state (P1)', () => {
  it('it is drawn under the same title in the same place whatever the browser may do: the only difference is a fieldset that is disabled', () => {
    const form = wholeFunction('addSourceForm');
    assert.match(form, /const title = el\('h2', null, t\('sources\.add\.title'\)\);\s*title\.id = 'source-add-title';/);
    assert.match(form, /if \(lock === null\) \{\s*form\.append\(title, fields, error\);\s*\} else \{/);
    assert.match(form, /const inactive = el\('fieldset', 'source-set'\);\s*inactive\.disabled = true;\s*inactive\.append\(fields\);/);
    assert.match(form, /form\.append\(title, \.\.\.lockNotice\(lock\), inactive\)/, 'the title, then the line that says why, then the boxes');
    assert.equal([...code('app.js').matchAll(/(?<!function )\baddSourceForm\(\)/g)].length, 1, 'one place draws it');
  });

  it('where the list cannot be changed the line at the top of the box has a drawn lock and the words of the reason; under it the ways that are left', () => {
    const notice = wholeFunction('lockNotice');
    assert.match(notice, /el\('div', 'source-lock'\)/);
    assert.match(notice, /refs\.lockIcon\.content\.firstElementChild\.cloneNode\(true\), el\('p', null, lockedText\(lock\)\)/);
    assert.match(notice, /return ways === null \? \[notice\] : \[notice, ways\]/);
    const template = html.match(/<template id="lock-icon-template">([\s\S]*?)<\/template>/)?.[1] ?? '';
    assert.match(template, /<svg class="lock-icon"[^>]*aria-hidden="true"/, 'a drawn icon');
    assert.match(code('app.js'), /refs\.lockIcon = document\.getElementById\('lock-icon-template'\)/);
    assert.ok(!/[🔒🔐]/.test(html + css + code('app.js')), 'no glyph stands in for the lock');
  });

  it('over the network it says which machine to open the page on; a server that names none says "the computer pusula runs on"', () => {
    const body = wholeFunction('lockedText');
    assert.match(body, /if \(kind === 'Remote'\) return machine === null \? t\('sources\.locked\.RemoteAnon'\) : t\('sources\.locked\.Remote', \{ machine \}\)/);
    assert.match(body, /return t\(`sources\.locked\.\$\{kind\}`\)/, 'the command line, and a server that does not say why');
    const ways = wholeFunction('sourceWays');
    assert.match(ways, /lock\.machine === null \? t\('sources\.way\.hereAnon'\) : t\('sources\.way\.here', \{ machine: lock\.machine \}\)/);
  });

  it('the ways: only for a page asked over the network; the flag only where the server was not started with it; the file with a button that copies it', () => {
    const ways = wholeFunction('sourceWays');
    assert.match(ways, /if \(lock\.kind !== 'Remote'\) return null/, 'started with the folders: nothing to do from here');
    assert.match(ways, /if \(lock\.flag\) \{[\s\S]*withCode\(t\('sources\.way\.flag'\), 'flag', REMOTE_EDIT_FLAG, 'sources-flag'\)/);
    assert.match(ways, /if \(lock\.file !== null\) \{[\s\S]*withCode\(t\('sources\.way\.file'\), 'file', lock\.file, 'sources-file'\)/);
    assert.match(ways, /if \(method !== 'none'\) file\.append\(' ', copyButton\(lock\.file, parts\[1\]\)\)/, 'no button where nothing can copy: the path stays selectable');
    assert.match(ways, /copyMethod\(\{ clipboard: Boolean\(navigator\.clipboard\?\.writeText\), command: typeof document\.queryCommandSupported === 'function' && document\.queryCommandSupported\('copy'\) \}\)/);
    assert.match(code('core.js'), /export const REMOTE_EDIT_FLAG = '--Pusula:AllowRemoteEdit true';/);
    const split = wholeFunction('withCode');
    assert.match(split, /template\.split\(`\{\$\{placeholder\}\}`\)/);
    assert.match(split, /el\('code', className, text\)/, 'the code is text, never markup');
    assert.ok(!/innerHTML/.test(ways + split));
  });

  it('copying: the clipboard API where the browser has it, otherwise a selected hidden field and execCommand; the field goes again and the focus is given back; a copy that fails selects the path', () => {
    const copy = wholeFunction('copyText');
    assert.match(copy, /if \(navigator\.clipboard\?\.writeText\) \{\s*try \{\s*await navigator\.clipboard\.writeText\(text\);\s*return true;\s*\} catch \{/);
    assert.match(copy, /return copyBySelection\(text\);/, 'also where the clipboard refuses');
    const select = wholeFunction('copyBySelection');
    assert.match(select, /el\('textarea', 'copy-proxy'\)/);
    assert.match(select, /field\.readOnly = true;[\s\S]*field\.tabIndex = -1;[\s\S]*aria-hidden/);
    assert.match(select, /field\.select\(\);\s*field\.setSelectionRange\(0, text\.length\);/, 'a phone needs the range as well');
    assert.match(select, /document\.execCommand\('copy'\)/);
    assert.match(select, /field\.remove\(\);\s*if \(before instanceof HTMLElement\) before\.focus\(\{ preventScroll: true \}\);/);
    const button = wholeFunction('copyButton');
    assert.match(button, /if \(!done\) selectText\(anchor\);/);
    assert.match(button, /announce\(button\.textContent\)/, 'a button is no live region');
    assert.match(button, /window\.setTimeout\(\(\) => \{[\s\S]*t\('sources\.copy'\)/);
    assert.match(code('app.js'), /const COPY_MS = 2000;/);
  });

  it('the heading carries a small neutral chip where the list cannot be changed, and no chip where it can; it is not inside the h1', () => {
    const head = wholeFunction('sourcesHead');
    assert.match(head, /head\.append\(el\('h1', null, t\('sources\.title'\)\)\)/);
    assert.match(head, /if \(lock !== null\) head\.append\(el\('span', 'source-chip', lock\.chip === 'Remote' \? t\('sources\.readonly\.Remote'\) : t\('sources\.readonly'\)\)\)/);
    const chip = ownRule('.source-chip') ?? '';
    assert.match(chip, /color: var\(--muted\)/);
    assert.match(chip, /border: 1px solid var\(--border\)/);
    assert.ok(!/--danger|--accent/.test(chip), 'nothing is wrong, so nothing is red or the accent');
  });

  it('the inactive form looks inactive without losing its text: dashed edges and the quiet grey (which is 4.5:1 on the form), the fieldset has no frame of its own', () => {
    const set = ownRule('.source-set') ?? '';
    assert.match(set, /border: 0/);
    assert.match(set, /padding: 0/);
    assert.match(set, /min-width: 0/);
    const input = ownRule('.source-add.is-locked .source-input') ?? '';
    assert.match(input, /border-style: dashed/);
    assert.match(input, /color: var\(--muted\)/);
    const submit = ownRule('.source-add.is-locked .source-submit') ?? '';
    assert.match(submit, /border-style: dashed/);
    assert.match(submit, /color: var\(--muted\)/);
    assert.match(submit, /opacity: 1/, 'not faded: the quiet grey is the dimming');
    assert.match(ownRule('.source-add.is-locked label, .source-add.is-locked .field-hint') ?? '', /color: var\(--muted\)/);
    for (const [theme, tokens] of [['light', lightTokens], ['dark', darkTokens]]) {
      const color = (name) => parseColor(tokens[name]);
      assert.ok(contrast(color('--muted'), color('--bg-side')) >= 4.5, `${theme}: the quiet grey on the form`);
      assert.ok(contrast(color('--text'), color('--code-bg')) >= 4.5, `${theme}: the path and the flag are code`);
    }
  });

  it('the lock line and the ways keep to a reading measure; the path is selected whole with a click; the flag is not broken in the middle', () => {
    assert.match(ownRule('.source-lock') ?? '', /max-width: 72ch/);
    assert.match(ownRule('.source-ways') ?? '', /max-width: 72ch/);
    assert.match(ownRule('.sources-file') ?? '', /user-select: all/);
    assert.match(ownRule('.sources-flag') ?? '', /white-space: nowrap/);
    assert.match(ownRule('.lock-icon') ?? '', /flex: none/);
  });

  it('the field the browser copies from is out of sight and takes no click', () => {
    const rule = ownRule('.copy-proxy') ?? '';
    assert.match(rule, /position: fixed/);
    assert.match(rule, /opacity: 0/);
    assert.match(rule, /pointer-events: none/);
  });

  it('under touch the new controls are 40px', () => {
    const coarse = css.match(/@media \(pointer: coarse\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    const sized = coarse.match(/([^{}]*)\{\s*min-height: var\(--touch\);\s*\}/)?.[1] ?? '';
    for (const name of ['.menu-item', '.copy-btn', '.entry-card']) assert.ok(sized.includes(name), `${name} is not made touch-sized`);
    assert.match(coarse, /\.sources-help > summary/);
  });

  it('the new pieces exist on both sides', () => {
    for (const name of ['sources-head', 'source-chip', 'source-lock', 'source-ways', 'sources-flag', 'copy-btn', 'copy-proxy', 'source-set', 'is-locked']) {
      assert.match(code('app.js'), new RegExp(`['\`" ]${name}['\`" ]`), `${name} is not used in app.js`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
  });
});

describe('the menu of sources (P2)', () => {
  const menuHtml = html.match(/<div id="source-menu"[\s\S]*?<\/div>\s*<\/div>\s*<\/div>/)?.[0] ?? '';

  it('the menu is in index.html next to its button, hidden: a menu with a list of sources (filled in by the page), "manage sources" and the language', () => {
    assert.match(html, /<div id="source-host" class="source-host">\s*<button id="source-switch"/);
    assert.match(menuHtml, /<div id="source-menu" class="source-menu" role="menu" data-i18n-aria-label="sources\.title" hidden>/);
    assert.match(menuHtml, /<ul id="source-menu-list" class="menu-list" role="none"><\/ul>/);
    assert.match(menuHtml, /<hr class="menu-sep" role="separator">/);
    assert.match(menuHtml, /<div class="menu-lang" role="group" data-i18n-aria-label="lang\.label">/);
    const buttons = [...menuHtml.matchAll(/<button class="lang-btn" type="button" role="menuitemradio" aria-checked="false" tabindex="-1" data-lang="(\w+)">/g)].map((match) => match[1]);
    assert.deepEqual(buttons, ['tr', 'en'], 'the two languages, as items of the menu');
    assert.ok(html.indexOf('id="source-menu"') < html.indexOf('class="spacer"'), 'in the place of the switch');
    assert.ok(!/\sstyle\s*=/.test(menuHtml) && !/\son[a-z]+\s*=/i.test(menuHtml));
  });

  it('the switch opens and closes it; the arrow keys on the switch open it (the up arrow at the last row); the state is aria-expanded', () => {
    const bind = functionSource('bindEvents');
    assert.match(bind, /refs\.sourceSwitch\.addEventListener\('click', \(\) => \{\s*if \(sourceMenuOpen\) closeSourceMenu\(\);\s*else openSourceMenu\(\);/);
    assert.match(bind, /if \(event\.key !== 'ArrowDown' && event\.key !== 'ArrowUp'\) return;\s*event\.preventDefault\(\);\s*openSourceMenu\(\{ last: event\.key === 'ArrowUp' \}\)/);
    const open = wholeFunction('openSourceMenu');
    assert.match(open, /refs\.sourceMenu\.hidden = false;\s*refs\.sourceSwitch\.setAttribute\('aria-expanded', 'true'\)/);
    assert.match(open, /\(last \? items\[items\.length - 1\] : current \?\? items\[0\]\)\?\.focus\(\{ preventScroll: true \}\)/, 'the open source, else the first row, has the focus');
    const close = wholeFunction('closeSourceMenu');
    assert.match(close, /refs\.sourceMenu\.hidden = true;\s*refs\.sourceSwitch\.setAttribute\('aria-expanded', 'false'\)/);
  });

  it('inside it: the arrow keys move (and wrap), Home and End go to the ends, Escape closes and gives the switch the focus (the drawers\' own Escape is not also run), Tab closes it and the key moves on, Space chooses a link', () => {
    const body = wholeFunction('onSourceMenuKey');
    assert.match(body, /if \(event\.isComposing\) return;/);
    assert.match(body, /event\.key === 'ArrowDown'\) go\(items\[\(index \+ 1\) % items\.length\]\)/);
    assert.match(body, /event\.key === 'ArrowUp'\) go\(items\[index < 0 \? items\.length - 1 : \(index - 1 \+ items\.length\) % items\.length\]\)/);
    assert.match(body, /event\.key === 'Home'\) go\(items\[0\]\)/);
    assert.match(body, /event\.key === 'End'\) go\(items\[items\.length - 1\]\)/);
    assert.match(body, /event\.key === 'Escape'\) \{\s*event\.preventDefault\(\);\s*event\.stopPropagation\(\);\s*closeSourceMenu\(\);/);
    assert.match(body, /event\.key === 'Tab'\) \{\s*closeSourceMenu\(\);/);
    assert.match(body, /event\.key === ' ' && document\.activeElement instanceof HTMLAnchorElement\) \{\s*go\(null\);\s*document\.activeElement\.click\(\)/);
    assert.match(functionSource('bindEvents'), /refs\.sourceMenu\.addEventListener\('keydown', onSourceMenuKey\)/);
    const items = wholeFunction('sourceMenuItems');
    assert.match(items, /\[role="menuitem"\], \[role="menuitemradio"\]/);
    assert.match(items, /getClientRects\(\)\.length > 0/, 'the language row is not reachable where it is not shown');
  });

  it('a click elsewhere closes it and leaves the focus where the reader put it; choosing a source or the page of sources closes it and takes the focus to the page', () => {
    const bind = functionSource('bindEvents');
    assert.match(bind, /document\.addEventListener\('pointerdown', \(event\) => \{\s*if \(sourceMenuOpen && !\(event\.target instanceof Node && refs\.sourceHost\.contains\(event\.target\)\)\) closeSourceMenu\(\{ focus: 'none' \}\);/);
    assert.match(bind, /closest\('a\[role="menuitem"\]'\)\) closeSourceMenu\(\{ focus: 'content' \}\)/);
    const close = wholeFunction('closeSourceMenu');
    assert.match(close, /if \(focus === 'switch' \|\| \(focus === 'none' && inside\)\) refs\.sourceSwitch\.focus\(\{ preventScroll: true \}\);\s*else if \(focus === 'content'\) refs\.content\.focus\(\{ preventScroll: true \}\)/);
    assert.match(functionSource('onRoute'), /closeSourceMenu\(\{ focus: 'none' \}\);/, 'Back or Forward while it is open');
    assert.match(functionSource('openPalette'), /closeSourceMenu\(\);[\s\S]*const active = document\.activeElement;/, 'the quick opener is over it, and gives the focus back to the switch');
  });

  it('a row is a link to the overview of its source: its dot (a filled one for a folder that can be read, a ring for one that cannot), its name, how it is read, and that it cannot be read; the open one is marked', () => {
    const row = wholeFunction('sourceMenuRow');
    assert.match(row, /link\.setAttribute\('role', 'menuitem'\);\s*link\.tabIndex = -1;\s*link\.href = overviewHash\(source\.id\);/);
    assert.match(row, /if \(sid\(\) === source\.id\) link\.setAttribute\('aria-current', 'true'\)/);
    assert.match(row, /el\('span', readable \? 'source-dot is-ok' : 'source-dot is-off'\)/);
    assert.match(row, /dot\.setAttribute\('aria-hidden', 'true'\)/);
    assert.match(row, /readable \? t\('source\.status\.ok'\) : t\('source\.status\.off'\)/);
    assert.match(row, /t\(`source\.profile\.\$\{profileName\(source\.profile\)\}`\)/);
    assert.match(row, /if \(!readable\) meta\.append\(` \\u00b7 \$\{t\('source\.status\.off'\)\}`\)/, 'in words as well: a dot is not the only way to say it');
    assert.match(row, /link\.title = source\.path/);
    assert.ok(!/innerHTML/.test(row), 'a name and a path are text');
    const render = wholeFunction('renderSourceMenu');
    assert.match(render, /\.find\(\(link\) => link\.dataset\.source === focused\)\?\.focus/, 'a list drawn again keeps the row the keyboard was on');
    assert.match(render, /note\.setAttribute\('aria-disabled', 'true'\)/, 'with no source the menu says so, as a dimmed item');
    assert.match(render, /refs\.sourceManage\.setAttribute\('aria-current', 'page'\)/);
  });

  it('it follows the list and the language: drawn again when the switch is, only while it is open', () => {
    assert.match(functionSource('updateSourceSwitch'), /if \(sourceMenuOpen\) renderSourceMenu\(\);/);
    assert.match(functionSource('applyStaticI18n'), /updateSourceSwitch\(\)/);
  });

  it('the two languages in the menu are items of it (aria-checked), the two in the bar are toggle buttons (aria-pressed)', () => {
    assert.match(functionSource('applyStaticI18n'), /button\.setAttribute\(button\.getAttribute\('role'\) === 'menuitemradio' \? 'aria-checked' : 'aria-pressed', String\(button\.dataset\.lang === getLang\(\)\)\)/);
    assert.match(css, /\.lang-btn\[aria-pressed="true"\],\s*\.lang-btn\[aria-checked="true"\]\s*\{\s*background: var\(--accent-soft\)/);
    assert.match(code('app.js'), /refs\.langButtons = \[\.\.\.document\.querySelectorAll\('button\[data-lang\]'\)\]/, 'all four are found');
  });

  it('it is over the drawers (30) and their scrim (20) and under the quick opener (70); under the switch where there is room, as wide as the screen on a phone', () => {
    const rule = ownRule('.source-menu') ?? '';
    assert.match(rule, /position: absolute/);
    const z = Number(/z-index: (\d+)/.exec(rule)?.[1]);
    assert.ok(z > 30 && z < 70, `z-index ${z}`);
    assert.match(rule, /max-height: min\(70vh, 480px\)/);
    assert.match(rule, /overflow: auto/);
    assert.match(rule, /box-shadow: 0 4px 14px rgba\(0, 0, 0, 0\.22\)/, 'a hairline edge and a small, tight lift');
    assert.match(ownRule('.source-host') ?? '', /position: relative/);
    const narrow = css.match(/@media \(max-width: 480px\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    assert.match(narrow, /\.source-menu\s*\{\s*position: fixed;\s*top: calc\(var\(--topbar-h\) \+ 4px\);\s*right: 8px;\s*left: 8px;\s*width: auto;/);
  });

  it('on a phone the language leaves the bar for the menu, and the live indicator is its dot (its words stay for a screen reader, out of sight, not display none)', () => {
    const narrow = css.match(/@media \(max-width: 480px\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    assert.match(narrow, /\.topbar > \.lang\s*\{\s*display: none;/);
    assert.match(narrow, /\.menu-lang\s*\{\s*display: flex;/);
    assert.match(ownRule('.menu-lang') ?? '', /display: none/, 'not shown where the bar has room for the language');
    const live = narrow.match(/\.live-text\s*\{([^}]*)\}/)?.[1] ?? '';
    assert.match(live, /position: absolute/);
    assert.match(live, /clip: rect\(0, 0, 0, 0\)/);
    assert.ok(!/display:\s*none/.test(live), 'display none would take the words away from a screen reader');
    assert.match(css, /\n\.live\s*\{[^}]*position: relative[^}]*\}/, 'the hidden words are placed inside the indicator');
    assert.match(html, /<span id="live-text" class="live-text"><\/span>/);
  });

  it('the open source and the page of sources are marked by the tint, the weight and a drawn check; a quiet figure takes the text colour on that tint (the quiet grey is under 4.5:1 there in the dark theme)', () => {
    assert.match(ownRule('.menu-item[aria-current]') ?? '', /background: var\(--accent-soft\); font-weight: 600/);
    const check = ownRule('.menu-item[aria-current="true"]::after') ?? '';
    assert.match(check, /border: solid var\(--text\)/);
    assert.match(check, /transform: rotate\(45deg\)/);
    assert.match(ownRule('.menu-item[aria-current] .menu-meta') ?? '', /color: var\(--text\)/);
    assert.ok(css.indexOf('.menu-item[aria-current] {') > css.indexOf('.menu-item:hover,'), 'the open row keeps its tint under the pointer');
    for (const [theme, tokens] of [['light', lightTokens], ['dark', darkTokens]]) {
      const color = (name) => parseColor(tokens[name]);
      const bg = color('--bg');
      assert.ok(contrast(color('--text'), over(color('--accent-soft'), bg)) >= 4.5, `${theme}: the open row`);
      assert.ok(contrast(color('--muted'), bg) >= 4.5 && contrast(color('--muted'), over(color('--hover'), bg)) >= 4.5, `${theme}: how a source is read, on the menu and under the pointer`);
      assert.ok(contrast(color('--live'), bg) >= 3 && contrast(color('--muted'), bg) >= 3, `${theme}: the dots`);
    }
  });

  it('a name is cut at its end and the rest of the row keeps its size; the dots keep their shape in forced colours', () => {
    const name = ownRule('.menu-name') ?? '';
    assert.match(name, /flex: 1 1 auto/);
    assert.match(name, /text-overflow: ellipsis/);
    assert.match(ownRule('.menu-meta') ?? '', /flex: none/);
    assert.match(ownRule('.source-dot.is-off') ?? '', /border: 1\.5px solid var\(--muted\)/);
    assert.match(css, /@media \(forced-colors: active\)\s*\{\s*\.source-dot\s*\{\s*forced-color-adjust: none;/);
  });

  it('the new pieces exist on both sides', () => {
    for (const name of ['menu-item', 'menu-name', 'menu-meta', 'menu-empty', 'source-dot', 'is-ok', 'is-off']) {
      assert.match(code('app.js'), new RegExp(`['\`" ]${name}['\`" ]`), `${name} is not used in app.js`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
    for (const name of ['source-host', 'source-menu', 'menu-list', 'menu-sep', 'menu-lang', 'menu-lang-label']) {
      assert.match(html, new RegExp(`class="[^"]*\\b${name}\\b`), `${name} is not in index.html`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
  });
});

describe('no tree, no column (P2)', () => {
  it('with no source open, or one whose folder cannot be read, the tree column, its drawer and the panel are left out, once the server has been asked', () => {
    assert.match(wholeFunction('syncNoSource'), /document\.body\.classList\.toggle\('no-source', state\.listed && \(state\.source === null \|\| sourceUnavailable\(\)\)\)/);
    assert.match(code('app.js'), /listed: false,/);
    assert.match(wholeFunction('loadSources'), /state\.listed = true;/);
    assert.match(wholeFunction('loadFresh'), /catch \(error\) \{\s*setError\(error\);\s*state\.listed = true;\s*updateSourceSwitch\(\);/, 'a server that does not answer is an answer too: no column for what will not come');
    assert.match(functionSource('updateSourceSwitch'), /syncNoSource\(\);/);
    assert.match(wholeFunction('renderContent'), /syncNoSource\(\);/, 'a page that is an error may be the one that says the folder cannot be read');
  });

  it('the CSS gives the page the whole width and takes the toggles of the drawers away', () => {
    const hidden = css.match(/(body\.no-source \.sidebar,[\s\S]*?)\{\s*display: none;\s*\}/)?.[1] ?? '';
    for (const selector of ['body.no-source .sidebar', 'body.no-source .info', 'body.no-source .tree-toggle', 'body.no-source .info-toggle']) assert.ok(hidden.includes(selector), selector);
    assert.match(ownRule('body.no-source .layout') ?? '', /grid-template-columns: minmax\(0, 1fr\)/);
  });
});

describe('a folder that cannot be read says so without alarm (P2)', () => {
  it('nothing is opened for it: no live connection, the indicator is neutral and says "source unavailable"; a lost connection is still "offline"', () => {
    const connect = wholeFunction('connectEvents');
    assert.match(connect, /if \(sourceUnavailable\(\)\) \{[^}]*markOnline\(\);\s*setLive\('unavailable'\);\s*return;\s*\}/);
    assert.ok(connect.indexOf("setLive('unavailable')") < connect.indexOf('new EventSource('), 'before anything is opened');
    assert.match(connect, /setLive\('offline'\)/, 'a real loss of the connection is as it was');
    const unavailable = wholeFunction('sourceUnavailable');
    assert.match(unavailable, /if \(state\.source\.available === false\) return true;/);
    assert.match(unavailable, /loadFailure\(state\.error\)\.kind === 'unavailable'/);
    const dot = ownRule('.live-unavailable .live-dot') ?? '';
    assert.match(dot, /box-shadow: inset 0 0 0 1\.5px var\(--muted\)/);
    assert.ok(!/--danger/.test(dot), 'neutral, not red');
    assert.ok(!/animation/.test(dot), 'it does not pulse: nothing is on its way');
  });

  it('the card says why in a few words from the code the list gives; the page of the source says it in full, with the folder and what to do', () => {
    assert.match(wholeFunction('unavailableShort'), /const known = sourceErrorCode\(code\);\s*return known === '' \? t\('source\.unavailable'\) : t\(`source\.whyShort\.\$\{known\}`\)/);
    assert.match(wholeFunction('failureWords'), /t\(`source\.why\.\$\{code\}`/);
    assert.match(wholeFunction('failureWords'), /sourceErrorCode\(state\.source\?\.errorCode\)/, 'the list\'s code is what the page knows of a source when the 503 had none');
  });

  it('what the 503 says is read once, with its code; the server\'s English sentence is kept in the error object and passed on, and no page is made of it', () => {
    assert.match(wholeFunction('readProblem'), /code: typeof problem\?\.code === 'string' \? problem\.code : ''/);
    const uses = code('app.js').split('\n').filter((line) => /\b(?:this|problem|error|failure|response)\??\.detail\b/.test(line)).map((line) => line.trim());
    assert.deepEqual(uses, [
      'this.detail = detail;',
      'throw new ApiError(response.status, problem.detail, problem.code);',
      "detail: typeof problem?.detail === 'string' ? problem.detail : '',",
      'throw new ApiError(response.status, problem.detail, problem.code);',
    ], 'the only places that touch it: the error object, the two readers of an answer and what they pass on');
  });
});

describe('where to start in a source of notes (P2)', () => {
  it('the overview puts it between the notes of a tag and the errors, and draws nothing when the server has nothing to say', () => {
    const body = functionSource('notesOverviewPage');
    assert.match(body, /const front = frontSection\(noteFront\(data\)\);\s*if \(front !== null\) page\.append\(front\);/);
    assert.ok(body.indexOf('taggedSection') < body.indexOf('frontSection') && body.indexOf('frontSection') < body.indexOf('issuesBlock'));
    const section = wholeFunction('frontSection');
    assert.match(section, /if \(front\.entry === null && lists\.length === 0\) return null;/);
    assert.match(section, /if \(front\.recent\.length > 0\) lists\.push\(noteList\('recent', t\('recent\.title'\), front\.recent, \(note\) => ageNode\(note\.modifiedAt\)\)\)/);
    assert.match(section, /if \(front\.mostLinked\.length > 0\) lists\.push\(noteList\('linked', t\('linked\.title'\), front\.mostLinked, \(note\) => el\('span', 'note-meta', tn\('overview\.backlinks', note\.count\)\)\)\)/);
    assert.match(section, /el\('section', 'section ov-front'\)/);
    assert.match(section, /el\('div', 'front-lists'\)/);
  });

  it('the entry note is a card that is a link to the note: its name and what it is (of a vault, or of a folder)', () => {
    const body = wholeFunction('entryBlock');
    assert.match(body, /el\('h2', null, t\('entry\.title'\)\)/);
    assert.match(body, /card\.href = fileHash\(sid\(\), entry\.path\)/);
    assert.match(body, /el\('span', 'entry-name', entry\.title\), el\('span', 'entry-hint', t\(`entry\.hint\.\$\{profileName\(state\.source\.profile\)\}`\)\)/);
    assert.ok(!/innerHTML/.test(body), 'a name is text');
  });

  it('the lists: a path link and, quiet at the end of the row, a time or a count; more than a short list holds is "show more"', () => {
    const body = wholeFunction('noteList');
    assert.match(body, /row\.append\(pathLink\(note\.path\), meta\(note\)\)/);
    assert.match(body, /limitedList\(`front:\$\{key\}`, 'note-list', rows\)/);
    const age = wholeFunction('ageNode');
    assert.match(age, /el\('time', 'note-age', ageText\(iso\)\)/);
    assert.match(age, /node\.dateTime = iso/);
    assert.match(wholeFunction('ageText'), /const age = ageSince\(iso\);\s*return age === null \? '' : t\(`age\.\$\{age\.unit\}`, \{ n: age\.n \}\)/, 'the words of the top bar');
  });

  it('the times go on with the clock, as the one of the top bar does: on the same tick and when the page is seen again', () => {
    assert.match(wholeFunction('updateAges'), /querySelectorAll\('time\.note-age'\)\) node\.textContent = ageText\(node\.dateTime\)/);
    assert.match(functionSource('boot'), /setInterval\(updateAges, AGE_TICK_MS\)/);
    assert.match(functionSource('bindEvents'), /renderLastChange\(\);[^}]*updateAges\(\);/);
  });

  it('a group with nothing in it has no chip in a source of notes; a Claude configuration keeps its quiet zero chips', () => {
    const chips = wholeFunction('issueChips');
    assert.match(chips, /function issueChips\(groups, \{ hideZero = false \} = \{\}\)/);
    assert.match(chips, /if \(count === 0 && hideZero\) continue;/);
    assert.match(functionSource('errorsSection'), /hideZero: isNotes\(\)/);
    assert.match(functionSource('reviewSection'), /hideZero: isNotes\(\)/);
    assert.match(css, /\.issue-chip\.is-zero\s*\{/, 'the quiet zero chip is still there for the other overview');
  });

  it('the front is laid out in the grid of the overview: the entry and the lists in one flow, the lists side by side where the section is wide enough for two and one under the other where it is not', () => {
    assert.match(css, /\n\.ov-front\s*\{\s*display: flex;\s*flex-direction: column;/);
    assert.match(ownRule('.front-lists') ?? '', /grid-template-columns: repeat\(auto-fit, minmax\(min\(100%, 18rem\), 1fr\)\)/);
    assert.match(ownRule('.front-list') ?? '', /min-width: 0/);
    const row = ownRule('.note-row') ?? '';
    assert.match(row, /display: flex/);
    assert.match(row, /min-width: 0/);
    const quiet = ownRule('.note-age, .note-meta') ?? '';
    assert.match(quiet, /color: var\(--muted\)/);
    assert.match(quiet, /flex: none/);
    assert.match(quiet, /font-variant-numeric: tabular-nums/);
  });

  it('the entry card is a card of the page\'s own kind (as the cards of the sources are), without a coloured side stripe; its link is accent text on the card\'s background (4.5:1)', () => {
    const card = ownRule('.entry-card') ?? '';
    assert.match(card, /border: 1px solid var\(--border\)/);
    assert.match(card, /border-radius: 8px/);
    assert.match(card, /background: var\(--bg-side\)/);
    assert.ok(!/border-(?:left|right)/.test(card));
    assert.match(ownRule('.entry-hint') ?? '', /color: var\(--muted\)/);
    for (const [theme, tokens] of [['light', lightTokens], ['dark', darkTokens]]) {
      const color = (name) => parseColor(tokens[name]);
      assert.ok(contrast(color('--accent-text'), color('--bg-side')) >= 4.5, `${theme}: the name of the entry note`);
      assert.ok(contrast(color('--muted'), color('--bg-side')) >= 4.5, `${theme}: what it is`);
      assert.ok(contrast(color('--muted'), color('--bg')) >= 4.5, `${theme}: the times and the counts`);
    }
  });

  it('the new pieces exist on both sides', () => {
    for (const name of ['ov-front', 'front-entry', 'front-list', 'front-lists', 'entry-card', 'entry-name', 'entry-hint', 'note-list', 'note-row', 'note-age', 'note-meta']) {
      assert.match(code('app.js'), new RegExp(`['\`" ]${name}['\`" ]`), `${name} is not used in app.js`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
  });
});

describe('polish (P3)', () => {
  it('the typing in a box takes the refusal away: the line under the boxes and the marks on them go, only for the box it is about (any box for a refusal about none), and the page is not drawn again', () => {
    const field = wholeFunction('sourceField');
    assert.match(field, /addEventListener\('input', \(\) => \{\s*state\.sourceEdit\[name\] = input\.value;\s*clearAddError\(name\);/);
    const clear = wholeFunction('clearAddError');
    assert.match(clear, /if \(edit\.addError === null \|\| \(edit\.addError\.field !== null && edit\.addError\.field !== name\)\) return;/);
    assert.match(clear, /edit\.addError = null;/);
    assert.match(clear, /input\.removeAttribute\('aria-invalid'\);\s*input\.removeAttribute\('aria-describedby'\);/);
    assert.match(clear, /line\.textContent = ''/);
    assert.ok(!/renderContent|redrawSources/.test(clear), 'the caret stays where it is');
  });

  it('a removal is seen as well as heard: a toast, said once', () => {
    assert.match(wholeFunction('removeSource'), /showToast\(t\('sources\.removed', \{ name \}\)\)/);
    assert.match(html, /<div id="toasts" class="toasts" role="status"><\/div>/, 'the region of the toasts is a live one');
  });

  it('a note\'s properties have no row for its tags (they are the chips under its name); a Claude file keeps all of its own', () => {
    const body = wholeFunction('propertiesBlock');
    assert.match(body, /const shown = isNotes\(\) \? propertiesWithoutTags\(frontmatter\) : frontmatter;/);
    assert.match(body, /Object\.keys\(shown\)\.length === 0\) return null/, 'a note with nothing but tags has no block');
    assert.match(body, /propertyList\(shown\)/);
  });

  it('the reading column: a paragraph, a list, a quote and a callout run to about 72 characters; a table and a code block are as wide as the page', () => {
    const rule = css.match(/\.md p,\s*\.md ul,\s*\.md ol,\s*\.md blockquote,\s*\.md \.callout\s*\{([^}]*)\}/)?.[1] ?? '';
    assert.match(rule, /max-width: 72ch/);
    assert.ok(!/max-width/.test(ownRule('.md pre') ?? '') && !/max-width/.test(ownRule('.md .table-wrap') ?? ''), 'no measure for code and tables');
    assert.ok(!/\.md pre[^{]*\{[^}]*max-width: 72ch/.test(css));
  });

  it('a quote keeps its left edge and has no rounded corners; the blockquote keeps its edge as it was', () => {
    const callout = ownRule('.callout') ?? '';
    assert.match(callout, /border-left: 4px solid var\(--cc\)/);
    assert.ok(!/border-radius/.test(callout), 'a quote is not a card');
    assert.match(ownRule('.md blockquote') ?? '', /border-left: 3px solid var\(--accent\)/);
  });

  it('the figure in a badge has room above and below it, and the badge is no taller than it was (the line height gives up what the padding takes)', () => {
    const badge = ownRule('.badge') ?? '';
    const vertical = Number(/padding: (\d+)px/.exec(badge)?.[1]);
    const line = Number(/line-height: (\d+)px/.exec(badge)?.[1]);
    assert.ok(vertical >= 3, `padding ${vertical}px`);
    assert.ok(vertical * 2 + line <= 18, `the badge is ${vertical * 2 + line}px tall`);
    assert.match(ownRule('.count') ?? '', /padding: [1-9]\d*px/, 'the count of a group too');
  });

  it('a path link gives up its upper folders for a plain "…/" when less than three characters of them are left: measured as drawn, all at once, after the page is drawn, a row is shown, a group opens or the size changes', () => {
    const fit = wholeFunction('fitPathHeads');
    assert.match(fit, /querySelectorAll\('a\.path-link'\)\]\.filter\(\(link\) => link\.firstElementChild\?\.classList\.contains\('path-head'\)\)/, 'a memory path has no head');
    assert.ok(fit.indexOf("classList.remove('is-cut')") < fit.indexOf('scrollWidth'), 'every link is measured as drawn, uncut');
    assert.ok(fit.indexOf('scrollWidth') < fit.indexOf("classList.toggle('is-cut'"), 'the reads come before the writes');
    assert.match(fit, /link\.getClientRects\(\)\.length === 0 \|\| head\.scrollWidth <= head\.clientWidth\) return false/, 'a row that is not on screen, and a head that fits, are left alone');
    assert.match(fit, /pathHeadCut\(\{[\s\S]*room: head\.clientWidth,[\s\S]*whole: head\.scrollWidth,[\s\S]*Array\.from\(head\.textContent\)\.slice\(0, 3\)/);
    assert.match(fit, /ellipsis: textWidth\('\\u2026', font\)/);
    assert.ok(!/innerHTML|textContent =/.test(fit), 'the head stays in the document: only a class changes');
    const schedule = wholeFunction('scheduleFit');
    assert.match(schedule, /window\.requestAnimationFrame\(\(\) => \{[\s\S]*fitPathHeads\(refs\.content\);\s*fitPathHeads\(refs\.info\);/);
    for (const name of ['renderContent', 'renderInfo', 'limitedList', 'issueGroup', 'toggleBandGroup']) assert.match(functionSource(name), /scheduleFit\(\);/, name);
    assert.match(functionSource('bindEvents'), /new ResizeObserver\(scheduleFit\);\s*observer\.observe\(refs\.content\);\s*observer\.observe\(refs\.info\)/);
  });

  it('the head that is left out is hidden from the eye and not from a screen reader, and "…/" is drawn in its place', () => {
    const head = ownRule('.path-link.is-cut > .path-head') ?? '';
    assert.match(head, /position: absolute/);
    assert.match(head, /clip: rect\(0, 0, 0, 0\)/);
    assert.ok(!/display:\s*none/.test(head));
    const mark = ownRule('.path-link.is-cut::before') ?? '';
    assert.match(mark, /content: "\\2026\/"/);
    assert.match(mark, /color: var\(--muted\)/);
    assert.match(mark, /flex: none/);
  });
});

// ---------------------------------------------------------------------------------------------
// The folder picker: "Choose a folder..." on the sources page
// ---------------------------------------------------------------------------------------------

describe('the folder picker (the "Choose a folder..." window of the sources page)', () => {
  const window_ = html.match(/<div id="browse" class="browse" hidden>[\s\S]*?<\/body>/)?.[0] ?? '';

  it('the button is the first of the form\'s fields (typing the path stays under it) and does nothing where the list cannot be changed', () => {
    const form = wholeFunction('addSourceForm');
    assert.match(form, /const pick = el\('button', 'button source-pick', t\('sources\.add\.pick'\)\);\s*pick\.type = 'button';/);
    assert.match(form, /pick\.setAttribute\('aria-haspopup', 'dialog'\);\s*pick\.dataset\.keep = 'browse';\s*pick\.addEventListener\('click', openBrowse\);/, 'the keyboard is given back to it by its key');
    assert.match(form, /fields\.append\(\s*pick,\s*el\('p', 'source-or', t\('sources\.add\.or'\)\),\s*sourceField\('path'/);
    assert.match(form, /inactive\.append\(fields\)/, 'it is one of the fields of the fieldset that is disabled');
    assert.match(functionSource('openBrowse'), /if \(!refs\.browse\.hidden \|\| !state\.sourcesAccess\.canEdit\) return;/);
  });

  it('the window is a modal dialog with a name; the folders are a listbox with a live status; what was refused is an alert; every mark is a drawn icon', () => {
    assert.ok(window_.length > 0 && html.indexOf('id="palette"') < html.indexOf('id="browse"'), 'no <div id="browse"> after the quick opener');
    assert.match(window_, /<div id="browse-box" class="browse-box" role="dialog" aria-modal="true" aria-labelledby="browse-title" tabindex="-1">/);
    assert.match(window_, /<section id="browse-found" class="browse-found" aria-labelledby="browse-found-head" hidden>/);
    assert.match(window_, /<ul id="browse-list" class="browse-list" role="listbox" tabindex="0" data-i18n-aria-label="browse\.list"><\/ul>/);
    assert.match(window_, /<p id="browse-status" class="browse-status" role="status"><\/p>/);
    assert.match(window_, /<p id="browse-error" class="browse-error" role="alert"><\/p>/);
    assert.match(window_, /<button id="browse-add" class="button" type="button" aria-describedby="browse-why" disabled><\/button>/);
    assert.ok(!/\sstyle\s*=/.test(window_) && !/\son[a-z]+\s*=/i.test(window_));
    const template = html.match(/<template id="browse-icons-template">([\s\S]*?)<\/template>/)?.[1] ?? '';
    assert.deepEqual([...template.matchAll(/<svg data-icon="(\w+)" class="browse-icon"[^>]*aria-hidden="true"/g)].map((match) => match[1]), ['folder', 'vault', 'chevron', 'check']);
    assert.ok(!/[◆▸▾→↑✓✔✕]/.test(html + css + code('app.js')), 'a glyph stands in for an icon');
  });

  it('opening makes the rest of the page inert, starts where the tab was left and asks for both lists; closing drops what is on its way, lifts the inert before the keyboard goes back to the button (by its key), and Back closes it', () => {
    const open = functionSource('openBrowse');
    assert.match(open, /closeSourceMenu\(\);\s*setOverlay\(null\);\s*const memory = readBrowseMemory\(\);/);
    assert.match(open, /refs\.browse\.hidden = false;\s*setPageInert\(true\);\s*renderBrowse\(\);/);
    assert.match(open, /loadFound\(\);\s*loadFolder\(memory\.path, \{ restore: true \}\);/);
    assert.match(functionSource('openPalette'), /if \(!refs\.browse\.hidden\) return;/, 'the quick opener does not open over it');
    const close = functionSource('closeBrowse');
    assert.match(close, /browseSeq \+= 1;[\s\S]*foundSeq \+= 1;[\s\S]*window\.clearTimeout\(browseWait\);/);
    assert.ok(close.indexOf('setPageInert(false)') < close.indexOf('opener.focus('), 'an inert element cannot take the focus back');
    assert.match(close, /\.find\(\(node\) => node\.dataset\.keep === 'browse'\)/);
    assert.match(functionSource('onRoute'), /if \(!refs\.browse\.hidden\) closeBrowse\(\{ focus: 'content' \}\);/);
  });

  it('inside it Escape closes (unless a folder is being added), Tab goes round, the keys of the list are the list\'s, and a key with Alt, Ctrl or Cmd is the browser\'s; the selected row is named by aria-activedescendant and kept in view inside the list', () => {
    const body = functionSource('onBrowseKey');
    assert.match(body, /event\.key === 'Escape'\) \{\s*event\.preventDefault\(\);\s*event\.stopPropagation\(\);\s*if \(state\.browse\.adding === null\) closeBrowse\(\);/);
    assert.match(body, /event\.key === 'Tab'\) \{\s*trapBrowseFocus\(event\);/);
    assert.match(body, /event\.target === refs\.browseList && state\.browse\.adding === null && !event\.ctrlKey && !event\.metaKey && !event\.altKey/);
    assert.match(functionSource('trapBrowseFocus'), /!node\.disabled && node\.getClientRects\(\)\.length > 0/, 'what cannot be reached is not a stop');
    const keys = functionSource('onBrowseListKey');
    assert.match(keys, /listboxIndex\(event\.key, browse\.active, folders\.length, BROWSE_PAGE\)/);
    assert.match(keys, /event\.key === 'ArrowLeft' \|\| event\.key === 'Backspace'\) \{\s*event\.preventDefault\(\);\s*if \(!event\.repeat\) browseUp\(\);/, 'a key that is held does not go up folder after folder');
    assert.match(keys, /if \(folders\[browse\.active\] && !event\.repeat\) loadFolder\(folders\[browse\.active\]\.path\);/, 'nor down');
    assert.match(functionSource('browseRow'), /setAttribute\('role', 'option'\);\s*row\.setAttribute\('aria-selected', 'false'\);/);
    const active = functionSource('setBrowseActive');
    assert.match(active, /aria-activedescendant/);
    assert.match(active, /scrollWithin\(refs\.browseList, options\[next\], \{ block: 'nearest', margin: 4 \}\)/);
    assert.match(functionSource('bindBrowseEvents'), /refs\.browse\.addEventListener\('keydown', onBrowseKey\);/);
  });

  it('a slow folder says it is loading only after a moment; only the last answer asked for is shown; a failure leaves the list that is there; a remembered folder that is gone starts at home; where it was left is kept for the tab', () => {
    const load = wholeFunction('loadFolder');
    assert.match(load, /const seq = \+\+browseSeq;/);
    assert.equal([...load.matchAll(/if \(seq !== browseSeq\) return;/g)].length, 3, 'the timer, the failure and the answer each check it');
    assert.match(load, /if \(restore && path !== null && error instanceof ApiError && \(error\.status === 400 \|\| error\.status === 404\)\) \{\s*await loadFolder\(null\);\s*return;/);
    assert.ok(!/browse\.listing = null/.test(load), 'a failure does not take the list away');
    assert.match(code('app.js'), /const BROWSE_WAIT_MS = 150;/);
    assert.match(code('app.js'), /const BROWSE_KEY = 'pusula\.browse';/);
    assert.match(wholeFunction('readBrowseMemory'), /sessionStorage\.getItem\(BROWSE_KEY\)[\s\S]*catch \{\s*return parseBrowseState\(null\);/);
    assert.match(wholeFunction('rememberBrowse'), /sessionStorage\.setItem\(BROWSE_KEY, browseStateJson\([\s\S]*catch \{/);
    assert.match(wholeFunction('renderBrowse'), /syncBrowseFoot\(\);\s*settleBrowseFocus\(\);/, 'the keyboard stays in the window where a control was drawn again or locked');
  });

  it('the button that adds says why it cannot (the home folder, the root, a folder already listed); adding is the form\'s arrival (the list again, the last source, the window closed, the page); a refusal is said inside the window and the keyboard goes back to the button', () => {
    const foot = wholeFunction('syncBrowseFoot');
    assert.match(foot, /refs\.browseAdd\.disabled = listing === null \|\| block !== null \|\| adding;/);
    assert.match(foot, /if \(block === 'home'\) refs\.browseWhy\.textContent = t\('browse\.why\.home'\);\s*else if \(block === 'root'\) refs\.browseWhy\.textContent = t\('browse\.why\.root'\);\s*else refs\.browseWhy\.textContent = block === 'listed' \? t\('sources\.error\.AlreadyListed'\) : '';/);
    assert.match(foot, /refs\.browseCancel\.disabled = adding;\s*refs\.browseClose\.disabled = adding;/);
    const body = wholeFunction('addFromBrowse');
    assert.match(body, /source = await editSources\('POST', '\/api\/sources', addSourceBody\(path\)\);/);
    assert.ok(body.indexOf('await loadSources()') < body.indexOf('rememberSource(') && body.indexOf('rememberSource(') < body.indexOf('closeBrowse(') && body.indexOf('closeBrowse(') < body.indexOf('window.location.hash'), 'the list, the last source, the window, the page');
    assert.match(body, /if \(state\.route\.view !== 'sources'\) return;/);
    assert.match(body, /browse\.error = \{ \.\.\.sourceFailure\(error, \{ file: state\.sourcesFile \}\), retry: null \};\s*syncBrowseFoot\(\);/);
    assert.match(body, /again\?\.focus\(\{ preventScroll: true \}\);\s*return;/);
    assert.match(foot, /refs\.browseError\.textContent = browse\.error === null \? '' : browseFailureText\(browse\.error\);/);
  });

  it('what the server says is shown as text (no markup is built from a name), a path is cut by the page\'s own path link, and a Claude configuration holds files where any other folder holds notes', () => {
    for (const name of ['browseRow', 'foundRow', 'browseMeta', 'renderBrowseNav', 'renderBrowseFound', 'renderBrowseList', 'syncBrowseList', 'syncBrowseFoot']) {
      assert.ok(!/innerHTML/.test(wholeFunction(name)), name);
    }
    assert.match(wholeFunction('foundRow'), /displayPath\(folder\.display\)/);
    assert.match(functionSource('scheduleFit'), /fitPathHeads\(refs\.browse\);/);
    assert.match(functionSource('browseMeta'), /tn\(`source\.count\.\$\{notes \? 'notes' : 'files'\}`, holds\.n\)/);
  });

  it('it is a dialog over everything, of the quick opener\'s own kind (its scrim, the page\'s shadow and no hairline beside it); the list is what scrolls; the selected row is the accent tint, and what is quiet takes the text colour on it', () => {
    const rule = ownRule('.browse') ?? '';
    assert.match(rule, /position: fixed/);
    assert.ok(Number(/z-index: (\d+)/.exec(rule)?.[1]) > 60, 'above the skip link');
    const box = ownRule('.browse-box') ?? '';
    assert.match(box, /box-shadow: var\(--shadow\)/);
    assert.ok(!/border/.test(box.replace(/border-radius:[^;]*;/, '')), 'no border next to the shadow');
    assert.match(box, /height: min\(80vh, 680px\)/, 'a fixed height: it does not change size with the folders');
    assert.match(ownRule('.browse-list') ?? '', /overflow: auto/);
    assert.match(ownRule('.browse-row[aria-selected="true"]') ?? '', /background: var\(--accent-soft\)/);
    assert.match(ownRule('.browse-row[aria-selected="true"] .browse-meta, .browse-row[aria-selected="true"] .browse-listed') ?? '', /color: var\(--text\)/);
    const forced = css.match(/@media \(forced-colors: active\)\s*\{\s*\.browse-box\s*\{([^}]*)\}\s*\.browse-row\[aria-selected="true"\]\s*\{([^}]*)\}/);
    assert.ok(forced, 'no forced-colors rules for the picker');
    assert.match(css, /@media \(prefers-reduced-motion: no-preference\)\s*\{\s*\.browse:not\(\[hidden\]\) \.browse-box\s*\{\s*animation: palette-in 0\.14s ease-out;/);
  });

  it('a name is cut at its end and a path at its beginning without turning a box round (the page has none); the path of the folder is scrolled to its end so the last folders are in view', () => {
    for (const selector of ['.browse-name', '.browse-found-name']) assert.match(ownRule(selector) ?? '', /text-overflow: ellipsis/, selector);
    assert.match(ownRule('.browse-found-row .browse-found-path') ?? '', /min-height: 0/, 'it is no control: the 40px of a path link under touch is not for it');
    assert.ok(!/direction:\s*rtl/.test(css), 'no right-to-left box anywhere in the page');
    assert.match(ownRule('.browse-crumbs') ?? '', /overflow-x: auto/);
    assert.match(functionSource('renderBrowseNav'), /refs\.browseCrumbs\.scrollLeft = refs\.browseCrumbs\.scrollWidth;/);
  });

  it('on a phone (480px) it is a page of its own, and every row and button in it is 40px (under touch too, whatever the width); where the window is short the found folders give way to the list', () => {
    const phone = css.match(/@media \(max-width: 480px\)\s*\{\s*\.browse\s*\{([^}]*)\}\s*\.browse-box\s*\{([^}]*)\}/);
    assert.ok(phone, 'no phone rules for the picker');
    assert.match(phone[1], /padding: 0/);
    assert.match(phone[2], /width: 100%; height: 100%|width: 100%;\s*height: 100%/);
    assert.match(phone[2], /border-radius: 0/);
    const touch = css.match(/@media \(pointer: coarse\), \(max-width: 480px\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    const sized = (touch.match(/([^{}]*)\{\s*min-height: var\(--touch\);\s*\}/g) ?? []).join('');
    for (const name of ['.browse-row', '.browse-found-row', '.browse-crumb', '.browse-up', '.browse-toggle', '.browse .button', '.browse .more-btn']) assert.ok(sized.includes(name), `${name} is not made touch-sized`);
    assert.match(touch, /\.browse-close\s*\{\s*width: var\(--touch\);\s*height: var\(--touch\);/);
    assert.match(ownRule('.browse-found') ?? '', /flex: 0 1 auto/);
    assert.match(ownRule('.browse-folders') ?? '', /min-height: 9rem/);
    assert.match(ownRule('.browse-folders') ?? '', /flex: 1 1 0;/, 'a long list of folders takes the room the found folders leave, not theirs');
  });

  it('the found folders that can be added come first, and where they scroll their bottom edge fades while there are more below it', () => {
    assert.match(functionSource('loadFound'), /folders: listedLast\(found\.folders\)/);
    assert.match(ownRule('.browse-found > .browse-found-list.is-cut') ?? '', /mask-image: linear-gradient\(to bottom, #000 calc\(100% - 28px\), transparent\)/);
    assert.match(ownRule('.browse-found > .browse-found-list') ?? '', /scroll-padding-bottom: 28px/, 'the keyboard brings a button in clear of the fade');
    assert.match(functionSource('syncFoundCut'), /classList\.toggle\('is-cut', list\.scrollTop \+ list\.clientHeight < list\.scrollHeight - 1\)/);
    assert.match(functionSource('scheduleFit'), /syncFoundCut\(\);/, 'measured again when the window changes size and when rows are drawn');
    assert.match(code('app.js'), /refs\.browseFoundList\.addEventListener\('scroll', syncFoundCut, \{ passive: true \}\)/);
  });
});

describe('colour of the folder picker (WCAG contrast of the real tokens)', () => {
  for (const [theme, tokens] of [['light', lightTokens], ['dark', darkTokens]]) {
    const color = (name) => parseColor(tokens[name]);
    const bg = color('--bg');
    const side = color('--bg-side');
    const row = over(color('--accent-soft'), bg);

    it(`${theme}: the quiet text is 4.5:1 on the window and on the panel of the found folders; a place of the path takes the text colour under the pointer; what was refused is 4.5:1`, () => {
      for (const [label, surface] of [['--bg', bg], ['--bg-side', side]]) assert.ok(contrast(color('--muted'), surface) >= 4.5, `--muted on ${label}`);
      assert.ok(contrast(color('--text'), over(color('--hover'), bg)) >= 4.5, '--text on the hover tint over the window');
      assert.ok(contrast(color('--accent-text'), side) >= 4.5, '"show more" on the panel');
      assert.ok(contrast(color('--danger'), bg) >= 4.5, '--danger on the window');
    });

    it(`${theme}: the selected row: the text colour is 4.5:1 on it, and the accent mark of a vault is 3:1 on it, on the window and on the panel`, () => {
      assert.ok(contrast(color('--text'), row) >= 4.5, `--text on the selected row: ${contrast(color('--text'), row).toFixed(2)}`);
      assert.ok(contrast(color('--accent'), row) >= 3, `--accent on the selected row: ${contrast(color('--accent'), row).toFixed(2)}`);
      assert.ok(contrast(color('--accent'), bg) >= 3 && contrast(color('--accent'), side) >= 3);
    });
  }
});

// ---------------------------------------------------------------------------------------------
// The version of pusula: the last line of the sources page, and the tooltip of the brand
// ---------------------------------------------------------------------------------------------

describe('the version of pusula and the link to its release notes', () => {
  it('the version comes with the list of sources: loadSources reads it with appVersion, and a server that says none leaves it null', () => {
    assert.match(wholeFunction('loadSources'), /state\.appVersion = appVersion\(data\);/);
    assert.match(code('app.js'), /appVersion: null,/);
  });

  it('it is the last thing on the sources page, written from the state, and a page without a version has no such line', () => {
    assert.match(wholeFunction('sourcesPage'), /page\.append\(sourceList\(\), addSourceForm\(\), sourcesHelp\(\)\);\s*const version = versionLine\(\);\s*if \(version !== null\) page\.append\(version\);\s*return page;/);
    const line = wholeFunction('versionLine');
    assert.match(line, /if \(state\.appVersion === null\) return null;/);
    assert.match(line, /el\('p', 'version-line', t\('about\.version', \{ version: state\.appVersion \}\)\)/);
    assert.match(line, /el\('a', 'release-link', t\('about\.releases'\)\)/);
    assert.match(line, /separator\.setAttribute\('aria-hidden', 'true'\)/, 'the dot between the two is no word');
    assert.match(line, /link\.dataset\.keep = 'releases'/, 'a redraw keeps the keyboard on the link');
    assert.ok(!/innerHTML/.test(line));
  });

  it('the link opens the release notes in a new tab and tells the new page nothing about this one', () => {
    assert.match(wholeFunction('versionLine'), /link\.href = RELEASES_URL;\s*link\.target = '_blank';\s*link\.rel = 'noopener noreferrer';/);
  });

  it('the address is named once, in core.js, and the page uses it only as the target of that link: it never asks it for anything', () => {
    const named = Object.entries(scripts).flatMap(([name, source]) => [...source.matchAll(/https?:\/\/[^\s'"`]+/gi)].map((match) => `${name} ${match[0]}`));
    assert.deepEqual(named, [`core.js ${RELEASES_URL}`]);
    const app = code('app.js');
    assert.equal([...app.matchAll(/\bRELEASES_URL\b/g)].length, 2, 'imported once, used once');
    assert.ok(!/(?:getJson|fetch|EventSource|open)\([^)]*RELEASES_URL/.test(app), 'it is not requested');
    assert.ok(!/location[^;\n]*RELEASES_URL/.test(app), 'the page does not navigate to it by itself');
    assert.ok(!/<link[^>]*rel="(?:prefetch|preload|preconnect|dns-prefetch)"/i.test(html), 'nothing in index.html reaches out before a click');
  });

  it('the brand\'s tooltip is the version, written where its address is, and gone again for a server that says none', () => {
    assert.match(functionSource('updateSourceSwitch'), /if \(state\.appVersion === null\) refs\.brand\.removeAttribute\('title'\);\s*else refs\.brand\.title = t\('about\.version', \{ version: state\.appVersion \}\);/);
    assert.ok(!/<a class="brand"[^>]*\stitle=/.test(html), 'index.html has no version of its own');
    assert.match(functionSource('applyStaticI18n'), /updateSourceSwitch\(\)/, 'the other language is written at once');
  });

  it('the new pieces exist on both sides', () => {
    for (const name of ['version-line', 'release-link']) {
      assert.match(code('app.js'), new RegExp(`['\`" ]${name}['\`" ]`), `${name} is not used in app.js`);
      assert.match(css, new RegExp(`\\.${name}\\b`), `${name} has no rule in app.css`);
    }
  });

  it('the line is quiet text (the quiet grey, small) in the page\'s own flow; under touch its link is 40px', () => {
    const line = ownRule('.version-line') ?? '';
    assert.match(line, /color: var\(--muted\)/);
    assert.match(line, /font-size: 13px/);
    assert.match(line, /flex-wrap: wrap/, 'on a narrow screen the link goes to the next line instead of running off');
    const coarse = css.match(/@media \(pointer: coarse\)\s*\{([\s\S]*?)\n\}\n/)?.[1] ?? '';
    const sized = coarse.match(/([^{}]*)\{\s*min-height: var\(--touch\);\s*\}/)?.[1] ?? '';
    assert.ok(sized.includes('.release-link'), '.release-link is not made touch-sized');
    assert.match(coarse, /\.error-link,\s*\.release-link\s*\{\s*display: inline-flex;\s*align-items: center;\s*\}/);
  });

  for (const [theme, tokens] of [['light', lightTokens], ['dark', darkTokens]]) {
    it(`${theme}: the line is the quiet grey and its link the link colour on the page, 4.5:1 on it`, () => {
      const color = (name) => parseColor(tokens[name]);
      assert.ok(contrast(color('--muted'), color('--bg')) >= 4.5, 'the line');
      assert.ok(contrast(color('--accent-text'), color('--bg')) >= 4.5, 'the link');
    });
  }
});
