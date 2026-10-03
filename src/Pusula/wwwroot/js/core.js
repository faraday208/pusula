// pusula - DOM-free helpers shared by app.js and the unit tests (tests/web).
//
// Nothing in this file touches the DOM, `window`, `fetch` or storage, so it can be imported from
// Node. The link rules mirror the server's link extraction (spec 3.2): the server reports every
// link with a `kind` and the exact `raw` text it extracted, and the client finds those links
// again in the rendered page through the key `Kind|Raw`.

// ---------------------------------------------------------------------------------------------
// Enumerations (JSON string values produced by the server)
// ---------------------------------------------------------------------------------------------

export const LAYERS = [
  'ClaudeMd', 'Rule', 'PathRule', 'Skill', 'SkillResource', 'Agent', 'Command', 'OutputStyle',
  'MemoryIndex', 'Memory', 'Reference', 'Shared', 'Other', 'Note',
];

export const LOAD_MODES = [
  'EverySession', 'DescriptionEverySession', 'ProjectSession', 'Conditional', 'OnDemand',
  'UserInvoked', 'Inactive',
];

export const LINK_KINDS = ['MarkdownLink', 'WikiLink', 'ClaudePath', 'RelativePath', 'Embed'];

export const LINK_STATUSES = ['Resolved', 'NonMarkdown', 'Broken', 'Pending', 'External'];

/** How a source (a folder the server reads) is read: a Claude configuration, an Obsidian vault, any other Markdown folder. */
export const PROFILES = ['Claude', 'Vault', 'Markdown'];

/**
 * Normalizes an enum string to PascalCase. The server sends PascalCase names; this keeps the
 * client working if a naming policy ever turns them into camelCase.
 */
export function enumName(value) {
  return typeof value === 'string' && value.length > 0
    ? value[0].toUpperCase() + value.slice(1)
    : '';
}

/** One of `PROFILES`: the profile a server sends, or `Claude` for anything else (an older server sends none). */
export function profileName(profile) {
  const name = enumName(profile);
  return PROFILES.includes(name) ? name : 'Claude';
}

/** True for the profiles that are read as notes (a vault, a Markdown folder): no layers, no load modes, no tokens. */
export function isNoteProfile(profile) {
  return profileName(profile) !== 'Claude';
}

/** A note's name: its file name without `.md`. */
export function noteName(name) {
  return String(name ?? '').replace(/\.md$/i, '');
}

// ---------------------------------------------------------------------------------------------
// Link keys (`Kind|Raw`) and href decoding
// ---------------------------------------------------------------------------------------------

/** The key that joins a rendered link to the server's link list. */
export function linkKey(kind, raw) {
  return `${enumName(kind)}|${raw}`;
}

/** Builds a `Kind|Raw` -> link map from a file's `links`. The first occurrence of a key wins. */
export function buildLinkMap(links) {
  const map = new Map();
  if (!Array.isArray(links)) return map;
  for (const link of links) {
    const key = linkKey(link.kind, link.raw);
    if (!map.has(key)) {
      map.set(key, { ...link, kind: enumName(link.kind), status: enumName(link.status) });
    }
  }
  return map;
}

/** Percent-decodes an href; when it is not valid percent-encoding the input is returned as is. */
export function decodeHref(href) {
  try {
    return decodeURIComponent(href);
  } catch {
    return href;
  }
}

/** Spec key of a rendered markdown-it href: `MarkdownLink|decode(href)`. */
export function markdownLinkKey(href) {
  return linkKey('MarkdownLink', decodeHref(href));
}

/**
 * Candidate keys for a rendered href, most specific first. markdown-it percent-encodes the link
 * destination, while the server reports the destination exactly as written. So the decoded href
 * matches sources written as `<my file.md>` and `ä.md`, and the href as rendered matches sources
 * that were already encoded (`my%20file.md`).
 */
export function markdownLinkKeys(href) {
  const decoded = decodeHref(href);
  return decoded === href
    ? [linkKey('MarkdownLink', href)]
    : [linkKey('MarkdownLink', decoded), linkKey('MarkdownLink', href)];
}

/** Finds the server link that belongs to a rendered `<a href>`, or undefined. */
export function findMarkdownLink(map, href) {
  for (const key of markdownLinkKeys(href)) {
    const link = map.get(key);
    if (link) return link;
  }
  return undefined;
}

/**
 * Finds the server link for a wikilink placeholder (`data-raw`); `kind` is `Embed` for the placeholder of `![[x]]`
 * (its raw text is what is inside the brackets, as a wikilink's). A table cell loses the
 * backslash of an escaped pipe (`[[a\|b]]` renders as `[[a|b]]`), so that form is tried too.
 */
export function findWikiLink(map, raw, kind = 'WikiLink') {
  return map.get(linkKey(kind, raw))
    ?? (raw.includes('|') ? map.get(linkKey(kind, raw.replaceAll('|', '\\|'))) : undefined);
}

// ---------------------------------------------------------------------------------------------
// Path rules (spec 3.2-6 and 3.2-7; same patterns as the server)
// ---------------------------------------------------------------------------------------------

const CLAUDE_PATH_SOURCE = '~/\\.claude/[^\\s`\'"<>()\\[\\]{}*|,;$]*[^\\s`\'"<>()\\[\\]{}*|,;$.:]';

/** A fresh global regex for `ClaudePath` (a new instance each call, so `lastIndex` is never shared). */
export function claudePathRegExp() {
  return new RegExp(CLAUDE_PATH_SOURCE, 'g');
}

/** Every `ClaudePath` in `text`: `{ index, raw }`, in order. */
export function findClaudePaths(text) {
  const found = [];
  const re = claudePathRegExp();
  let match;
  while ((match = re.exec(text)) !== null) {
    found.push({ index: match.index, raw: match[0] });
  }
  return found;
}

/** `RelativePath` shape: a path with at least one `/`, optionally prefixed with `./`. */
export const RELATIVE_PATH_RE = /^(?:\.\/)?(?:[\p{L}\p{N}_.\-]+\/)+[\p{L}\p{N}_.\-]*$/u;

/**
 * True when an inline code span's content is a `RelativePath` candidate (after `trim`): the
 * extraction rule of spec 3.2-7. The server narrows it further when it resolves the candidate
 * (spec 3.3), so a candidate is a link only if the server reported it in `links`.
 */
export function isRelativePath(text) {
  const value = text.trim();
  return RELATIVE_PATH_RE.test(value)
    && !value.startsWith('~')
    && !value.startsWith('/')
    && !value.startsWith('..');
}

// ---------------------------------------------------------------------------------------------
// Wikilinks
// ---------------------------------------------------------------------------------------------

/**
 * Splits the inner text of `[[target#heading|alias]]`. The alias starts at the first `|`, the
 * heading at the first `#` of what is left. `label` is the visible text: alias, else
 * `target#heading`, else `target`.
 */
export function parseWikilink(inner) {
  let rest = inner;
  let alias = null;
  const bar = rest.indexOf('|');
  if (bar >= 0) {
    alias = rest.slice(bar + 1).trim();
    rest = rest.slice(0, bar);
  }
  let target = rest;
  let heading = null;
  const hash = rest.indexOf('#');
  if (hash >= 0) {
    heading = rest.slice(hash + 1).trim();
    target = rest.slice(0, hash);
  }
  target = target.trim();

  let label;
  if (alias) {
    label = alias;
  } else if (heading) {
    label = `${target}#${heading}`;
  } else {
    label = target;
  }
  if (!label) label = inner.trim() || inner;

  return { target, heading: heading || null, alias: alias || null, label };
}

/**
 * What the visible text of an embed `![[inner]]` is: the name of the note it embeds (its file name without `.md`, a file that
 * is not Markdown keeps its extension), and `#heading` when it names one. `inner` is what is inside the brackets.
 */
export function embedName(inner) {
  const { target, heading } = parseWikilink(inner);
  const name = noteName(target.slice(target.lastIndexOf('/') + 1));
  if (!heading) return name;
  return `${name}#${heading}`;
}

/** `image` for an embedded file a browser would show as a picture, `file` for any other (a PDF, audio, a note's attachment). */
export function embedAssetKind(target) {
  return /\.(?:png|jpe?g|gif|svg|webp|avif|bmp|ico|tiff?)$/i.test(String(target ?? '').trim()) ? 'image' : 'file';
}

/**
 * A line of Markdown with its wikilinks written as the words a reader would read: `[[a|b]]` is `b` (the alias), `[[a#h]]` is `a` (the
 * note, not the heading) and `[[a]]` is `a`; `[[#h]]`, a link to a heading of the same note, is `h`. An embed (`![[a#h|300]]`) is the note or file
 * it puts in (`a`): what follows its `|` is a size, not a name. What is not a wikilink is left as it is, triple brackets (which the server does not
 * take for one either) included. For a backlink's excerpt, which is the line of the other file as it is written.
 */
export function readableWikilinks(text) {
  return String(text ?? '').replace(/(?<!\[)(!?)\[\[([^\[\]\n]+?)\]\]/g, (whole, embed, inner) => {
    const { target, heading, alias } = parseWikilink(inner);
    const name = target || heading || inner.trim();
    return embed === '!' ? name : alias ?? name;
  });
}

/**
 * markdown-it plugin: `[[...]]` becomes an `<a class="wikilink" data-raw="...">` placeholder, `![[...]]` (an embed) an
 * `<a class="wikilink" data-embed="1" data-raw="...">` one.
 * `data-raw` is the inner text exactly as the server extracts it (`(?<![!\[])\[\[([^\[\]\n]+?)\]\]`),
 * so the UI can look the link up as `WikiLink|raw` (an embed as `Embed|raw`). Triple brackets, empty
 * or multi-line bodies are left alone, and nothing is linked inside another link's label.
 */
export function wikilinkPlugin(md) {
  const escapeHtml = md.utils.escapeHtml;

  /** The index of the `]` that closes a `[[...]]` whose body starts at `from`, or -1 (empty body, a `[` or a newline inside it, no `]]`). */
  function closeOf(state, from) {
    const src = state.src;
    const max = state.posMax;
    let pos = from;
    while (pos < max) {
      const ch = src.charCodeAt(pos);
      if (ch === 0x5d) break; // `]`
      if (ch === 0x5b || ch === 0x0a) return -1; // `[` or newline inside the body
      pos++;
    }
    if (pos === from || pos + 1 >= max || src.charCodeAt(pos + 1) !== 0x5d) return -1;
    return pos;
  }

  function wikilink(state, silent) {
    const src = state.src;
    const start = state.pos;
    if (src.charCodeAt(start) !== 0x5b || src.charCodeAt(start + 1) !== 0x5b) return false;

    // Same look-behind as the server: not preceded by `!` (embed) or `[`.
    if (start > 0) {
      const before = src.charCodeAt(start - 1);
      if (before === 0x21 || before === 0x5b) return false;
    }
    if (state.linkLevel > 0) return false;

    const end = closeOf(state, start + 2);
    if (end < 0) return false;

    if (!silent) {
      const token = state.push('wikilink', '', 0);
      token.content = src.slice(start + 2, end);
      token.meta = parseWikilink(token.content);
    }
    state.pos = end + 2;
    return true;
  }

  function embed(state, silent) {
    const src = state.src;
    const start = state.pos;
    if (src.charCodeAt(start) !== 0x21 || src.charCodeAt(start + 1) !== 0x5b || src.charCodeAt(start + 2) !== 0x5b) return false;
    if (state.linkLevel > 0) return false;

    const end = closeOf(state, start + 3);
    if (end < 0) return false;

    if (!silent) {
      const token = state.push('embed', '', 0);
      token.content = src.slice(start + 3, end);
      token.meta = parseWikilink(token.content);
    }
    state.pos = end + 2;
    return true;
  }

  md.inline.ruler.before('link', 'wikilink', wikilink);
  md.inline.ruler.before('image', 'embed', embed);
  md.renderer.rules.wikilink = (tokens, idx) => {
    const token = tokens[idx];
    return `<a class="wikilink" data-raw="${escapeHtml(token.content)}">${escapeHtml(token.meta.label)}</a>`;
  };
  md.renderer.rules.embed = (tokens, idx) => {
    const token = tokens[idx];
    return `<a class="wikilink" data-embed="1" data-raw="${escapeHtml(token.content)}">${escapeHtml(token.meta.label)}</a>`;
  };
}

/**
 * markdown-it plugin: allow `file:` link destinations (the server extracts them as links;
 * markdown-it refuses them by default). The UI never keeps such an href: a link is either
 * rewritten to an in-app route or rendered without href.
 */
export function allowFileLinks(md) {
  const original = md.validateLink;
  md.validateLink = (url) => original(url) || /^file:/i.test(url.trim());
}

/**
 * markdown-it plugin: the table rule emits `style="text-align:..."`, which the page's CSP
 * (`style-src 'self'`) would block. Turn it into an `align-*` class instead.
 */
export function cspSafeAttrs(md) {
  md.core.ruler.push('pusula_no_inline_style', (state) => {
    for (const token of state.tokens) {
      if (!token.attrs) continue;
      const index = token.attrIndex('style');
      if (index < 0) continue;
      const align = /text-align:\s*(left|center|right)/.exec(token.attrs[index][1]);
      token.attrs.splice(index, 1);
      if (align) token.attrJoin('class', `align-${align[1]}`);
    }
  });
}

/**
 * markdown-it plugin: images become `<span class="md-image" data-alt="..." title="src">`.
 * The folder being read is not served, so an image could never load; a placeholder also means
 * the browser never starts a request for an address that came from a file. The UI fills in the
 * visible text from `data-alt`.
 */
export function imagePlaceholders(md) {
  const escapeHtml = md.utils.escapeHtml;
  md.renderer.rules.image = (tokens, idx, options, env, self) => {
    const token = tokens[idx];
    const alt = self.renderInlineAsText(token.children, options, env);
    const src = token.attrGet('src') ?? '';
    return `<span class="md-image" data-alt="${escapeHtml(alt)}" title="${escapeHtml(src)}"></span>`;
  };
}

/**
 * markdown-it plugin: every block element gets `data-line="<n>"`, the 1-based line of the FILE its first
 * source line is on, so a `?l=<line>` route (an issue's line, a backlink's line) can find the block that
 * holds it. The server's `bodyStartLine` (the file line of the first body line; 1 without frontmatter)
 * comes in through the render environment: `md.render(body, { bodyStartLine })`. Text rendered without
 * it is not a file's, so it has no lines to name and is left as it is. Inline tokens have no element
 * of their own, and a block without a source map (table cells) gets nothing.
 */
export function lineNumbers(md) {
  md.core.ruler.push('pusula_line_numbers', (state) => {
    const first = state.env?.bodyStartLine;
    if (!Number.isInteger(first) || first < 1) return;
    for (const token of state.tokens) {
      if (token.map && token.type !== 'inline') token.attrSet('data-line', String(first + token.map[0]));
    }
  });
}

/** Builds the page's markdown-it instance. `markdownit` is the UMD factory. */
export function createMarkdown(markdownit) {
  const md = markdownit({ html: false, linkify: false, typographer: false });
  allowFileLinks(md);
  md.use(wikilinkPlugin);
  md.use(cspSafeAttrs);
  md.use(imagePlaceholders);
  md.use(lineNumbers);
  return md;
}

// ---------------------------------------------------------------------------------------------
// Callouts and headings
// ---------------------------------------------------------------------------------------------

const CALLOUT_MARKER_RE = /^\[!([A-Za-z][\w-]*)\]([+-])?[ \t]*/;

/**
 * Parses the `[!type]` marker at the start of a blockquote's first text.
 * Returns `{ type, fold, rest }` (`rest` is the text after the marker, which starts with the
 * title when there is one) or null. `fold` (`+`/`-`) is parsed but has no effect.
 */
export function parseCalloutMarker(text) {
  const match = CALLOUT_MARKER_RE.exec(text);
  if (!match) return null;
  return { type: match[1].toLowerCase(), fold: match[2] ?? '', rest: text.slice(match[0].length) };
}

const CALLOUT_ALIASES = {
  note: 'note',
  abstract: 'abstract', summary: 'abstract', tldr: 'abstract',
  info: 'info',
  todo: 'todo',
  tip: 'tip', hint: 'tip', important: 'tip',
  success: 'success', check: 'success', done: 'success',
  question: 'question', help: 'question', faq: 'question',
  warning: 'warning', caution: 'warning', attention: 'warning',
  failure: 'failure', fail: 'failure', missing: 'failure',
  danger: 'danger', error: 'danger',
  bug: 'bug',
  example: 'example',
  quote: 'quote', cite: 'quote',
};

/** The canonical callout type used for colouring; unknown types look like `note`. */
export function calloutClass(type) {
  return CALLOUT_ALIASES[type.toLowerCase()] ?? 'note';
}

/** `warning` -> `Warning` (the title of a callout that has no title text). */
export function capitalize(text) {
  return text.length > 0 ? text[0].toUpperCase() + text.slice(1) : text;
}

/** Heading id: lower case, spaces to `-`, everything but letters, digits, `-` and `_` dropped. */
export function slugify(text) {
  return String(text)
    .trim()
    .toLowerCase()
    .replace(/\s/g, '-')
    .replace(/[^\p{L}\p{N}_-]/gu, '');
}

/** Returns a function that slugs headings of one document; repeats become `x-2`, `x-3`, ... */
export function createSlugger() {
  const used = new Set();
  return (text) => {
    const base = slugify(text);
    if (!base) return '';
    let slug = base;
    let n = 1;
    while (used.has(slug)) {
      n += 1;
      slug = `${base}-${n}`;
    }
    used.add(slug);
    return slug;
  };
}

// ---------------------------------------------------------------------------------------------
// Numbers, routes, trees
// ---------------------------------------------------------------------------------------------

function oneDecimal(value) {
  return value.toFixed(1).replace(/\.0$/, '');
}

/** Token estimate for display: `~850`, `~1.2K`, `~1.2M`. Nothing at all is a plain `0`, not `~0`. */
export function formatTokens(tokens) {
  const n = Number.isFinite(tokens) && tokens > 0 ? Math.round(tokens) : 0;
  if (n === 0) return '0';
  if (n < 1000) return `~${n}`;
  if (n < 999_950) return `~${oneDecimal(n / 1000)}K`;
  return `~${oneDecimal(n / 1_000_000)}M`;
}

/** `part` as a percentage of `whole`, clamped to 0..100. */
export function percent(part, whole) {
  if (!(whole > 0) || !(part > 0)) return 0;
  return Math.min(100, (part / whole) * 100);
}

/** A 0..100 value for display: `28%`, `<1%` (there is something, just under a percent) or `0%`. */
export function formatPercent(value) {
  if (!(value > 0)) return '0%';
  if (value < 1) return '<1%';
  return `${Math.round(value)}%`;
}

/**
 * How the file tree writes the tokens of a file or a folder. What goes into every session is the
 * number that matters, so it leads (`lead`, `~3.5K`) and the total follows quietly (`total`, `/ 3.5K`,
 * without a second `~`). Nothing in every session (or a server that does not say): only the total,
 * written as everywhere else, and `lead` is null.
 */
export function tokenParts(everySession, total) {
  const every = Number.isFinite(everySession) && everySession > 0 ? everySession : 0;
  if (every === 0) return { lead: null, total: formatTokens(total) };
  return { lead: formatTokens(every), total: `/ ${formatTokens(total).replace(/^~/, '')}` };
}

/**
 * The token count that belongs to a file's load mode: what the file puts into the context when it is
 * loaded that way. Every session (the description alone for a skill, an agent or a command): its every-session
 * share; in its project: its project share; any other mode loads the whole file when it loads at all.
 * `tokens` is the `tokens` object of `/api/file`: `{ total, everySession, projectSession }`.
 */
export function loadedTokens(loadMode, tokens) {
  const count = (value) => (Number.isFinite(value) && value > 0 ? value : 0);
  switch (enumName(loadMode)) {
    case 'EverySession':
    case 'DescriptionEverySession':
      return count(tokens?.everySession);
    case 'ProjectSession':
      return count(tokens?.projectSession);
    default:
      return count(tokens?.total);
  }
}

function safeDecode(text) {
  try {
    return decodeURIComponent(text);
  } catch {
    return text;
  }
}

/**
 * The issue groups of the overview, in display order (the `issues` route parameter): the errors first, then what is only
 * there to be looked at.
 */
export const ISSUE_GROUPS = ['broken', 'frontmatter', 'pending', 'orphans'];

/**
 * The overview's two sections of issues. `errors` are things that are wrong: links whose target is not there and
 * frontmatter that could not be read. `review` is what is not an error: a link to a note that is not written yet (a
 * to-do list) and a file no other file links to.
 */
export const ISSUE_SECTIONS = { errors: ['broken', 'frontmatter'], review: ['pending', 'orphans'] };

/** `'errors'` or `'review'`: the section an issue group belongs to. */
export function issueSection(group) {
  return ISSUE_SECTIONS.errors.includes(group) ? 'errors' : 'review';
}

/** Whether a group starts open: the errors that exist do, everything to review starts folded. */
export function issueStartsOpen(group, count) {
  return issueSection(group) === 'errors' && count > 0;
}

/**
 * The issues of an `/api/overview` response in their two sections: `{ errors, review, errorCount, reviewCount }`, each
 * section a list of `{ key, items }` in `ISSUE_SECTIONS` order. A field the server does not send is an empty group.
 */
export function splitIssues(data) {
  const list = (value) => (Array.isArray(value) ? value : []);
  const items = {
    broken: list(data?.broken), frontmatter: list(data?.frontmatterErrors), pending: list(data?.pending), orphans: list(data?.orphans),
  };
  const section = (keys) => keys.map((key) => ({ key, items: items[key] }));
  const count = (groups) => groups.reduce((sum, group) => sum + group.items.length, 0);
  const errors = section(ISSUE_SECTIONS.errors);
  const review = section(ISSUE_SECTIONS.review);
  return { errors, review, errorCount: count(errors), reviewCount: count(review) };
}

/** `value` when it is a line number (a positive whole number), otherwise null. */
export function positiveLine(value) {
  return Number.isInteger(value) && value > 0 ? value : null;
}

/** The address that names no page of a source: the sources page. */
export const SOURCES_HASH = '#/sources';

/**
 * The front of every address of a source: `#/s/<id>/`. A page that has no source (`null`) gets `#/`, which is how the page was
 * addressed before there were sources: such an address picks a source itself (see `resolveRoute`).
 */
function sourcePrefix(source) {
  return typeof source === 'string' && source !== '' ? `#/s/${encodeURIComponent(source)}/` : '#/';
}

/**
 * Hash for a file view: `#/s/<source>/f/<encodeURIComponent(path)>`, plus `?h=<heading>` and `&l=<line>` when
 * given (`l` is the 1-based source line the page should bring into view).
 */
export function fileHash(source, path, heading, line) {
  const query = [];
  if (heading) query.push(`h=${encodeURIComponent(heading)}`);
  if (Number.isInteger(line) && line > 0) query.push(`l=${line}`);
  return `${sourcePrefix(source)}f/${encodeURIComponent(path)}${query.length > 0 ? `?${query.join('&')}` : ''}`;
}

/**
 * Hash for the overview of a source: `#/s/<source>/`, `#/s/<source>/?issues=broken` (open that issue group),
 * `?memory=1` or `?tag=<tag>` (list the notes that carry that tag).
 */
export function overviewHash(source, { issues, memory, tag } = {}) {
  const front = sourcePrefix(source);
  if (issues) return `${front}?issues=${encodeURIComponent(issues)}`;
  if (memory) return `${front}?memory=1`;
  if (tag) return `${front}?tag=${encodeURIComponent(tag)}`;
  return front;
}

/**
 * Parses `location.hash`. `#/sources` is the sources page; `#/s/<source>/...` is a page of a source; anything else
 * (`#/`, an old `#/f/<path>`, nothing at all) is a page that names no source (`source` is null: `resolveRoute` picks one).
 * Anything that is not a file route is the overview.
 * Sources: `{ view: 'sources', source: null }`.
 * File: `{ view: 'file', source, path, heading, line }` (`heading` and `line` are null when absent).
 * Overview: `{ view: 'overview', source, issues, memory, tag }` - `issues` is one of `ISSUE_GROUPS` or null,
 * `memory` says whether the project memory table was asked for, `tag` is the tag whose notes were asked for (without a leading `#`) or null.
 */
export function parseRoute(hash) {
  let text = hash ?? '';
  if (text.startsWith('#')) text = text.slice(1);
  const question = text.indexOf('?');
  const pathPart = question >= 0 ? text.slice(0, question) : text;
  const query = question >= 0 ? text.slice(question + 1) : '';

  if (/^\/sources\/?$/.test(pathPart)) return { view: 'sources', source: null };

  const params = Object.create(null);
  for (const pair of query.split('&')) {
    const eq = pair.indexOf('=');
    if (eq > 0) params[pair.slice(0, eq)] = safeDecode(pair.slice(eq + 1));
  }

  let source = null;
  let rest = pathPart;
  const scoped = /^\/s\/([^/]*)(?:\/(.*))?$/.exec(pathPart);
  if (scoped) {
    source = scoped[1] === '' ? null : safeDecode(scoped[1]);
    rest = `/${scoped[2] ?? ''}`;
  }

  const match = /^\/f\/(.+)$/.exec(rest);
  if (!match) {
    return {
      view: 'overview',
      source,
      issues: ISSUE_GROUPS.includes(params.issues) ? params.issues : null,
      memory: params.memory === '1',
      tag: params.tag?.replace(/^#/, '') || null,
    };
  }

  const line = /^\d{1,9}$/.test(params.l ?? '') ? Number(params.l) : null;
  return { view: 'file', source, path: safeDecode(match[1]), heading: params.h || null, line: line > 0 ? line : null };
}

/** The address of a parsed route: what `parseRoute` reads back. A route that has no source is written the old way (`#/`, `#/f/...`). */
export function routeHash(route) {
  if (route.view === 'sources') return SOURCES_HASH;
  if (route.view === 'file') return fileHash(route.source, route.path, route.heading, route.line);
  return overviewHash(route.source, { issues: route.issues, memory: route.memory, tag: route.tag });
}

/**
 * The source a page that names none opens: the one opened last (`lastId`) when the server lists it and can read it, otherwise the
 * first one it can read; null when there is none. `sources` is the `sources` of `/api/sources`.
 */
export function chooseSource(sources, lastId) {
  const usable = (Array.isArray(sources) ? sources : []).filter((source) => source && source.available !== false);
  return usable.find((source) => source.id === lastId) ?? usable[0] ?? null;
}

/**
 * What the page does with the route of its address, given the sources the server lists and the one opened last. `{ route, redirect, missing }`:
 * `route` is the page to show, `redirect` the address to write over the current one without a new history entry (null: the address
 * stays), `missing` the source the address named that the list does not have (the sources page says so).
 *
 * - The sources page is shown as it is.
 * - An address that names no source (`#/`, an old `#/f/<path>`, `#/?issues=...`) opens the same page in the source `chooseSource` picks,
 *   and is written as that page's own address, so a bookmark made before there were sources keeps working and Back does not return to it.
 *   With no source to pick, it is the sources page.
 * - A page of a source the list has is shown. A page of one it has not is the sources page, with `missing` set.
 */
export function resolveRoute(route, sources, lastId) {
  if (route.view === 'sources') return { route, redirect: null, missing: null };
  const list = Array.isArray(sources) ? sources : [];
  if (route.source === null || route.source === undefined) {
    const chosen = chooseSource(list, lastId);
    if (chosen === null) return { route: { view: 'sources', source: null }, redirect: SOURCES_HASH, missing: null };
    const next = { ...route, source: chosen.id };
    return { route: next, redirect: routeHash(next), missing: null };
  }
  if (!list.some((source) => source.id === route.source)) {
    return { route: { view: 'sources', source: null }, redirect: SOURCES_HASH, missing: route.source };
  }
  return { route, redirect: null, missing: null };
}

/** The key a per-source setting is stored under: `pusula.openDirs.claude`. */
export function sourceKey(base, source) {
  return `${base}.${source}`;
}

/** True when a link leads to an indexed file the page can open: it is resolved and has a target. */
export function isOpenable(link) {
  return enumName(link.status) === 'Resolved' && Boolean(link.target);
}

/** In-app href of a resolved link of `source`, or null when the link does not lead to an indexed file. */
export function linkHref(source, link) {
  return isOpenable(link) ? fileHash(source, link.target, link.heading) : null;
}

/**
 * Splits a path for display on one line: `head` (the folders above the last one), `parent` (the
 * last folder, with its trailing slash) and `name`. A part that does not exist is `''`.
 * `a/b/c.md` -> `{ head: 'a/', parent: 'b/', name: 'c.md' }`. The UI cuts `head` first when the
 * path does not fit, so the folder next to the file and the file name stay readable.
 */
export function splitPath(path) {
  const slash = path.lastIndexOf('/');
  const name = path.slice(slash + 1);
  const folders = path.slice(0, slash + 1);
  if (folders === '') return { head: '', parent: '', name };
  const parentStart = folders.lastIndexOf('/', folders.length - 2) + 1;
  return { head: folders.slice(0, parentStart), parent: folders.slice(parentStart), name };
}

/** `a/b/c.md` -> `['a', 'a/b']`: the directories that must be open to show the file. */
export function ancestorDirs(path) {
  const parts = path.split('/');
  parts.pop();
  const dirs = [];
  let current = '';
  for (const part of parts) {
    current = current ? `${current}/${part}` : part;
    dirs.push(current);
  }
  return dirs;
}

/** True for a directory node of `/api/tree`. */
export function isDirectory(node) {
  return enumName(node.type) === 'Directory';
}

/** All file nodes of a `/api/tree` response, in tree order. */
export function flattenFiles(nodes, into = []) {
  for (const node of nodes ?? []) {
    if (isDirectory(node)) flattenFiles(node.children, into);
    else into.push(node);
  }
  return into;
}

/** The folder paths of a `/api/tree` response, without a trailing slash, as a Set. */
export function folderPaths(nodes, into = new Set()) {
  for (const node of nodes ?? []) {
    if (!isDirectory(node)) continue;
    into.add(String(node.path).replace(/\/+$/, ''));
    folderPaths(node.children, into);
  }
  return into;
}

/**
 * For each layer, the load modes that occur among its files, in `LOAD_MODES` order:
 * `Map<layer, Array<{ mode, count }>>`.
 */
export function loadModesByLayer(files) {
  const counts = new Map();
  for (const file of files) {
    const layer = enumName(file.layer);
    const mode = enumName(file.loadMode);
    if (!counts.has(layer)) counts.set(layer, new Map());
    const modes = counts.get(layer);
    modes.set(mode, (modes.get(mode) ?? 0) + 1);
  }
  const result = new Map();
  for (const [layer, modes] of counts) {
    result.set(
      layer,
      LOAD_MODES.filter((mode) => modes.has(mode)).map((mode) => ({ mode, count: modes.get(mode) })),
    );
  }
  return result;
}

// ---------------------------------------------------------------------------------------------
// Links in the side panel
// ---------------------------------------------------------------------------------------------

/**
 * The status a link is shown with. The server calls everything it cannot open `NonMarkdown`; a
 * `NonMarkdown` link that points at a folder (it ends in `/`, or it is a folder of the tree) is shown as
 * `Folder`, because "not Markdown" is no answer to "what is this". `folders` is `folderPaths(tree)`.
 */
export function displayStatus(link, folders) {
  const status = enumName(link.status);
  if (status !== 'NonMarkdown') return status;
  const target = String(link.target ?? '');
  const folder = String(link.raw ?? '').endsWith('/') || target.endsWith('/') || (folders?.has(target.replace(/\/+$/, '')) ?? false);
  return folder ? 'Folder' : status;
}

/** What a row of the side panel calls a link's target: the path (and heading) of a resolved link, otherwise what was written. */
export function linkLabel(link) {
  return isOpenable(link) ? `${link.target}${link.heading ? `#${link.heading}` : ''}` : link.raw;
}

/**
 * Merges links that point at the same target into one row: `[{ label, link, lines }]` in order of first
 * appearance, `link` being the first of them and `lines` the (sorted, distinct) lines they are on.
 * The input is the links of one status.
 */
export function mergeLinks(links) {
  const rows = new Map();
  for (const link of links) {
    const label = linkLabel(link);
    if (!rows.has(label)) rows.set(label, { label, link, lines: [] });
    const row = rows.get(label);
    if (Number.isInteger(link.line) && link.line > 0 && !row.lines.includes(link.line)) row.lines.push(link.line);
  }
  return [...rows.values()].map((row) => ({ ...row, lines: row.lines.sort((a, b) => a - b) }));
}

// ---------------------------------------------------------------------------------------------
// Context budget band (overview)
// ---------------------------------------------------------------------------------------------

function byTokensThenPath(a, b) {
  return b.tokens - a.tokens || (a.path < b.path ? -1 : a.path > b.path ? 1 : 0);
}

/**
 * Splits what the config loads into every session into the pieces of the budget band.
 *
 * `files` are the file nodes of `/api/tree`, whose `everySessionTokens` is what the file puts into
 * the context of every session (for a skill, an agent or a command: its description only). Only
 * files with a positive value count. A file is one piece, except the files whose description is
 * loaded (`DescriptionEverySession`): all of a layer's descriptions form one group piece, so 33
 * skills are one `Skill` piece, not 33 slivers.
 *
 * Returns `null` when no file carries `everySessionTokens` at all (the server does not send the
 * field), so the page can leave the band out; otherwise `{ total, parts }`. Whole files come
 * first, then the description groups, each by tokens, biggest first. A part is
 * `{ id, kind: 'file' | 'group', layer, loadMode, tokens, percent, path?, name?, count?, files? }`;
 * a group's `files` are `{ path, name, layer, loadMode, tokens }`, biggest first.
 */
export function budgetParts(files) {
  if (!Array.isArray(files) || !files.some((file) => typeof file.everySessionTokens === 'number')) return null;

  const loaded = files
    .filter((file) => file.everySessionTokens > 0)
    .map((file) => ({
      path: file.path,
      name: file.name ?? file.path.slice(file.path.lastIndexOf('/') + 1),
      layer: enumName(file.layer),
      loadMode: enumName(file.loadMode),
      tokens: file.everySessionTokens,
    }));

  const wholeFiles = [];
  const groups = new Map();
  for (const file of loaded) {
    if (file.loadMode !== 'DescriptionEverySession') {
      wholeFiles.push({ id: `file:${file.path}`, kind: 'file', ...file });
      continue;
    }
    if (!groups.has(file.layer)) {
      groups.set(file.layer, {
        id: `group:${file.layer}`, kind: 'group', layer: file.layer, loadMode: file.loadMode, tokens: 0, files: [],
      });
    }
    const group = groups.get(file.layer);
    group.tokens += file.tokens;
    group.files.push(file);
  }

  const groupParts = [...groups.values()].map((group) => ({
    ...group, count: group.files.length, files: group.files.sort(byTokensThenPath),
  }));
  const parts = [...wholeFiles.sort(byTokensThenPath), ...groupParts.sort(byTokensThenPath)];
  const total = parts.reduce((sum, part) => sum + part.tokens, 0);
  return { total, parts: parts.map((part) => ({ ...part, percent: percent(part.tokens, total) })) };
}

// ---------------------------------------------------------------------------------------------
// Layers table (overview)
// ---------------------------------------------------------------------------------------------

/**
 * The layers table of the overview in two: the layers that put something into every session, whose share
 * is what the table is for, and the rest, which load only when something asks for them (on demand, when a
 * matching file is read, when the user calls them, not active) and are folded into one line. A project's
 * memory index is loaded in every session of its project, in every project: it stays with the first,
 * though no share of the every-session total is its own.
 * `layers` is the `layers` of `/api/overview`: `{ layer, files, tokens, everySessionTokens }`. Both lists keep the
 * order given; `restTokens` is the total tokens of the folded layers.
 */
export function splitLayers(layers) {
  const shown = [];
  const rest = [];
  for (const entry of Array.isArray(layers) ? layers : []) {
    const loads = entry.everySessionTokens > 0 || enumName(entry.layer) === 'MemoryIndex';
    (loads ? shown : rest).push(entry);
  }
  const restTokens = rest.reduce((sum, entry) => sum + (Number.isFinite(entry.tokens) && entry.tokens > 0 ? entry.tokens : 0), 0);
  return { shown, rest, restTokens };
}

// ---------------------------------------------------------------------------------------------
// Names for display
// ---------------------------------------------------------------------------------------------

/**
 * `skills/a/SKILL.md` -> `{ skill: 'a', file: 'SKILL.md' }`; any other path -> null. Every skill's
 * file has the same name, so a list that shows the skill first is easier to scan than the path.
 */
export function skillFile(path) {
  const parts = path.split('/');
  if (parts.length < 2 || parts[parts.length - 1] !== 'SKILL.md') return null;
  const skill = parts[parts.length - 2];
  return skill ? { skill, file: 'SKILL.md' } : null;
}

/**
 * The slug Claude Code gives a folder: the path with every character that is not an ASCII letter or
 * digit turned into `-`. This is the slug of the *parent* of the folder being read, which is the home
 * folder for `~/.claude`: `/home/me/.claude` -> `-home-me`. '' when there is no parent.
 */
export function homeSlug(root) {
  const trimmed = String(root ?? '').replace(/[\\/]+$/, '');
  const slash = Math.max(trimmed.lastIndexOf('/'), trimmed.lastIndexOf('\\'));
  if (slash <= 0) return '';
  return trimmed.slice(0, slash).replace(/[^A-Za-z0-9]/g, '-');
}

// A folder slug cannot be taken apart exactly (`sample-app` has a hyphen, so does every separator), so
// these are the conventional container folders after which the project's own name starts.
const SLUG_CONTAINERS = 'Workspace|Desktop|Documents|Downloads|storage';

/**
 * Splits a project folder slug (`-home-ai-Workspace-alpha`, `C--Workspace-sample-app`) into the part
 * that only says where it lives (`prefix`, shown quiet) and the project's own name (`name`, shown
 * strong). `home` is the slug of this machine's home folder (`homeSlug`), which tells the folder
 * apart from the user name. A slug it cannot split is all `name`. `prefix + name` is the slug.
 */
export function splitSlug(slug, home = '') {
  const split = (prefix) => ({ prefix, name: slug.slice(prefix.length) });
  const container = new RegExp(`^(?:${SLUG_CONTAINERS})-`);

  if (home && slug.startsWith(`${home}-`) && slug.length > home.length + 1) {
    const rest = slug.slice(home.length + 1);
    const inner = container.exec(rest);
    return split(`${home}-${inner && rest.length > inner[0].length ? inner[0] : ''}`);
  }
  const marker = new RegExp(`^(.*?-(?:${SLUG_CONTAINERS}))-(.+)$`).exec(slug);
  if (marker) return split(`${marker[1]}-`);
  const base = /^-home-|^[A-Za-z]--Users-/.exec(slug);
  if (base && slug.length > base[0].length) return split(base[0]);
  return { prefix: '', name: slug };
}

/**
 * Where the front of a folder slug (`splitSlug`'s `prefix`, without its last hyphen) says a project lives, written the
 * way a person writes a path: `C--Users-Ana-Desktop` -> `C:\Users\Ana\Desktop`, `-mnt-storage` -> `/mnt/storage`,
 * the home folder of this machine -> `~`. A guess: a slug has a hyphen for every separator and also for every hyphen
 * inside a name, so a user name keeps its hyphens only where a container folder follows (`~ana-maria/Workspace`).
 * '' when there is no front.
 */
function placeOf(front, home) {
  if (!front) return '';
  if (home && front === home) return '~';
  if (home && front.startsWith(`${home}-`)) return `~/${front.slice(home.length + 1).replace(/-+/g, '/')}`;
  const drive = /^([A-Za-z])--(.*)$/.exec(front);
  if (drive) return `${drive[1]}:\\${drive[2].replace(/-+/g, '\\')}`;
  const user = new RegExp(`^-home-(.+)-(${SLUG_CONTAINERS})$`).exec(front);
  if (user) return `~${user[1]}/${user[2]}`;
  const lone = /^-home-(.+)$/.exec(front); // a home folder with nothing after it: its user may hold hyphens
  if (lone) return `/home/${lone[1]}`;
  return `/${front.replace(/^-+/, '').replace(/-+/g, '/')}`;
}

/** `{ name, context }` of one folder slug, see `projectLabels`. */
function describeSlug(slug, home) {
  if (home && slug === home) return { name: '~', context: placeOf(slug, '') };
  const { prefix, name } = splitSlug(slug, home);
  if (prefix === '') return { name: slug.replace(/^-+/, '') || slug, context: '' };
  return { name, context: placeOf(prefix.replace(/-$/, ''), home) };
}

/**
 * The names a list, the tree and a breadcrumb call the project folders by (`projects/<slug>`), so that the one thing
 * that tells projects apart, which is at the end of the slug, is what is read. `slugs` are the folder names;
 * `home` is `homeSlug(root)`. Returns `Map<slug, { name, context, full, shared }>` in the order given, one entry
 * per distinct slug:
 *
 * - `name` is the project's own name, what `splitSlug` finds after the container folder; this machine's home
 *   folder is `~`, and a slug that is no known shape is the slug without its leading hyphen.
 * - `context` is where it lives, written as a path (`placeOf`): a guess, to be shown quiet. '' when the slug says
 *   nothing about it.
 * - `full` is the slug as it is on disk.
 * - `shared` is true when another slug has the same name (the same project on Windows and on Linux, say). Then the
 *   context is what tells them apart: every one of them has one, and none reads like another's. Where two places read
 *   the same, or a slug has none, the slug stands in for it.
 */
export function projectLabels(slugs, home = '') {
  const labels = new Map();
  for (const slug of slugs ?? []) {
    if (typeof slug !== 'string' || slug === '' || labels.has(slug)) continue;
    labels.set(slug, { ...describeSlug(slug, home), full: slug, shared: false });
  }

  const byName = new Map();
  for (const label of labels.values()) {
    const key = label.name.toLowerCase();
    if (byName.has(key)) byName.get(key).push(label);
    else byName.set(key, [label]);
  }
  for (const group of byName.values()) {
    if (group.length < 2) continue;
    const contexts = group.map((label) => label.context);
    for (const label of group) {
      label.shared = true;
      if (label.context === '' || contexts.filter((context) => context === label.context).length > 1) label.context = label.full;
    }
  }
  return labels;
}

/** The folder names directly under `projects/` in a `/api/tree` response: one slug per project. */
export function projectFolders(nodes) {
  const projects = (nodes ?? []).find((node) => isDirectory(node) && String(node.path).replace(/\/+$/, '') === 'projects');
  return (projects?.children ?? []).filter(isDirectory).map((node) => node.name);
}

// Turkish rules, without regard to case or accent: `i` and `İ` are the same letter, `ı` and `ç` are not `i` and `c`.
const nameCollator = new Intl.Collator('tr', { sensitivity: 'base' });

/**
 * Orders the folders under `projects/` by the name each is shown by (`projectLabels`): the name first, then where it
 * lives (the same project on two machines), then the folder as it is on disk, so the order never depends on how the
 * server listed them. Folders come before files, as everywhere in the tree; a file under `projects/` (there should be none) keeps its
 * place among the files. Returns a new list.
 */
export function orderProjectFolders(nodes, labels) {
  const list = Array.isArray(nodes) ? nodes : [];
  const labelOf = (node) => labels?.get(node.name) ?? { name: String(node.name ?? ''), context: '', full: String(node.name ?? '') };
  const compare = (a, b) => {
    const left = labelOf(a);
    const right = labelOf(b);
    return nameCollator.compare(left.name, right.name)
      || nameCollator.compare(left.context, right.context)
      || (left.full < right.full ? -1 : left.full > right.full ? 1 : 0);
  };
  return [...list.filter(isDirectory).sort(compare), ...list.filter((node) => !isDirectory(node))];
}

const MEMORY_PATH_RE = /^projects\/([^/]+)\/memory\/(.+)$/;

/**
 * What a list shows for a file path. A memory file (`projects/<slug>/memory/<file>`) is named by its project and
 * its file: `{ kind: 'project', project, file }`, `project` being its entry of `projectLabels` and `file` what
 * follows `memory/` (everything under a project's folder is memory, so that folder is left out; the whole path is
 * the tooltip's). Any other path, and a memory path whose project `labels` does not know, is
 * `{ kind: 'path', head, parent, name }`, see `splitPath`.
 */
export function displayPath(path, labels) {
  const match = MEMORY_PATH_RE.exec(path);
  const project = match ? labels?.get(match[1]) : undefined;
  if (project) return { kind: 'project', project, file: match[2] };
  return { kind: 'path', ...splitPath(path) };
}

/**
 * Splits a file name for a cut in its middle: `end` is its last `keep` characters (the extension and a little of
 * the name before it), which a one-line list keeps; `start` is the rest, which gives way with an ellipsis when the
 * line is short of room. A name of `keep` characters or less is all `end`, so it is never cut. Counts whole
 * characters, so an emoji is not split. `start + end` is the name.
 */
export function fileEnds(name, keep = 10) {
  const text = String(name ?? '');
  const chars = Array.from(text);
  if (chars.length <= keep) return { start: '', end: text };
  return { start: chars.slice(0, chars.length - keep).join(''), end: chars.slice(chars.length - keep).join('') };
}

/**
 * Whether the upper folders of a path (`.path-head`, which gives way first when a path does not fit) should be left out for a plain `…/`: when
 * less than `min` (3) characters of them are left to read, which is a piece of a name and not a name ("0Kararlar/"). `room` is the width the layout
 * gives the head and `whole` the width the head has uncut; a head that fits whole is never cut. `widths` are the widths of its first characters
 * (`min` of them are enough) and `ellipsis` the width of the `…` that ends a cut text: the characters that stay readable are the ones that fit
 * in front of it.
 */
export function pathHeadCut({ room, whole, widths, ellipsis = 0, min = 3 }) {
  if (!(whole > room)) return false;
  const list = Array.isArray(widths) ? widths : [];
  let used = ellipsis;
  let shown = 0;
  for (const width of list) {
    if (used + width > room) break;
    used += width;
    shown += 1;
  }
  return shown < Math.min(min, list.length);
}

// ---------------------------------------------------------------------------------------------
// Scrolling inside a scroll container
// ---------------------------------------------------------------------------------------------

/**
 * Which block holds a source line. `lines` is the `data-line` of every block of the page in document
 * order (see `lineNumbers`); the answer is the index of the block that starts closest before the line, or
 * at it. Of several that start on the same line (a list item and its paragraph) it is the last, the
 * innermost. -1 when every block starts after the line (the line is in the frontmatter) or there are none.
 */
export function blockIndexForLine(lines, target) {
  let found = -1;
  let foundLine = -Infinity;
  lines.forEach((line, index) => {
    if (Number.isFinite(line) && line <= target && line >= foundLine) {
      found = index;
      foundLine = line;
    }
  });
  return found;
}

/**
 * The `scrollTop` that brings an item into view inside a scroll container (the page scrolls
 * `#content` and `#sidebar`, never the document, so `scrollIntoView` is not used).
 *
 * `itemTop` is the item's distance from the first pixel of the container's content. `block`
 * `'start'` puts the item at the top, `margin` px below the edge; `'nearest'` moves only as far as
 * needed and leaves the scroll position alone when the item is already in view. `bottomMargin` (the
 * same as `margin` unless given) is the room kept below the item: something sticky at the bottom of the
 * container covers that much of it. An item taller than the view is aligned at its top. The result is
 * never negative.
 */
export function scrollTargetTop({ scrollTop, viewHeight, itemTop, itemHeight, block = 'nearest', margin = 0, bottomMargin = margin }) {
  const start = Math.max(0, itemTop - margin);
  if (block === 'start') return start;
  if (itemTop - margin < scrollTop) return start;
  const bottom = itemTop + itemHeight + bottomMargin;
  if (bottom > scrollTop + viewHeight) return Math.max(0, Math.min(start, bottom - viewHeight));
  return scrollTop;
}

// ---------------------------------------------------------------------------------------------
// Where the reader was (history entries)
// ---------------------------------------------------------------------------------------------

function wholePixels(value) {
  return Number.isFinite(value) && value > 0 ? Math.round(value) : 0;
}

/**
 * The reading place a history entry carries in its `state`: `{ scroll, treeScroll, infoScroll }`, the `scrollTop` of
 * `#content`, of the tree and of the info panel. `#content` scrolls, not the document, so the browser's own restoration has
 * nothing to restore; the page writes the place into the entry as the reader scrolls (`withPosition`) and
 * reads it back when the entry comes back (Back, Forward, a reload). Null when the entry carries none (a
 * link that was just followed has no state), so "has a place" also says "was shown before". The tree and the panel
 * are the top when the entry does not say (an entry written before the panel had a place).
 */
export function savedPosition(state) {
  if (state === null || typeof state !== 'object' || !Number.isFinite(state.scroll) || state.scroll < 0) return null;
  return { scroll: Math.round(state.scroll), treeScroll: wholePixels(state.treeScroll), infoScroll: wholePixels(state.infoScroll) };
}

/** `state` with the reading place set (whatever else the entry carries is kept); never negative, never fractional. */
export function withPosition(state, scroll, treeScroll, infoScroll = 0) {
  const rest = state !== null && typeof state === 'object' && !Array.isArray(state) ? state : {};
  return { ...rest, scroll: wholePixels(scroll), treeScroll: wholePixels(treeScroll), infoScroll: wholePixels(infoScroll) };
}

/**
 * Whether the page of `route` (what `parseRoute` said when the page was asked for) is the page the address `hash` names. The browser
 * is on another history entry (Back, Forward) before the page has caught up: its `hashchange` has not run yet, or the page of the
 * entry it has just left is still on its way and finishes loading. A reading place written then would be written over that entry's
 * own place (an entry Back returned to lost its place, and a quick Forward and Back brought the reader to the top). A page writes
 * its place only while this is true.
 */
export function pageIsAtAddress(route, hash) {
  if (route === null || typeof route !== 'object') return false;
  const at = parseRoute(hash);
  if (route.view !== at.view) return false;
  if (at.view === 'sources') return true;
  if ((route.source ?? null) !== at.source) return false;
  if (at.view === 'file') {
    return route.path === at.path && (route.heading ?? null) === at.heading && (route.line ?? null) === at.line;
  }
  return (route.issues ?? null) === at.issues && Boolean(route.memory) === at.memory && (route.tag ?? null) === at.tag;
}

/**
 * Where a route puts the reader. An entry that comes back (`saved`, see `savedPosition`) wins over what its
 * address names: the reader may have scrolled on after the line (`?l=`) or the heading (`?h=`) brought them
 * there, and the mark is not shown again. A page reached the first time goes to its line, else its heading,
 * else its top; the overview to the group or table its address names (or its top), which the page works out; the sources page its top.
 * `{ kind: 'restore', scroll, treeScroll, infoScroll } | { kind: 'line', line } | { kind: 'heading', heading } | { kind: 'overview' } | { kind: 'top' }`.
 */
export function arrivalTarget(route, saved) {
  if (saved) return { kind: 'restore', scroll: saved.scroll, treeScroll: saved.treeScroll, infoScroll: saved.infoScroll ?? 0 };
  if (route.view === 'sources') return { kind: 'top' };
  if (route.view !== 'file') return { kind: 'overview' };
  if (route.line) return { kind: 'line', line: route.line };
  if (route.heading) return { kind: 'heading', heading: route.heading };
  return { kind: 'top' };
}

/**
 * What the overview and the file view remember about being open or closed, so that a reload brings back the
 * page the scroll position was measured on (a list shown past its first rows is taller than the same list
 * folded). `text` is what `uiStateJson` wrote, or anything at all: whatever is not of the right shape falls
 * back to the default. `{ issueOpen: Map<string, boolean>, moreOpen: Set, openBand: Set, memoryOpen, layersRestOpen,
 * legendOpen, propertiesOpen }`.
 */
export function parseUiState(text) {
  let data = null;
  try {
    data = JSON.parse(text ?? 'null');
  } catch {
    data = null;
  }
  if (data === null || typeof data !== 'object' || Array.isArray(data)) data = {};
  const names = (value) => (Array.isArray(value) ? value.filter((item) => typeof item === 'string') : []);
  const flag = (value, fallback) => (typeof value === 'boolean' ? value : fallback);
  return {
    issueOpen: new Map((Array.isArray(data.issueOpen) ? data.issueOpen : [])
      .filter((entry) => Array.isArray(entry) && ISSUE_GROUPS.includes(entry[0]) && typeof entry[1] === 'boolean')
      .map(([key, open]) => [key, open])),
    moreOpen: new Set(names(data.moreOpen)),
    openBand: new Set(names(data.openBand)),
    memoryOpen: flag(data.memoryOpen, false),
    layersRestOpen: flag(data.layersRestOpen, false),
    legendOpen: flag(data.legendOpen, false),
    propertiesOpen: flag(data.propertiesOpen, true),
  };
}

/** The JSON `parseUiState` reads back. */
export function uiStateJson(ui) {
  return JSON.stringify({
    issueOpen: [...ui.issueOpen],
    moreOpen: [...ui.moreOpen],
    openBand: [...ui.openBand],
    memoryOpen: ui.memoryOpen,
    layersRestOpen: ui.layersRestOpen,
    legendOpen: ui.legendOpen,
    propertiesOpen: ui.propertiesOpen,
  });
}

// ---------------------------------------------------------------------------------------------
// Turkish clock times
// ---------------------------------------------------------------------------------------------

// The suffix that follows a time written in digits (`08:29'da`) is the one of the last number SPOKEN:
// 08:29 is "sekiz yirmi dokuz", so dokuz-da; 14:30 is "on dört otuz", so otuz-da. Vowel harmony and the
// hard consonant after "üç", "dört", "beş" (te) and "kırk" (ta) are all that decide it.
const LOCATIVE_BY_UNIT = ['da', 'de', 'de', 'te', 'te', 'te', 'da', 'de', 'de', 'da']; // sıfır bir iki üç dört beş altı yedi sekiz dokuz
const LOCATIVE_BY_TENS = { 1: 'da', 2: 'de', 3: 'da', 4: 'ta', 5: 'de' }; // on yirmi otuz kırk elli

/**
 * `da`, `de`, `ta` or `te`: the locative suffix of the time `hour:minute` as a Turkish reader says it. A time on the
 * hour is said by its hour (14:00 is "on dört"), any other by its minutes.
 */
export function trLocative(hour, minute) {
  const spoken = minute === 0 ? hour : minute;
  const unit = spoken % 10;
  if (unit !== 0) return LOCATIVE_BY_UNIT[unit];
  return LOCATIVE_BY_TENS[spoken / 10] ?? LOCATIVE_BY_UNIT[0];
}

// ---------------------------------------------------------------------------------------------
// Live changes (what the `changed` event of the server says)
// ---------------------------------------------------------------------------------------------

/**
 * One line for what a `changed` event brought: the file the top bar names and how many others came with it.
 * `{ path, kind, extra, paths }` or null for an event with no path in it. A file that appeared comes before a file
 * that changed, which comes before one that went (what is new is what a reader looks for; a deleted file cannot be
 * opened); inside a list the server's order is kept. `extra` counts the other distinct paths.
 */
export function summarizeChange({ added = [], changed = [], removed = [] } = {}) {
  const lists = [['added', added], ['changed', changed], ['removed', removed]]
    .map(([kind, list]) => [kind, (Array.isArray(list) ? list : []).filter((path) => typeof path === 'string' && path !== '')]);
  const first = lists.find(([, list]) => list.length > 0);
  if (!first) return null;
  const paths = [...new Set(lists.flatMap(([, list]) => list))];
  return { path: first[1][0], kind: first[0], extra: paths.length - 1, paths };
}

/**
 * What the top bar calls a changed file: its name; a skill's file by its skill (`x · SKILL.md`: every skill's file
 * has the same name) and a project's memory file by its project (`app · MEMORY.md`), the way lists name them.
 * `labels` is `projectLabels(...)`. A note (`notes`: the source is a vault or a Markdown folder) has no skills and no
 * projects: it is called by its name without `.md`.
 */
export function changeName(path, labels, { notes = false } = {}) {
  const shown = displayPath(path, notes ? undefined : labels);
  if (shown.kind === 'project') return `${shown.project.name} · ${shown.file}`;
  if (notes) return noteName(shown.name);
  const skill = skillFile(path);
  return skill ? `${skill.skill} · ${skill.file}` : shown.name;
}

/**
 * How long ago something was, as the unit the top bar writes it in and the count of it:
 * `{ unit: 'now' | 's' | 'm' | 'h' | 'd', n }`. Under five seconds is "now": the line is rewritten every fifteen,
 * so a count of seconds that small would be wrong before it was read. Nothing, or a time in the future, is "now".
 */
export function ageParts(milliseconds) {
  const ms = Number.isFinite(milliseconds) && milliseconds > 0 ? milliseconds : 0;
  if (ms < 5_000) return { unit: 'now', n: 0 };
  if (ms < 60_000) return { unit: 's', n: Math.floor(ms / 1_000) };
  if (ms < 3_600_000) return { unit: 'm', n: Math.floor(ms / 60_000) };
  if (ms < 86_400_000) return { unit: 'h', n: Math.floor(ms / 3_600_000) };
  return { unit: 'd', n: Math.floor(ms / 86_400_000) };
}

/** How long ago an ISO date was, in the units of `ageParts`: `{ unit, n }`; null when `iso` is no date. */
export function ageSince(iso, now = Date.now()) {
  const at = typeof iso === 'string' ? Date.parse(iso) : Number.NaN;
  return Number.isNaN(at) ? null : ageParts(now - at);
}

/**
 * A change in a number, as the badge beside it writes it: `+3`, `−1` (a real minus sign, which does not
 * break from its digit). With `tokens` the size is written as tokens everywhere else is, without the `~`:
 * `+120`, `−1.2K`. Empty when there is no change, or less than one of what is counted.
 */
export function formatDelta(delta, { tokens = false } = {}) {
  if (!Number.isFinite(delta) || Math.round(Math.abs(delta)) === 0) return '';
  const size = tokens ? formatTokens(Math.abs(delta)).replace(/^~/, '') : String(Math.round(Math.abs(delta)));
  return `${delta > 0 ? '+' : '\u2212'}${size}`; // U+2212, the minus sign: as wide as the plus, and it stays with its digits
}

/**
 * What changed on the overview between two `/api/overview` responses: `[{ key, from, to }]`, `key` one of the issue
 * groups (their counts) or `budget` (what every session loads, in tokens), in the order of the page: the budget
 * first. Only what differs.
 */
export function overviewDiff(previous, next) {
  if (!previous || !next) return [];
  const length = (list) => (Array.isArray(list) ? list.length : 0);
  const issues = (data) => ({
    broken: length(data.broken), pending: length(data.pending), orphans: length(data.orphans), frontmatter: length(data.frontmatterErrors),
  });
  const tokens = (data) => (Number.isFinite(data.everySessionTokens) ? data.everySessionTokens : 0);

  const diffs = [];
  if (tokens(previous) !== tokens(next)) diffs.push({ key: 'budget', from: tokens(previous), to: tokens(next) });
  const before = issues(previous);
  const after = issues(next);
  for (const key of ISSUE_GROUPS) {
    if (before[key] !== after[key]) diffs.push({ key, from: before[key], to: after[key] });
  }
  return diffs;
}

/**
 * Which blocks of a file are new, or are not what they were, after it changed: the indices into `after`. `before` and
 * `after` are the text of every block of the page, in order. What both ends have in common is left out first (an edit
 * is almost always in one place); of what is left, a block is new when the text of the old middle has no block like it
 * to give (one is used up per block, so a paragraph that was written twice is not both marked for one new copy).
 * A rewrite marks nothing: when more than `max` blocks are new, saying so about each is saying nothing.
 */
export function changedBlocks(before, after, max = 40) {
  let start = 0;
  while (start < before.length && start < after.length && before[start] === after[start]) start += 1;
  let endBefore = before.length;
  let endAfter = after.length;
  while (endBefore > start && endAfter > start && before[endBefore - 1] === after[endAfter - 1]) {
    endBefore -= 1;
    endAfter -= 1;
  }
  const pool = new Map();
  for (let index = start; index < endBefore; index += 1) pool.set(before[index], (pool.get(before[index]) ?? 0) + 1);
  const found = [];
  for (let index = start; index < endAfter; index += 1) {
    const left = pool.get(after[index]) ?? 0;
    if (left > 0) pool.set(after[index], left - 1);
    else found.push(index);
  }
  return found.length > max ? [] : found;
}

/**
 * Where to point a reader at changes they cannot see: `rects` are `{ top, bottom }` of the changed blocks, `view` the
 * `{ top, bottom }` of what is on screen, both in the same coordinates. Null when any of them is (even partly) in view.
 * Otherwise `{ dir: 'up' | 'down', index }` of the one nearest to the view, which is where a click on the pill goes.
 */
export function pillTarget(rects, view) {
  let best = null;
  for (let index = 0; index < rects.length; index += 1) {
    const { top, bottom } = rects[index];
    if (bottom > view.top && top < view.bottom) return null;
    const dir = bottom <= view.top ? 'up' : 'down';
    const distance = dir === 'up' ? view.top - bottom : top - view.bottom;
    if (best === null || distance < best.distance) best = { dir, index, distance };
  }
  return best === null ? null : { dir: best.dir, index: best.index };
}

/**
 * The folder row that should wear the dot for a file that changed: the outermost folder above it that is closed,
 * which is the only one of them on screen. Null when every folder above it is open (its own row shows the change).
 * `open` is a Set of the open folder paths.
 */
export function closedAncestor(path, open) {
  return ancestorDirs(path).find((dir) => !open.has(dir)) ?? null;
}

// ---------------------------------------------------------------------------------------------
// Errors in words
// ---------------------------------------------------------------------------------------------

/** Why the server cannot show the folder of a source: the `errorCode` of a source in `/api/sources` and the `code` of the 503 its endpoints answer with. */
export const SOURCE_ERROR_CODES = ['FolderMissing', 'NotReadable', 'TooLarge'];

/** One of `SOURCE_ERROR_CODES` for a code the server sends (whatever the case of its first letter), '' for anything else: a server that says no code, or one the page has no words for. */
export function sourceErrorCode(code) {
  const name = enumName(code);
  return SOURCE_ERROR_CODES.includes(name) ? name : '';
}

/**
 * What kind of failure a load error is, so the page can say it in words (the message of a failed `fetch` is "Failed to
 * fetch", which tells a reader nothing). `error` is whatever the page caught: an error that carries the HTTP `status` of an
 * answer, or the `TypeError` that `fetch` throws when there was no answer at all. `{ kind, status }`, `kind` being
 * `network` (no answer: the server is down or the network is), `unavailable` (a 503 that says why: the folder of the source
 * cannot be read; its `code` is one of `SOURCE_ERROR_CODES`, or '' when the server gives a reason in words only, which the page
 * does not show: it is the server's English), `server` (any other 5xx), `request` (any other HTTP failure) or `unexpected` (an
 * answer that could not be read, or anything else); `status` is 0 when there was no HTTP answer.
 */
export function loadFailure(error) {
  const status = Number.isInteger(error?.status) ? error.status : 0;
  if (status === 503) {
    const code = sourceErrorCode(error?.code);
    if (code !== '' || (typeof error?.detail === 'string' && error.detail.trim() !== '')) return { kind: 'unavailable', status, code };
  }
  if (status >= 500) return { kind: 'server', status };
  if (status >= 400) return { kind: 'request', status };
  if (status === 0 && (error?.name === 'TypeError' || error?.name === 'NetworkError')) return { kind: 'network', status: 0 };
  return { kind: 'unexpected', status: 0 };
}

/**
 * What the page says about a file whose frontmatter has an error, or null when it has none. `file` is the `/api/file`
 * response. `partial` is true when part of the frontmatter was read all the same (the server's second reader got some
 * keys out of it), so the page can say "partly read" and not "could not be read" above values it shows. `line` is the line
 * of the FILE the error is on and `text` that line as written, when the server says (older servers do not: both are
 * then null); `message` is the parser's own words.
 */
export function frontmatterProblem(file) {
  const message = typeof file?.frontmatterError === 'string' ? file.frontmatterError : '';
  if (message === '') return null;
  const data = file.frontmatter;
  const partial = data !== null && typeof data === 'object' && !Array.isArray(data) && Object.keys(data).length > 0;
  const text = typeof file.frontmatterErrorText === 'string' && file.frontmatterErrorText.trim() !== '' ? file.frontmatterErrorText : null;
  return { partial, line: positiveLine(file.frontmatterErrorLine), text, message };
}

// ---------------------------------------------------------------------------------------------
// Adding and removing sources (the sources page)
// ---------------------------------------------------------------------------------------------

/**
 * What the sources page may do, and what it says about the sources file, from the answer of `/api/sources`. `canEdit` is whether this
 * browser may add and remove sources (the server allows it to the browser of the machine it runs on and to no other). `closed` is why it
 * may not, as the page's note says it: `Remote` (asked over the network; the sources file can still be edited by hand) or `CommandLine`
 * (the server was started with the folders, so it has no file to change; a `Remote` answer that names no sources file is the same
 * thing). `closed` is null when it may, and when the server does not say why not. `fileError` is what is wrong with the sources file
 * (the last list that was good is in use meanwhile), or null.
 */
export function sourcesAccess(data) {
  const canEdit = data?.canEdit === true;
  const hasFile = typeof data?.sourcesFile === 'string' && data.sourcesFile !== '';
  const blocked = enumName(data?.editBlocked);
  let closed = null;
  if (!canEdit && blocked === 'CommandLine') closed = 'CommandLine';
  else if (!canEdit && blocked === 'Remote') closed = hasFile ? 'Remote' : 'CommandLine';
  const error = typeof data?.sourcesFileError === 'string' ? data.sourcesFileError.trim() : '';
  return { canEdit, closed, fileError: error === '' ? null : error };
}

/**
 * Where the server runs and whether it lets other machines change the list, from the answer of `/api/sources`: `machine` is the name of
 * the computer pusula runs on (null when the server does not say), `remoteEdit` whether it was started to accept changes of the list from
 * the network (only `true` is "yes": an older server says nothing and is "no").
 */
export function sourcesHost(data) {
  const machine = typeof data?.machine === 'string' ? data.machine.trim() : '';
  return { machine: machine === '' ? null : machine, remoteEdit: data?.remoteEdit === true };
}

/**
 * The version of pusula that the server says it is, from the answer of `/api/sources`: the number as the server wrote it (`0.1.0`), without the
 * spaces around it; null when the server says none (an older one does not) or says something that is not a version number. The page shows it
 * and asks for nothing more.
 */
export function appVersion(data) {
  const version = typeof data?.version === 'string' ? data.version.trim() : '';
  return /^\d+(?:\.\d+){1,3}(?:[-+][0-9A-Za-z.+-]+)?$/.test(version) ? version : null;
}

/**
 * Where the release notes of pusula are. It is the one address outside the server that the page names, and only as the target of a link that
 * the reader follows (in a new tab): the page itself asks no one but its own server for anything.
 */
export const RELEASES_URL = 'https://github.com/faraday208/pusula/releases';

/** What a server is started with to let other machines add and remove sources: the page names it where it says how else the list can be changed. */
export const REMOTE_EDIT_FLAG = '--Pusula:AllowRemoteEdit true';

/**
 * What the "add a folder" box says where this browser may not change the list; null where it may. `access` is `sourcesAccess` of the answer,
 * `host` is `sourcesHost` of it and `sourcesFile` the file the list is read from (null when there is none). `kind` is `Remote` (asked over the
 * network), `CommandLine` (the server was started with the folders) or `Unknown` (a server that does not say why not). `machine` is the computer
 * to open the page on. `flag` is whether to say that the server can be started to accept changes from the network (only where it was not,
 * and only for `Remote`), `file` the sources file to edit by hand (only for `Remote`, which is the one that has it). `chip` is how the
 * heading says the page is read-only: `Remote` ("read-only, network") or `Plain`.
 */
export function sourceLock(access, host, sourcesFile) {
  if (access?.canEdit) return null;
  const kind = access?.closed ?? 'Unknown';
  const file = typeof sourcesFile === 'string' && sourcesFile !== '' ? sourcesFile : null;
  return {
    kind,
    machine: host?.machine ?? null,
    flag: kind === 'Remote' && host?.remoteEdit !== true,
    file: kind === 'Remote' ? file : null,
    chip: kind === 'Remote' ? 'Remote' : 'Plain',
  };
}

/**
 * How the page can copy a text for the reader: `clipboard` (the asynchronous clipboard API, which only a secure context has: a page opened
 * by the address of a machine over plain http does not), `selection` (a selected field and `execCommand('copy')`, which a browser still
 * has there) or `none` (no button then: the text stays selectable). `has` says what the browser has: `{ clipboard, command }`.
 */
export function copyMethod({ clipboard = false, command = false } = {}) {
  if (clipboard) return 'clipboard';
  return command ? 'selection' : 'none';
}

/** What the form that adds a source sends: the path, and the name only when one was typed (it is optional), both without the spaces around them. */
export function addSourceBody(path, name) {
  const body = { path: String(path ?? '').trim() };
  const given = String(name ?? '').trim();
  if (given !== '') body.name = given;
  return body;
}

/** The `code` of a refusal that has words of its own on the sources page (`sources.error.<code>`). `Remote` and `CommandLine` are said by the notes of a page that is closed. */
export const SOURCE_REFUSALS = ['PathRequired', 'PathNotAbsolute', 'FolderNotFound', 'TooBroad', 'AlreadyListed', 'InvalidName', 'FileInvalid', 'WriteFailed'];

const PATH_REFUSALS = ['PathRequired', 'PathNotAbsolute', 'FolderNotFound', 'TooBroad', 'AlreadyListed'];

/**
 * What adding or removing a source failed with, for the page to say it in words: `{ kind, params, field }`. `error` is whatever was caught: an
 * error that carries the HTTP `status`, the `detail` and the `code` of the server's ProblemDetails, or the `TypeError` that `fetch` throws
 * when there was no answer; `file` is the sources file the server named. `kind` is one of `SOURCE_REFUSALS`, `Remote` or `CommandLine` (what
 * the notes of a closed page say; `CrossOrigin` is read as `Remote`, which is the same to a reader), or, for a code the page has no words
 * for, what `loadFailure` calls the failure (`network`, `server`, `request`, `unexpected`). `params` fills in the words. `field` is the box of
 * the form the refusal is about (`path`, `name`), or null. A code that needs something to say it (the file, the reason) and has none is
 * said as the failure it is.
 */
export function sourceFailure(error, { file = null } = {}) {
  const code = typeof error?.code === 'string' ? error.code : '';
  const detail = typeof error?.detail === 'string' ? error.detail.trim() : '';
  const named = typeof file === 'string' && file !== '' ? file : null;
  if (PATH_REFUSALS.includes(code)) return { kind: code, params: {}, field: 'path' };
  if (code === 'InvalidName') return { kind: code, params: {}, field: 'name' };
  if (code === 'FileInvalid' && named !== null) return { kind: code, params: { file: named }, field: null };
  if (code === 'WriteFailed' && detail !== '') return { kind: code, params: { detail }, field: null };
  if (code === 'CommandLine') return { kind: code, params: {}, field: null };
  if (code === 'Remote' || code === 'CrossOrigin') {
    return named === null ? { kind: 'CommandLine', params: {}, field: null } : { kind: 'Remote', params: { file: named }, field: null };
  }
  const failure = loadFailure(error);
  // The folder of a source that cannot be read is not what went wrong here: a 503 is the server's error.
  return { kind: failure.kind === 'unavailable' ? 'server' : failure.kind, params: { status: failure.status }, field: null };
}

// ---------------------------------------------------------------------------------------------
// Choosing a folder (the picker that "Choose a folder..." opens on the sources page)
// ---------------------------------------------------------------------------------------------

/**
 * The address that lists the folders inside `path` (the home folder when there is none: the answer says where it is), with the folders whose names
 * start with a dot as well when `hidden` is set. A path is whatever the server gave for a folder: it is encoded, never taken apart.
 */
export function browseUrl(path, { hidden = false } = {}) {
  const query = [];
  if (typeof path === 'string' && path !== '') query.push(`path=${encodeURIComponent(path)}`);
  if (hidden) query.push('hidden=1');
  return `/api/browse${query.length > 0 ? `?${query.join('&')}` : ''}`;
}

/** What a folder that holds something this page reads is called: `Vault`, `Claude`, or '' for any other folder (and for a kind that the page has no word for). */
export function folderKind(kind) {
  const name = enumName(kind);
  return name === 'Vault' || name === 'Claude' ? name : '';
}

/** The last name of a path, whichever separator it has (`/a/b/` is `b`); the whole path when it has none (the root). */
function lastName(path) {
  const trimmed = String(path ?? '').replace(/[\\/]+$/, '');
  return trimmed.slice(Math.max(trimmed.lastIndexOf('/'), trimmed.lastIndexOf('\\')) + 1) || String(path ?? '');
}

/**
 * One folder of an answer of the picker, as the page draws it, or null for an entry that names none (no path). `name` is the folder's own
 * name, `display` its path as the server writes it for a reader (`~/notes`), `kind` is `folderKind`, `count` the Markdown files it holds
 * (null when the server did not count; `more`: the count stopped there and there are more), `listed` whether a source has it already.
 */
export function browseFolder(entry) {
  const path = typeof entry?.path === 'string' ? entry.path : '';
  if (path === '') return null;
  const count = Number.isFinite(entry.markdownCount) && entry.markdownCount >= 0 ? Math.floor(entry.markdownCount) : null;
  return {
    name: typeof entry.name === 'string' && entry.name !== '' ? entry.name : lastName(path),
    path,
    display: typeof entry.display === 'string' && entry.display !== '' ? entry.display : path,
    kind: folderKind(entry.kind),
    count,
    more: count !== null && entry.more === true,
    listed: entry.listed === true,
  };
}

/** The folders of a list of an answer, in the server's order; what is not a folder is left out. */
export function browseFolders(list) {
  return (Array.isArray(list) ? list : []).map(browseFolder).filter((folder) => folder !== null);
}

/** What a folder holds, for its row: `{ n, more }` (`more`: the count stopped at `n`, there are more), or null when there is nothing to say (no count, or no Markdown file in it). */
export function folderHolds(folder) {
  if (typeof folder?.count !== 'number') return null;
  // 0 is not worth a label, and "0+" (counting stopped before any note was found) says nothing either.
  if (folder.count === 0) return null;
  return { n: folder.count, more: folder.more === true };
}

/**
 * What `/api/browse` answered, as the picker keeps it: `{ path, display, parent, home, folders, truncated }`. `parent` is null at the root of the file
 * system; `home` is the user's home folder (null when the server does not say); `truncated` says the list was cut short. Null for an answer that names no folder.
 */
export function browseListing(data) {
  const path = typeof data?.path === 'string' ? data.path : '';
  if (path === '') return null;
  const text = (value) => (typeof value === 'string' && value !== '' ? value : null);
  return {
    path,
    display: text(data.display) ?? path,
    parent: text(data.parent),
    home: text(data.home),
    folders: browseFolders(data.folders),
    truncated: data.truncated === true,
  };
}

/** What `/api/browse/found` answered: `{ folders, complete }`; `complete` is false when the search was cut short (only an answer that says so is). */
export function browseFound(data) {
  return { folders: browseFolders(data?.folders), complete: data?.complete !== false };
}

/** A path without its separators at the end (`/a/b/` is `/a/b`); the root stays as it is. */
function trimSeparators(path) {
  const text = String(path ?? '');
  const trimmed = text.replace(/[\\/]+$/, '');
  return trimmed === '' ? text : trimmed;
}

/** Whether two paths name the same folder: written alike, but for the separator at the end (a sources file may say `/a/b/` where the picker says `/a/b`). */
export function sameFolder(left, right) {
  return typeof left === 'string' && typeof right === 'string' && left !== '' && trimSeparators(left) === trimSeparators(right);
}

/**
 * The places of a folder's path, outermost first, each one a place to go to: `[{ label, path, kind }]`, the last being the folder itself. A folder
 * inside the home folder starts with `~` (`kind: 'home'`), any other with the root (`/`, or the drive of a Windows path; `kind: 'root'`); the
 * folders after that are `kind: 'folder'`. `home` is the home folder's path, which the answer of the server carries.
 */
export function browseCrumbs(path, home = null) {
  const full = String(path ?? '');
  if (full === '') return [];
  const separator = /^[A-Za-z]:/.test(full) || (full.includes('\\') && !full.includes('/')) ? '\\' : '/';
  const names = (text) => text.split(separator).filter((part) => part !== '');
  const homePath = typeof home === 'string' && home !== '' ? trimSeparators(home) : '';
  let crumbs;
  let at;
  let rest;
  if (homePath !== '' && homePath !== '/' && (trimSeparators(full) === homePath || full.startsWith(`${homePath}${separator}`))) {
    crumbs = [{ label: '~', path: homePath, kind: 'home' }];
    at = homePath;
    rest = names(full.slice(homePath.length));
  } else if (separator === '\\') {
    const [drive, ...others] = names(full);
    at = `${drive}\\`;
    crumbs = [{ label: drive, path: at, kind: 'root' }];
    rest = others;
  } else {
    at = '/';
    crumbs = [{ label: '/', path: '/', kind: 'root' }];
    rest = names(full);
  }
  for (const name of rest) {
    at = at.endsWith(separator) ? `${at}${name}` : `${at}${separator}${name}`;
    crumbs.push({ label: name, path: at, kind: 'folder' });
  }
  return crumbs;
}

/**
 * Why the folder the picker is in cannot be added: `home` (the home folder itself), `root` (nothing is above it) or `listed` (a source has it already, among
 * `sources`, the list of `/api/sources`); null when it can, and when there is no folder yet. The server has the last word (it answers `TooBroad` and
 * `AlreadyListed` all the same); this is what the button says before it is pressed. `listing` is `browseListing` of the answer.
 */
export function addBlock(listing, sources) {
  if (listing === null || listing === undefined) return null;
  if (listing.parent === null) return 'root'; // before `home`: a user who has no home folder starts at the root, which is not "the home folder itself"
  if (typeof listing.home === 'string' && sameFolder(listing.path, listing.home)) return 'home';
  if ((Array.isArray(sources) ? sources : []).some((source) => sameFolder(source?.path, listing.path))) return 'listed';
  return null;
}

/**
 * Where the selected row of a list of `count` rows goes for a key: the arrow keys by one, Home and End to the ends, Page Up and Page Down by `page`; null for
 * a key that moves nothing (and for a list with no row). The ends do not wrap: a list of folders can be hundreds long, and the down arrow
 * on the last one must not jump back to the first.
 */
export function listboxIndex(key, index, count, page = 8) {
  if (!(count > 0)) return null;
  const last = count - 1;
  switch (key) {
    case 'ArrowDown': return Math.min(index + 1, last);
    case 'ArrowUp': return Math.max(index - 1, 0);
    case 'Home': return 0;
    case 'End': return last;
    case 'PageDown': return Math.min(index + page, last);
    case 'PageUp': return Math.max(index - page, 0);
    default: return null;
  }
}

/** What the picker remembers for the tab, read back from what `browseStateJson` wrote (or anything at all): `{ path, hidden }`, `path` being the folder it was in last, or null. */
export function parseBrowseState(text) {
  let data = null;
  try {
    data = JSON.parse(text ?? 'null');
  } catch {
    data = null;
  }
  if (data === null || typeof data !== 'object' || Array.isArray(data)) data = {};
  return { path: typeof data.path === 'string' && data.path !== '' ? data.path : null, hidden: data.hidden === true };
}

/** The JSON `parseBrowseState` reads back. */
export function browseStateJson({ path, hidden }) {
  return JSON.stringify({ path: typeof path === 'string' && path !== '' ? path : null, hidden: hidden === true });
}

// ---------------------------------------------------------------------------------------------
// Tags (notes: a vault, a Markdown folder)
// ---------------------------------------------------------------------------------------------

/** What tags are compared by: the name without a leading `#`, in lower case (`#Proje` and `proje` are one tag; so are `İş` and `iş`: the dot that `İ` leaves behind is dropped). */
export function tagKey(tag) {
  return String(tag ?? '').replace(/^#/, '').toLowerCase().replace(/̇/g, '');
}

/**
 * The `tags` of `/api/overview` in the order the page lists them: the tag with the most notes first, tags with as many by name
 * (Turkish rules). Entries that are not `{ name, count }` are left out. Returns a new list.
 */
export function sortTags(tags) {
  return (Array.isArray(tags) ? tags : [])
    .filter((tag) => typeof tag?.name === 'string' && tag.name !== '')
    .map((tag) => ({ name: tag.name, count: Number.isFinite(tag.count) && tag.count > 0 ? Math.round(tag.count) : 0 }))
    .sort((a, b) => b.count - a.count
      || nameCollator.compare(a.name, b.name)
      || (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
}

/** The files of a tree (`flattenFiles`) that carry `tag` in their `tags`, whatever its case, in the order given. */
export function filesWithTag(files, tag) {
  const key = tagKey(tag);
  if (key === '') return [];
  return (Array.isArray(files) ? files : []).filter((file) => Array.isArray(file?.tags) && file.tags.some((name) => tagKey(name) === key));
}

/**
 * The properties of a note without its tags (`tags`, and `tag`, which Obsidian also knows): they are chips under the name of the note, so
 * the list of properties does not say them twice. A value that is no object (a note with no frontmatter) comes back as it is.
 */
export function propertiesWithoutTags(frontmatter) {
  if (frontmatter === null || typeof frontmatter !== 'object' || Array.isArray(frontmatter)) return frontmatter;
  return Object.fromEntries(Object.entries(frontmatter).filter(([key]) => !['tags', 'tag'].includes(key.toLowerCase())));
}

/**
 * What the overview of a source of notes has to start from, out of an `/api/overview` response: `{ entry, recent, mostLinked }`. `entry` is
 * `{ path, title }` of the note to start reading from, or null (a server that names none, or an older one). `recent` is
 * `[{ path, modifiedAt }]`, as the server ordered it. `mostLinked` is `[{ path, count }]`, and a note with no backlink is not in it.
 * An entry that is no path is left out of its list.
 */
export function noteFront(data) {
  const list = (value) => (Array.isArray(value) ? value : []);
  const named = (item) => typeof item?.path === 'string' && item.path !== '';
  const entry = data?.entry;
  return {
    entry: named(entry)
      ? { path: entry.path, title: typeof entry.title === 'string' && entry.title.trim() !== '' ? entry.title.trim() : noteName(entry.path.slice(entry.path.lastIndexOf('/') + 1)) }
      : null,
    recent: list(data?.recent).filter(named).map((item) => ({ path: item.path, modifiedAt: typeof item.modifiedAt === 'string' ? item.modifiedAt : '' })),
    mostLinked: list(data?.mostLinked).filter(named)
      .map((item) => ({ path: item.path, count: Number.isFinite(item.count) ? Math.round(item.count) : 0 }))
      .filter((item) => item.count > 0),
  };
}

// A tag in running text: `#` after the start or a space or a bracket (not after a letter, a digit or a slash: `a#b`, the `#top` of a web
// address), then letters, digits, `_`, `-` and `/` (`#alan/alt`).
const INLINE_TAG_RE = /(?<![\p{L}\p{N}_/&#-])#([\p{L}\p{N}_/-]+)/gu;

/**
 * The `#tag`s in `text` that are tags of the file (`tags`, the file's own list: `/api/file` says which tags it has, so a
 * `#word` that is not one of them is just a word). `[{ index, raw, tag }]` in order: `raw` is the text as written (`#Proje`), `tag` the
 * name the file lists it by (`proje`). The page makes them links to the notes with that tag. Code and links are the caller's to skip.
 */
export function findInlineTags(text, tags) {
  const known = new Map();
  for (const tag of Array.isArray(tags) ? tags : []) {
    if (typeof tag === 'string' && tag !== '') known.set(tagKey(tag), tag);
  }
  const found = [];
  if (known.size === 0) return found;
  for (const match of String(text ?? '').matchAll(INLINE_TAG_RE)) {
    let used = match[1];
    let name = known.get(tagKey(used));
    if (name === undefined) {
      used = used.replace(/[-/]+$/, ''); // `#proje-` at the end of a phrase: the dash is not the tag's
      name = used === '' ? undefined : known.get(tagKey(used));
    }
    if (name !== undefined) found.push({ index: match.index, raw: `#${used}`, tag: name });
  }
  return found;
}

// ---------------------------------------------------------------------------------------------
// Quick open: finding a file by its name, its path or its project
// ---------------------------------------------------------------------------------------------

/** How many files the quick opener lists, and how many it remembers for an empty search. */
export const QUICK_LIMIT = 50;
export const RECENT_MAX = 12;

/** One character as it is compared: lower case, without its accent (`ş` is `s`), and the Turkish `ı` is an `i`. A reader types what is on the keyboard. */
export function foldChar(char) {
  const first = Array.from(String(char).normalize('NFD'))[0] ?? '';
  const lower = first.toLowerCase();
  return lower === 'ı' ? 'i' : lower;
}

/** The characters (code points) of `text`, each folded (see `foldChar`): what `matchText` compares. */
export function foldText(text) {
  return Array.from(String(text ?? '')).map(foldChar);
}

// A search folds the same names again with every key that is typed: they are folded once.
const foldCache = new Map();

function charsOf(text) {
  let chars = foldCache.get(text);
  if (chars === undefined) {
    const original = Array.from(text);
    chars = { original, folded: original.map(foldChar) };
    if (foldCache.size >= 30_000) foldCache.clear();
    foldCache.set(text, chars);
  }
  return chars;
}

/** A word starts at the beginning, after anything that is not a letter or a digit (`-`, `_`, `.`, `/`, a space), and at a capital after a small letter. */
function isWordStart(chars, index) {
  if (index === 0) return true;
  const before = chars[index - 1];
  if (!/[\p{L}\p{N}]/u.test(before)) return true;
  return /\p{Lu}/u.test(chars[index]) && /\p{Ll}/u.test(before);
}

const TIER_BASE = { prefix: 4000, word: 3000, substring: 2000, fuzzy: 1000 };

/**
 * How one word of a search (`query`, folded: see `foldText`) matches `text`, or null when it does not. The tiers, in the
 * order a reader means them: `prefix` (the text starts with it), `word` (it starts a word of the text: after `-`, `_`, `.`,
 * `/`, a space, or at a capital), `substring` (it is inside a word) and `fuzzy` (its letters are in the text in order,
 * with others between them). `indices` are the places (code points) of the text that matched, for the highlight. `score`
 * orders matches of any tier: a better tier always beats a worse one, and inside a tier a shorter text and an earlier
 * match come first. A fuzzy match is either the initials of words (`drm` for `deployment-rules.md`) or letters that are
 * close together: as many in between as the word has (four at least), because letters scattered over a long text are not a
 * match of anything. `fuzzy: false` leaves that tier out.
 */
export function matchText(query, text, { fuzzy = true } = {}) {
  const m = query.length;
  const { original, folded } = charsOf(String(text ?? ''));
  const n = original.length;
  if (m === 0 || m > n) return null;
  const run = (start) => Array.from({ length: m }, (_, offset) => start + offset);
  const score = (tier, start) => TIER_BASE[tier] - Math.min(n, 300) - Math.min(start, 300);

  let word = -1;
  let inside = -1;
  for (let start = 0; start + m <= n; start += 1) {
    let k = 0;
    while (k < m && folded[start + k] === query[k]) k += 1;
    if (k < m) continue;
    if (start === 0) return { tier: 'prefix', score: score('prefix', 0), indices: run(0) };
    if (word < 0 && isWordStart(original, start)) word = start;
    if (inside < 0) inside = start;
  }
  if (word >= 0) return { tier: 'word', score: score('word', word), indices: run(word) };
  if (inside >= 0) return { tier: 'substring', score: score('substring', inside), indices: run(inside) };
  if (!fuzzy) return null;

  // Fuzzy. First the initials: every letter starts a word, one after the other (`drm`: deployment-rules.md).
  const initials = [];
  for (let at = 0, k = 0; k < m; k += 1) {
    let found = -1;
    for (let i = at; i < n && found < 0; i += 1) if (folded[i] === query[k] && isWordStart(original, i)) found = i;
    if (found < 0) break;
    initials.push(found);
    at = found + 1;
  }
  // Never below 1: a weaker place of the same entry must not look better for being worth less (its score is multiplied by a weight under 1).
  const loose = (indices) => Math.max(1, TIER_BASE.fuzzy - Math.min(n, 300) - Math.min(indices[0], 300) - (indices.at(-1) - indices[0] + 1 - m) * 3);
  if (initials.length === m) return { tier: 'fuzzy', score: loose(initials) + m * 4, indices: initials };

  // Then the letters that are close together: walk forward to the first place where every letter has been seen, then
  // back from there, so the letters that are taken are the tightest ones that end there.
  let next = 0;
  let end = -1;
  for (let i = 0; i < n && end < 0; i += 1) {
    if (folded[i] === query[next]) {
      next += 1;
      if (next === m) end = i;
    }
  }
  if (end < 0) return null;
  const indices = [];
  next = m - 1;
  for (let i = end; next >= 0; i -= 1) {
    if (folded[i] === query[next]) {
      indices.push(i);
      next -= 1;
    }
  }
  indices.reverse();
  if (end - indices[0] + 1 - m > Math.max(4, m)) return null;
  const starts = indices.filter((index) => isWordStart(original, index)).length;
  return { tier: 'fuzzy', score: loose(indices) + starts * 4, indices };
}

/** `text` in runs, `[{ text, hit }]`, `hit` being true for the characters at `indices` (code points): what the page marks. `text` is the parts joined. */
export function highlightParts(text, indices) {
  const marked = new Set(indices ?? []);
  const parts = [];
  Array.from(String(text ?? '')).forEach((char, index) => {
    const hit = marked.has(index);
    const last = parts[parts.length - 1];
    if (last && last.hit === hit) last.text += char;
    else parts.push({ text: char, hit });
  });
  return parts;
}

/**
 * What the quick opener can find, one entry per file of `/api/tree` (`files`, see `flattenFiles`): `{ path, name, context, layer,
 * loadMode }`. `name` and `context` are what a row shows. A file is its name and the folder it is in; a skill's file (every
 * one is `SKILL.md`) is the skill and the folder the skill is in; a project's memory file is its file and the project, by
 * the name the page calls it (`labels`, see `projectLabels`; where it lives is added when another project has the same name).
 * The path is matched too, but not shown. A note (`notes`: the source is a vault or a Markdown folder) is its name without `.md`
 * and its folder: there are no skills and no projects there.
 */
export function quickEntries(files, labels, { notes = false } = {}) {
  const entries = [];
  for (const file of Array.isArray(files) ? files : []) {
    if (typeof file?.path !== 'string' || file.path === '') continue;
    const path = file.path;
    const shown = displayPath(path, notes ? undefined : labels);
    const skill = notes ? null : skillFile(path);
    let name;
    let context;
    if (shown.kind === 'project') {
      name = shown.file;
      context = shown.project.shared && shown.project.context ? `${shown.project.name} · ${shown.project.context}` : shown.project.name;
    } else if (skill) {
      name = skill.skill;
      context = path.split('/').slice(0, -2).join('/');
    } else {
      name = notes ? noteName(shown.name) : shown.name;
      context = `${shown.head}${shown.parent}`.replace(/\/+$/, '');
    }
    entries.push({ path, name, context, layer: enumName(file.layer), loadMode: enumName(file.loadMode) });
  }
  return entries;
}

// What a word of the search is worth in each place it can match: the name of the file is what a reader types most often.
const FIELD_WEIGHT = { name: 1, context: 0.6, path: 0.4 };

/**
 * The files that answer a search, best first: `{ items: [{ entry, score, nameMarks, contextMarks }], total }`. Every word of
 * `query` (split at spaces) must match somewhere in an entry (`quickEntries`): its name, the place it is shown in or its
 * path (which is matched whole or by its words, never loosely), the name being worth most; a better kind of match wins (`matchText`), and what matches equally well comes
 * the file opened most recently first (`recent`, paths, newest first), then the shorter name, then the place and the
 * path. `nameMarks` and `contextMarks` are the characters to mark in what a row shows (the path is not shown, so
 * nothing of it is marked); a loose (fuzzy) match is marked only where it is the best one. At most `limit` items;
 * `total` counts them all. An empty query finds nothing: it is the recent files' (`recentResults`).
 */
export function quickSearch(entries, query, { limit = QUICK_LIMIT, recent = [] } = {}) {
  const words = String(query ?? '').trim().split(/\s+/).filter(Boolean).map(foldText);
  if (words.length === 0) return { items: [], total: 0 };
  const rank = new Map((recent ?? []).map((path, index) => [path, index]));
  const rankOf = (entry) => rank.get(entry.path) ?? Infinity;
  const hits = [];
  for (const entry of entries ?? []) {
    let score = 0;
    const marks = { name: new Set(), context: new Set() };
    let matched = true;
    for (const word of words) {
      const found = [
        ['name', matchText(word, entry.name)],
        ['context', entry.context ? matchText(word, entry.context) : null],
        ['path', matchText(word, entry.path, { fuzzy: false })],
      ];
      let best = null;
      for (const [field, match] of found) {
        const weighted = match ? match.score * FIELD_WEIGHT[field] : 0;
        if (match && (best === null || weighted > best.weighted)) best = { field, weighted };
      }
      if (best === null) {
        matched = false;
        break;
      }
      score += best.weighted;
      for (const [field, match] of found) {
        if (match && field !== 'path' && (field === best.field || match.tier !== 'fuzzy')) match.indices.forEach((index) => marks[field].add(index));
      }
    }
    if (matched) {
      hits.push({
        entry, score, nameMarks: [...marks.name].sort((a, b) => a - b), contextMarks: [...marks.context].sort((a, b) => a - b),
      });
    }
  }
  hits.sort((a, b) => b.score - a.score
    || rankOf(a.entry) - rankOf(b.entry)
    || a.entry.name.length - b.entry.name.length
    || nameCollator.compare(a.entry.context, b.entry.context)
    || (a.entry.path < b.entry.path ? -1 : a.entry.path > b.entry.path ? 1 : 0));
  return { items: hits.slice(0, limit), total: hits.length };
}

/** The rows of an empty search: the files opened lately (`paths`, newest first) that are still in `entries`, in that order. Same shape as `quickSearch`'s items, with nothing marked. */
export function recentResults(entries, paths, limit = RECENT_MAX) {
  const byPath = new Map((entries ?? []).map((entry) => [entry.path, entry]));
  const items = [];
  for (const path of paths ?? []) {
    const entry = byPath.get(path);
    if (entry) items.push({ entry, score: 0, nameMarks: [], contextMarks: [] });
  }
  return items.slice(0, limit);
}

/** The list of recently opened files with `path` put first (once), at most `max` long. Returns a new list. */
export function pushRecent(list, path, max = RECENT_MAX) {
  return [path, ...(Array.isArray(list) ? list : []).filter((item) => item !== path)].slice(0, max);
}

/** What `recentJson` wrote, or anything at all: a list of distinct paths, newest first; whatever is not of that shape is no list. */
export function parseRecent(text) {
  let data = null;
  try {
    data = JSON.parse(text ?? 'null');
  } catch {
    data = null;
  }
  if (!Array.isArray(data)) return [];
  return [...new Set(data.filter((item) => typeof item === 'string' && item !== ''))].slice(0, RECENT_MAX);
}

/** The JSON `parseRecent` reads back. */
export function recentJson(list) {
  return JSON.stringify(list);
}

/** Whether a platform name (`navigator.platform`) is Apple's: its shortcut key is Cmd. */
export function isApplePlatform(platform) {
  return /^(mac|iphone|ipad|ipod)/i.test(String(platform ?? ''));
}

/**
 * Whether a key press asks for the quick opener: Ctrl+O or Ctrl+K (Cmd+O and Cmd+K on a Mac), or `/` when the focus is not in a
 * field that takes text (`editable`). With Shift or Alt Ctrl+O / Ctrl+K are other commands of the browser, and a `/` typed
 * with Ctrl or Cmd belongs to something else. Shift is allowed for `/`: a Turkish keyboard has it on 7. A letter that the
 * layout does not have (another alphabet) is read from the physical key (`code`). `event` is `{ key, code, ctrlKey, metaKey,
 * altKey, shiftKey, isComposing, repeat }`.
 */
export function opensQuickOpen(event, { editable = false } = {}) {
  if (event.isComposing) return false;
  const key = String(event.key ?? '');
  if (event.ctrlKey || event.metaKey) {
    if (event.altKey || event.shiftKey) return false;
    const letter = /^[a-z]$/i.test(key) ? key.toLowerCase() : String(event.code ?? '').replace(/^Key/, '').toLowerCase();
    return letter === 'o' || letter === 'k';
  }
  return key === '/' && !event.altKey && !editable && !event.repeat;
}
