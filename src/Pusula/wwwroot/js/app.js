// pusula - the page: routing, API calls, rendering, DOM post-processing and live updates.
//
// The page talks to the server only through the JSON API. Markdown is rendered here with
// markdown-it (loaded as a classic script before this module). `innerHTML` receives markdown-it
// output and nothing else; every other node is built with createElement / textContent.

import {
  addBlock, addSourceBody, ageParts, ageSince, ancestorDirs, appVersion, arrivalTarget, blockIndexForLine, browseCrumbs, browseFound, browseListing,
  browseStateJson, browseUrl, budgetParts, buildLinkMap, calloutClass, capitalize,
  changedBlocks, changeName, closedAncestor, copyMethod, createMarkdown, createSlugger, decodeHref, displayPath, displayStatus,
  embedAssetKind, embedName, enumName, fileEnds, fileHash, filesWithTag, findClaudePaths, findInlineTags, findMarkdownLink,
  findWikiLink, flattenFiles, folderHolds, folderPaths, formatDelta, formatPercent, formatTokens, frontmatterProblem, highlightParts,
  homeSlug, isApplePlatform, isDirectory, isNoteProfile, isRelativePath, issueSection, issueStartsOpen,
  linkHref, linkKey, listboxIndex, listedLast, loadedTokens, loadFailure, LOAD_MODES, loadModesByLayer, mergeLinks, noteFront, noteName, opensQuickOpen,
  orderProjectFolders, overviewDiff, overviewHash, pageIsAtAddress, parseBrowseState, parseCalloutMarker, parseRecent, parseRoute,
  parseUiState, pathHeadCut, percent, pillTarget, positiveLine, profileName, projectFolders, projectLabels, propertiesWithoutTags,
  pushRecent, quickEntries, quickSearch, readableWikilinks, recentJson, recentResults, RELEASES_URL, REMOTE_EDIT_FLAG, resolveRoute, savedPosition,
  scrollTargetTop, skillFile, slugify, sortTags, SOURCE_REFUSALS, sourceErrorCode, sourceFailure, SOURCES_HASH, sourceKey,
  sourceLock, sourcesAccess, sourcesHost, splitIssues, splitLayers, summarizeChange, tagKey, tokenParts, trLocative, uiStateJson,
  withPosition,
} from './core.js';
import { getLang, initLang, setLang, setVariant, t, tn } from './i18n.js';

// What the page remembers is kept per source (see `sourceKey`): the folders open in its tree, the files opened lately in this tab,
// what its overview and files have open or folded. The source opened last is the one the next visit starts with.
const OPEN_DIRS_KEY = 'pusula.openDirs';
const UI_KEY = 'pusula.ui'; // sessionStorage: what the overview and the file view have open, so a reload shows the same page
const RECENT_KEY = 'pusula.recent'; // sessionStorage: the files opened in this tab, newest first, for the quick opener
const LAST_SOURCE_KEY = 'pusula.lastSource'; // localStorage: the id of the source opened last
const RECONNECT_MS = 5000;
const TOAST_MS = 6000;
const COPY_MS = 2000; // how long "copied" stays on the button that copied
const LIST_ROWS = 8; // rows an overview list shows before "show more"
const TAG_ROWS = 30; // tags the overview lists before "show more"
const HIT_MS = 1500; // how long the place a route moved to stays marked
const LINE_MARGIN = 48; // px of the text above a block that a ?l=<line> route leaves in view
const POSITION_SETTLE_MS = 200; // quiet time after a scroll before the reading place is written into the history entry
const POSITION_MAX_MS = 1000; // ...but never later than this after the first scroll that is not written yet
const FRESH_MS = 3000; // how long the tree marks a file that changed (the mark fades out over it)
const FRESH_DOT_MS = 10000; // how long a closed folder wears the dot for a file that changed inside it
const BLOCK_MS = 3000; // how long the blocks of the open file that changed stay marked
const BADGE_MS = 4000; // how long a change in a number of the overview is written beside it
const PILL_MS = 8000; // how long the pill that points at a change off screen waits to be used
const AGE_TICK_MS = 15000; // how often "12 sn önce" is written again
const ANNOUNCE_MS = 10000; // how long what a screen reader was told stays in its live region
const BROWSE_KEY = 'pusula.browse'; // sessionStorage: the folder the folder picker was in last, and whether it showed the hidden ones
const FOUND_ROWS = 3; // folders the server found by itself that the picker lists before "show more" (the list below is what the window is for)
const BROWSE_WAIT_MS = 150; // how long a folder may take before the picker says it is loading (a quicker answer would only make it flicker)
const BROWSE_PAGE = 8; // rows that Page Up and Page Down move in the picker

/** Problems first: this is the order of the link groups in the info panel (`Folder` is shown, never sent). */
const STATUS_ORDER = ['Broken', 'Pending', 'Resolved', 'Folder', 'NonMarkdown', 'External'];

const state = {
  sources: [], // /api/sources: the sources the server lists, `{ id, name, path, profile, available, fileCount, errorCode? }` (its English `error` sentence is never shown)
  sourcesFile: null, // the file the server reads them from, or null when they came from the command line
  sourcesAccess: { canEdit: false, closed: null, fileError: null }, // may this browser add and remove sources, why not, what is wrong with the file (see `sourcesAccess`)
  host: { machine: null, remoteEdit: false }, // the computer the server runs on, and whether it lets other machines change the list (see `sourcesHost`)
  appVersion: null, // the version of pusula the server says it is (see `appVersion`); null when it says none, as an older server does
  listed: false, // the server has answered for its sources once, or has been tried and did not: before that the page cannot tell that no source is open
  sourceEdit: { // what the sources page keeps while it is drawn again: the form under the cards, and the card that asks "remove it?"
    path: '', name: '', // what is typed in the form
    helpOpen: false, // whether "other ways" under the form is unfolded
    adding: false, // a request that adds a source is on its way
    addError: null, // what it was refused with: `{ kind, params, field }`, see `sourceFailure`
    asking: null, // the id of the source whose card asks for the removal to be confirmed
    removing: false, // a request that removes it is on its way
    removeError: null, // what that was refused with
  },
  source: null, // the source that is open (an entry of `sources`), null when none is
  missing: null, // the source an address named that the server does not list: the sources page says so
  tree: null, // /api/sources/<id>/tree response
  folders: new Set(), // the folder paths of the tree (a link to one is shown as "folder")
  labels: new Map(), // project folder (slug) -> the name it is shown by, see projectLabels in core.js
  overview: null, // .../overview response
  file: null, // .../file response of the open file
  notFound: null, // path whose .../file answered 404
  error: null, // last load error
  route: { view: 'overview', source: null, issues: null, memory: false, tag: null },
  version: null, // index version the page was built from
  openDirs: new Set(), // the next five are what the page remembers per source, see `loadSourceState`
  recent: [], // paths of the files opened in this tab, newest first
  issueOpen: new Map(), // issue group -> open, only where the reader (or a route) overrode the default
  moreOpen: new Set(), // overview lists shown past their first rows
  openBand: new Set(), // description groups of the budget band whose files are listed
  memoryOpen: false, // whether the project memory table of the overview is expanded
  layersRestOpen: false, // whether the layers that load only when needed are listed
  legendOpen: false, // whether the legend at the bottom of the tree is expanded
  propertiesOpen: true, // whether the Properties block of a file is expanded
  live: 'connecting',
  offline: false, // the live connection is lost: what is on screen may be out of date (the strip under the top bar says so)
  contentAt: null, // when what is on screen was last known to be what the server has
  errorAt: null, // when the load error that is on screen happened
  palette: { items: [], active: 0 }, // the quick opener: the rows it lists and the one that is selected
  lastChange: null, // what the last `changed` event brought: { path, kind, extra, paths, at }, see summarizeChange
  fresh: new Map(), // path -> when it changed, for the files the tree still marks
  stamp: null, // { path, at }: the open file was updated at that moment (shown in its load line)
  browse: { // the folder picker of the sources page (see "The folder picker")
    listing: null, // `browseListing` of the folder it shows, null until one has come
    loading: false, // a folder is on its way
    waiting: false, // ...and it has been on its way longer than BROWSE_WAIT_MS
    hidden: false, // list the folders whose names start with a dot as well
    found: { status: 'idle', folders: [], complete: true, all: false }, // what the server found by itself: `status` is idle, loading, ready or failed; `all`: the rows past FOUND_ROWS are shown
    active: 0, // the row of the folder list that is selected
    adding: null, // `{ path, row }` while the request that adds a folder is on its way (`row`: it was a found folder's button), else null
    error: null, // what the last request was refused with (see `sourceFailure`), and `retry`: the folder to ask for again, or null
  },
};

const refs = {};
let navSeq = 0; // bumped per navigation so a slow response cannot overwrite a newer view
let overlay = null; // 'tree' | 'info' | null (narrow screens)
let eventSource = null;
let eventsFor = null; // the id of the source the live connection is for
let reconnectTimer = null;
let retryTimer = null;
let hadConnectionError = false;
let refreshChain = Promise.resolve();
let infoPath = null; // the file the info panel shows, so the panel starts at its top only for another file
let paletteOpener = null; // what had the focus when the quick opener opened, to give it back when it closes
let paletteIndex = { tree: null, entries: [] }; // what the quick opener searches, made again when the tree is
let browseSeq = 0; // bumped for every folder the picker asks for (and when it closes): an answer that is not the last one asked for is dropped
let foundSeq = 0; // the same for the folders the server found
let browseWait = 0; // the timer behind `state.browse.waiting`

const markdown = typeof window.markdownit === 'function' ? createMarkdown(window.markdownit) : null;

// ---------------------------------------------------------------------------------------------
// Small helpers
// ---------------------------------------------------------------------------------------------

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined && text !== null) node.textContent = text;
  return node;
}

function readOpenDirs(id) {
  try {
    const parsed = JSON.parse(localStorage.getItem(sourceKey(OPEN_DIRS_KEY, id)) ?? '[]');
    return new Set(Array.isArray(parsed) ? parsed.filter((item) => typeof item === 'string') : []);
  } catch {
    return new Set();
  }
}

function saveOpenDirs() {
  if (state.source === null) return;
  try {
    localStorage.setItem(sourceKey(OPEN_DIRS_KEY, state.source.id), JSON.stringify([...state.openDirs]));
  } catch {
    // Storage can be blocked; open folders then last for this page only.
  }
}

function readUi(id) {
  try {
    return parseUiState(sessionStorage.getItem(sourceKey(UI_KEY, id)));
  } catch {
    return parseUiState(null);
  }
}

function readRecent(id) {
  try {
    return parseRecent(sessionStorage.getItem(sourceKey(RECENT_KEY, id)));
  } catch {
    return [];
  }
}

/** The file that is open is the one opened last: the quick opener lists it first when nothing is typed. */
function rememberRecent(path) {
  state.recent = pushRecent(state.recent, path);
  try {
    sessionStorage.setItem(sourceKey(RECENT_KEY, state.source.id), recentJson(state.recent));
  } catch {
    // Storage can be blocked; the list then lasts for this page only.
  }
}

/** The id of the source opened last, or null (nothing stored, or storage blocked). */
function readLastSource() {
  try {
    return localStorage.getItem(LAST_SOURCE_KEY);
  } catch {
    return null;
  }
}

function rememberSource(id) {
  try {
    localStorage.setItem(LAST_SOURCE_KEY, id);
  } catch {
    // Storage can be blocked; the next visit then starts with the first source.
  }
}

/** A source that was removed is not the one opened last any more: the next visit starts with the first source. */
function forgetSource(id) {
  try {
    if (localStorage.getItem(LAST_SOURCE_KEY) === id) localStorage.removeItem(LAST_SOURCE_KEY);
  } catch {
    // Storage can be blocked; there is then nothing remembered to forget.
  }
}

let uiWritten = '';

/** Remembers what is open or folded for the tab (not for the next visit): a reload then shows the page the scroll position belongs to. */
function saveUi() {
  if (state.source === null) return;
  const json = uiStateJson(state);
  if (json === uiWritten) return; // a <details> also fires `toggle` when it is built open
  try {
    sessionStorage.setItem(sourceKey(UI_KEY, state.source.id), json);
    uiWritten = json;
  } catch {
    // Storage can be blocked; the page then comes back folded the way it starts.
  }
}

/** What the page remembers about a source, read back when it is opened (and when another has been open in between). */
function loadSourceState(id) {
  const ui = readUi(id);
  state.openDirs = readOpenDirs(id);
  state.recent = readRecent(id);
  state.issueOpen = ui.issueOpen;
  state.moreOpen = ui.moreOpen;
  state.openBand = ui.openBand;
  state.memoryOpen = ui.memoryOpen;
  state.layersRestOpen = ui.layersRestOpen;
  state.legendOpen = ui.legendOpen;
  state.propertiesOpen = ui.propertiesOpen;
  uiWritten = '';
}

function stripSlashes(path) {
  return path.replace(/\/+$/, '');
}

function baseName(path) {
  return path.slice(path.lastIndexOf('/') + 1);
}

/** True while the page shows a source of notes (an Obsidian vault, a Markdown folder): no layers, load markers or tokens there. */
function isNotes() {
  return state.source !== null && isNoteProfile(state.source.profile);
}

/** The id of the open source: every address the page writes starts with it (see `fileHash`). */
function sid() {
  return state.source === null ? null : state.source.id;
}

/** The name a file is shown by: a note without its `.md`. */
function shownName(name) {
  return isNotes() ? noteName(name) : name;
}

function layerName(layer) {
  return t(`layer.${enumName(layer)}`);
}

function loadName(mode) {
  return t(`load.${enumName(mode)}`);
}

function loadShortName(mode) {
  return t(`loadShort.${enumName(mode)}`);
}

/** `decorative`: the words next to the marker already say what it says, so it is only there to be recognised. */
function loadDot(mode, { decorative = false } = {}) {
  const name = enumName(mode);
  const dot = el('span', `dot lm-${name}`);
  if (decorative) {
    dot.setAttribute('aria-hidden', 'true');
    return dot;
  }
  dot.title = t(`load.${name}`);
  dot.setAttribute('role', 'img');
  dot.setAttribute('aria-label', dot.title);
  return dot;
}

function tokensText(tokens) {
  const text = el('span', 'tokens', formatTokens(tokens));
  text.title = t('tokens.hint');
  return text;
}

/**
 * The tokens of a tree row (a file or a folder): what it puts into every session first, its total after
 * it, quietly (see `tokenParts`). The tooltip and the words a screen reader gets say both in full.
 */
function treeTokens(node) {
  const { lead, total } = tokenParts(node.everySessionTokens, node.tokens);
  const words = lead === null
    ? t('tokens.total', { total: formatTokens(node.tokens) })
    : t('tokens.pair', { every: lead, total: formatTokens(node.tokens) });

  const shown = el('span');
  shown.setAttribute('aria-hidden', 'true');
  if (lead === null) shown.append(total);
  else shown.append(el('span', 'tokens-lead', lead), ' ', el('span', 'tokens-total', total));

  const cell = el('span', 'tokens');
  cell.title = words;
  cell.append(shown, el('span', 'sr-only', words));
  return cell;
}

/**
 * A project's folder (`projects/<slug>`) named the way a reader looks for it: its name in strong type, which
 * a line never cuts (see `.proj-name` in app.css), and where it lives beside it, quiet and the first thing to
 * give way. `context` says when the place is shown: 'shared' only for a name that more than one folder has, since
 * a list or the tree has no room to spare for what the name already says; 'always' where there is room (a table, the
 * breadcrumb). `label` is an entry of `state.labels`.
 */
function projectName(label, { context = 'shared' } = {}) {
  const name = el('span', 'proj-name');
  name.append(el('span', 'proj-text', label.name));
  if (!label.context || (context === 'shared' && !label.shared)) return [name];
  // The space is for text that is not laid out (a screen reader, a copy): a flex row ignores it and spaces them with a margin.
  return [name, ' ', el('span', label.shared ? 'proj-context is-shared' : 'proj-context', label.context)];
}

/** The file of a project's memory: its end stays and its middle gives way (see `fileEnds`, `.file-start`). */
function fileName(file) {
  const { start, end } = fileEnds(file);
  return [...(start ? [el('span', 'file-start', start)] : []), el('span', 'file-end', end)];
}

/**
 * A link to a file that stays on one line. The file name is shown in full, the folders around it
 * are muted, and when the path does not fit the upper folders are cut with an ellipsis (see
 * `.path-link` in app.css). A project's memory file is shown as `project · file` instead, the project by its
 * name and not its slug (`displayPath`): what gives way there is the project's place, then the middle of the file
 * name, and the project's name last of all. The full path is the tooltip. `line` makes the link open the file at
 * that source line, `heading` at that heading (and shows it after the name); `skillFirst` shows
 * `skills/x/SKILL.md` as `x · SKILL.md`, the skill first.
 */
function pathLink(path, { line, heading, skillFirst = false } = {}) {
  const link = el('a', 'path-link');
  link.href = fileHash(sid(), path, heading, line);
  link.title = heading ? `${path}#${heading}` : path;
  const shown = displayPath(path, state.labels);
  const skill = skillFirst && !isNotes() ? skillFile(path) : null;
  if (shown.kind === 'project') {
    link.classList.add('is-project');
    link.append(...projectName(shown.project), el('span', 'path-tail', ' · '), ...fileName(shown.file));
  } else if (skill) {
    link.append(el('span', 'path-name', skill.skill), el('span', 'path-tail', ` · ${skill.file}`));
  } else {
    const { head, parent, name } = shown;
    if (head) link.append(el('span', 'path-head', head));
    if (parent) link.append(el('span', 'path-parent', parent));
    link.append(el('span', 'path-name', shownName(name)));
  }
  if (heading) link.append(el('span', 'path-tail', `#${heading}`));
  // A cut file name is two flex items, which a screen reader reads with a space between them: the link says its text itself.
  if (shown.kind === 'project') link.setAttribute('aria-label', link.textContent);
  return link;
}

/**
 * "line 15" or "lines 15, 58". The wording (info.lines) says where the numbers go, so a language can
 * put them before or after the word. With `hrefOf` every number is a link to that line (of the file
 * the page shows; on a touch screen each is a 40px control and the commas are hidden, see app.css);
 * without it the note is plain text, for rows whose path link already goes to the line.
 */
function lineNote(lines, hrefOf = null) {
  const note = el('span', hrefOf ? 'muted line-note' : 'muted tail');
  const [before, after = ''] = tn('info.lines', lines.length).split('{lines}');
  note.append(before);
  lines.forEach((line, index) => {
    if (index > 0) note.append(el('span', 'line-sep', ', '));
    if (hrefOf) {
      const link = el('a', 'line-link', String(line));
      link.href = hrefOf(line);
      note.append(link);
    } else {
      note.append(String(line));
    }
  });
  note.append(after);
  return note;
}

// ---- Paths on one line: the upper folders that no longer fit ----------------------------------

let measureContext = null;
let fitFrame = 0;

/** The width of a text in a font, measured on a canvas: the page measures a few characters of a path, not a line. */
function textWidth(text, font) {
  measureContext ??= document.createElement('canvas').getContext('2d');
  measureContext.font = font;
  return measureContext.measureText(text).width;
}

/**
 * A path link on one line gives its upper folders (`.path-head`) up first when it does not fit, and an ellipsis does the cutting: with little room that
 * leaves a piece of a name ("0Kararlar/"). Where less than three characters of them are left to read, the head is left out for a plain "…/" (`.is-cut`:
 * the head stays in the document for a screen reader, and the "…/" is drawn). The links of `root` are measured as they are drawn, all of them at
 * once (a read after each write would be a layout for each); one that is not on screen (a row folded away, a group that is closed) is left for when it is.
 */
function fitPathHeads(root) {
  const links = [...root.querySelectorAll('a.path-link')].filter((link) => link.firstElementChild?.classList.contains('path-head'));
  for (const link of links) link.classList.remove('is-cut');
  const cuts = links.map((link) => {
    const head = link.firstElementChild;
    if (link.getClientRects().length === 0 || head.scrollWidth <= head.clientWidth) return false;
    const style = window.getComputedStyle(head);
    const font = `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
    return pathHeadCut({
      room: head.clientWidth,
      whole: head.scrollWidth,
      widths: Array.from(head.textContent).slice(0, 3).map((char) => textWidth(char, font)),
      ellipsis: textWidth('\u2026', font),
    });
  });
  links.forEach((link, index) => link.classList.toggle('is-cut', cuts[index]));
}

/** The paths of the page and of the panel are measured again after the next frame: once a page is drawn, a row is shown, a group opens or the window changes size. */
function scheduleFit() {
  if (fitFrame !== 0) return;
  fitFrame = window.requestAnimationFrame(() => {
    fitFrame = 0;
    fitPathHeads(refs.content);
    fitPathHeads(refs.info);
    fitPathHeads(refs.browse);
    syncFoundCut();
  });
}

/** The found folders' bottom edge fades (`.is-cut`) while there are rows below it: once they are drawn, scrolled, or the window changes size. */
function syncFoundCut() {
  const list = refs.browseFoundList;
  list.classList.toggle('is-cut', list.scrollTop + list.clientHeight < list.scrollHeight - 1);
}

/** Compares two /api/file responses ignoring the index version. */
function sameFileData(a, b) {
  return JSON.stringify({ ...a, version: 0 }) === JSON.stringify({ ...b, version: 0 });
}

// ---------------------------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------------------------

class ApiError extends Error {
  constructor(status, detail = '', code = '') {
    super(`HTTP ${status}`);
    this.status = status;
    this.detail = detail; // the server's own sentence, in English: never shown (see loadFailure)
    this.code = code; // what the server calls why a source cannot be read (a 503, see loadFailure) or a refusal to add or remove one (see sourceFailure)
  }
}

async function getJson(url) {
  const response = await fetch(url, { headers: { Accept: 'application/json' }, cache: 'no-store' });
  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(response.status, problem.detail, problem.code);
  }
  const data = await response.json();
  state.contentAt = new Date(); // the server has just said what it has
  return data;
}

/** The `detail` and the machine-readable `code` of a ProblemDetails answer; '' for what is missing, or when the answer is not one. */
async function readProblem(response) {
  try {
    const problem = await response.json();
    return {
      detail: typeof problem?.detail === 'string' ? problem.detail : '',
      code: typeof problem?.code === 'string' ? problem.code : '',
    };
  } catch {
    return { detail: '', code: '' };
  }
}

/**
 * The only place the page writes to the server: adds a source (POST, the JSON `body`; the answer is the new source) or removes one (DELETE; the
 * answer is empty). A refusal is an `ApiError` that carries the `code` of its ProblemDetails; no answer at all is the `TypeError` of `fetch`.
 */
async function editSources(method, url, body) {
  const headers = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(response.status, problem.detail, problem.code);
  }
  return response.status === 204 ? null : response.json();
}

/** The address of something the open source has: `/api/sources/<id>/<resource>`. Everything the page asks of a source goes through here. */
function sourceUrl(resource) {
  return `/api/sources/${encodeURIComponent(state.source.id)}/${resource}`;
}

/** The address of a source of the list, whichever it is: what a request to remove it goes to. */
function sourceItemUrl(id) {
  return `/api/sources/${encodeURIComponent(id)}`;
}

/** The page's load error, and when it happened (the error page says "last attempt", so a retry that fails again is seen to have run). */
function setError(error) {
  state.error = error;
  state.errorAt = new Date();
}

/**
 * Asks the server which sources it has, and whether this browser may add and remove them. The open source's entry is renewed (its name, its
 * count), and the switch in the top bar follows.
 */
async function loadSources() {
  const data = await getJson('/api/sources');
  state.sources = (Array.isArray(data?.sources) ? data.sources : []).filter((source) => typeof source?.id === 'string' && source.id !== '');
  state.sourcesFile = typeof data?.sourcesFile === 'string' && data.sourcesFile !== '' ? data.sourcesFile : null;
  state.sourcesAccess = sourcesAccess(data);
  state.host = sourcesHost(data);
  state.appVersion = appVersion(data);
  state.listed = true;
  const asking = state.sourceEdit.asking;
  if (asking !== null && !state.sources.some((source) => source.id === asking)) state.sourceEdit.asking = null; // the card that asked is gone
  if (state.source !== null) {
    state.source = state.sources.find((source) => source.id === state.source.id) ?? state.source;
    setVariant(isNotes() ? 'note' : '');
  }
  updateSourceSwitch();
}

/** Returns false when the reader went to another source while the tree was on its way: that tree is not the page's. */
async function loadTree() {
  const id = state.source.id;
  const tree = await getJson(sourceUrl('tree'));
  if (state.source === null || state.source.id !== id) return false;
  state.tree = tree;
  state.folders = folderPaths(tree.nodes);
  state.labels = isNotes() ? new Map() : projectLabels(projectFolders(tree.nodes), homeSlug(tree.root));
  state.version = tree.version;
  renderTree();
  if (!refs.palette.hidden) renderPalette({ keepActive: true }); // a file that came or went while it is open
  return true;
}

async function showOverview({ keepScroll = false, target = null, live = false } = {}) {
  const seq = ++navSeq;
  const previous = state.overview;
  try {
    const data = await getJson(sourceUrl('overview'));
    if (seq !== navSeq) return;
    state.overview = data;
    state.error = null;
  } catch (error) {
    if (seq !== navSeq) return;
    // A refresh that fails leaves the page that is there (the strip under the top bar says it may be old).
    if (keepScroll && state.overview !== null && state.error === null) return;
    setError(error);
  }
  state.file = null;
  state.notFound = null;
  const arrival = keepScroll ? null : (target ?? arrivalTarget(state.route, null));
  const active = document.activeElement;
  renderContent({ keepScroll, target: arrival });
  renderInfo();
  updateChrome({ reveal: !keepScroll });
  if (!keepScroll) {
    restoreTree(arrival);
    settleFocus(active);
  }
  if (live && previous && state.overview && !state.error) showOverviewDiff(previous, state.overview);
  settlePosition();
}

/**
 * `live`: this refresh is the answer to a change of the open file itself. It is then "updated" (the time in its load
 * line, and in words for a screen reader), the blocks that are new are marked, and a pill points at them when they are
 * out of sight (see `showFileChange`).
 */
async function showFile(route, { keepScroll = false, target = null, live = false } = {}) {
  const seq = ++navSeq;
  let unchanged = false;
  try {
    const data = await getJson(sourceUrl(`file?path=${encodeURIComponent(route.path)}`));
    if (seq !== navSeq) return;
    unchanged = keepScroll && state.file !== null && sameFileData(state.file, data);
    state.file = data;
    state.notFound = null;
    state.error = null;
  } catch (error) {
    if (seq !== navSeq) return;
    const gone = error instanceof ApiError && error.status === 404;
    // A refresh that fails leaves the page that is there (the strip under the top bar says it may be old); a file that is gone is news.
    if (keepScroll && !gone && state.file !== null && state.file.path === route.path && state.error === null) return;
    state.file = null;
    if (gone) {
      state.notFound = route.path;
      state.error = null;
    } else {
      state.notFound = null;
      setError(error);
    }
  }
  if (state.file !== null) rememberRecent(route.path);
  if (state.stamp !== null && state.stamp.path !== route.path) state.stamp = null;
  // A live refresh leaves the reader where they are: it neither scrolls nor marks a line again.
  const arrival = keepScroll ? null : (target ?? arrivalTarget(route, null));
  if (unchanged) {
    // Only the index version moved: keep the rendered page (and the reader's place) as it is.
    renderInfo();
  } else {
    const active = document.activeElement;
    const before = live && state.file !== null ? collectBlocks(refs.content) : null;
    if (before !== null) state.stamp = { path: route.path, at: new Date() };
    renderContent({ keepScroll, target: arrival });
    renderInfo();
    if (!keepScroll) settleFocus(active);
    if (before !== null) {
      showFileChange(before);
      announce(t('announce.updated', { name: baseName(route.path), time: clockText(state.stamp.at) }));
    }
  }
  updateChrome({ reveal: !keepScroll });
  if (!keepScroll) {
    restoreTree(arrival);
    restoreInfo(arrival);
  }
  settlePosition();
}

/**
 * A page that was navigated to replaces the element the reader had focus on (a link of the previous
 * page, of the info panel): the focus would fall back to the start of the document, and the next Tab
 * with it. It goes to the content instead, where the reader is going.
 */
function settleFocus(previous) {
  if (previous && previous !== document.body && document.activeElement === document.body) {
    refs.content.focus({ preventScroll: true });
  }
}

function loadRoute(options) {
  if (state.route.view === 'sources') return showSources(options);
  return state.route.view === 'file' ? showFile(state.route, options) : showOverview(options);
}

/**
 * The sources page: what the server lists, asked again every time it is opened (the list may have been changed from outside: its file edited,
 * a source added in another tab), and nothing of any source's pages. A refresh that fails leaves the list that is there. The error that was
 * on screen was a source's, and this page is the way out of it. A card that asks "remove it?" and a refusal on screen do not outlive the visit.
 */
async function showSources({ keepScroll = false, target = null } = {}) {
  const seq = ++navSeq;
  try {
    await loadSources();
    if (seq !== navSeq) return;
    state.error = null;
  } catch (error) {
    if (seq !== navSeq) return;
    if (keepScroll && state.error === null) return;
    if (state.sources.length === 0) setError(error);
    else state.error = null;
  }
  if (!keepScroll) {
    state.sourceEdit.asking = null;
    state.sourceEdit.addError = null;
    state.sourceEdit.removeError = null;
  }
  const arrival = keepScroll ? null : (target ?? arrivalTarget(state.route, null));
  const active = document.activeElement;
  renderContent({ keepScroll, target: arrival });
  renderInfo();
  updateChrome();
  if (!keepScroll) {
    restoreTree(arrival);
    settleFocus(active);
  }
  settlePosition();
}

/**
 * Loads the sources and the page of the address the way the page is loaded when it opens: the reader arrives at the place the route
 * (or the history entry) names. At boot, and when a page that is an error is tried again.
 */
async function loadFresh() {
  window.clearTimeout(retryTimer);
  state.error = null;
  try {
    await loadSources();
  } catch (error) {
    setError(error);
    state.listed = true; // the server did not answer: there is still no source to show a tree for
    updateSourceSwitch();
  }
  if (state.error) {
    renderContent();
    renderInfo();
    settlePosition();
    if (state.source === null) scheduleRetry();
    return;
  }
  const route = resolveAddress();
  state.route = route;
  updateLayoutMode();
  const target = arrivalTarget(route, savedPosition(history.state));
  await openRoute(route, { target });
}

/**
 * With no source open there is no live connection that would bring the page back when the server is back: the page asks again now and
 * then, quietly (it is drawn again only when the server answers).
 */
function scheduleRetry() {
  window.clearTimeout(retryTimer);
  retryTimer = window.setTimeout(async () => {
    try {
      await loadSources();
    } catch {
      scheduleRetry();
      return;
    }
    await loadFresh();
  }, RECONNECT_MS);
}

/**
 * Shows the route `resolveAddress` made of the address: the sources page, or a page of a source (opened first when another was open,
 * or when the one that is open never got its tree). The live connection is made once the page is there.
 */
async function openRoute(route, options) {
  if (route.view === 'sources') {
    await showSources(options);
    return;
  }
  const entry = state.sources.find((source) => source.id === route.source);
  if (state.source === null || state.source.id !== entry.id) openSource(entry);
  state.error = null;
  try {
    if (!(await loadTree())) return;
  } catch (error) {
    setError(error);
  }
  if (state.error) {
    renderContent();
    renderInfo();
    updateChrome();
    settlePosition();
  } else {
    await loadRoute(options);
  }
  ensureEvents();
}

/**
 * What is done when the live connection comes back: the page is read again where the reader is. A page that was an error is
 * tried again as a new page; a page that is there stays when the refresh fails (the strip says it may be old). A source that
 * the server no longer lists (it was started again with other folders) is gone: the address names a source that is not there.
 */
async function refreshAll() {
  if (state.error) {
    await loadFresh();
    return;
  }
  try {
    await loadSources();
  } catch {
    // The list that is there stays.
  }
  if (state.source !== null && !state.sources.some((source) => source.id === state.source.id)) {
    closeSource();
    await onRoute({ fresh: true });
    return;
  }
  if (state.source !== null) {
    try {
      if (!(await loadTree())) return;
    } catch {
      return;
    }
  }
  await loadRoute({ keepScroll: true });
}

// ---------------------------------------------------------------------------------------------
// Sources
// ---------------------------------------------------------------------------------------------

/**
 * Opens a source: what the page knew of the one before is dropped, what it remembers of this one is read back (see
 * `loadSourceState`). Its tree, its page and its live connection follow (see `openRoute`).
 */
function openSource(entry) {
  stopEvents();
  state.source = entry;
  setVariant(isNotes() ? 'note' : '');
  loadSourceState(entry.id);
  resetSourceView();
  if (entry.available !== false) rememberSource(entry.id);
  updateSourceSwitch();
}

/** No source is open: the sources page is all there is (the source that was open is no longer listed). */
function closeSource() {
  stopEvents();
  state.source = null;
  setVariant('');
  resetSourceView();
  updateSourceSwitch();
}

/** What belongs to the open source starts again: its tree and pages, what changed in it, the marks of that. */
function resetSourceView() {
  navSeq += 1; // an answer that is still on its way is the other source's
  state.tree = null;
  state.folders = new Set();
  state.labels = new Map();
  state.overview = null;
  state.file = null;
  state.notFound = null;
  state.error = null;
  state.version = null;
  state.lastChange = null;
  state.fresh = new Map();
  state.stamp = null;
  paletteIndex = { tree: null, entries: [] };
  infoPath = null;
  window.clearTimeout(freshTimer);
  clearBlockMarks();
  hidePill();
  renderTree();
  refs.sidebar.scrollTop = 0; // the other source's tree was scrolled somewhere that this one may not reach
  renderLastChange();
}

/** The live connection belongs to a source: it ends with it, and so does what it said about the connection. */
function stopEvents() {
  window.clearTimeout(reconnectTimer);
  if (eventSource) eventSource.close();
  eventSource = null;
  eventsFor = null;
  hadConnectionError = false;
  state.offline = false;
  updateStale();
  refs.live.hidden = true;
}

/** The connection is made once there is a page to keep up to date, and only once for a source. */
function ensureEvents() {
  if (state.source !== null && eventsFor !== state.source.id) connectEvents();
}

/**
 * True while the folder of the open source cannot be read: the list says so, or what the page asked of it was answered 503. There is nothing to
 * keep up to date then, and the server would only answer the live connection with 503 too.
 */
function sourceUnavailable() {
  if (state.source === null) return false;
  if (state.source.available === false) return true;
  return state.error !== null && loadFailure(state.error).kind === 'unavailable';
}

/**
 * The switch in the top bar names the open source (its whole path is its tooltip) and opens the menu of sources (see `openSourceMenu`); the
 * brand and the overview link lead to the overview of the open source. With no source open the switch asks for one and there is no overview.
 * The tooltip of the brand is the version of pusula, where the server says it (an older one does not: then the brand has no tooltip).
 */
function updateSourceSwitch() {
  const source = state.source;
  refs.sourceName.textContent = source === null ? t('source.pick') : source.name;
  refs.sourceSwitch.setAttribute('aria-label', source === null ? t('source.pick') : t('source.switch', { name: source.name }));
  refs.sourceSwitch.title = source === null ? t('source.pick') : source.path;
  const home = overviewHash(sid());
  refs.brand.href = home;
  if (state.appVersion === null) refs.brand.removeAttribute('title');
  else refs.brand.title = t('about.version', { version: state.appVersion });
  refs.overviewLink.href = home;
  refs.overviewLink.hidden = source === null;
  syncNoSource();
  if (sourceMenuOpen) renderSourceMenu();
}

/**
 * No tree to show and none coming (`body.no-source`): no source is open, or the folder of the one that is cannot be read. Its column, its drawer and
 * the panel are left out then and the page has the whole width; but only once the server has been asked, so that the column does not come and go
 * while the list is on its way.
 */
function syncNoSource() {
  document.body.classList.toggle('no-source', state.listed && (state.source === null || sourceUnavailable()));
}

// ---- The menu of sources -----------------------------------------------------------------------

// What the switch opens: a menu (WAI-ARIA menu button) of the sources the server lists, each with how it is read and whether its folder can be
// read, the open one marked; under them "manage sources" (the sources page) and, where the top bar has no room for the language (a phone),
// the language. The arrow keys move through it, Enter chooses, Escape and a click elsewhere close it, and the focus goes back to the switch
// (or, where a source was chosen, to the page the reader is going to).
let sourceMenuOpen = false;

/** The rows a key can reach, in order: the sources, "manage sources", and the language where it is shown. */
function sourceMenuItems() {
  return [...refs.sourceMenu.querySelectorAll('[role="menuitem"], [role="menuitemradio"]')].filter((item) => item.getClientRects().length > 0);
}

/** One source of the list: its dot (a folder that can be read is a filled one, one that cannot a ring), its name, how it is read and, if need be, that it cannot be read. A link to its overview. */
function sourceMenuRow(source) {
  const readable = source.available !== false;
  const row = document.createElement('li');
  row.setAttribute('role', 'none');
  const link = el('a', 'menu-item');
  link.setAttribute('role', 'menuitem');
  link.tabIndex = -1;
  link.href = overviewHash(source.id);
  link.dataset.source = source.id;
  if (sid() === source.id) link.setAttribute('aria-current', 'true');
  const dot = el('span', readable ? 'source-dot is-ok' : 'source-dot is-off');
  dot.setAttribute('aria-hidden', 'true');
  dot.title = readable ? t('source.status.ok') : t('source.status.off');
  const meta = el('span', 'menu-meta', t(`source.profile.${profileName(source.profile)}`));
  if (!readable) meta.append(` \u00b7 ${t('source.status.off')}`);
  link.append(dot, el('span', 'menu-name', source.name), meta);
  link.title = source.path;
  row.append(link);
  return row;
}

/** The sources of the list drawn again (and the focus put back on the row it was on); "manage sources" is the current page on the sources page. */
function renderSourceMenu() {
  const active = document.activeElement;
  const focused = active instanceof HTMLElement && refs.sourceMenu.contains(active) ? active.dataset.source ?? null : null;
  let rows;
  if (state.sources.length === 0) {
    const empty = document.createElement('li');
    empty.setAttribute('role', 'none');
    const note = el('span', 'menu-item menu-empty', t('sources.none'));
    note.setAttribute('role', 'menuitem');
    note.setAttribute('aria-disabled', 'true');
    note.tabIndex = -1;
    empty.append(note);
    rows = [empty];
  } else {
    rows = state.sources.map(sourceMenuRow);
  }
  refs.sourceMenuList.replaceChildren(...rows);
  if (focused !== null) [...refs.sourceMenuList.querySelectorAll('a')].find((link) => link.dataset.source === focused)?.focus({ preventScroll: true });
  if (state.route.view === 'sources') refs.sourceManage.setAttribute('aria-current', 'page');
  else refs.sourceManage.removeAttribute('aria-current');
}

/** `last`: the key that opened it was the up arrow, which starts at the end. The row of the open source (else the first) takes the focus. */
function openSourceMenu({ last = false } = {}) {
  if (sourceMenuOpen) return;
  sourceMenuOpen = true;
  renderSourceMenu();
  refs.sourceMenu.hidden = false;
  refs.sourceSwitch.setAttribute('aria-expanded', 'true');
  const items = sourceMenuItems();
  const current = items.find((item) => item.getAttribute('aria-current') === 'true');
  (last ? items[items.length - 1] : current ?? items[0])?.focus({ preventScroll: true });
}

/**
 * `focus`: `'switch'` (Escape, the switch itself: the button that opened it gets the focus back), `'content'` (a source or the page of sources was
 * chosen: the reader is going to a page, and the focus goes with them) or `'none'` (the reader pointed elsewhere, or went Back: where they are
 * is theirs; only a focus that was in the menu, which is gone now, goes to the switch).
 */
function closeSourceMenu({ focus = 'switch' } = {}) {
  if (!sourceMenuOpen) return;
  sourceMenuOpen = false;
  const inside = refs.sourceMenu.contains(document.activeElement);
  refs.sourceMenu.hidden = true;
  refs.sourceSwitch.setAttribute('aria-expanded', 'false');
  if (focus === 'switch' || (focus === 'none' && inside)) refs.sourceSwitch.focus({ preventScroll: true });
  else if (focus === 'content') refs.content.focus({ preventScroll: true });
}

function onSourceMenuKey(event) {
  if (event.isComposing) return;
  const items = sourceMenuItems();
  const index = items.indexOf(document.activeElement);
  const go = (target) => {
    event.preventDefault();
    target?.focus({ preventScroll: true });
  };
  if (event.key === 'ArrowDown') go(items[(index + 1) % items.length]);
  else if (event.key === 'ArrowUp') go(items[index < 0 ? items.length - 1 : (index - 1 + items.length) % items.length]);
  else if (event.key === 'Home') go(items[0]);
  else if (event.key === 'End') go(items[items.length - 1]);
  else if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation(); // the drawers' own Escape is for when the menu is not open
    closeSourceMenu();
  } else if (event.key === 'Tab') {
    closeSourceMenu(); // the focus goes to the switch, and the key moves on from there
  } else if (event.key === ' ' && document.activeElement instanceof HTMLAnchorElement) {
    go(null);
    document.activeElement.click(); // a link is chosen with Enter; a menu item is chosen with Space too
  }
}

// ---------------------------------------------------------------------------------------------
// Routing and page chrome
// ---------------------------------------------------------------------------------------------

/**
 * The route of the address, read against the sources the server lists (see `resolveRoute`). An address that names no source, or an old one
 * (`#/`, `#/f/<path>`), is written over by the page it stands for in the source the page opens: a bookmark made before there were sources
 * keeps working, and Back does not return to the address that was replaced.
 */
function resolveAddress() {
  const resolved = resolveRoute(parseRoute(window.location.hash), state.sources, readLastSource());
  if (resolved.redirect !== null) replaceAddress(resolved.redirect);
  state.missing = resolved.missing;
  return resolved.route;
}

/** Writes an address over the current history entry: no new entry, and no `hashchange`. The place the entry carries is kept. */
function replaceAddress(hash) {
  try {
    history.replaceState(history.state, '', hash);
  } catch {
    // A browser may refuse it; the address then stays as it was and the page shows the route all the same.
  }
}

/**
 * `fresh`: the address was asked for (a link to the address the page already has), so it is followed even
 * though its history entry has a place of its own. Otherwise an entry that has one (Back, Forward) is shown
 * where the reader left it, and an entry that has none starts at what its address names.
 */
async function onRoute({ fresh = false } = {}) {
  const previous = state.route;
  const route = resolveAddress();
  state.route = route;
  untrackPosition();
  updateLayoutMode();
  setOverlay(null);
  closeSourceMenu({ focus: 'none' }); // Back or Forward while the menu of sources is open
  if (!refs.palette.hidden) closePalette({ focus: 'content' }); // Back or Forward while the quick opener is open
  if (!refs.browse.hidden) closeBrowse({ focus: 'content' }); // Back or Forward while the folder picker is open
  const target = arrivalTarget(route, fresh ? null : savedPosition(history.state));

  // The sources page, or a page of a source that is not the open one (or whose tree never arrived): it is shown the way a page opens.
  if (route.view === 'sources' || state.source === null || state.source.id !== route.source || state.tree === null) {
    await openRoute(route, { target });
    return;
  }

  if (route.view === 'overview' && previous.view === 'overview' && previous.source === route.source && state.overview && !state.error) {
    // Same page, another place on it (an issue group, the memory table): no reload, just move. Another tag is another list: it is drawn again.
    updateChrome();
    if ((previous.tag ?? null) !== (route.tag ?? null)) renderContent({ target });
    else moveTo(target);
    restoreTree(target);
    settlePosition();
    return;
  }

  const sameFile = route.view === 'file' && previous.view === 'file' && previous.source === route.source
    && previous.path === route.path && state.file !== null && state.file.path === route.path;
  if (sameFile) {
    // Only the heading or the line changed: no reload, just move.
    updateChrome();
    moveTo(target);
    restoreTree(target);
    restoreInfo(target);
    settlePosition();
    return;
  }
  await loadRoute({ target });
}

/** `reveal`: the page was navigated to, so open the folders above the file and scroll to it. */
function updateChrome({ reveal = false } = {}) {
  const view = state.route.view;
  if (view === 'overview') refs.overviewLink.setAttribute('aria-current', 'page');
  else refs.overviewLink.removeAttribute('aria-current');
  if (view === 'sources') refs.sourceManage.setAttribute('aria-current', 'page');
  else refs.sourceManage.removeAttribute('aria-current');
  document.title = pageTitle();
  updateTreeSelection(reveal);
}

/** What the tab is called: the note, the source of notes, the sources page; "pusula" for the overview of a Claude configuration. */
function pageTitle() {
  const route = state.route;
  if (route.view === 'sources') return `${t('sources.title')} - pusula`;
  if (route.view === 'file') return `${shownName(baseName(route.path))} - pusula`;
  return isNotes() ? `${state.source.name} - pusula` : 'pusula';
}

/**
 * The overview has nothing for the info panel, so its column is hidden there, and its legend is in the page, so
 * the tree's is hidden too (it is back, with the height the keyboard focus must keep clear of, in a file view).
 */
function updateLayoutMode() {
  document.body.classList.toggle('overview-view', state.route.view !== 'file');
  syncFooterSpace();
}

function setOverlay(name) {
  overlay = name;
  document.body.classList.toggle('tree-open', name === 'tree');
  document.body.classList.toggle('info-open', name === 'info');
  refs.treeToggle.setAttribute('aria-expanded', String(name === 'tree'));
  refs.infoToggle.setAttribute('aria-expanded', String(name === 'info'));
  refs.scrim.hidden = name === null;
}

function setLive(status) {
  state.live = status;
  refs.live.className = `live live-${status}`;
  refs.liveText.textContent = t(`live.${status}`);
  refs.live.title = t('live.title', { state: t(`live.${status}`) });
}

/**
 * The strip under the top bar: the live connection is lost, so what is on screen may be out of date. It says since when the
 * content is what the server had, and the page that was there stays as it is. It is for a page that is there: an error page says what it has to.
 */
function updateStale() {
  const show = state.offline && state.tree !== null && !state.error;
  refs.stale.hidden = !show;
  if (show && state.contentAt) refs.staleText.textContent = t('stale.text', { time: clockText(state.contentAt) });
}

/** Call it before `setLive('offline')`: it asks whether the connection was live until now. */
function markOffline() {
  if (state.offline) return;
  state.offline = true;
  if (state.live === 'live') state.contentAt = new Date(); // the page was told what the folder has until this moment
  updateStale();
  if (!refs.stale.hidden) announce(refs.staleText.textContent);
}

function markOnline() {
  state.offline = false;
  updateStale();
}

/** `Cmd+K` on an Apple device, `Ctrl+K` anywhere else: what the tooltip of the search button names. Ctrl+O and `/` work too. */
function quickShortcutLabel() {
  return isApplePlatform(navigator.userAgentData?.platform ?? navigator.platform) ? 'Cmd+K' : 'Ctrl+K';
}

function applyStaticI18n() {
  document.documentElement.lang = getLang();
  for (const node of document.querySelectorAll('[data-i18n]')) {
    node.textContent = t(node.dataset.i18n);
  }
  for (const node of document.querySelectorAll('[data-i18n-title]')) {
    node.title = t(node.dataset.i18nTitle);
  }
  for (const node of document.querySelectorAll('[data-i18n-aria-label]')) {
    node.setAttribute('aria-label', t(node.dataset.i18nAriaLabel));
  }
  for (const button of refs.langButtons) {
    // The two in the top bar are toggle buttons; the two in the menu of sources (a phone's top bar has no room for them) are items of it.
    button.setAttribute(button.getAttribute('role') === 'menuitemradio' ? 'aria-checked' : 'aria-pressed', String(button.dataset.lang === getLang()));
    button.title = t(`lang.${button.dataset.lang}`);
  }
  const openLabel = t('quick.open', { key: quickShortcutLabel() });
  refs.quickOpen.title = openLabel;
  refs.quickOpen.setAttribute('aria-label', openLabel);
  refs.paletteInput.placeholder = t('quick.placeholder');
  setLive(state.live);
  updateStale();
  updateSourceSwitch();
}

function changeLanguage(lang) {
  if (lang === getLang()) return;
  setLang(lang);
  applyStaticI18n();
  renderTree();
  renderContent({ keepScroll: true });
  renderInfo();
  renderLastChange();
  updateChrome();
  if (!refs.palette.hidden) renderPalette({ keepActive: true });
  if (!refs.browse.hidden) renderBrowse();
}

function showToast(text) {
  const toast = el('div', 'toast', text);
  refs.toasts.append(toast);
  window.setTimeout(() => toast.remove(), TOAST_MS);
}

// ---------------------------------------------------------------------------------------------
// File tree
// ---------------------------------------------------------------------------------------------

function renderTree() {
  const nav = refs.sidebar;
  if (!state.tree) {
    nav.replaceChildren();
    return;
  }
  const focused = focusedTreeKey();
  if (state.tree.nodes.length === 0) {
    nav.replaceChildren(el('p', 'muted pad', t('tree.empty')));
  } else {
    nav.replaceChildren(treeHead(), buildTreeList(state.tree.nodes), ...(isNotes() ? [] : [treeFooter()]));
  }
  syncFooterSpace();
  updateTreeSelection();
  restoreTreeFocus(focused);
  applyFresh();
}

/**
 * The header strip sticks to the top of the tree and the legend to its bottom, and each covers the rows that scroll under it.
 * Their heights are handed to the CSS (`--head-h` and `--foot-h`, which `scroll-padding` of the tree uses), so the keyboard focus
 * scrolls a row clear of them; the legend's changes when it is opened or closed.
 */
function syncFooterSpace() {
  const foot = refs.sidebar.querySelector('.tree-foot');
  refs.sidebar.style.setProperty('--foot-h', `${foot ? foot.offsetHeight : 0}px`);
  refs.sidebar.style.setProperty('--head-h', `${treeHeadHeight()}px`);
}

function treeHeadHeight() {
  return refs.sidebar.querySelector('.tree-head')?.offsetHeight ?? 0;
}

/**
 * The strip above the tree: its name, and two tools. "Collapse all" closes every folder (the ones the page opened for the file
 * that is open too, which is what makes a tree that has been used a while worth clearing); "show open file" opens the folders above
 * the file that is open and brings its row into view. It is the template of index.html, drawn again with the tree.
 */
function treeHead() {
  const head = refs.treeHeadTemplate.content.firstElementChild.cloneNode(true);
  head.querySelector('.tree-head-title').textContent = t('nav.tree');
  const labels = { collapse: t('tree.collapseAll'), reveal: t('tree.revealOpen') };
  for (const button of head.querySelectorAll('.tree-tool')) {
    const label = labels[button.dataset.action];
    button.title = label;
    button.setAttribute('aria-label', label);
  }
  return head;
}

/** "Show open file" has nothing to show on the overview, or for a file the tree does not have. */
function syncTreeTools() {
  const reveal = refs.sidebar.querySelector('.tree-tool[data-action="reveal"]');
  if (reveal) reveal.disabled = refs.sidebar.querySelector('a.tree-row.current') === null;
}

function collapseAll() {
  state.openDirs.clear();
  for (const item of refs.sidebar.querySelectorAll('li.tree-dir.open')) setDirOpen(item, false);
  saveOpenDirs();
  applyFresh(); // a folder that closes wears the dot for a change inside it
  refs.sidebar.scrollTop = 0;
}

function revealOpenFile() {
  const row = refs.sidebar.querySelector('a.tree-row.current');
  if (!row) return;
  updateTreeSelection(true); // opens the folders above it and brings the row into view
  flash(row);
  row.focus({ preventScroll: true });
}

/**
 * The legend of the load markers, folded, at the bottom of the tree: a touch screen has no tooltip to ask.
 * It also says what the two numbers beside a row are (every session first, then the total).
 */
function treeFooter() {
  const foot = el('div', 'tree-foot');
  const details = el('details', 'legend-pop');
  details.open = state.legendOpen;
  details.addEventListener('toggle', () => {
    state.legendOpen = details.open;
    syncFooterSpace();
    saveUi();
  });
  details.append(el('summary', null, t('legend.title')), legendList(), el('p', 'legend-note muted', t('legend.tokens')));
  foot.append(details);
  return foot;
}

function buildTreeList(nodes) {
  const list = el('ul', 'tree-list');
  for (const node of nodes) list.append(buildTreeItem(node));
  return list;
}

/** What is under a folder row: the folders under `projects/` by the name of their project (the tree shows that name, not the slug the server sorts by), every other folder as the server sent it. Projects are a Claude configuration's: a folder of notes called `projects` is a folder. */
function treeChildren(node, path) {
  const children = node.children ?? [];
  return path === 'projects' && !isNotes() ? orderProjectFolders(children, state.labels) : children;
}

/** The entry of `state.labels` for the folder row of a project (`projects/<slug>`), and none for any other row. */
function projectLabelOf(node, path) {
  return path === `projects/${node.name}` ? state.labels.get(node.name) : undefined;
}

/**
 * The name of a folder row. A project's folder (`projects/<slug>`) is shown by its project's name, not by its
 * slug: the name, and the place beside it only when another project has the same name. A name that does not fit is cut
 * at its end with an ellipsis, as every name in the tree is, the place going first; the whole name, the place and the slug
 * are the row's tooltip (see `buildTreeItem`).
 */
function treeName(node, path) {
  const label = projectLabelOf(node, path);
  if (!label) return el('span', 'tree-name', node.name);
  const name = el('span', 'tree-name tree-project');
  name.append(...projectName(label));
  return name;
}

function buildTreeItem(node) {
  const item = document.createElement('li');

  if (isDirectory(node)) {
    const path = stripSlashes(node.path);
    const open = state.openDirs.has(path);
    item.className = open ? 'tree-dir open' : 'tree-dir';
    item.dataset.path = path;

    const button = el('button', 'tree-row');
    button.type = 'button';
    button.setAttribute('aria-expanded', String(open));
    const label = projectLabelOf(node, path);
    const whole = label ? `${label.name}${label.context ? ` \u00b7 ${label.context}` : ''}\n` : '';
    button.title = `${whole}${path}\n${t('tree.files', { n: node.fileCount })}`;
    const chevron = el('span', 'chevron');
    chevron.setAttribute('aria-hidden', 'true');
    button.append(chevron, treeName(node, path), ...(isNotes() ? [] : [treeTokens(node)]));
    item.append(button, buildTreeList(treeChildren(node, path)));
    return item;
  }

  // A note has no load marker and no tokens (those are a Claude configuration's), and no `.md`.
  item.className = 'tree-file';
  const link = el('a', node.orphan ? 'tree-row orphan' : 'tree-row');
  link.href = fileHash(sid(), node.path);
  link.dataset.path = node.path;
  link.title = `${node.path}${isNotes() ? '' : `\n${layerName(node.layer)} - ${loadName(node.loadMode)}`}`
    + (node.orphan ? `\n${t('tree.orphan')}` : '');
  if (!isNotes()) link.append(loadDot(node.loadMode));
  link.append(el('span', 'tree-name', shownName(node.name)));
  if (node.brokenLinks > 0) {
    const text = t('tree.broken', { n: node.brokenLinks });
    const badge = el('span', 'badge badge-broken');
    badge.title = text;
    const count = el('span', null, String(node.brokenLinks));
    count.setAttribute('aria-hidden', 'true');
    badge.append(el('span', 'sr-only', text), count);
    link.append(badge);
  }
  if (!isNotes()) link.append(treeTokens(node));
  item.append(link);
  return item;
}

/**
 * Highlights the open file. With `reveal` it also opens the folders above it and scrolls it into
 * view; that happens on navigation only, so a live refresh never undoes the reader's own choices.
 */
function updateTreeSelection(reveal = false) {
  const path = state.route.view === 'file' ? state.route.path : null;
  let current = null;
  for (const link of refs.sidebar.querySelectorAll('a.tree-row')) {
    const isCurrent = link.dataset.path === path;
    link.classList.toggle('current', isCurrent);
    if (isCurrent) {
      link.setAttribute('aria-current', 'page');
      current = link;
    } else {
      link.removeAttribute('aria-current');
    }
  }
  syncTreeTools();
  if (path === null || !reveal) return;

  let changed = false;
  for (const dir of ancestorDirs(path)) {
    if (!state.openDirs.has(dir)) {
      state.openDirs.add(dir);
      changed = true;
    }
    const item = findDirItem(dir);
    if (item) setDirOpen(item, true);
  }
  if (changed) {
    saveOpenDirs();
    applyFresh(); // a dot on a folder that was just opened moves to the next closed one
  }
  // The strip at the top of the tree and the legend at the bottom cover the rows under them: the open file is kept clear of both.
  const covered = refs.sidebar.querySelector('.tree-foot')?.offsetHeight ?? 0;
  if (current) scrollWithin(refs.sidebar, current, { block: 'nearest', margin: 4 + treeHeadHeight(), bottomMargin: 4 + covered });
}

function findDirItem(path) {
  for (const item of refs.sidebar.querySelectorAll('li.tree-dir')) {
    if (item.dataset.path === path) return item;
  }
  return null;
}

function setDirOpen(item, open) {
  item.classList.toggle('open', open);
  item.firstElementChild.setAttribute('aria-expanded', String(open));
  if (open) item.firstElementChild.classList.remove('has-fresh'); // what the dot pointed at is on screen now
}

function toggleDir(item) {
  const path = item.dataset.path;
  const open = !state.openDirs.has(path);
  if (open) state.openDirs.add(path);
  else state.openDirs.delete(path);
  setDirOpen(item, open);
  saveOpenDirs();
  if (open) remarkUnder(path);
  else applyFresh();
}

function focusedTreeKey() {
  const active = document.activeElement;
  if (!active || !refs.sidebar.contains(active)) return null;
  if (active.matches('a.tree-row')) return { dir: false, path: active.dataset.path };
  if (active.matches('button.tree-row')) return { dir: true, path: active.parentElement.dataset.path };
  if (active.matches('button.tree-tool')) return { tool: active.dataset.action };
  if (active.matches('.legend-pop > summary')) return { legend: true };
  return null;
}

function restoreTreeFocus(key) {
  if (!key) return;
  let target;
  if (key.legend) target = refs.sidebar.querySelector('.legend-pop > summary');
  else if (key.tool) target = refs.sidebar.querySelector(`.tree-tool[data-action="${key.tool}"]:not(:disabled)`);
  else if (key.dir) target = findDirItem(key.path)?.firstElementChild;
  else target = [...refs.sidebar.querySelectorAll('a.tree-row')].find((link) => link.dataset.path === key.path);
  if (target) target.focus({ preventScroll: true });
}

// ---------------------------------------------------------------------------------------------
// Content: shared pieces
// ---------------------------------------------------------------------------------------------

function renderContent({ keepScroll = false, target = null } = {}) {
  const scrollTop = refs.content.scrollTop;
  const kept = keepScroll ? keptFocus() : null;
  hidePill(); // the blocks it points at are about to be replaced
  window.clearTimeout(blockTimer);
  let page = null;
  if (state.error) page = errorPage(state.error);
  else if (state.route.view === 'sources') page = sourcesPage();
  else if (state.route.view === 'file') {
    if (state.file) page = filePage(state.file);
    else if (state.notFound !== null) page = notFoundPage(state.notFound);
  } else if (state.overview) {
    page = overviewPage(state.overview);
  }
  refs.content.replaceChildren(...(page ? [page] : []));
  refs.content.scrollTop = keepScroll ? scrollTop : 0;
  updateStale();
  syncNoSource(); // a page that is an error may be the one that says the folder cannot be read
  scheduleFit();
  if (target) moveTo(target);
  if (kept) focusKept(kept.key, kept);
}

/**
 * A page that is drawn again where the reader is (a live refresh, the sources page after a request) would take the focus with the
 * element that had it, and the reader would lose their place: a field half typed in, a button the keyboard was on. What the sources page
 * has that can keep the focus is marked `data-keep` (a key that is the same in the page that replaces it); this is the one that has it,
 * and where the caret was.
 */
function keptFocus() {
  const node = document.activeElement;
  if (!(node instanceof HTMLElement) || !refs.content.contains(node) || !node.dataset.keep) return null;
  return { key: node.dataset.keep, start: node.selectionStart ?? null, end: node.selectionEnd ?? null };
}

/** The page that was drawn again has the element with that key: it takes the focus back, with the caret where it was. Not one that cannot take it. */
function focusKept(key, caret = null) {
  const node = [...refs.content.querySelectorAll('[data-keep]')].find((candidate) => candidate.dataset.keep === key);
  if (!node || node.disabled) return;
  node.focus({ preventScroll: true });
  if (caret !== null && caret.start !== null && caret.end !== null) {
    try {
      node.setSelectionRange(caret.start, caret.end);
    } catch {
      // Not a text field: there is no caret to put back.
    }
  }
}

/**
 * Takes `#content` to where an arrival (`arrivalTarget`) puts the reader: the place they left (the
 * entry's own, so nothing is marked again), a line, a heading, the group the overview's address names, or
 * the top.
 */
function moveTo(target) {
  switch (target.kind) {
    case 'restore':
      clearFlash(); // the mark of the place the reader has just left
      refs.content.scrollTop = target.scroll;
      break;
    case 'line':
      scrollToLine(target.line);
      break;
    case 'heading':
      scrollToHeading(target.heading);
      break;
    case 'overview':
      if (state.overview && !state.error && state.route.view !== 'file') applyOverviewRoute(state.route);
      break;
    default:
      refs.content.scrollTop = 0;
  }
}

/** The tree comes back to where the reader had scrolled it, after the folders above the open file were opened. */
function restoreTree(target) {
  if (target?.kind === 'restore') refs.sidebar.scrollTop = target.treeScroll;
}

/** The info panel too: an entry that comes back (Back, Forward, a reload) has the panel where it was; any other arrival starts it at its top (see `renderInfo`). */
function restoreInfo(target) {
  if (target?.kind === 'restore') refs.info.scrollTop = target.infoScroll;
}

// ---------------------------------------------------------------------------------------------
// The reading place of a history entry
// ---------------------------------------------------------------------------------------------

// #content (and the tree and the info panel) scroll, not the document, so the browser has no place to restore on Back or on a
// reload. The place goes into the entry's `state` instead (`history.replaceState`), a moment after the reader
// stops scrolling and always before a link is followed, and comes back with the entry (see `onRoute`, `boot`).
// `positionTracked` is false while a navigation is on its way: the page on screen is then not the page of the
// entry, and what it would write belongs to the entry that is leaving.
let positionTracked = false;
let positionTimer = 0;
let positionSince = 0;
let positionWritten = '';

function untrackPosition() {
  positionTracked = false;
  window.clearTimeout(positionTimer);
  positionTimer = 0;
  positionSince = 0;
}

/** The page on screen is the page of the current entry: write its place now, and keep writing as it scrolls. */
function settlePosition() {
  positionTracked = true;
  positionWritten = '';
  savePosition();
}

function savePosition() {
  window.clearTimeout(positionTimer);
  positionTimer = 0;
  positionSince = 0;
  if (!positionTracked) return;
  // The browser may be on another entry already (Back, Forward) while this page is still the one on screen, or only now finishes
  // loading: what is written then is written over that entry's own place. A page writes its place only at its own address.
  if (!pageIsAtAddress(state.route, window.location.hash)) return;
  const next = withPosition(history.state, refs.content.scrollTop, refs.sidebar.scrollTop, refs.info.scrollTop);
  const key = `${next.scroll}/${next.treeScroll}/${next.infoScroll}`;
  if (key === positionWritten) return;
  try {
    history.replaceState(next, '');
    positionWritten = key;
  } catch {
    // A browser limits how often an entry may be rewritten (or refuses it); the place is then not kept this time.
  }
}

/** Waits for the scroll to settle, but never longer than POSITION_MAX_MS, so a long scroll is written on the way. */
function schedulePositionSave() {
  if (!positionTracked) return;
  const now = performance.now();
  if (positionSince === 0) positionSince = now;
  window.clearTimeout(positionTimer);
  positionTimer = window.setTimeout(savePosition, Math.min(POSITION_SETTLE_MS, Math.max(0, positionSince + POSITION_MAX_MS - now)));
}

/**
 * A page that could not be loaded, in words: what failed (`loadFailure`: no answer, an error of the server, a refused request or
 * a reply that could not be read) and what to do, never the browser's own message ("Failed to fetch"). When the connection comes
 * back the page is loaded again by itself (see `onReady`); the button tries now, and the time of the last attempt shows that it did.
 */
function errorPage(error) {
  const failure = loadFailure(error);
  const page = el('div', 'page');
  const box = el('div', 'notice notice-error');
  box.setAttribute('role', 'alert');
  box.append(el('p', null, failureWords(failure)));
  if (state.errorAt) box.append(el('p', 'muted error-attempt', t('error.attempt', { time: clockText(state.errorAt) })));
  const retry = el('button', 'button', t('error.retry'));
  retry.type = 'button';
  retry.addEventListener('click', () => {
    refreshAll();
  });
  box.append(retry);
  // A source whose folder cannot be read: the way out is another source.
  if (failure.kind === 'unavailable') {
    const sources = el('a', 'error-link', t('error.toSources'));
    sources.href = SOURCES_HASH;
    box.append(' ', sources);
  }
  page.append(box);
  return page;
}

/**
 * What a page that could not be loaded says: the words of its kind of failure. A folder that cannot be read says why, by the code the server gave
 * (or, if the 503 had none, the one the list has for the source), with the folder in it and what to do about it; the server's own sentence is
 * English and is never shown. A code the page has no words for is the plain "cannot be read".
 */
function failureWords(failure) {
  if (failure.kind !== 'unavailable') return t(`error.${failure.kind}`, { status: failure.status });
  const code = failure.code !== '' ? failure.code : sourceErrorCode(state.source?.errorCode);
  return code === '' ? t('error.unavailable') : t(`source.why.${code}`, { path: state.source?.path ?? '' });
}

function notFoundPage(path) {
  const page = el('div', 'page');
  const box = el('div', 'notice notice-warn');
  box.append(el('p', null, t('file.notFound', { path })));
  const back = el('a', null, t('file.backToOverview'));
  back.href = overviewHash(sid());
  box.append(back);
  page.append(box);
  return page;
}

/**
 * Scrolls `container` (never the page) so that `node` is in view. This is the only way the page
 * scrolls to something: `scrollIntoView` moves every scrollable ancestor, the document included,
 * and a scrolled document takes the top bar out of sight.
 */
function scrollWithin(container, node, { block = 'nearest', margin = 0, bottomMargin = margin } = {}) {
  const box = container.getBoundingClientRect();
  const rect = node.getBoundingClientRect();
  const top = scrollTargetTop({
    scrollTop: container.scrollTop,
    viewHeight: container.clientHeight,
    itemTop: rect.top - box.top - container.clientTop + container.scrollTop,
    itemHeight: rect.height,
    block,
    margin,
    bottomMargin,
  });
  if (top !== container.scrollTop) container.scrollTop = top;
}

function scrollToHeading(heading) {
  const id = slugify(heading);
  if (!id) return;
  for (const node of refs.content.querySelectorAll('h1, h2, h3, h4, h5, h6')) {
    if (node.id === id) {
      scrollWithin(refs.content, node, { block: 'start', margin: 12 });
      return;
    }
  }
}

let flashTimer = 0;

function clearFlash() {
  window.clearTimeout(flashTimer);
  for (const marked of document.querySelectorAll('.hit-flash')) marked.classList.remove('hit-flash');
}

/** Marks a place the page just moved to (see `.hit-flash`); there is one at a time. */
function flash(node) {
  clearFlash();
  node.getBoundingClientRect(); // lets the animation start over when the same place is marked again
  node.classList.add('hit-flash');
  flashTimer = window.setTimeout(() => node.classList.remove('hit-flash'), HIT_MS);
}

/**
 * Moves to the block that holds a source line (`?l=<line>`, the line of a problem, a backlink or a link
 * of this file): the last block that starts on or before it (see `blockIndexForLine`), inside
 * `#content` only, and marks it for a moment. A line above every block (it is in the frontmatter)
 * leaves the page at the top, where the properties are.
 */
function scrollToLine(line) {
  const blocks = [...refs.content.querySelectorAll('[data-line]')];
  const index = blockIndexForLine(blocks.map((block) => Number(block.dataset.line)), line);
  if (index < 0) {
    refs.content.scrollTop = 0;
    // The line is in the frontmatter, which is at the top: what is wrong with it is said there.
    const notice = refs.content.querySelector('.fm-error');
    if (notice) flash(notice);
    return;
  }
  const block = blocks[index];
  scrollWithin(refs.content, block, { block: 'start', margin: LINE_MARGIN });
  flash(block);
  // The keyboard goes with the reader: the next Tab leads to what follows this block, not to the top of the page.
  block.tabIndex = -1;
  block.focus({ preventScroll: true });
}

/**
 * Shows a folder of the path above a file in the tree: the folders on the way are opened, the folder is
 * brought to the top of the tree (a drawer comes out first) and marked for a moment.
 */
function revealDir(path) {
  let changed = false;
  for (const dir of path.split('/').map((_, index, parts) => parts.slice(0, index + 1).join('/'))) {
    if (!state.openDirs.has(dir)) {
      state.openDirs.add(dir);
      changed = true;
    }
    const item = findDirItem(dir);
    if (item) setDirOpen(item, true);
  }
  if (changed) {
    saveOpenDirs();
    applyFresh();
  }

  const row = findDirItem(path)?.firstElementChild;
  if (!row) return;
  if (window.getComputedStyle(refs.sidebar).position === 'fixed') setOverlay('tree');
  scrollWithin(refs.sidebar, row, { block: 'start', margin: 8 + treeHeadHeight() });
  flash(row);
  row.focus({ preventScroll: true });
}

// ---------------------------------------------------------------------------------------------
// Content: overview
// ---------------------------------------------------------------------------------------------

/**
 * The page is a column of sections in reading order: the budget (its sentence is the title), the errors, the layers, what is to be
 * reviewed (both of issues, in one wrapper: see `issuesBlock`) and project memory. When #content is wide enough app.css lays the issues out
 * as a second column, the errors above what is to be reviewed.
 */
function overviewPage(data) {
  if (isNotes()) return notesOverviewPage(data);
  const page = el('div', 'page overview');
  page.append(overviewHead(data), budgetSection(data), issuesBlock(data), layersSection(data), memorySection(data));
  return page;
}

/** `08:29'da` in Turkish, `08:29` in English: the time the index was built, as the sentence around it wants it. */
function indexedTime(date) {
  const time = clockText(date);
  return getLang() === 'tr' ? `${time}'${trLocative(date.getHours(), date.getMinutes())}` : time;
}

/**
 * The page's title is what the page is about: what every session loads, as a sentence. The number is an
 * estimate and an upper limit, which the tooltip says (and the note under the band, for a touch screen). The
 * top bar already says which page this is; the folder being read is in the top bar too.
 */
function overviewHead(data) {
  const head = el('header', 'ov-head');
  const title = el('h1', null, t('budget.title', { tokens: formatTokens(data.everySessionTokens) }));
  title.title = t('budget.hint');
  head.append(title);

  const parts = [tn('overview.files', data.fileCount)];
  const builtAt = new Date(data.builtAt);
  if (!Number.isNaN(builtAt.getTime())) parts.push(t('overview.indexed', { time: indexedTime(builtAt) }));
  if (data.outputStyle) parts.push(t('overview.outputStyle', { name: data.outputStyle }));
  head.append(el('p', 'muted overview-meta', parts.join(' · ')));
  return head;
}

// ---- Context budget band ---------------------------------------------------------------------

/** What the band shows for a part: the file name, or "Skill descriptions (33)" for a group. */
function budgetPartName(part) {
  if (part.kind === 'file') return part.name;
  return ['Skill', 'Agent', 'Command'].includes(part.layer)
    ? t(`budget.group.${part.layer}`, { n: part.count })
    : t('budget.group.generic', { layer: layerName(part.layer), n: part.count });
}

function bandFilesId(part) {
  return `band-files-${part.layer}`;
}

/**
 * The one stop of a part, for the keyboard, the screen reader and the mouse alike: a link that opens the
 * file, or a button that lists a group's files.
 */
function budgetLabel(part) {
  let control;
  if (part.kind === 'file') {
    control = el('a', 'band-label');
    control.href = fileHash(sid(), part.path);
  } else {
    control = el('button', 'band-label');
    control.type = 'button';
    control.setAttribute('aria-expanded', String(state.openBand.has(part.id)));
    control.setAttribute('aria-controls', bandFilesId(part));
    control.addEventListener('click', () => toggleBandGroup(part));
  }
  control.dataset.part = part.id;
  return control;
}

/**
 * The picture of a part in the band, as wide as its tokens. It is not a second stop (the band is hidden
 * from the keyboard's and the screen reader's view, see `budgetBand`): the label is, and the mouse or a
 * finger on the picture does what the label does.
 */
function budgetSegment(part, label, title) {
  const segment = el('span', `band-seg lm-${part.loadMode}`);
  segment.dataset.part = part.id;
  segment.style.flexGrow = String(part.tokens);
  segment.title = title;
  segment.addEventListener('click', () => label.click());
  return segment;
}

function toggleBandGroup(part) {
  const open = !state.openBand.has(part.id);
  if (open) state.openBand.add(part.id);
  else state.openBand.delete(part.id);
  for (const control of refs.content.querySelectorAll('button[data-part]')) {
    if (control.dataset.part === part.id) control.setAttribute('aria-expanded', String(open));
  }
  const files = document.getElementById(bandFilesId(part));
  if (files) files.hidden = !open;
  saveUi();
  scheduleFit();
}

/** A segment and its label are one thing: pointing at either (or focusing the label) marks both. */
function linkBandHighlights(...containers) {
  const mark = (event, on) => {
    const control = event.target.closest('[data-part]');
    if (!control) return;
    for (const other of refs.content.querySelectorAll('.band-seg, .band-label')) {
      if (other.dataset.part === control.dataset.part) other.classList.toggle('is-active', on);
    }
  };
  for (const container of containers) {
    container.addEventListener('mouseover', (event) => mark(event, true));
    container.addEventListener('mouseout', (event) => mark(event, false));
    container.addEventListener('focusin', (event) => mark(event, true));
    container.addEventListener('focusout', (event) => mark(event, false));
  }
}

/**
 * Up to `limit` (`LIST_ROWS`) of the `rows`, then a button that shows the rest (and hides it again). The
 * choice lives in `state.moreOpen` under `key`, so a live refresh keeps it. The rows are `<li>`s;
 * returns the list and, when there are more rows than fit, the button.
 */
function limitedList(key, className, rows, limit = LIST_ROWS) {
  const list = el('ul', className);
  const expanded = state.moreOpen.has(key);
  rows.forEach((row, index) => {
    row.hidden = !expanded && index >= limit;
  });
  list.append(...rows);
  if (rows.length <= limit) return [list];

  const rest = rows.length - limit;
  const button = el('button', 'more-btn');
  button.type = 'button';
  const show = (open) => {
    button.textContent = open ? t('more.less') : t('more.show', { n: rest });
    button.setAttribute('aria-expanded', String(open));
  };
  show(expanded);
  button.addEventListener('click', () => {
    const open = !state.moreOpen.has(key);
    if (open) state.moreOpen.add(key);
    else state.moreOpen.delete(key);
    rows.forEach((row, index) => {
      row.hidden = !open && index >= limit;
    });
    show(open);
    saveUi();
    scheduleFit();
  });
  return [list, button];
}

/** The files of a description group, biggest first; closed until the group's control is used. */
function bandFiles(part, name) {
  const box = el('div', 'band-files');
  box.id = bandFilesId(part);
  box.hidden = !state.openBand.has(part.id);
  box.append(el('div', 'band-files-title', name));
  const rows = part.files.map((file) => {
    const row = el('li', 'band-file');
    row.append(loadDot(file.loadMode), pathLink(file.path, { skillFirst: true }), tokensText(file.tokens));
    return row;
  });
  box.append(...limitedList(`band:${part.layer}`, 'band-file-list', rows));
  return box;
}

/**
 * The stacked bar, the labels under it and the file lists of the groups (closed at first). The bar is a
 * picture of the labels: what it says, the list of labels says in words, so it is left out of the
 * accessibility tree and the labels are the list a screen reader reads.
 */
function budgetBand(budget) {
  const band = el('div', 'band');
  band.setAttribute('aria-hidden', 'true');
  const labels = el('ul', 'band-labels');
  labels.setAttribute('aria-label', t('budget.label'));
  const lists = [];

  for (const part of budget.parts) {
    const name = budgetPartName(part);
    const tokens = formatTokens(part.tokens);
    const share = formatPercent(part.percent);
    const detail = t('budget.partTitle', { tokens, percent: share });

    const label = budgetLabel(part);
    band.append(budgetSegment(part, label, `${name}\n${detail}`));
    if (part.kind === 'group') {
      const chevron = el('span', 'chevron');
      chevron.setAttribute('aria-hidden', 'true');
      label.append(chevron);
    }
    label.append(loadDot(part.loadMode), el('span', 'band-name', name), el('span', 'band-meta', `${tokens} · ${share}`));
    const item = document.createElement('li');
    item.append(label);
    labels.append(item);

    if (part.kind === 'group') lists.push(bandFiles(part, name));
  }

  linkBandHighlights(band, labels);
  return [band, labels, ...lists];
}

function memoryNote(data) {
  if (data.projectMemory.length === 0) return null;
  const largest = Math.max(...data.projectMemory.map((entry) => entry.tokens));
  const note = el('p', 'budget-note');
  const link = el('a', null, t('budget.memoryNote', { tokens: formatTokens(largest) }));
  link.href = overviewHash(sid(), { memory: true });
  note.append(link);
  return note;
}

/**
 * What every session starts with: one bar whose pieces are as wide as their tokens, the words that say what
 * it is, and a note on what each project adds on top. The headline is the page's title (`overviewHead`). The
 * pieces come from `/api/tree` (each file's `everySessionTokens`); a server that does not send that field
 * gets no bar.
 */
function budgetSection(data) {
  const section = el('section', 'section ov-budget');
  const budget = state.tree ? budgetParts(flattenFiles(state.tree.nodes)) : null;
  if (budget !== null) {
    if (budget.parts.length === 0) section.append(el('p', 'muted', t('budget.none')));
    else section.append(...budgetBand(budget), el('p', 'budget-note muted', t('budget.explain')));
  }
  const note = memoryNote(data);
  if (note) section.append(note);
  return section;
}

// ---- Issues ----------------------------------------------------------------------------------

/** The first line of an issue row: the path, and its details beside it or, when short of room, under it. */
function issueLine(path, ...details) {
  const line = el('div', 'issue-line');
  line.append(path);
  if (details.length > 0) {
    const meta = el('span', 'issue-meta');
    meta.append(...details);
    line.append(meta);
  }
  return line;
}

function rawCode(raw) {
  const code = el('code', 'issue-raw', raw);
  code.title = raw;
  return code;
}

/** What one item of each issue group shows in its `<li>`: the nodes of the row. */
function issueRows() {
  return {
    broken: (item) => [
      issueLine(pathLink(item.source, { line: item.line }), lineNote([item.line]), rawCode(item.raw),
        el('span', 'muted tail', `(${t(`kind.${enumName(item.kind)}`)})`)),
    ],
    pending: (item) => [
      issueLine(pathLink(item.source, { line: item.line }), lineNote([item.line]), rawCode(item.raw)),
    ],
    orphans: (item) => [
      issueLine(pathLink(item.path), ...(isNotes() ? [] : [el('span', 'muted tail', `(${layerName(item.layer)})`)])),
    ],
    // The line the parser stopped at (an older server does not say): the link opens the file there, and what is wrong with it is said at the top of the file.
    frontmatter: (item) => {
      const line = positiveLine(item.line);
      return [
        issueLine(pathLink(item.path, { line }), ...(line === null ? [] : [lineNote([line])])),
        el('div', 'muted issue-detail', item.error),
      ];
    },
  };
}

/**
 * The counts of one section, each leading to its group: opens it and brings it into view (the `issues` route). The errors are red; what is only to be
 * reviewed is not. `hideZero`: a group with nothing in it has no chip ("0 notes not created yet" is not news).
 */
function issueChips(groups, { hideZero = false } = {}) {
  const list = el('ul', 'issue-chips');
  for (const { key, items } of groups) {
    const count = items.length;
    if (count === 0 && hideZero) continue;
    const item = document.createElement('li');
    const chip = el(count > 0 ? 'a' : 'span', 'issue-chip');
    chip.dataset.group = key;
    chip.append(el('b', null, String(count)), ' ', tn(`issues.chip.${key}`, count));
    if (count > 0) {
      chip.href = overviewHash(sid(), { issues: key });
      if (issueSection(key) === 'errors') chip.classList.add('tone-error');
    } else {
      chip.classList.add('is-zero');
    }
    item.append(chip);
    list.append(item);
  }
  return list;
}

/**
 * An errors group starts open; a group to review starts folded, and says in a sentence under its name what it is and that it is not an error
 * (a reader who has never been told what a link "not written yet" is has nothing else to go by). A group the reader (or a route) opens or closes keeps that.
 */
function issueGroup({ key, items, row }) {
  const review = issueSection(key) === 'review';
  const startsOpen = issueStartsOpen(key, items.length);
  const details = el('details', review ? 'issues' : 'issues is-error');
  details.dataset.issues = key;
  details.open = state.issueOpen.has(key) ? state.issueOpen.get(key) : startsOpen;
  details.addEventListener('toggle', () => {
    if (details.open === startsOpen) state.issueOpen.delete(key);
    else state.issueOpen.set(key, details.open);
    saveUi();
    scheduleFit();
  });

  const summary = el('summary');
  summary.append(el('span', 'issue-title', t(`issues.${key}`)), ' ', el('span', 'count', String(items.length)));
  if (review) summary.append(el('span', 'issue-help', t(`issues.help.${key}`)));
  details.append(summary);
  const rows = items.map((item) => {
    const li = document.createElement('li');
    li.append(...row(item));
    return li;
  });
  details.append(...limitedList(`issues:${key}`, 'issue-list', rows));
  return details;
}

/** A section's head: its name and its counts. Beside the rest of the page (two columns) it stays at the top while its groups scroll by. */
function issuesHead(title, groups, options) {
  const head = el('div', 'issues-head');
  head.append(el('h2', null, title));
  if (groups !== null) head.append(issueChips(groups, options));
  return head;
}

/** Errors: broken links and frontmatter that could not be read. Nothing wrong is a calm line, not an empty box. */
function errorsSection(groups) {
  const section = el('section', 'section ov-errors');
  if (groups.every((group) => group.items.length === 0)) {
    section.append(issuesHead(t('issues.errors'), null), el('p', 'issue-clear muted', t('issues.errorsNone')));
    return section;
  }
  section.append(issuesHead(t('issues.errors'), groups, { hideZero: isNotes() }), ...groups.filter((group) => group.items.length > 0).map(issueGroup));
  return section;
}

/** To review: links not written yet and orphan files. Not errors, so folded and neutral. */
function reviewSection(groups) {
  const section = el('section', 'section ov-review');
  section.append(issuesHead(t('issues.review'), groups, { hideZero: isNotes() }), ...groups.filter((group) => group.items.length > 0).map(issueGroup));
  return section;
}

/**
 * The two sections of issues in one wrapper (`.ov-issues`): in a narrow page the wrapper is not a box, so the errors and what is to be
 * reviewed take their places among the other sections (the layers between them); in a wide one the wrapper is the second column.
 * Nothing to review leaves that section out.
 */
function issuesBlock(data) {
  const rows = issueRows();
  const { errors, review, reviewCount } = splitIssues(data);
  const withRows = (groups) => groups.map((group) => ({ ...group, row: rows[group.key] }));
  const block = el('div', 'ov-issues');
  block.append(errorsSection(withRows(errors)));
  if (reviewCount > 0) block.append(reviewSection(withRows(review)));
  return block;
}

/**
 * Moves to what the overview route asks for: an issue group (opened) or the project memory table
 * (opened), brought to the top of `#content`; with neither, to the top of the page. Where the
 * issues head is pinned to the top (wide screens) the target is placed below it.
 */
function applyOverviewRoute(route) {
  const page = refs.content.firstElementChild;
  if (!page) return;

  let target = null;
  if (route.issues) target = page.querySelector(`details[data-issues="${route.issues}"]`);
  else if (route.memory) target = page.querySelector('details.memory-index');
  if (!target) {
    refs.content.scrollTop = 0;
    return;
  }
  if (route.issues) state.issueOpen.set(route.issues, true);
  else state.memoryOpen = true;
  target.open = true;
  saveUi();

  const pinned = target.closest('.section')?.querySelector('.issues-head') ?? null;
  const below = pinned && window.getComputedStyle(pinned).position === 'sticky' && pinned.parentElement.contains(target);
  scrollWithin(refs.content, target, { block: 'start', margin: below ? pinned.offsetHeight + 8 : 12 });
}

// ---- Layers, project memory ------------------------------------------------------------------

/** What every load marker means, one line each; `legendBlock` (the overview) and `treeFooter` (the file views) show it. */
function legendList() {
  const list = el('ul', 'legend');
  for (const mode of LOAD_MODES) {
    const item = el('li', 'legend-item');
    item.append(loadDot(mode, { decorative: true }), el('span', null, t(`load.${mode}`)));
    list.append(item);
  }
  return list;
}

/**
 * The legend of the overview: the load markers and what the two numbers beside a tree row are. It is here, once; the
 * tree's own legend (`treeFooter`) is for the file views, and the overview hides it (see `body.overview-view`).
 */
function legendBlock() {
  const block = el('div', 'legend-block');
  block.append(el('div', 'legend-title muted', t('legend.title')), legendList(), el('p', 'legend-note muted', t('legend.tokens')));
  return block;
}

/** `fixed` sets `table-layout: fixed`, so a long cell is cut instead of widening the table. */
function tableOf(headers, rows, { fixed = false } = {}) {
  const wrap = el('div', 'table-wrap');
  const table = el('table', fixed ? 'data fixed' : 'data');
  const head = table.createTHead().insertRow();
  for (const header of headers) {
    const cell = el('th', [header.numeric ? 'num' : '', header.className ?? ''].join(' ').trim() || null, header.text);
    cell.scope = 'col';
    if (header.title) cell.title = header.title;
    head.append(cell);
  }
  const body = table.createTBody();
  for (const row of rows) body.append(row);
  wrap.append(table);
  return wrap;
}

function cell(content, className) {
  const td = el('td', className);
  td.append(content);
  return td;
}

/** The link that takes a number of the layers table to the table of project memory indexes it is made of. */
function memoryLink(tokens) {
  const link = el('a', 'memory-link', formatTokens(tokens));
  link.href = overviewHash(sid(), { memory: true });
  link.title = t('layers.memoryLink');
  return link;
}

/**
 * One layer: its name, how its files load, how many files and tokens it has, and its share of what every
 * session loads. The memory index is the one layer with no share that is not folded away: it is loaded in every
 * session of its project, so its share is "per project" and its tokens lead to the table of those projects.
 * A folded layer (`rest`) is dimmed: its row is there for completeness.
 */
function layerRow(entry, data, modes, { rest = false } = {}) {
  const row = document.createElement('tr');
  if (rest) row.className = 'row-zero';
  row.append(cell(layerName(entry.layer)));

  const loading = el('ul', 'mode-list');
  for (const { mode } of modes.get(enumName(entry.layer)) ?? []) {
    const item = el('li', 'legend-item');
    item.title = loadName(mode);
    item.append(loadDot(mode), el('span', null, loadShortName(mode)));
    loading.append(item);
  }
  row.append(cell(loading));

  const perProject = enumName(entry.layer) === 'MemoryIndex' && !(entry.everySessionTokens > 0);
  row.append(cell(String(entry.files), 'num col-files'));
  row.append(cell(perProject && data.projectMemory.length > 0 ? memoryLink(entry.tokens) : tokensText(entry.tokens), 'num col-total'));

  const share = el('div', 'share');
  const bar = el('span', 'bar');
  bar.setAttribute('aria-hidden', 'true');
  if (entry.everySessionTokens > 0) {
    const pct = percent(entry.everySessionTokens, data.everySessionTokens);
    const fill = el('span', 'bar-fill');
    fill.style.width = `${pct}%`;
    bar.append(fill);
    share.append(bar, tokensText(entry.everySessionTokens), el('span', 'muted', formatPercent(pct)));
  } else if (perProject) {
    share.append(el('span', 'muted per-project', t('layers.perProject')));
  } else {
    share.append(bar, tokensText(0));
  }
  row.append(cell(share));
  return row;
}

/**
 * The layers in two: the ones that put something into every session (and the memory index, which does in every
 * project) are the table, and the rest, which load only when needed, are one folded line under it that opens into
 * their rows (`splitLayers`).
 */
function layersSection(data) {
  const section = el('section', 'section ov-layers');
  section.append(el('h2', null, t('layers.title')), legendBlock());

  const modes = state.tree ? loadModesByLayer(flattenFiles(state.tree.nodes)) : new Map();
  const { shown, rest, restTokens } = splitLayers(data.layers);
  const wrap = tableOf([
    { text: t('col.layer') },
    { text: t('col.load') },
    { text: t('col.files'), numeric: true, className: 'col-files' },
    { text: t('col.tokens'), numeric: true, className: 'col-total', title: t('tokens.hint') },
    { text: t('col.share'), title: t('tokens.hint') },
  ], shown.map((entry) => layerRow(entry, data, modes)));
  section.append(wrap);
  if (rest.length === 0) return section;

  const table = wrap.firstElementChild;
  const columns = table.tHead.rows[0].cells.length;
  const body = document.createElement('tbody');
  body.id = 'layers-rest';
  body.hidden = !state.layersRestOpen;
  body.append(...rest.map((entry) => layerRow(entry, data, modes, { rest: true })));

  const button = el('button', 'fold-btn');
  button.type = 'button';
  button.setAttribute('aria-controls', body.id);
  button.setAttribute('aria-expanded', String(state.layersRestOpen));
  const chevron = el('span', 'chevron');
  chevron.setAttribute('aria-hidden', 'true');
  button.append(chevron, el('span', null, tn('layers.rest', rest.length, { tokens: formatTokens(restTokens) })));
  button.addEventListener('click', () => {
    state.layersRestOpen = !state.layersRestOpen;
    body.hidden = !state.layersRestOpen;
    button.setAttribute('aria-expanded', String(state.layersRestOpen));
    saveUi();
  });
  const fold = document.createElement('tbody');
  const foldCell = fold.insertRow().insertCell();
  foldCell.colSpan = columns;
  foldCell.append(button);
  table.append(fold, body);
  return section;
}

/**
 * A project's memory index: the project's name and where it lives, as everywhere a project's folder is named.
 * A table has the height for it, so what does not fit wraps (see `.is-wrap`) and nothing is cut. The slug is the tooltip.
 */
function projectLink(path, slug) {
  const label = state.labels.get(slug) ?? projectLabels([slug], homeSlug(state.tree?.root)).get(slug);
  const link = el('a', 'path-link is-wrap');
  link.href = fileHash(sid(), path);
  link.title = slug;
  link.append(...projectName(label, { context: 'always' }));
  return link;
}

/** Last, and folded: a table of 24 rows of reference, not something to act on. */
function memorySection(data) {
  const section = el('section', 'section ov-memory');
  if (data.projectMemory.length === 0) {
    section.append(el('h2', null, t('memory.title')), el('p', 'muted', t('memory.none')));
    return section;
  }
  const details = el('details', 'memory-index');
  details.open = state.memoryOpen;
  details.addEventListener('toggle', () => {
    state.memoryOpen = details.open;
    saveUi();
  });
  const summary = el('summary', null, t('memory.title'));
  summary.append(' ', el('span', 'count', String(data.projectMemory.length)));

  const rows = data.projectMemory.map((entry) => {
    const row = document.createElement('tr');
    row.append(cell(projectLink(entry.path, entry.project)), cell(tokensText(entry.tokens), 'num'));
    return row;
  });
  details.append(summary, tableOf([
    { text: t('col.project') },
    { text: t('col.tokensShort'), numeric: true, className: 'col-tokens', title: t('tokens.hint') },
  ], rows, { fixed: true }));
  section.append(details);
  return section;
}

// ---------------------------------------------------------------------------------------------
// Content: overview of a source of notes
// ---------------------------------------------------------------------------------------------

/**
 * The overview of a vault or a Markdown folder: its name and what it holds, the notes that carry the tag the address asks for (above
 * everything else: it is where the reader arrives), where to start (the entry note, the notes changed last, the notes linked to most), the
 * errors, the tags and what is to be reviewed. There is no budget, no layers and no memory: those are a Claude configuration's. Wide, it is the
 * same two columns as the other overview: the issues beside the rest.
 */
function notesOverviewPage(data) {
  const page = el('div', 'page overview');
  page.append(notesHead(data));
  if (state.route.tag) page.append(taggedSection(state.route.tag));
  const front = frontSection(noteFront(data));
  if (front !== null) page.append(front);
  page.append(issuesBlock(data), tagsSection(data));
  return page;
}

/**
 * Where to start in a source of notes: the note it is entered by (when the server names one), and two short lists, the notes that changed last and the notes
 * that the most notes link to. One section (`.ov-front`); a list the server has nothing for is left out, and with nothing at all there is no section.
 */
function frontSection(front) {
  const lists = [];
  if (front.recent.length > 0) lists.push(noteList('recent', t('recent.title'), front.recent, (note) => ageNode(note.modifiedAt)));
  if (front.mostLinked.length > 0) lists.push(noteList('linked', t('linked.title'), front.mostLinked, (note) => el('span', 'note-meta', tn('overview.backlinks', note.count))));
  if (front.entry === null && lists.length === 0) return null;
  const section = el('section', 'section ov-front');
  if (front.entry !== null) section.append(entryBlock(front.entry));
  if (lists.length > 0) {
    const grid = el('div', 'front-lists');
    grid.append(...lists);
    section.append(grid);
  }
  return section;
}

/** The note the source is entered by: its name, a link to it, and what it is. */
function entryBlock(entry) {
  const block = el('div', 'front-entry');
  block.append(el('h2', null, t('entry.title')));
  const card = el('a', 'entry-card');
  card.href = fileHash(sid(), entry.path);
  card.title = entry.path;
  card.append(el('span', 'entry-name', entry.title), el('span', 'entry-hint', t(`entry.hint.${profileName(state.source.profile)}`)));
  block.append(card);
  return block;
}

/** A short list of notes: each a path link and, quiet and at the end of its row, what `meta` says of it (how long ago it changed, how many notes link to it). */
function noteList(key, title, notes, meta) {
  const box = el('div', 'front-list');
  box.append(el('h2', null, title));
  const rows = notes.map((note) => {
    const row = el('li', 'note-row');
    row.append(pathLink(note.path), meta(note));
    return row;
  });
  box.append(...limitedList(`front:${key}`, 'note-list', rows));
  return box;
}

/** How long ago an ISO date was, in the words of the top bar ("3 dk önce"); nothing for a date that is none. */
function ageText(iso) {
  const age = ageSince(iso);
  return age === null ? '' : t(`age.${age.unit}`, { n: age.n });
}

/** The time of a note as a `<time>` that `updateAges` keeps up with the clock. */
function ageNode(iso) {
  const node = el('time', 'note-age', ageText(iso));
  node.dateTime = iso;
  return node;
}

/** The times of the overview go on, as the one in the top bar does (see `AGE_TICK_MS`). */
function updateAges() {
  for (const node of refs.content.querySelectorAll('time.note-age')) node.textContent = ageText(node.dateTime);
}

/** The title is the name of the source; under it how many notes and folders it has, and when the index was built. */
function notesHead(data) {
  const head = el('header', 'ov-head');
  const title = el('h1', null, state.source.name);
  title.title = state.source.path;
  head.append(title);

  const parts = [tn('overview.files', data.fileCount), tn('overview.folders', folderPaths(state.tree?.nodes).size)];
  const builtAt = new Date(data.builtAt);
  if (!Number.isNaN(builtAt.getTime())) parts.push(t('overview.indexed', { time: indexedTime(builtAt) }));
  head.append(el('p', 'muted overview-meta', parts.join(' · ')));
  return head;
}

/**
 * A tag as a chip that leads to the notes that carry it (`?tag=`): `#name`, and how many notes when `count` is given. `label` is the
 * text as it was written (a tag in the text of a note keeps its case). The tag the page is filtered by is marked.
 */
function tagChip(name, { count, label = `#${name}` } = {}) {
  const chip = el('a', 'tag-chip', label);
  chip.href = overviewHash(sid(), { tag: name });
  if (count !== undefined) chip.append(' ', el('span', 'tag-count', String(count)));
  if (state.route.view === 'overview' && state.route.tag && tagKey(state.route.tag) === tagKey(name)) {
    chip.classList.add('is-current');
    chip.setAttribute('aria-current', 'true');
  }
  return chip;
}

/** The tags of the source, the one with the most notes first. The first TAG_ROWS are shown, the rest when the reader asks. */
function tagsSection(data) {
  const section = el('section', 'section ov-tags');
  section.append(el('h2', null, t('tags.title')));
  const tags = sortTags(data.tags);
  if (tags.length === 0) {
    section.append(el('p', 'muted', t('tags.none')));
    return section;
  }
  const rows = tags.map((tag) => {
    const item = document.createElement('li');
    item.append(tagChip(tag.name, { count: tag.count }));
    return item;
  });
  section.append(...limitedList('tags', 'tag-chips', rows, TAG_ROWS));
  return section;
}

/**
 * The notes that carry a tag: asked of the tree (it has the tags of every note), whatever the case of the tag. The way back to
 * the overview is the link that closes the list.
 */
function taggedSection(tag) {
  const notes = filesWithTag(flattenFiles(state.tree?.nodes), tag);
  const section = el('section', 'section ov-tagged');
  const head = el('div', 'tagged-head');
  head.append(el('h2', null, `#${tag} · ${tn('tagged.count', notes.length)}`));
  const close = el('a', 'tagged-close', t('tagged.close'));
  close.href = overviewHash(sid());
  head.append(close);
  section.append(head);
  if (notes.length === 0) {
    section.append(el('p', 'muted', t('tagged.none')));
    return section;
  }
  const rows = notes.map((note) => {
    const item = document.createElement('li');
    item.append(pathLink(note.path));
    return item;
  });
  section.append(...limitedList(`tag:${tagKey(tag)}`, 'tagged-list', rows));
  return section;
}

// ---------------------------------------------------------------------------------------------
// Content: the sources page
// ---------------------------------------------------------------------------------------------

/**
 * Every source the server lists, one card each: its name, its folder, how it is read, how much it holds, and which is open and which was
 * opened last. A card is a link to the overview of its source; the card of a source whose folder cannot be read says why and leads
 * nowhere. Where this browser may change the list (see `sourcesAccess`) a card also has a way to remove its source. Under the cards is the
 * form that adds one: always, in the same place and under the same title; where this browser may not change the list its boxes are inactive and
 * the box says why and what can be done. Under that, how else one is added, folded. Last of all, which pusula this is (see `versionLine`).
 */
function sourcesPage() {
  const page = el('div', 'page sources-page');
  page.append(sourcesHead());
  if (state.missing !== null) {
    const notice = el('div', 'notice notice-warn');
    notice.setAttribute('role', 'status');
    notice.append(el('p', null, t('sources.missing', { id: state.missing })));
    page.append(notice);
  }
  if (state.sourcesAccess.fileError !== null) {
    const notice = el('div', 'notice notice-warn source-file-error');
    notice.setAttribute('role', 'status');
    notice.append(el('p', null, t('sources.fileError', { error: state.sourcesAccess.fileError })));
    page.append(notice);
  }
  page.append(sourceList(), addSourceForm(), sourcesHelp());
  const version = versionLine();
  if (version !== null) page.append(version);
  return page;
}

/** What this browser is told about where the list may be changed from: `sourceLock` of what the server said; null where it may. */
function currentLock() {
  return sourceLock(state.sourcesAccess, state.host, state.sourcesFile);
}

/** The title and, where this browser may not change the list, a quiet chip beside it that says so (and that it is the network that makes it so). */
function sourcesHead() {
  const head = el('div', 'sources-head');
  head.append(el('h1', null, t('sources.title')));
  const lock = currentLock();
  if (lock !== null) head.append(el('span', 'source-chip', lock.chip === 'Remote' ? t('sources.readonly.Remote') : t('sources.readonly')));
  // The computer whose folders the sources are: the one the server runs on, whatever device this page is on.
  if (state.host.machine !== null) head.append(el('p', 'sources-machine', t('sources.machine', { machine: state.host.machine })));
  return head;
}

function sourceList() {
  if (state.sources.length === 0) return el('p', 'muted', t('sources.none'));
  const last = readLastSource();
  const list = el('ul', 'source-list');
  for (const source of state.sources) {
    const item = document.createElement('li');
    item.append(sourceCard(source, { open: sid() === source.id, last: last === source.id }));
    list.append(item);
  }
  return list;
}

/**
 * `open`: the source the page shows (it says so, and the opened-last mark would only repeat it); `last`: the one opened last. The card is a
 * box that holds the link (its text; a click anywhere on the card follows it, see `.source-body` in app.css) and, where this browser may remove
 * sources, a button beside it: a button inside a link would be invalid, and a click on it must not follow the link.
 */
function sourceCard(source, { open, last }) {
  const readable = source.available !== false;
  const editing = state.sourcesAccess.canEdit;
  const asking = editing && state.sourceEdit.asking === source.id;
  const card = el('div', 'source-card');
  if (!readable) card.classList.add('is-unavailable');
  if (open) card.classList.add('is-open');
  if (asking) card.classList.add('is-asking');

  const body = el(readable ? 'a' : 'div', 'source-body');
  if (readable) {
    body.href = overviewHash(source.id);
    body.dataset.keep = `open:${source.id}`;
    if (open) body.setAttribute('aria-current', 'true');
  }
  const main = el('span', 'source-main');
  main.append(el('strong', 'source-title', source.name), el('span', 'source-path', source.path));
  const meta = el('span', 'source-meta');
  meta.append(el('span', 'source-profile', t(`source.profile.${profileName(source.profile)}`)));
  if (readable) meta.append(el('span', 'source-count', tn(`source.count.${isNoteProfile(source.profile) ? 'notes' : 'files'}`, source.fileCount ?? 0)));
  if (open) meta.append(el('span', 'source-tag is-open', t('source.open')));
  else if (last) meta.append(el('span', 'source-tag', t('source.last')));
  body.append(main, meta);
  if (!readable) body.append(el('span', 'source-error', unavailableShort(source.errorCode)));
  card.append(body);
  if (editing) card.append(asking ? removeConfirm(source) : removeButton(source));
  return card;
}

/** Why the folder of a source cannot be read, short, for its card (the card has the folder; the page of the error has the long form): by the code the list gives, never by the server's own English sentence. */
function unavailableShort(code) {
  const known = sourceErrorCode(code);
  return known === '' ? t('source.unavailable') : t(`source.whyShort.${known}`);
}

/** The calm way to remove a source: a quiet button in the corner of its card. It does nothing yet but ask (see `removeConfirm`). */
function removeButton(source) {
  const actions = el('div', 'source-actions');
  const button = el('button', 'source-remove', t('sources.remove'));
  button.type = 'button';
  button.dataset.keep = `remove:${source.id}`;
  button.setAttribute('aria-label', t('sources.remove.named', { name: source.name })); // every card has one: the name tells them apart
  button.addEventListener('click', () => askRemove(source.id));
  actions.append(button);
  return actions;
}

/**
 * The question a card asks inside itself (the browser's own dialog is not used): the folder is not touched, only the list. The keyboard
 * is on "cancel" when it opens, so a second Enter removes nothing; Escape cancels, too.
 */
function removeConfirm(source) {
  const edit = state.sourceEdit;
  const group = el('div', 'source-confirm');
  group.setAttribute('role', 'group');
  group.setAttribute('aria-labelledby', `source-ask-${source.id}`);
  const ask = el('p', 'source-ask', t('sources.remove.ask'));
  ask.id = `source-ask-${source.id}`;

  const yes = el('button', 'button is-danger', edit.removing ? t('sources.remove.busy') : t('sources.remove'));
  yes.type = 'button';
  yes.disabled = edit.removing;
  yes.dataset.keep = `yes:${source.id}`;
  yes.addEventListener('click', () => removeSource(source.id));
  const no = el('button', 'button is-quiet', t('sources.remove.cancel'));
  no.type = 'button';
  no.disabled = edit.removing;
  no.dataset.keep = `no:${source.id}`;
  no.addEventListener('click', () => cancelRemove(source.id));
  const buttons = el('div', 'source-confirm-actions');
  buttons.append(yes, no);

  group.append(ask, buttons);
  if (edit.removeError !== null) {
    const problem = el('p', 'source-problem', failureText(edit.removeError));
    problem.setAttribute('role', 'alert');
    group.append(problem);
  }
  group.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape' || edit.removing) return;
    event.preventDefault();
    cancelRemove(source.id);
  });
  return group;
}

/**
 * The form under the cards: the folder's path (as it is typed: the server takes `~` and says what is wrong with a path, in words) and, if the
 * source should be called something else, a name. What is typed is kept in `state.sourceEdit`, so a page that is drawn again (a live refresh)
 * has it back. A refusal is said in a line under the boxes, and the focus is on the path. Where this browser may not change the list (see
 * `sourceLock`) it is the same form in the same place under the same title, its boxes and its button inside a `fieldset disabled`, and above
 * them one line, with a lock, that says so and why, and the ways that are left.
 */
function addSourceForm() {
  const edit = state.sourceEdit;
  const lock = currentLock();
  const failure = edit.addError;
  const form = el('form', lock === null ? 'source-add' : 'source-add is-locked');
  form.noValidate = true;
  form.setAttribute('aria-labelledby', 'source-add-title');
  if (edit.adding) form.setAttribute('aria-busy', 'true');
  const title = el('h2', null, t('sources.add.title'));
  title.id = 'source-add-title';

  const submit = el('button', 'button source-submit', edit.adding ? t('sources.add.busy') : t('sources.add.submit'));
  submit.type = 'submit';
  submit.disabled = edit.adding;
  submit.dataset.keep = 'add';
  // The first way to add a folder is to choose it (see "The folder picker"); typing its path is the second, under it.
  const pick = el('button', 'button source-pick', t('sources.add.pick'));
  pick.type = 'button';
  pick.prepend(browseIcon('folder'));
  pick.setAttribute('aria-haspopup', 'dialog');
  pick.dataset.keep = 'browse';
  pick.addEventListener('click', openBrowse);
  const fields = el('div', 'source-fields');
  fields.append(
    pick,
    el('p', 'source-or', t('sources.add.or')),
    sourceField('path', { label: t('sources.add.path'), placeholder: t('sources.add.pathPlaceholder'), mono: true }),
    sourceField('name', { label: t('sources.add.name'), hint: t('sources.add.optional') }),
    submit,
  );
  const error = el('p', 'source-add-error', failure === null ? '' : failureText(failure));
  error.id = 'source-add-error';
  error.setAttribute('role', 'alert');

  if (lock === null) {
    form.append(title, fields, error);
  } else {
    const inactive = el('fieldset', 'source-set');
    inactive.disabled = true;
    inactive.append(fields);
    form.append(title, ...lockNotice(lock), inactive);
  }
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    addSource();
  });
  return form;
}

/** Where the list cannot be changed from here: the one line that says so and why (with a drawn lock), and under it the ways that are left, if any. */
function lockNotice(lock) {
  const notice = el('div', 'source-lock');
  notice.append(refs.lockIcon.content.firstElementChild.cloneNode(true), el('p', null, lockedText(lock)));
  const ways = sourceWays(lock);
  return ways === null ? [notice] : [notice, ways];
}

/** Why the list cannot be changed from here, in words. A server that names its machine says where it can be; one that does not says "the computer pusula runs on". */
function lockedText({ kind, machine }) {
  if (kind === 'Remote') return machine === null ? t('sources.locked.RemoteAnon') : t('sources.locked.Remote', { machine });
  return t(`sources.locked.${kind}`);
}

/**
 * A wording that says where a piece of code goes (`{file}`, `{flag}`, as `info.lines` says where the numbers go): the text before it, the code as an element
 * and the text after. The code is not a word of the wording, so it is neither translated nor cut.
 */
function withCode(template, placeholder, text, className) {
  const [before, after = ''] = template.split(`{${placeholder}}`);
  return [before, el('code', className, text), after];
}

/**
 * What can be done instead, for a page asked over the network: open it on the machine that runs the server; start the server so that the network may
 * change the list too (only where it was not started so); or edit the sources file, with a button that copies its path where the browser can. Null
 * where there is nothing to say (the server was started with the folders, so the list has no file and no one may change it).
 */
function sourceWays(lock) {
  if (lock.kind !== 'Remote') return null;
  const list = el('ul', 'source-ways');
  list.append(el('li', null, lock.machine === null ? t('sources.way.hereAnon') : t('sources.way.here', { machine: lock.machine })));
  if (lock.flag) {
    const flag = el('li');
    flag.append(...withCode(t('sources.way.flag'), 'flag', REMOTE_EDIT_FLAG, 'sources-flag'));
    list.append(flag);
  }
  if (lock.file !== null) {
    const file = el('li');
    const parts = withCode(t('sources.way.file'), 'file', lock.file, 'sources-file');
    file.append(...parts);
    const method = copyMethod({ clipboard: Boolean(navigator.clipboard?.writeText), command: typeof document.queryCommandSupported === 'function' && document.queryCommandSupported('copy') });
    // No button where the browser can copy by neither way: the path stays as it is, and one click selects the whole of it.
    if (method !== 'none') file.append(' ', copyButton(lock.file, parts[1]));
    list.append(file);
  }
  return list;
}

/** "Copy" beside a path: it says "copied" for a moment, in the button and (a button is no live region) to a screen reader; where copying fails it selects the path for the reader to copy. */
function copyButton(text, anchor) {
  const button = el('button', 'copy-btn', t('sources.copy'));
  button.type = 'button';
  button.setAttribute('aria-label', t('sources.copy.named'));
  button.dataset.keep = 'copy';
  let timer = 0;
  button.addEventListener('click', async () => {
    const done = await copyText(text);
    if (!done) selectText(anchor);
    button.textContent = done ? t('sources.copied') : t('sources.copyFailed');
    button.setAttribute('aria-label', button.textContent);
    announce(button.textContent);
    window.clearTimeout(timer);
    timer = window.setTimeout(() => {
      button.textContent = t('sources.copy');
      button.setAttribute('aria-label', t('sources.copy.named'));
    }, COPY_MS);
  });
  return button;
}

/** Copies a text: through the clipboard where the page may use it (a secure context), otherwise by selecting it in a field and asking the browser to copy (a page opened by a machine's address over plain http has no clipboard). True when it worked. */
async function copyText(text) {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // The browser refused (the page has no focus, no permission): the other way is tried.
    }
  }
  return copyBySelection(text);
}

function copyBySelection(text) {
  const field = el('textarea', 'copy-proxy');
  field.value = text;
  field.readOnly = true;
  field.tabIndex = -1;
  field.setAttribute('aria-hidden', 'true');
  const before = document.activeElement;
  document.body.append(field);
  field.select();
  field.setSelectionRange(0, text.length);
  let done = false;
  try {
    done = document.execCommand('copy');
  } catch {
    done = false;
  }
  field.remove();
  if (before instanceof HTMLElement) before.focus({ preventScroll: true });
  return done;
}

/** The text of a node selected, for a reader to copy it with the keys. */
function selectText(node) {
  const range = document.createRange();
  range.selectNodeContents(node);
  const selection = window.getSelection();
  selection?.removeAllRanges();
  selection?.addRange(range);
}

/**
 * A box of the form: `name` is the key of what it holds in `state.sourceEdit` (`path` or `name`). The refusal that is on screen describes it,
 * and marks the box it is about.
 */
function sourceField(name, { label, hint = null, placeholder = '', mono = false }) {
  const failure = state.sourceEdit.addError;
  const field = el('div', `field field-${name}`);
  const caption = el('label', null, label);
  caption.htmlFor = `source-add-${name}`;
  if (hint !== null) caption.append(' ', el('span', 'field-hint', hint));
  const input = el('input', mono ? 'source-input is-mono' : 'source-input');
  input.type = 'text';
  input.id = `source-add-${name}`;
  input.value = state.sourceEdit[name];
  input.placeholder = placeholder;
  input.autocomplete = 'off';
  input.spellcheck = false;
  input.setAttribute('autocapitalize', 'off');
  input.setAttribute('autocorrect', 'off');
  input.dataset.keep = name;
  if (failure !== null) input.setAttribute('aria-describedby', 'source-add-error');
  if (failure !== null && failure.field === name) input.setAttribute('aria-invalid', 'true');
  input.addEventListener('input', () => {
    state.sourceEdit[name] = input.value;
    clearAddError(name);
  });
  field.append(caption, input);
  return field;
}

/**
 * What was refused is no longer what is typed: once the box the refusal is about is edited (any box, for a refusal that is about none), the line under
 * the boxes and the marks on them go. The page is not drawn again for it, so the caret stays where it is.
 */
function clearAddError(name) {
  const edit = state.sourceEdit;
  if (edit.addError === null || (edit.addError.field !== null && edit.addError.field !== name)) return;
  edit.addError = null;
  const form = refs.content.querySelector('form.source-add');
  if (!form) return;
  for (const input of form.querySelectorAll('.source-input')) {
    input.removeAttribute('aria-invalid');
    input.removeAttribute('aria-describedby');
  }
  const line = form.querySelector('.source-add-error');
  if (line) line.textContent = '';
}

/** A refusal to add or remove a source in words: its own, the note of a closed page, or the page's words for a failure (see `sourceFailure`). */
function failureText({ kind, params }) {
  if (kind === 'Remote' || kind === 'CommandLine') return lockedText({ kind, machine: state.host.machine });
  return SOURCE_REFUSALS.includes(kind) ? t(`sources.error.${kind}`, params) : t(`error.${kind}`, params);
}

/** What a sources file looks like: the example in the help. */
function sourcesExample() {
  const example = el('pre', 'sources-example');
  example.append(el('code', null, t('sources.example')));
  return example;
}

/**
 * How else to add a source, folded under "other ways": the file the server reads them from (named here unless the box above names it already), with an
 * example and the one thing to know after changing it; the command line; and when a folder is a vault.
 */
function sourcesHelp() {
  const help = el('details', 'sources-help');
  help.open = state.sourceEdit.helpOpen;
  help.addEventListener('toggle', () => {
    state.sourceEdit.helpOpen = help.open;
  });
  help.append(el('summary', null, t('sources.more')));
  if (state.sourcesFile !== null) {
    if ((currentLock()?.file ?? null) === null) {
      const where = el('p');
      where.append(`${t('sources.help.file')} `, el('code', 'sources-file', state.sourcesFile));
      help.append(where);
    }
    help.append(sourcesExample(), el('p', null, t('sources.help.refresh')));
  }
  const more = el('p');
  more.append(`${t('sources.help.more')} `, el('code', 'sources-command', t('sources.command')));
  help.append(more, el('p', 'muted', t('sources.help.auto')));
  return help;
}

/**
 * The last line of the sources page: which pusula this is, and where its release notes are. Quiet text, and the one link out of the page: it
 * opens the release notes in a new tab when the reader follows it, and the page asks that address for nothing. A server that says no version
 * (an older one) gets no line, and null is returned.
 */
function versionLine() {
  if (state.appVersion === null) return null;
  const line = el('p', 'version-line', t('about.version', { version: state.appVersion }));
  const separator = el('span', null, '\u00b7');
  separator.setAttribute('aria-hidden', 'true');
  const link = el('a', 'release-link', t('about.releases'));
  link.href = RELEASES_URL;
  link.target = '_blank';
  link.rel = 'noopener noreferrer';
  link.dataset.keep = 'releases';
  line.append(separator, link);
  return line;
}

/**
 * The sources page drawn again from what the page keeps (a card that asks, a request that is on its way or was refused). A page that the
 * reader has gone to is left alone.
 */
function redrawSources() {
  if (state.route.view === 'sources' && !state.error) renderContent({ keepScroll: true });
}

/** The card asks, inside itself, whether to remove: one card at a time. The keyboard goes to the answer that does nothing. */
function askRemove(id) {
  const edit = state.sourceEdit;
  if (edit.removing) return;
  edit.asking = id;
  edit.removeError = null;
  redrawSources();
  focusKept(`no:${id}`);
}

function cancelRemove(id) {
  const edit = state.sourceEdit;
  if (edit.removing) return;
  edit.asking = null;
  edit.removeError = null;
  redrawSources();
  focusKept(`remove:${id}`);
}

/**
 * Removes a source from the list (the folder is not touched). The server answering "no such source" is what was asked for, and the list is
 * read again either way. What the page kept of the source goes with it: it is not the one opened last any more and, when it was the open one,
 * its tree, pages and live connection are dropped (see `closeSource`); the reader stays on the sources page, with the keyboard on the card that
 * took its place. A refusal is said inside the card, which keeps asking.
 */
async function removeSource(id) {
  const edit = state.sourceEdit;
  if (edit.removing) return;
  const name = state.sources.find((source) => source.id === id)?.name ?? id;
  const position = Math.max(0, state.sources.findIndex((source) => source.id === id));
  edit.removing = true;
  edit.removeError = null;
  redrawSources();
  try {
    await editSources('DELETE', sourceItemUrl(id));
  } catch (error) {
    if (!(error instanceof ApiError && error.status === 404)) {
      edit.removing = false;
      edit.removeError = sourceFailure(error, { file: state.sourcesFile });
      redrawSources();
      focusKept(`yes:${id}`);
      return;
    }
  }
  edit.removing = false;
  edit.asking = null;
  edit.removeError = null;
  forgetSource(id);
  if (state.source !== null && state.source.id === id) closeSource();
  try {
    await loadSources();
  } catch {
    state.sources = state.sources.filter((source) => source.id !== id); // the server has removed it, whatever it says now
  }
  if (state.route.view !== 'sources') return;
  redrawSources();
  showToast(t('sources.removed', { name })); // seen, and (the region of the toasts is a live one) heard
  const cards = [...refs.content.querySelectorAll('.source-card')];
  const next = cards[Math.min(position, cards.length - 1)]?.querySelector('a.source-body, button.source-remove');
  (next ?? refs.content.querySelector('[data-keep="path"]') ?? refs.content).focus({ preventScroll: true });
}

/**
 * Adds the folder that is typed to the list. The button is locked and says so while the request is on its way. The new source is the one
 * opened last from now on, and the reader goes to it; a refusal is said under the boxes and the keyboard goes back to the path.
 */
async function addSource() {
  const edit = state.sourceEdit;
  if (edit.adding) return;
  edit.adding = true;
  edit.addError = null;
  redrawSources();
  let source; // the new source, as the server answers
  try {
    source = await editSources('POST', '/api/sources', addSourceBody(edit.path, edit.name));
    if (typeof source?.id !== 'string' || source.id === '') throw new SyntaxError('the answer names no source');
  } catch (error) {
    edit.adding = false;
    edit.addError = sourceFailure(error, { file: state.sourcesFile });
    redrawSources();
    focusKept('path');
    return;
  }
  edit.adding = false;
  edit.path = '';
  edit.name = '';
  try {
    await loadSources();
  } catch {
    state.sources = [...state.sources.filter((entry) => entry.id !== source.id), source]; // the server has it, whatever it says now
  }
  rememberSource(source.id);
  if (state.route.view !== 'sources') return; // the reader has gone elsewhere meanwhile: the source is in the list, and that is all
  savePosition(); // the place on this page is where Back returns to
  window.location.hash = overviewHash(source.id);
}

// ---------------------------------------------------------------------------------------------
// The folder picker
// ---------------------------------------------------------------------------------------------

// "Choose a folder..." on the sources page opens a window of its own: above, the folders the server found by itself (Obsidian vaults and Claude
// configurations, `/api/browse/found`); below, a browser of the folders of the machine the server runs on (`/api/browse`), which starts where it
// was left in this tab (else in the home folder). Both only read; the folder that is chosen is added with the `POST /api/sources` that the form
// under the cards sends. The window is a modal dialog like the quick opener: the rest of the page is inert while it is open, Tab goes round inside
// it, and Escape closes it and gives the focus back to the button that opened it. The folders are a listbox (WAI-ARIA): the focus stays in the
// list and the row that Enter opens is named by `aria-activedescendant`. While a folder is being added nothing can be closed or chosen.

/** A drawn icon of the picker (`folder`, `vault`, `chevron`, `check`), a new node each time, from the template of index.html (the arrow of "up" is drawn in its button). */
function browseIcon(name) {
  return refs.browseIcons.content.querySelector(`[data-icon="${name}"]`).cloneNode(true);
}

/** What the picker was left with in this tab: `{ path, hidden }`. */
function readBrowseMemory() {
  try {
    return parseBrowseState(sessionStorage.getItem(BROWSE_KEY));
  } catch {
    return parseBrowseState(null);
  }
}

function rememberBrowse() {
  try {
    sessionStorage.setItem(BROWSE_KEY, browseStateJson({ path: state.browse.listing?.path ?? null, hidden: state.browse.hidden }));
  } catch {
    // Storage can be blocked; the picker then starts in the home folder the next time.
  }
}

function openBrowse() {
  if (!refs.browse.hidden || !state.sourcesAccess.canEdit) return;
  closeSourceMenu();
  setOverlay(null);
  const memory = readBrowseMemory();
  const browse = state.browse;
  Object.assign(browse, { listing: null, loading: false, waiting: false, hidden: memory.hidden, active: 0, adding: null, error: null });
  browse.found = { status: 'loading', folders: [], complete: true, all: false };
  refs.browseHidden.checked = browse.hidden;
  refs.browse.hidden = false;
  setPageInert(true);
  renderBrowse(); // the window has the focus: there is no list to take it yet, and it goes there once a folder with folders in it has come
  loadFound();
  loadFolder(memory.path, { restore: true });
}

/** The window's title names the computer whose folders it shows, where the server says which: a page opened from another device sees that computer's folders, not its own. */
function renderBrowseTitle() {
  const { machine } = state.host;
  refs.browseTitle.textContent = machine === null ? t('browse.title') : t('browse.titleOn', { machine });
}

/** `focus`: `'opener'` gives the focus back to the button that opened the picker (the page's content when that is gone), `'content'` takes it to the content, where the reader is going. */
function closeBrowse({ focus = 'opener' } = {}) {
  if (refs.browse.hidden) return;
  browseSeq += 1; // what is still on its way is no longer wanted
  foundSeq += 1;
  window.clearTimeout(browseWait);
  state.browse.loading = false;
  state.browse.waiting = false;
  refs.browse.hidden = true;
  setPageInert(false); // an inert element cannot take the focus back
  const opener = focus === 'opener' ? [...refs.content.querySelectorAll('[data-keep]')].find((node) => node.dataset.keep === 'browse') : undefined;
  if (opener && !opener.disabled) opener.focus({ preventScroll: true });
  else refs.content.focus({ preventScroll: true });
}

/** The folders the server found by itself. Not being able to say is no reason to stop the reader: the browser below works without them. */
async function loadFound() {
  const seq = ++foundSeq;
  let found;
  try {
    found = browseFound(await getJson('/api/browse/found'));
  } catch {
    if (seq !== foundSeq) return;
    state.browse.found = { status: 'failed', folders: [], complete: true, all: false };
    renderBrowseFound();
    return;
  }
  if (seq !== foundSeq) return;
  state.browse.found = { status: 'ready', folders: listedLast(found.folders), complete: found.complete, all: false };
  renderBrowseFound();
}

/**
 * Asks for the folders inside `path` (the home folder when it is null) and shows them. `restore`: the path is the one the tab was left in, and
 * a folder that is gone (a 404) or no folder (a 400) is not worth a message: the picker starts at the home folder instead. `reveal`: the folder to
 * select once the list is there (the one that was just left, going up). An answer is shown only if it is the last one asked for; one that fails
 * leaves the list that is there, and says why under it.
 */
async function loadFolder(path, { restore = false, reveal = null } = {}) {
  const browse = state.browse;
  const seq = ++browseSeq;
  browse.loading = true;
  browse.waiting = false;
  browse.error = null;
  window.clearTimeout(browseWait);
  browseWait = window.setTimeout(() => {
    if (seq !== browseSeq) return;
    browse.waiting = true;
    syncBrowseList();
  }, BROWSE_WAIT_MS);
  syncBrowseList();
  syncBrowseFoot();
  let listing;
  try {
    listing = browseListing(await getJson(browseUrl(path, { hidden: browse.hidden })));
    if (listing === null) throw new SyntaxError('the answer names no folder');
  } catch (error) {
    if (seq !== browseSeq) return;
    window.clearTimeout(browseWait);
    browse.loading = false;
    browse.waiting = false;
    if (restore && path !== null && error instanceof ApiError && (error.status === 400 || error.status === 404)) {
      await loadFolder(null);
      return;
    }
    const failure = sourceFailure(error, { file: state.sourcesFile });
    browse.error = { ...failure, retry: ['network', 'server', 'request', 'unexpected'].includes(failure.kind) ? { path } : null };
    syncBrowseList();
    syncBrowseFoot();
    return;
  }
  if (seq !== browseSeq) return;
  window.clearTimeout(browseWait);
  browse.loading = false;
  browse.waiting = false;
  browse.listing = listing;
  browse.active = Math.max(reveal === null ? -1 : listing.folders.findIndex((folder) => folder.path === reveal), 0);
  rememberBrowse();
  renderBrowse();
}

/** One level up: the folder that was just left is the one that is selected. */
function browseUp() {
  const listing = state.browse.listing;
  if (listing === null || listing.parent === null || state.browse.adding !== null) return;
  loadFolder(listing.parent, { reveal: listing.path });
}

function renderBrowse() {
  renderBrowseTitle();
  renderBrowseFound();
  renderBrowseNav();
  renderBrowseList();
  syncBrowseFoot();
  settleBrowseFocus();
}

/**
 * The keyboard stays in the picker. A control that was drawn again or locked takes the focus with it (a place of the path, the button that went up to
 * the root), and the window itself has it while there is no list: it goes to the list, or to the window when the list has no row, so that the keys
 * (Escape included) still reach the picker.
 */
function settleBrowseFocus() {
  const focused = document.activeElement;
  if (focused instanceof HTMLElement && refs.browseBox.contains(focused) && focused !== refs.browseBox && !focused.disabled) return;
  const rows = state.browse.listing !== null && state.browse.listing.folders.length > 0;
  (rows ? refs.browseList : refs.browseBox).focus({ preventScroll: true });
}

/** What is said of a folder in a row: what it is (a vault, a folder of notes, a Claude configuration) and what it holds. Empty for a folder that is none of those and has no Markdown. */
function browseMeta(folder) {
  const meta = el('span', 'browse-meta');
  if (folder.kind !== '') meta.append(el('span', 'browse-kind', t(`browse.kind.${folder.kind}`)));
  const holds = folderHolds(folder);
  if (holds !== null) {
    const notes = folder.kind !== 'Claude'; // a Claude configuration holds files, any other folder notes
    let words;
    if (holds.more) words = notes ? t('browse.count.moreNotes', { n: holds.n }) : t('browse.count.moreFiles', { n: holds.n });
    else words = tn(`source.count.${notes ? 'notes' : 'files'}`, holds.n);
    meta.append(el('span', 'browse-count', words));
  }
  return meta;
}

/** "In the list": a drawn check and the words; where a folder is the list's already. */
function browseListed(className) {
  const mark = el('span', className === undefined ? 'browse-listed' : `browse-listed ${className}`);
  mark.append(browseIcon('check'), t('browse.listed'));
  return mark;
}

/** A folder the server found: its mark, its name, its path (it gives up its upper folders first, so the last ones stay), what it is and holds, and the button that adds it, or that it is in the list. */
function foundRow(folder) {
  const row = el('li', 'browse-found-row');
  const icon = browseIcon('vault');
  icon.classList.add('browse-found-icon');
  const path = el('a', 'path-link browse-found-path'); // an anchor with no address: it is the page's way of cutting a path at its beginning (fitPathHeads) that is wanted
  path.title = folder.path;
  const { head, parent, name } = displayPath(folder.display); // a folder of the disk, never a memory file: it is `head`, `parent` and `name` always
  if (head) path.append(el('span', 'path-head', head));
  if (parent) path.append(el('span', 'path-parent', parent));
  path.append(el('span', 'path-name', name));
  const meta = browseMeta(folder);
  meta.classList.add('browse-found-meta');
  row.append(icon, el('strong', 'browse-found-name', folder.name), path, meta);
  if (folder.listed) {
    row.append(browseListed('browse-found-action'));
  } else {
    const add = el('button', 'button browse-found-add', t('sources.add.submit'));
    add.type = 'button';
    add.dataset.path = folder.path;
    add.setAttribute('aria-label', t('browse.add.named', { name: folder.name }));
    row.append(add);
  }
  return row;
}

/** The folders the server found, above the browser: not there at all when it found none (and finished), "searching" until they have come. */
function renderBrowseFound() {
  const { status, folders, complete, all } = state.browse.found;
  const partial = status === 'ready' && !complete;
  refs.browseFound.hidden = !(status === 'loading' || status === 'failed' || folders.length > 0 || partial);
  if (status === 'loading') refs.browseFoundStatus.textContent = t('browse.found.searching');
  else refs.browseFoundStatus.textContent = status === 'failed' ? t('browse.found.failed') : '';
  refs.browseFoundStatus.hidden = refs.browseFoundStatus.textContent === '';
  const rows = folders.map(foundRow);
  rows.forEach((row, index) => {
    row.hidden = !all && index >= FOUND_ROWS;
  });
  refs.browseFoundList.replaceChildren(...rows);
  const rest = folders.length - FOUND_ROWS;
  refs.browseFoundMore.hidden = rest <= 0;
  refs.browseFoundMore.textContent = all ? t('more.less') : t('more.show', { n: Math.max(rest, 0) });
  refs.browseFoundMore.setAttribute('aria-expanded', String(all));
  refs.browseFoundPartial.textContent = partial ? t('browse.found.partial') : '';
  refs.browseFoundPartial.hidden = !partial;
  syncBrowseFoot();
  scheduleFit(); // the paths of the rows give up their upper folders where they do not fit
}

/** The name a place of the path has for a screen reader, where its label is a sign (`~`, `/`). */
function crumbName(crumb) {
  if (crumb.kind === 'home') return t('browse.home');
  return crumb.kind === 'root' ? t('browse.root') : null;
}

/**
 * The path of the folder that is shown, one place to go to each (the last is where the reader is). It is scrolled to its end, so that the last folders
 * are the ones in view when the path is longer than the room (the place a cut path gives up is its beginning).
 */
function renderBrowseNav() {
  const listing = state.browse.listing;
  const crumbs = listing === null ? [] : browseCrumbs(listing.path, listing.home);
  const items = crumbs.map((crumb, index) => {
    const item = document.createElement('li');
    const name = crumbName(crumb);
    if (index > 0 && !(crumbs[index - 1].kind === 'root' && crumbs[index - 1].label === '/')) { // the root is a "/" already
      const separator = el('span', 'browse-sep', '/');
      separator.setAttribute('aria-hidden', 'true');
      item.append(separator);
    }
    if (index === crumbs.length - 1) {
      const here = el('span', 'browse-here');
      here.setAttribute('aria-current', 'location');
      if (name === null) {
        here.append(crumb.label);
      } else {
        const sign = el('span', null, crumb.label);
        sign.setAttribute('aria-hidden', 'true');
        here.append(sign, el('span', 'sr-only', name));
      }
      item.append(here);
    } else {
      const button = el('button', 'browse-crumb', crumb.label);
      button.type = 'button';
      button.dataset.path = crumb.path;
      button.dataset.reveal = crumbs[index + 1].path; // the folder on the way to the one that is shown is the one to select
      if (name !== null) button.setAttribute('aria-label', name);
      item.append(button);
    }
    return item;
  });
  refs.browseCrumbs.replaceChildren(...items);
  refs.browseCrumbs.title = listing === null ? '' : listing.display;
  refs.browseCrumbs.scrollLeft = refs.browseCrumbs.scrollWidth;
  refs.browseCrumbs.classList.toggle('is-cut', refs.browseCrumbs.scrollWidth > refs.browseCrumbs.clientWidth);
}

/** A row of the list of folders: it is entered with a click or Enter. The mark says what it is (a folder, or one this page reads), the words after the name what it holds. */
function browseRow(folder, index) {
  const row = el('li', 'browse-row');
  row.id = `browse-option-${index}`;
  row.setAttribute('role', 'option');
  row.setAttribute('aria-selected', 'false');
  row.dataset.path = folder.path;
  row.title = folder.path;
  row.append(browseIcon(folder.kind === '' ? 'folder' : 'vault'), el('span', 'browse-name', folder.name), browseMeta(folder));
  if (folder.listed) row.append(browseListed());
  row.append(browseIcon('chevron'));
  return row;
}

/** The list of folders, from the folder that has come. It is hidden while it has no row (an empty listbox is no listbox). */
function renderBrowseList() {
  const browse = state.browse;
  const folders = browse.listing === null ? [] : browse.listing.folders;
  refs.browseList.replaceChildren(...folders.map(browseRow));
  refs.browseList.hidden = folders.length === 0;
  const truncated = browse.listing !== null && browse.listing.truncated;
  refs.browseNote.textContent = truncated ? t('browse.truncated', { n: folders.length }) : '';
  refs.browseNote.hidden = !truncated;
  refs.browseList.scrollTop = 0;
  setBrowseActive(browse.active);
  syncBrowseList();
}

/** Selects a row (the ends do not wrap) and keeps it in view inside the list, never scrolling the page. */
function setBrowseActive(index, { scroll = true } = {}) {
  const options = [...refs.browseList.children];
  if (options.length === 0) {
    refs.browseList.removeAttribute('aria-activedescendant');
    return;
  }
  const next = Math.min(Math.max(index, 0), options.length - 1);
  state.browse.active = next;
  options.forEach((option, position) => option.setAttribute('aria-selected', String(position === next)));
  refs.browseList.setAttribute('aria-activedescendant', options[next].id);
  if (scroll) scrollWithin(refs.browseList, options[next], { block: 'nearest', margin: 4 });
}

/**
 * What the list says about itself while a folder is on its way and when it is there: how many folders it has, that it has none, or that it is loading
 * (at once when there is no list yet, after a moment when there is one: the old rows are then dimmed, and a quick answer does not flicker).
 */
function syncBrowseList() {
  const browse = state.browse;
  const count = browse.listing === null ? 0 : browse.listing.folders.length;
  const loading = browse.loading && (browse.waiting || browse.listing === null);
  refs.browseList.setAttribute('aria-busy', String(browse.loading));
  refs.browseList.classList.toggle('is-loading', loading && browse.listing !== null);
  if (loading) refs.browseStatus.textContent = t('browse.loading');
  else if (browse.listing === null) refs.browseStatus.textContent = '';
  else refs.browseStatus.textContent = count === 0 ? t('browse.empty') : tn('browse.count', count);
}

/** A refusal in words: the page's own for a network that is down (the page cannot refresh itself here), otherwise the words of the sources page. */
function browseFailureText(failure) {
  return failure.kind === 'network' ? t('browse.error.network') : failureText(failure);
}

/**
 * The bottom of the picker and what depends on a request being on its way: the button that adds the folder that is shown (it says why it cannot, when it
 * cannot), the line of what was refused, and the controls that a folder being added locks.
 */
function syncBrowseFoot() {
  const browse = state.browse;
  const listing = browse.listing;
  const adding = browse.adding !== null;
  const block = addBlock(listing, state.sources);
  refs.browseAdd.disabled = listing === null || block !== null || adding;
  refs.browseAdd.textContent = adding && !browse.adding.row ? t('sources.add.busy') : t('browse.add');
  if (block === 'home') refs.browseWhy.textContent = t('browse.why.home');
  else if (block === 'root') refs.browseWhy.textContent = t('browse.why.root');
  else refs.browseWhy.textContent = block === 'listed' ? t('sources.error.AlreadyListed') : '';
  refs.browseError.textContent = browse.error === null ? '' : browseFailureText(browse.error);
  refs.browseRetry.hidden = browse.error === null || !browse.error.retry;
  refs.browseCancel.disabled = adding;
  refs.browseClose.disabled = adding;
  refs.browseHidden.disabled = adding;
  refs.browseUp.disabled = listing === null || listing.parent === null || adding;
  refs.browseBox.classList.toggle('is-adding', adding);
  refs.browseBox.setAttribute('aria-busy', String(adding));
  for (const button of refs.browseFoundList.querySelectorAll('.browse-found-add')) {
    button.disabled = adding;
    button.textContent = adding && browse.adding.row && browse.adding.path === button.dataset.path ? t('sources.add.busy') : t('sources.add.submit');
  }
}

/**
 * Adds a folder to the list (`row`: it is one the server found, so the button of its row says it is busy, otherwise it is the folder that is shown).
 * It is the arrival of `addSource`: the list is read again, the new source is the one opened last, and the reader is taken to it; the picker is closed
 * first. A refusal is said inside the picker, which stays as it was, and the keyboard goes back to the button that was pressed.
 */
async function addFromBrowse(path, { row = false } = {}) {
  const browse = state.browse;
  if (browse.adding !== null) return;
  browse.adding = { path, row };
  browse.error = null;
  refs.browseBox.focus({ preventScroll: true }); // the button that was pressed is locked now: the keyboard stays in the window
  syncBrowseFoot();
  let source; // the new source, as the server answers
  try {
    source = await editSources('POST', '/api/sources', addSourceBody(path));
    if (typeof source?.id !== 'string' || source.id === '') throw new SyntaxError('the answer names no source');
  } catch (error) {
    browse.adding = null;
    browse.error = { ...sourceFailure(error, { file: state.sourcesFile }), retry: null };
    syncBrowseFoot();
    const again = row ? [...refs.browseFoundList.querySelectorAll('.browse-found-add')].find((button) => button.dataset.path === path) : refs.browseAdd;
    again?.focus({ preventScroll: true });
    return;
  }
  browse.adding = null;
  try {
    await loadSources();
  } catch {
    state.sources = [...state.sources.filter((entry) => entry.id !== source.id), source]; // the server has it, whatever it says now
  }
  rememberSource(source.id);
  closeBrowse({ focus: 'content' });
  if (state.route.view !== 'sources') return; // the reader has gone elsewhere meanwhile: the source is in the list, and that is all
  savePosition(); // the place on this page is where Back returns to
  window.location.hash = overviewHash(source.id);
}

/** Tab and Shift+Tab go round between the controls of the picker that can be reached: the buttons of the found folders, the path, the list, the buttons at the bottom. */
function trapBrowseFocus(event) {
  const stops = [...refs.browseBox.querySelectorAll('button, input, [tabindex="0"]')].filter((node) => !node.disabled && node.getClientRects().length > 0);
  const index = stops.indexOf(document.activeElement);
  if (stops.length === 0 || index === -1 || (event.shiftKey && index === 0) || (!event.shiftKey && index === stops.length - 1)) {
    event.preventDefault();
    stops[event.shiftKey ? stops.length - 1 : 0]?.focus();
  }
}

/** In the list: the arrows, Home, End and the page keys move the selection; Enter, Space and the right arrow enter the folder; the left arrow and Backspace go up. */
function onBrowseListKey(event) {
  const browse = state.browse;
  const folders = browse.listing === null ? [] : browse.listing.folders;
  const next = listboxIndex(event.key, browse.active, folders.length, BROWSE_PAGE);
  if (next !== null) {
    event.preventDefault();
    setBrowseActive(next);
  } else if (event.key === 'Enter' || event.key === ' ' || event.key === 'ArrowRight') {
    event.preventDefault();
    if (folders[browse.active] && !event.repeat) loadFolder(folders[browse.active].path); // a key that is held would go down folder after folder (and up, below)
  } else if (event.key === 'ArrowLeft' || event.key === 'Backspace') {
    event.preventDefault();
    if (!event.repeat) browseUp();
  }
}

function onBrowseKey(event) {
  if (event.isComposing) return;
  if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation(); // the drawers' own Escape is for when the picker is not open
    if (state.browse.adding === null) closeBrowse();
  } else if (event.key === 'Tab') {
    trapBrowseFocus(event);
  } else if (event.target === refs.browseList && state.browse.adding === null && !event.ctrlKey && !event.metaKey && !event.altKey) {
    onBrowseListKey(event); // with Alt (Back in the browser), Ctrl or Cmd a key is the browser's
  }
}

function bindBrowseEvents() {
  refs.browse.addEventListener('keydown', onBrowseKey);
  refs.browseClose.addEventListener('click', () => closeBrowse());
  refs.browseCancel.addEventListener('click', () => closeBrowse());
  refs.browseScrim.addEventListener('click', () => {
    if (state.browse.adding === null) closeBrowse();
  });
  refs.browseUp.addEventListener('click', browseUp);
  refs.browseRetry.addEventListener('click', () => {
    const retry = state.browse.error?.retry;
    if (retry) loadFolder(retry.path);
  });
  refs.browseHidden.addEventListener('change', () => {
    const browse = state.browse;
    browse.hidden = refs.browseHidden.checked;
    loadFolder(browse.listing === null ? null : browse.listing.path, { reveal: browse.listing?.folders[browse.active]?.path ?? null });
  });
  refs.browseAdd.addEventListener('click', () => {
    if (state.browse.listing !== null) addFromBrowse(state.browse.listing.path);
  });
  refs.browseCrumbs.addEventListener('click', (event) => {
    const crumb = event.target instanceof Element ? event.target.closest('.browse-crumb') : null;
    if (crumb && state.browse.adding === null) loadFolder(crumb.dataset.path, { reveal: crumb.dataset.reveal });
  });
  refs.browseFoundList.addEventListener('click', (event) => {
    const button = event.target instanceof Element ? event.target.closest('.browse-found-add') : null;
    if (button) addFromBrowse(button.dataset.path, { row: true });
  });
  refs.browseFoundList.addEventListener('scroll', syncFoundCut, { passive: true });
  refs.browseFoundMore.addEventListener('click', () => {
    state.browse.found.all = !state.browse.found.all;
    renderBrowseFound();
  });
  // A click on a row lands on the list (it takes the focus: it is the one stop that names the row); a row is entered by it.
  refs.browseList.addEventListener('click', (event) => {
    const option = event.target instanceof Element ? event.target.closest('.browse-row') : null;
    if (option && state.browse.adding === null) loadFolder(option.dataset.path);
  });
  refs.browseList.addEventListener('mousemove', (event) => {
    const option = event.target instanceof Element ? event.target.closest('.browse-row') : null;
    if (!option) return;
    const index = [...refs.browseList.children].indexOf(option);
    if (index !== state.browse.active) setBrowseActive(index, { scroll: false });
  });
}

// ---------------------------------------------------------------------------------------------
// Content: file view
// ---------------------------------------------------------------------------------------------

function filePage(file) {
  const page = el('div', 'page');
  const head = el('header', 'file-head');
  // How a file loads is a Claude configuration's; a note has its tags under its name instead.
  head.append(breadcrumb(file.path), ...(isNotes() ? tagList(file.tags) : [loadLine(file)]));
  page.append(head);

  // What is wrong with the frontmatter comes before the properties it is about.
  const problem = frontmatterProblem(file);
  if (problem) page.append(frontmatterNotice(problem));

  const properties = propertiesBlock(file.frontmatter);
  if (properties) page.append(properties);

  page.append(bodyArticle(file));
  return page;
}

/**
 * A frontmatter that has an error (`frontmatterProblem`): whether it was read in part or not at all, the line the parser stopped at and
 * that line as it is written (when the server says: text only, never markup), what usually causes it, and the parser's own words, quiet and
 * folded: they are English and about the parser, not about the file.
 */
function frontmatterNotice({ partial, line, text, message }) {
  const box = el('div', 'notice notice-warn fm-error');
  box.setAttribute('role', 'alert');
  box.append(el('p', 'fm-title', partial ? t('fm.partial') : t('fm.failed')));
  if (line !== null || text !== null) {
    const where = el('p', 'fm-line');
    if (line !== null) where.append(el('span', 'fm-lineno', t('fm.line', { line })));
    if (text !== null) where.append(' ', el('code', 'fm-code', text));
    box.append(where);
  }
  box.append(el('p', 'fm-hint', t('fm.hint')));
  const detail = el('details', 'fm-detail');
  detail.append(el('summary', null, t('fm.detail')), el('p', 'muted fm-raw', message));
  box.append(detail);
  return box;
}

/** The path of the file: the folders in it show themselves in the tree, the file is where the reader is. */
function breadcrumb(path) {
  const nav = el('nav', 'breadcrumb');
  nav.setAttribute('aria-label', t('file.path'));
  const parts = path.split('/');
  parts.forEach((part, index) => {
    if (index > 0) {
      const separator = el('span', 'crumb-sep', '/');
      separator.setAttribute('aria-hidden', 'true');
      nav.append(separator);
    }
    if (index === parts.length - 1) {
      const last = el('span', 'crumb crumb-last', shownName(part));
      last.setAttribute('aria-current', 'page');
      nav.append(last);
      return;
    }
    const dir = parts.slice(0, index + 1).join('/');
    const crumb = el('button', 'crumb crumb-dir', part);
    const label = index === 1 && parts[0] === 'projects' ? state.labels.get(part) : undefined;
    if (label) {
      const named = el('span', 'crumb-label');
      named.append(...projectName(label, { context: 'always' }));
      crumb.replaceChildren(named);
    }
    crumb.type = 'button';
    crumb.title = t('file.revealDir', { path: dir });
    crumb.addEventListener('click', () => revealDir(dir));
    nav.append(crumb);
  });
  return nav;
}

/** How the file is loaded and for how many tokens, in words and with its marker: no tooltip needed. */
function loadLine(file) {
  const line = el('p', 'load-line');
  line.title = t('tokens.hint');
  line.append(
    loadDot(file.loadMode, { decorative: true }),
    el('span', null, t('file.loadLine', {
      load: loadName(file.loadMode),
      tokens: formatTokens(loadedTokens(file.loadMode, file.tokens)),
    })),
  );
  // "güncellendi 10:42": the file changed while it was open, and when. It stays until another file is opened.
  if (state.stamp?.path === file.path) line.append(el('span', 'updated-at', t('file.updated', { time: clockText(state.stamp.at) })));
  return line;
}

/** The tags of a note under its name, each a chip that leads to the notes with that tag; nothing (no list) when it has none. */
function tagList(tags) {
  const names = Array.isArray(tags) ? tags.filter((tag) => typeof tag === 'string' && tag !== '') : [];
  if (names.length === 0) return [];
  const list = el('ul', 'tag-chips file-tags');
  list.setAttribute('aria-label', t('tags.of'));
  for (const name of names) {
    const item = document.createElement('li');
    item.append(tagChip(name));
    list.append(item);
  }
  return [list];
}

function isScalar(value) {
  return value === null || typeof value !== 'object';
}

function propertyValue(value) {
  if (value === null || value === undefined) return el('span', 'muted', '-');
  if (Array.isArray(value)) {
    if (value.every(isScalar)) {
      const chips = el('div', 'chips');
      for (const item of value) chips.append(el('span', 'chip', String(item ?? '')));
      return chips;
    }
    const list = el('ul', 'prop-list');
    for (const item of value) {
      const row = document.createElement('li');
      row.append(propertyValue(item));
      list.append(row);
    }
    return list;
  }
  if (typeof value === 'object') return propertyList(value);
  return el('span', 'prop-text', String(value));
}

function propertyList(object) {
  const list = el('dl', 'prop-grid');
  for (const [key, value] of Object.entries(object)) {
    const term = el('dt', null, key);
    const description = document.createElement('dd');
    description.append(propertyValue(value));
    list.append(term, description);
  }
  return list;
}

/** The properties of a file. A note's tags are the chips under its name already (see `tagList`), so they are not a row here as well. */
function propertiesBlock(frontmatter) {
  const shown = isNotes() ? propertiesWithoutTags(frontmatter) : frontmatter;
  if (!shown || typeof shown !== 'object' || Object.keys(shown).length === 0) return null;
  const block = el('details', 'properties');
  block.open = state.propertiesOpen;
  block.addEventListener('toggle', () => {
    state.propertiesOpen = block.open;
    saveUi();
  });
  block.append(el('summary', null, t('file.properties')), propertyList(shown));
  return block;
}

function bodyArticle(file) {
  const article = el('article', 'md');
  if (!markdown) {
    article.append(el('p', 'notice notice-warn', t('error.render')), el('pre', null, file.body ?? ''));
    return article;
  }
  // The one place where markup is assigned: markdown-it output, raw HTML disabled (html: false).
  article.innerHTML = markdown.render(file.body ?? '', { bodyStartLine: file.bodyStartLine ?? 1 });
  enhance(article, file);
  return article;
}

// ---------------------------------------------------------------------------------------------
// Content: DOM post-processing of the rendered Markdown (spec 7)
// ---------------------------------------------------------------------------------------------

function enhance(root, file) {
  const links = buildLinkMap(file.links);
  liftFenceLines(root);
  linkWikilinks(root, links);
  linkAnchors(root, links, file.path);
  linkInlineCode(root, links);
  linkClaudePaths(root, links);
  if (isNotes()) linkInlineTags(root, file.tags);
  assignHeadingIds(root);
  convertTaskItems(root);
  convertCallouts(root);
  labelImages(root);
  wrapTables(root);
}

/** markdown-it puts a fence's attributes on its <code>; the block the page moves to is the <pre> around it. */
function liftFenceLines(root) {
  for (const code of root.querySelectorAll('pre > code[data-line]')) {
    code.parentElement.dataset.line = code.dataset.line;
    code.removeAttribute('data-line');
  }
}

/**
 * An <a> for a resolved link, otherwise a <span>; both carry the status class and a tooltip. The class
 * is the link's status; the tooltip says what it is for the reader (a link to a folder is a folder).
 */
function makeLink(link, children, extraClass) {
  const href = linkHref(sid(), link);
  const node = document.createElement(href ? 'a' : 'span');
  const status = enumName(link.status);
  node.className = `md-link link-${status.toLowerCase()}${extraClass ? ` ${extraClass}` : ''}`;
  if (href) node.setAttribute('href', href);
  node.title = t(`tip.${displayStatus(link, state.folders)}`, { target: link.target ?? link.raw });
  node.append(...children);
  return node;
}

function linkWikilinks(root, links) {
  for (const anchor of root.querySelectorAll('a.wikilink')) {
    const raw = anchor.getAttribute('data-raw') ?? '';
    if (anchor.hasAttribute('data-embed')) {
      placeEmbed(anchor, raw, links);
      continue;
    }
    const link = findWikiLink(links, raw);
    if (link) anchor.replaceWith(makeLink(link, [...anchor.childNodes], 'wikilink'));
    else anchor.replaceWith(document.createTextNode(`[[${raw}]]`));
  }
}

/**
 * `![[x]]`, a note put into a note, is not shown inside this one: alone in its paragraph it is a card, between words a link in the line,
 * both to that note. A note that is not created yet says so; a picture or a file (the folder is not served) is a placeholder like an
 * image's. What the server did not report as an embed stays as it was written.
 */
function placeEmbed(anchor, raw, links) {
  const link = findWikiLink(links, raw, 'Embed');
  if (!link) {
    anchor.replaceWith(document.createTextNode(`![[${raw}]]`));
    return;
  }
  const name = embedName(raw) || raw.trim();
  if (enumName(link.status) === 'NonMarkdown') {
    const asset = el('span', 'md-image', embedAssetKind(link.target ?? name) === 'image' ? t('md.image', { alt: name }) : t('md.file', { name }));
    asset.title = link.target ?? raw;
    anchor.replaceWith(asset);
    return;
  }

  const icon = refs.embedIcon.content.firstElementChild.cloneNode(true);
  const paragraph = anchor.parentElement;
  const alone = paragraph !== null && paragraph.tagName === 'P'
    && [...paragraph.childNodes].every((node) => node === anchor || (node.nodeType === Node.TEXT_NODE && node.nodeValue.trim() === ''));
  if (!alone) {
    anchor.replaceWith(makeLink(link, [icon, name], 'embed-link'));
    return;
  }
  const status = displayStatus(link, state.folders);
  const kind = el('span', 'embed-kind', status === 'Resolved' ? t('embed.kind') : t(`status.${status}`));
  const card = makeLink(link, [icon, el('span', 'embed-name', name), kind], 'embed-card');
  if (paragraph.dataset.line) card.dataset.line = paragraph.dataset.line; // the block a line route moves to
  paragraph.replaceWith(card);
}

function linkAnchors(root, links, currentPath) {
  for (const anchor of root.querySelectorAll('a[href]')) {
    if (anchor.classList.contains('md-link')) continue;
    const href = anchor.getAttribute('href') ?? '';

    if (href.startsWith('#')) {
      // A heading link inside the same file.
      const heading = decodeHref(href.slice(1));
      if (heading) anchor.setAttribute('href', fileHash(sid(), currentPath, heading));
      else anchor.removeAttribute('href');
      continue;
    }

    const link = findMarkdownLink(links, href);
    if (link) {
      anchor.replaceWith(makeLink(link, [...anchor.childNodes]));
    } else if (/^https?:/i.test(href)) {
      anchor.classList.add('web-link');
      anchor.target = '_blank';
      anchor.rel = 'noopener noreferrer';
    } else if (!/^mailto:/i.test(href)) {
      // Not a link the server knows and not a web address: keep the text, drop the href.
      const inert = el('span', 'md-link link-inert');
      inert.append(...anchor.childNodes);
      anchor.replaceWith(inert);
    }
  }
}

function insideLink(node) {
  return node.closest('pre, a, .md-link') !== null;
}

function linkInlineCode(root, links) {
  for (const code of root.querySelectorAll('code')) {
    if (insideLink(code)) continue;
    const text = code.textContent.trim();
    if (!text) continue;
    const link = isRelativePath(text)
      ? links.get(linkKey('RelativePath', text))
      : links.get(linkKey('ClaudePath', text));
    if (!link) continue;
    const parent = code.parentNode;
    const next = code.nextSibling;
    parent.insertBefore(makeLink(link, [code]), next);
  }
}

function linkClaudePaths(root, links) {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const nodes = [];
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (node.nodeValue.includes('~/.claude/') && !insideLink(node.parentElement)) nodes.push(node);
  }
  for (const node of nodes) {
    const text = node.nodeValue;
    const parts = [];
    let last = 0;
    for (const match of findClaudePaths(text)) {
      const link = links.get(linkKey('ClaudePath', match.raw));
      if (!link) continue;
      if (match.index > last) parts.push(text.slice(last, match.index));
      parts.push(makeLink(link, [match.raw]));
      last = match.index + match.raw.length;
    }
    if (parts.length === 0) continue;
    if (last < text.length) parts.push(text.slice(last));
    node.replaceWith(...parts);
  }
}

/**
 * A `#tag` in the text of a note that is one of the note's tags (`tags`) is a chip like those under its name: a link to the notes that
 * carry it. Code, links and chips are left as they are.
 */
function linkInlineTags(root, tags) {
  if (!Array.isArray(tags) || tags.length === 0) return;
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const nodes = [];
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (node.nodeValue.includes('#') && node.parentElement?.closest('pre, code, a, .md-link, .tag-chip') === null) nodes.push(node);
  }
  for (const node of nodes) {
    const text = node.nodeValue;
    const parts = [];
    let last = 0;
    for (const match of findInlineTags(text, tags)) {
      if (match.index > last) parts.push(text.slice(last, match.index));
      parts.push(tagChip(match.tag, { label: match.raw }));
      last = match.index + match.raw.length;
    }
    if (parts.length === 0) continue;
    if (last < text.length) parts.push(text.slice(last));
    node.replaceWith(...parts);
  }
}

function assignHeadingIds(root) {
  const slug = createSlugger();
  for (const heading of root.querySelectorAll('h1, h2, h3, h4, h5, h6')) {
    const id = slug(heading.textContent);
    if (id) heading.id = id;
  }
}

function convertTaskItems(root) {
  for (const item of root.querySelectorAll('li')) {
    let host = item.firstChild;
    if (host && host.nodeName === 'P') host = host.firstChild;
    if (!host || host.nodeType !== Node.TEXT_NODE) continue;
    const match = /^\[([ xX])\][ \t]+/.exec(host.nodeValue);
    if (!match) continue;
    host.nodeValue = host.nodeValue.slice(match[0].length);
    const box = document.createElement('input');
    box.type = 'checkbox';
    box.disabled = true;
    box.checked = match[1] !== ' ';
    host.parentNode.insertBefore(box, host);
    item.classList.add('task-item');
  }
}

function convertCallouts(root) {
  for (const quote of root.querySelectorAll('blockquote')) {
    const first = quote.firstElementChild;
    if (!first || first.tagName !== 'P') continue;
    const head = first.firstChild;
    if (!head || head.nodeType !== Node.TEXT_NODE) continue;
    const marker = parseCalloutMarker(head.nodeValue);
    if (!marker) continue;

    // The title is whatever follows the marker on the same line (inline elements included).
    head.nodeValue = marker.rest;
    const title = el('span', 'callout-title-text');
    let node = head;
    while (node) {
      const next = node.nextSibling;
      if (node.nodeType === Node.TEXT_NODE) {
        const newline = node.nodeValue.indexOf('\n');
        if (newline >= 0) {
          title.append(node.nodeValue.slice(0, newline));
          node.nodeValue = node.nodeValue.slice(newline + 1);
          break;
        }
        title.append(node);
      } else if (node.nodeName === 'BR') {
        node.remove();
        break;
      } else {
        title.append(node);
      }
      node = next;
    }
    if (title.textContent.trim() === '') title.textContent = capitalize(marker.type);
    if (first.children.length === 0 && first.textContent.trim() === '') first.remove();

    const callout = el('div', `callout callout-${calloutClass(marker.type)}`);
    callout.dataset.callout = marker.type;
    if (quote.dataset.line) callout.dataset.line = quote.dataset.line; // the block a line route moves to
    callout.setAttribute('role', 'note');
    const titleRow = el('div', 'callout-title');
    titleRow.append(title);
    const content = el('div', 'callout-content');
    content.append(...quote.childNodes);
    callout.append(titleRow, content);
    quote.replaceWith(callout);
  }
}

function labelImages(root) {
  // markdown-it emits a placeholder instead of <img> (see imagePlaceholders in core.js).
  for (const placeholder of root.querySelectorAll('span.md-image')) {
    placeholder.textContent = t('md.image', { alt: placeholder.dataset.alt || placeholder.title });
  }
}

function wrapTables(root) {
  for (const table of root.querySelectorAll('table')) {
    const wrap = el('div', 'table-wrap');
    table.before(wrap);
    wrap.append(table);
  }
}

// ---------------------------------------------------------------------------------------------
// Info panel
// ---------------------------------------------------------------------------------------------

function infoSection(title) {
  const section = el('section', 'info-section');
  if (title) section.append(el('h2', null, title));
  return section;
}

function infoRow(list, label, valueNode) {
  const term = el('dt', null, label);
  const description = document.createElement('dd');
  description.append(valueNode);
  list.append(term, description);
}

/**
 * The panel starts at its top for a file that is not the one it showed, and stays where the reader had it for the same file (a live refresh,
 * a language change; Back and Forward put it back from the entry, see `restoreInfo`). Nothing is shown on the overview, and nothing
 * where a page failed to load: "open a file" would say the opposite of what the page says.
 */
function renderInfo() {
  const scroll = refs.info.scrollTop;
  if (state.route.view !== 'file' || state.error) {
    refs.info.replaceChildren(); // hidden on the overview (see updateLayoutMode)
    infoPath = null;
    return;
  }
  const file = state.file;
  if (!file) {
    refs.info.replaceChildren(el('p', 'muted pad', t('info.empty')));
    infoPath = null;
    return;
  }

  // The layer, how the file loads and its tokens are a Claude configuration's: a note has its links.
  const sections = [];
  if (!isNotes()) {
    const details = infoSection();
    const facts = el('dl', 'info-list');
    infoRow(facts, t('info.layer'), document.createTextNode(layerName(file.layer)));
    const load = el('span', 'with-dot');
    load.append(loadDot(file.loadMode, { decorative: true }), el('span', null, loadName(file.loadMode)));
    infoRow(facts, t('info.load'), load);
    details.append(facts);

    const tokens = infoSection(t('info.tokens'));
    tokens.querySelector('h2').title = t('tokens.hint');
    const counts = el('dl', 'info-list');
    infoRow(counts, t('info.tokens.total'), tokensText(file.tokens.total));
    infoRow(counts, t('info.tokens.everySession'), tokensText(file.tokens.everySession));
    infoRow(counts, t('info.tokens.projectSession'), tokensText(file.tokens.projectSession));
    tokens.append(counts);
    sections.push(details, tokens);
  }
  if (file.orphan) sections.push(el('p', 'notice notice-warn', t('info.orphan')));

  // Who links here comes first: it is what a reader of this file is asking, before what the file links to.
  sections.push(backlinkList(file.backlinks ?? []), outgoingLinks(file));
  refs.info.replaceChildren(...sections);
  refs.info.scrollTop = infoPath === file.path ? scroll : 0;
  infoPath = file.path;
  scheduleFit();
}

/**
 * The links of the file, in groups by status (problems first). Links to the same target are one row, with
 * the lines they are on ("line 15, 58"); each number leads to that line of this file.
 */
function outgoingLinks(file) {
  const links = file.links ?? [];
  const section = infoSection(t('info.links', { n: links.length }));
  if (links.length === 0) {
    section.append(el('p', 'muted', t('info.none')));
    return section;
  }
  const groups = new Map();
  for (const link of links) {
    const shown = displayStatus(link, state.folders);
    if (!groups.has(shown)) groups.set(shown, []);
    groups.get(shown).push(link);
  }
  for (const status of STATUS_ORDER) {
    const group = groups.get(status);
    if (!group) continue;

    const title = el('h3', 'group-title');
    title.append(el('span', `status-mark link-${status.toLowerCase()}`, t(`status.${status}`)),
      ' ', el('span', 'count', String(group.length)));

    const list = el('ul', 'link-list');
    for (const row of mergeLinks(group)) list.append(outgoingRow(row, status, file.path));
    section.append(title, list);
  }
  return section;
}

/** One target of the file and the lines that link to it. A target that opens is a path link; the rest is what was written. */
function outgoingRow({ label, link, lines }, status, path) {
  let name;
  if (linkHref(sid(), link)) {
    name = pathLink(link.target, { heading: link.heading, skillFirst: true });
  } else {
    name = el('span', `link-name md-link link-${enumName(link.status).toLowerCase()}`, label);
    name.title = `${t(`kind.${enumName(link.kind)}`)}: ${link.raw}\n${t(`tip.${status}`, { target: link.target ?? link.raw })}`;
  }
  const item = document.createElement('li');
  item.append(issueLine(name, ...(lines.length > 0 ? [lineNote(lines, (line) => fileHash(sid(), path, null, line))] : [])));
  return item;
}

function backlinkList(backlinks) {
  const section = infoSection(t('info.backlinks', { n: backlinks.length }));
  if (backlinks.length === 0) {
    section.append(el('p', 'muted', t('info.none')));
    return section;
  }
  const list = el('ul', 'link-list');
  for (const backlink of backlinks) {
    const item = document.createElement('li');
    item.append(issueLine(
      pathLink(backlink.source, { line: backlink.line, skillFirst: true }),
      lineNote([backlink.line]),
    ));
    // The line of that file which links here, as it is written: raw Markdown, so it is text (it may hold "<"). A wikilink in it is its words ("[[a|b]]" is "b").
    if (backlink.excerpt) {
      const words = readableWikilinks(backlink.excerpt);
      const excerpt = el('div', 'link-excerpt', words);
      excerpt.title = words;
      item.append(excerpt);
    }
    list.append(item);
  }
  section.append(list);
  return section;
}

// ---------------------------------------------------------------------------------------------
// Live changes: showing what changed
// ---------------------------------------------------------------------------------------------

// The server says which files appeared, changed and went with every `changed` event. The page shows it in four
// places and takes nothing from the reader while it does: no focus moves, #content is not scrolled, and every mark is
// a class (or a small badge that takes no room) that is gone after a few seconds, whether the motion is allowed or not.
// With motion a mark fades out over its time; without it (prefers-reduced-motion) it stays as it is, then goes.

let announceTimer = 0;

/**
 * Says something to a screen reader and does nothing else: one polite live region that is never replaced (a region
 * that is built together with its text, as the pages are, is not announced). Cleared first, so the same words twice
 * in a row are heard twice.
 */
function announce(text) {
  window.clearTimeout(announceTimer);
  refs.announce.textContent = '';
  announceTimer = window.setTimeout(() => {
    refs.announce.textContent = text;
    // Said once: it is not left behind for a reader who walks through the page later.
    announceTimer = window.setTimeout(() => {
      refs.announce.textContent = '';
    }, ANNOUNCE_MS);
  }, 60);
}

function clockText(date) {
  return date.toLocaleTimeString(getLang() === 'tr' ? 'tr-TR' : 'en-GB', { hour: '2-digit', minute: '2-digit' });
}

/** What a `changed` event says goes into the state before the tree is drawn again, so the new rows wear the marks. */
function noteChange({ added, changed, removed }) {
  const summary = summarizeChange({ added, changed, removed });
  if (!summary) return;
  const now = Date.now();
  state.lastChange = { ...summary, at: now };
  for (const path of removed) state.fresh.delete(path);
  for (const path of [...added, ...changed]) state.fresh.set(path, now);
  renderLastChange();
  sweepFresh();
}

// ---- Top bar: the last change ----------------------------------------------------------------

/** "son: CLAUDE.md +2 · 12 sn önce", a link to the file; written again every AGE_TICK_MS so the time keeps up. */
function renderLastChange() {
  const link = refs.lastChange;
  const change = state.lastChange;
  if (!change) {
    link.hidden = true;
    return;
  }
  const { unit, n } = ageParts(Date.now() - change.at);
  const age = t(`age.${unit}`, { n });
  const what = [el('span', 'last-label', t('last.label')), el('span', 'last-name', changeName(change.path, state.labels, { notes: isNotes() }))];
  if (change.kind === 'removed') what.push(el('span', 'last-extra', t('last.removed')));
  if (change.extra > 0) what.push(el('span', 'last-extra', `+${change.extra}`));

  link.replaceChildren(...what, el('span', 'last-age', age));
  // A row of flex items is read without the spaces between them: the link says its text itself.
  link.setAttribute('aria-label', `${what.map((part) => part.textContent).join(' ')} · ${age}`);
  link.title = change.paths.slice(0, 12).join('\n') + (change.paths.length > 12 ? '\n...' : '');
  // A file that is gone cannot be opened: its line is the news and nothing more.
  if (change.kind === 'removed') link.removeAttribute('href');
  else link.href = fileHash(sid(), change.path);
  link.hidden = false;
}

// ---- Tree: the rows that changed -------------------------------------------------------------

/**
 * Marks the tree rows of the files that changed a moment ago (`.fresh`, a tint that fades out, see app.css), and a
 * closed folder above one with a small dot (`.has-fresh`): its row is the only thing of the change that is on
 * screen. Safe to call again at any time (a tree that is drawn again is marked again, the mark carrying on from how
 * far it had faded: `animation-delay` is the time already gone).
 */
function applyFresh() {
  const now = Date.now();
  const files = new Map([...refs.sidebar.querySelectorAll('a.tree-row')].map((link) => [link.dataset.path, link]));
  const rows = new Map();
  const dots = new Set();
  for (const [path, at] of state.fresh) {
    const age = now - at;
    if (age >= FRESH_DOT_MS) continue;
    const row = files.get(path);
    if (row && age < FRESH_MS) rows.set(row, age);
    const closed = closedAncestor(path, state.openDirs);
    const dir = closed === null ? null : findDirItem(closed)?.firstElementChild;
    if (dir) dots.add(dir);
  }
  for (const node of refs.sidebar.querySelectorAll('.fresh')) {
    if (!rows.has(node)) node.classList.remove('fresh');
  }
  for (const node of refs.sidebar.querySelectorAll('.has-fresh')) {
    if (!dots.has(node)) node.classList.remove('has-fresh');
  }
  for (const [row, age] of rows) {
    if (row.classList.contains('fresh')) continue;
    row.style.animationDelay = `${-age}ms`;
    row.classList.add('fresh');
  }
  for (const dir of dots) dir.classList.add('has-fresh');
}

let freshTimer = 0;

/** Forgets what has been marked long enough and wakes up again when the next mark runs out. */
function sweepFresh() {
  window.clearTimeout(freshTimer);
  const now = Date.now();
  let next = Infinity;
  for (const [path, at] of state.fresh) {
    const age = now - at;
    if (age >= FRESH_DOT_MS) state.fresh.delete(path);
    else next = Math.min(next, age < FRESH_MS ? FRESH_MS - age : FRESH_DOT_MS - age);
  }
  applyFresh();
  if (next !== Infinity) freshTimer = window.setTimeout(sweepFresh, next + 30);
}

/** A folder the reader opens shows what changed in it: the rows under it that are still on the list are marked again. */
function remarkUnder(dir) {
  const now = Date.now();
  let any = false;
  for (const [path, at] of state.fresh) {
    if (path.startsWith(`${dir}/`) && now - at < FRESH_DOT_MS) {
      state.fresh.set(path, now);
      any = true;
    }
  }
  if (!any) return;
  for (const node of refs.sidebar.querySelectorAll('.fresh')) node.classList.remove('fresh');
  refs.sidebar.getBoundingClientRect(); // lets the fade start over on the rows that were marked
  sweepFresh();
}

// ---- Open file: what changed in it -----------------------------------------------------------

/**
 * The text of each block of a rendered file, in order, with the node it belongs to: every element that carries a
 * `data-line` (see `lineNumbers`), by the text that is its own, not its children's blocks'. A list item that holds
 * a list is its own words and the list's items are theirs; a table row is its cells.
 */
function collectBlocks(root) {
  const texts = new Map();
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const block = node.parentElement?.closest('[data-line]');
    if (block && root.contains(block)) texts.set(block, (texts.get(block) ?? '') + node.nodeValue);
  }
  return [...texts]
    .map(([node, text]) => ({ node, text: text.replace(/\s+/g, ' ').trim() }))
    .filter((block) => block.text !== '');
}

let blockTimer = 0;

function clearBlockMarks() {
  window.clearTimeout(blockTimer);
  blockTimer = 0;
  for (const node of refs.content.querySelectorAll('.live-new')) node.classList.remove('live-new');
}

/** Marks blocks as new for BLOCK_MS (`.live-new`); the same blocks marked again start the fade over. */
function markBlocks(nodes) {
  clearBlockMarks();
  const here = nodes.filter((node) => node.isConnected);
  if (here.length === 0) return;
  refs.content.getBoundingClientRect(); // lets the fade start over
  for (const node of here) node.classList.add('live-new');
  blockTimer = window.setTimeout(clearBlockMarks, BLOCK_MS);
}

// ---- The pill: a change that is off screen ---------------------------------------------------

let pillState = null; // { node, nodes, scroll } while the pill is up
let pillTimer = 0;

function hidePill() {
  window.clearTimeout(pillTimer);
  pillTimer = 0;
  pillState = null;
  refs.pill.hidden = true;
}

/** Centred on #content (the tree and the panel are beside it, not under it): over its bottom edge, or under its top. */
function positionPill() {
  const box = refs.content.getBoundingClientRect();
  const up = refs.pill.classList.contains('is-up');
  refs.pill.style.left = `${box.left + (box.width / 2)}px`;
  refs.pill.style.top = up ? `${box.top + 12}px` : 'auto';
  refs.pill.style.bottom = up ? 'auto' : `${window.innerHeight - box.bottom + 16}px`;
}

/**
 * When no changed block is on screen, a small pill says on which side they are ("change below"); a click takes the
 * reader to the nearest one. It goes when the reader scrolls (on their own) or after PILL_MS, and it never takes
 * the focus.
 */
function offerPill(nodes) {
  hidePill();
  if (nodes.length === 0) return;
  const view = refs.content.getBoundingClientRect();
  const target = pillTarget(nodes.map((node) => {
    const { top, bottom } = node.getBoundingClientRect();
    return { top, bottom };
  }), { top: view.top, bottom: view.bottom });
  if (target === null) return;
  pillState = { node: nodes[target.index], nodes, scroll: refs.content.scrollTop };
  refs.pill.classList.toggle('is-up', target.dir === 'up');
  refs.pillText.textContent = target.dir === 'up' ? t('pill.above') : t('pill.below');
  positionPill();
  refs.pill.hidden = false;
  pillTimer = window.setTimeout(hidePill, PILL_MS);
}

function usePill() {
  if (pillState === null) return;
  const { node, nodes } = pillState;
  hidePill();
  scrollWithin(refs.content, node, { block: 'start', margin: LINE_MARGIN });
  markBlocks(nodes); // the marks may have faded by now: show what the pill was about
  // The reader asked to go there, so the keyboard goes with them (the pill, which had the focus, is gone): as `scrollToLine` does.
  node.tabIndex = -1;
  node.focus({ preventScroll: true });
}

/** What the open file's refresh shows: the blocks that are new, and the pill when they are out of sight. */
function showFileChange(before) {
  const blocks = collectBlocks(refs.content);
  const found = before.length === 0 ? [] : changedBlocks(before.map((block) => block.text), blocks.map((block) => block.text));
  const nodes = found.map((index) => blocks[index].node);
  markBlocks(nodes);
  offerPill(nodes);
}

// ---- Overview: the numbers that changed ------------------------------------------------------

function deltaBadge(text) {
  const badge = el('span', 'delta', text);
  badge.setAttribute('aria-hidden', 'true'); // the words for a screen reader are `announce`'s
  return badge;
}

/**
 * A small badge beside each number of the overview that changed (the issue chips, what every session loads): a minus
 * sign and 1, "+120", for BADGE_MS; and the same in words for a screen reader ("Kırık bağlar 13 -> 12", see
 * `announce.count`). The badges take no room of their own (see app.css), so the page does not move.
 */
function showOverviewDiff(previous, next) {
  const diffs = overviewDiff(previous, next);
  if (diffs.length === 0) return;
  const badges = [];
  const words = [];
  for (const { key, from, to } of diffs) {
    const budget = key === 'budget';
    const host = budget ? refs.content.querySelector('.ov-head h1') : refs.content.querySelector(`.issue-chip[data-group="${key}"]`);
    if (host) {
      const badge = deltaBadge(formatDelta(to - from, { tokens: budget }));
      host.append(badge);
      badges.push(badge);
    }
    // The tokens are said as the whole numbers they are: two totals that differ by a dozen are both "~3.6K".
    words.push(budget
      ? t('announce.budget', { from: Math.round(from), to: Math.round(to) })
      : t('announce.count', { label: t(`issues.${key}`), from, to }));
  }
  window.setTimeout(() => {
    for (const badge of badges) badge.remove();
  }, BADGE_MS);
  announce(words.join('. '));
}

// ---------------------------------------------------------------------------------------------
// Live updates (server-sent events)
// ---------------------------------------------------------------------------------------------

function parseEvent(event) {
  try {
    const data = JSON.parse(event.data);
    return data && typeof data === 'object' ? data : {};
  } catch {
    return {};
  }
}

function pathsOf(list) {
  return (Array.isArray(list) ? list : [])
    .map((entry) => (typeof entry === 'string' ? entry : entry?.path))
    .filter((path) => typeof path === 'string');
}

/** The live connection of the open source. What it says is for that source only: an event that is still queued when the reader has gone elsewhere is dropped. */
function connectEvents() {
  window.clearTimeout(reconnectTimer);
  if (eventSource) eventSource.close();
  eventSource = null;
  eventsFor = null;
  if (state.source === null) return;
  refs.live.hidden = false;
  if (sourceUnavailable()) {
    // Not a lost connection (that is red, "offline"): there is none to lose. The indicator says what is the matter, in the neutral colour.
    markOnline();
    setLive('unavailable');
    return;
  }
  setLive('connecting');

  const id = state.source.id;
  const source = new EventSource(sourceUrl('events'));
  eventSource = source;
  eventsFor = id;
  const forThisSource = (task) => (event) => enqueue(() => (state.source !== null && state.source.id === id ? task(parseEvent(event)) : undefined));
  source.addEventListener('open', () => setLive('live'));
  source.addEventListener('error', () => {
    markOffline();
    setLive('offline');
    hadConnectionError = true;
    // The browser retries on its own after a network error; it gives up after an HTTP error.
    if (source.readyState === EventSource.CLOSED) {
      reconnectTimer = window.setTimeout(reconnectEvents, RECONNECT_MS);
    }
  });
  source.addEventListener('ready', forThisSource(onReady));
  source.addEventListener('changed', forThisSource(onChanged));
}

/**
 * The browser has given up on the live connection (an answer that was an error, not a network that was down). A source that was taken out
 * of the list answers 404 to it, and only the list says so: it is asked first. A source that is gone is left, and the address that names it
 * is answered by the sources page (see `refreshAll`); any other is connected to again.
 */
async function reconnectEvents() {
  const id = eventsFor;
  try {
    await loadSources();
  } catch {
    // The server does not answer: the connection is tried again below.
  }
  if (state.source === null || state.source.id !== id) return; // the reader has gone elsewhere meanwhile
  if (!state.sources.some((source) => source.id === id)) {
    await refreshAll();
    return;
  }
  connectEvents();
}

function enqueue(task) {
  refreshChain = refreshChain.then(task).catch((error) => console.error(error));
}

async function onReady(data) {
  const reconnected = hadConnectionError;
  hadConnectionError = false;
  // After a dropped connection the server may have restarted, and a restart resets the version.
  if (reconnected || data.version !== state.version) await refreshAll();
  markOnline(); // the page is told again what the folder has
}

async function onChanged(data) {
  const removed = pathsOf(data.removed);
  const added = pathsOf(data.added);
  const changed = pathsOf(data.changed);
  noteChange({ added, changed, removed });
  try {
    if (!(await loadTree())) return;
  } catch {
    return;
  }
  if (state.route.view === 'file') {
    const path = state.route.path;
    if (removed.includes(path)) {
      showToast(t('file.deleted', { path }));
      window.location.hash = overviewHash(sid());
      return;
    }
    // Reload the open file on every change: other files' changes can alter its links and backlinks.
    // It is "updated" (and its new blocks marked) only when it is the file that changed.
    await showFile(state.route, { keepScroll: !added.includes(path), live: added.includes(path) || changed.includes(path) });
  } else if (state.route.view === 'sources') {
    await showSources({ keepScroll: true });
  } else {
    await showOverview({ keepScroll: true, live: true });
  }
}

// ---------------------------------------------------------------------------------------------
// Quick opener (Ctrl+O, Ctrl+K, /)
// ---------------------------------------------------------------------------------------------

// Finds a file by its name, its path or its project, from the tree the page already has (nothing is asked of the server), and
// opens it. It is NOT a search of what the files say. A dialog with a combobox and a listbox (WAI-ARIA): the focus stays in the text
// field and the row that Enter opens is named by `aria-activedescendant`. The rest of the page is inert while it is open, so the focus
// cannot leave it, and Escape gives the focus back to what had it.

/** What the opener searches: one entry per file of the tree, made again when the tree is (the names of the projects change with it). */
function quickIndex() {
  if (paletteIndex.tree !== state.tree) {
    paletteIndex = { tree: state.tree, entries: state.tree ? quickEntries(flattenFiles(state.tree.nodes), state.labels, { notes: isNotes() }) : [] };
  }
  return paletteIndex.entries;
}

/** `{ items, total, recent }`: the files that answer what is typed, or the ones opened last when nothing is. */
function quickResults(query) {
  const entries = quickIndex();
  if (query.trim() === '') return { items: recentResults(entries, state.recent), total: 0, recent: true };
  return { ...quickSearch(entries, query, { recent: state.recent }), recent: false };
}

/** `text` with the characters at `indices` in `<mark>`: every piece is a text node, never markup (a name is not ours). */
function markedText(className, text, indices) {
  const node = el('span', className);
  for (const part of highlightParts(text, indices)) node.append(part.hit ? el('mark', null, part.text) : document.createTextNode(part.text));
  return node;
}

/** A row: how the file loads, its name, and where it is (its project or its folder) quietly. The name and the place are the row's name for a screen reader. */
function quickOption({ entry, nameMarks, contextMarks }, index) {
  const option = el('li', 'palette-option');
  option.id = `palette-option-${index}`;
  option.setAttribute('role', 'option');
  option.setAttribute('aria-selected', 'false');
  option.setAttribute('aria-label', entry.context ? `${entry.name}, ${entry.context}` : entry.name);
  option.dataset.path = entry.path;
  option.title = entry.path;
  if (!isNotes()) option.append(loadDot(entry.loadMode, { decorative: true }));
  option.append(markedText('palette-name', entry.name, nameMarks));
  if (entry.context) option.append(markedText('palette-context', entry.context, contextMarks));
  return option;
}

function renderPalette({ keepActive = false } = {}) {
  const { items, total, recent } = quickResults(refs.paletteInput.value);
  state.palette = { items, active: keepActive ? Math.min(state.palette.active, Math.max(0, items.length - 1)) : 0 };
  refs.paletteList.replaceChildren(...items.map(quickOption));
  refs.paletteList.hidden = items.length === 0;
  refs.paletteInput.setAttribute('aria-expanded', String(items.length > 0));

  // The line above the rows is also what the list is called, and a polite live region: "12 sonuç", or that there is none, is heard once typing pauses.
  if (recent) refs.paletteHead.textContent = items.length > 0 ? t('quick.recent') : '';
  else refs.paletteHead.textContent = total > 0 ? tn('quick.count', total) : t('quick.none');

  let note = '';
  if (recent && items.length === 0) note = t('quick.empty');
  else if (!recent && total > items.length) note = t('quick.more', { n: total - items.length });
  refs.paletteNote.textContent = note;
  refs.paletteNote.hidden = note === '';
  setPaletteActive(state.palette.active);
}

/** Selects a row (the ends wrap round) and keeps it in view inside the list, never scrolling the page. */
function setPaletteActive(index) {
  const options = [...refs.paletteList.children];
  if (options.length === 0) {
    refs.paletteInput.removeAttribute('aria-activedescendant');
    return;
  }
  const next = (index + options.length) % options.length;
  state.palette.active = next;
  options.forEach((option, position) => option.setAttribute('aria-selected', String(position === next)));
  refs.paletteInput.setAttribute('aria-activedescendant', options[next].id);
  scrollWithin(refs.paletteList, options[next], { block: 'nearest', margin: 4 });
}

/** The rest of the page cannot be reached (by keyboard, pointer or screen reader) while the opener is open. */
function setPageInert(on) {
  for (const node of [refs.skip, refs.topbar, refs.stale, refs.layout]) node.inert = on;
}

function openPalette() {
  if (!refs.browse.hidden) return; // the folder picker has the page: the quick opener is for the file view behind it
  if (!refs.palette.hidden) {
    refs.paletteInput.focus();
    refs.paletteInput.select();
    return;
  }
  closeSourceMenu(); // the focus is back on the switch, which is where the opener goes back to
  const active = document.activeElement;
  paletteOpener = active instanceof HTMLElement && active !== document.body ? active : null;
  setOverlay(null);
  refs.palette.hidden = false;
  setPageInert(true);
  refs.paletteInput.value = '';
  renderPalette();
  refs.paletteInput.focus();
}

/** `focus`: `'opener'` gives the focus back to what had it (the page's content when that is gone), `'content'` takes it to the content, where a file is being opened. */
function closePalette({ focus = 'opener' } = {}) {
  if (refs.palette.hidden) return;
  refs.palette.hidden = true;
  setPageInert(false); // an inert element cannot take the focus back
  const opener = paletteOpener;
  paletteOpener = null;
  if (focus === 'opener' && opener !== null && opener.isConnected && !opener.disabled) opener.focus({ preventScroll: true });
  else refs.content.focus({ preventScroll: true });
}

function goToFile(path) {
  savePosition(); // the place of the page being left is what Back returns to (a link does this in a capture listener, a hash set from here does not)
  closePalette({ focus: 'content' });
  const hash = fileHash(sid(), path);
  if (window.location.hash === hash) onRoute({ fresh: true });
  else window.location.hash = hash;
}

/** Tab and Shift+Tab go round between the field and the close button. */
function trapPaletteFocus(event) {
  const stops = [refs.paletteInput, refs.paletteClose];
  const index = stops.indexOf(document.activeElement);
  if (index === -1 || (event.shiftKey && index === 0) || (!event.shiftKey && index === stops.length - 1)) {
    event.preventDefault();
    stops[event.shiftKey ? stops.length - 1 : 0].focus();
  }
}

function onPaletteKey(event) {
  if (event.isComposing) return;
  const inField = event.target === refs.paletteInput;
  if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation(); // the drawers' own Escape is for when the opener is not open
    closePalette();
  } else if (event.key === 'Tab') {
    trapPaletteFocus(event);
  } else if (inField && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
    event.preventDefault();
    setPaletteActive(state.palette.active + (event.key === 'ArrowDown' ? 1 : -1));
  } else if (inField && event.key === 'Enter') {
    event.preventDefault();
    const item = state.palette.items[state.palette.active];
    if (item) goToFile(item.entry.path);
  }
}

// ---------------------------------------------------------------------------------------------
// Boot
// ---------------------------------------------------------------------------------------------

function cacheRefs() {
  refs.sidebar = document.getElementById('sidebar');
  refs.content = document.getElementById('content');
  refs.info = document.getElementById('info');
  refs.sourceHost = document.getElementById('source-host');
  refs.sourceSwitch = document.getElementById('source-switch');
  refs.sourceName = document.getElementById('source-name');
  refs.sourceMenu = document.getElementById('source-menu');
  refs.sourceMenuList = document.getElementById('source-menu-list');
  refs.sourceManage = document.getElementById('source-manage');
  refs.brand = document.querySelector('.brand');
  refs.overviewLink = document.getElementById('overview-link');
  refs.live = document.getElementById('live');
  refs.liveText = document.getElementById('live-text');
  refs.treeToggle = document.getElementById('tree-toggle');
  refs.infoToggle = document.getElementById('info-toggle');
  refs.scrim = document.getElementById('scrim');
  refs.skip = document.getElementById('skip-link');
  refs.toasts = document.getElementById('toasts');
  refs.lastChange = document.getElementById('last-change');
  refs.pill = document.getElementById('change-pill');
  refs.pillText = document.getElementById('change-pill-text');
  refs.announce = document.getElementById('announce');
  refs.langButtons = [...document.querySelectorAll('button[data-lang]')];
  refs.topbar = document.querySelector('.topbar');
  refs.layout = document.querySelector('.layout');
  refs.stale = document.getElementById('stale');
  refs.staleText = document.getElementById('stale-text');
  refs.quickOpen = document.getElementById('quick-open');
  refs.treeHeadTemplate = document.getElementById('tree-head-template');
  refs.embedIcon = document.getElementById('embed-icon-template');
  refs.lockIcon = document.getElementById('lock-icon-template');
  refs.palette = document.getElementById('palette');
  refs.paletteScrim = document.getElementById('palette-scrim');
  refs.paletteInput = document.getElementById('palette-input');
  refs.paletteClose = document.getElementById('palette-close');
  refs.paletteHead = document.getElementById('palette-head');
  refs.paletteList = document.getElementById('palette-list');
  refs.paletteNote = document.getElementById('palette-note');
  refs.browseIcons = document.getElementById('browse-icons-template');
  refs.browse = document.getElementById('browse');
  refs.browseScrim = document.getElementById('browse-scrim');
  refs.browseBox = document.getElementById('browse-box');
  refs.browseTitle = document.getElementById('browse-title');
  refs.browseClose = document.getElementById('browse-close');
  refs.browseFound = document.getElementById('browse-found');
  refs.browseFoundStatus = document.getElementById('browse-found-status');
  refs.browseFoundList = document.getElementById('browse-found-list');
  refs.browseFoundMore = document.getElementById('browse-found-more');
  refs.browseFoundPartial = document.getElementById('browse-found-partial');
  refs.browseCrumbs = document.getElementById('browse-crumbs');
  refs.browseUp = document.getElementById('browse-up');
  refs.browseStatus = document.getElementById('browse-status');
  refs.browseHidden = document.getElementById('browse-hidden');
  refs.browseList = document.getElementById('browse-list');
  refs.browseNote = document.getElementById('browse-note');
  refs.browseError = document.getElementById('browse-error');
  refs.browseRetry = document.getElementById('browse-retry');
  refs.browseWhy = document.getElementById('browse-why');
  refs.browseCancel = document.getElementById('browse-cancel');
  refs.browseAdd = document.getElementById('browse-add');
}

function bindEvents() {
  window.addEventListener('hashchange', () => {
    onRoute();
  });
  refs.content.addEventListener('scroll', schedulePositionSave, { passive: true });
  refs.sidebar.addEventListener('scroll', schedulePositionSave, { passive: true });
  refs.info.addEventListener('scroll', schedulePositionSave, { passive: true });
  window.addEventListener('pagehide', savePosition);
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') {
      savePosition();
    } else {
      renderLastChange(); // the time it says has gone on while nobody looked
      updateAges();
    }
  });
  // The pill is for a reader who has not moved: a scroll of their own ends it (the page's own, which puts a place back, is not one).
  refs.content.addEventListener('scroll', () => {
    if (pillState !== null && Math.abs(refs.content.scrollTop - pillState.scroll) > 4) hidePill();
  }, { passive: true });
  refs.pill.addEventListener('click', usePill);
  window.addEventListener('resize', () => {
    if (pillState !== null) positionPill();
  });
  // A path gives up its upper folders by the room it has: the page and the panel are measured when their size changes (a drawer, the window, the tree).
  if (typeof ResizeObserver === 'function') {
    const observer = new ResizeObserver(scheduleFit);
    observer.observe(refs.content);
    observer.observe(refs.info);
    observer.observe(refs.browseBox);
  }
  // A link that is followed leaves the entry the reader is on: its place is written before the address changes
  // (the scroll that is still waiting would otherwise be lost, and it is the place Back returns to).
  document.addEventListener('click', (event) => {
    if (event.target instanceof Element && event.target.closest('a[href^="#"]')) savePosition();
  }, true);
  refs.sidebar.addEventListener('click', (event) => {
    const button = event.target.closest('button.tree-row');
    if (button) toggleDir(button.parentElement);
    const tool = event.target.closest('button.tree-tool');
    if (tool) (tool.dataset.action === 'collapse' ? collapseAll : revealOpenFile)();
  });
  for (const button of refs.langButtons) {
    button.addEventListener('click', () => changeLanguage(button.dataset.lang));
  }
  refs.sourceSwitch.addEventListener('click', () => {
    if (sourceMenuOpen) closeSourceMenu();
    else openSourceMenu();
  });
  refs.sourceSwitch.addEventListener('keydown', (event) => {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
    event.preventDefault();
    openSourceMenu({ last: event.key === 'ArrowUp' });
  });
  refs.sourceMenu.addEventListener('keydown', onSourceMenuKey);
  // Choosing a source, or the page of sources, is going to a page: the menu closes and the focus goes with the reader.
  refs.sourceMenu.addEventListener('click', (event) => {
    if (event.target instanceof Element && event.target.closest('a[role="menuitem"]')) closeSourceMenu({ focus: 'content' });
  });
  document.addEventListener('pointerdown', (event) => {
    if (sourceMenuOpen && !(event.target instanceof Node && refs.sourceHost.contains(event.target))) closeSourceMenu({ focus: 'none' });
  });
  refs.treeToggle.addEventListener('click', () => setOverlay(overlay === 'tree' ? null : 'tree'));
  refs.infoToggle.addEventListener('click', () => setOverlay(overlay === 'info' ? null : 'info'));
  refs.scrim.addEventListener('click', () => setOverlay(null));
  // The first stop for the keyboard: past the top bar and the tree, to the page.
  refs.skip.addEventListener('click', () => refs.content.focus({ preventScroll: true }));
  // A link to the address the page already has fires no hashchange, so nothing would move: apply the route
  // again (the issue group, heading or line the reader has scrolled away from, the top of the overview).
  // Links of the page, the panel and the top bar only: a tree row is left as it is.
  document.addEventListener('click', (event) => {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    const link = event.target instanceof Element ? event.target.closest('a[href^="#/"]') : null;
    if (!link || !(refs.content.contains(link) || refs.info.contains(link) || link.closest('.topbar'))) return;
    if (link.getAttribute('href') !== window.location.hash) return;
    event.preventDefault();
    onRoute({ fresh: true });
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && overlay) setOverlay(null);
  });
  // The quick opener: Ctrl+O, Ctrl+K (Cmd on a Mac) and `/` outside a text field. The browser's own use of the key (the open-file dialog,
  // the address bar) is taken away, which is the point.
  document.addEventListener('keydown', (event) => {
    const editable = event.target instanceof Element && event.target.closest('input, textarea, select, [contenteditable]:not([contenteditable="false"])') !== null;
    if (!opensQuickOpen(event, { editable })) return;
    event.preventDefault();
    openPalette();
  });
  refs.quickOpen.addEventListener('click', openPalette);
  refs.palette.addEventListener('keydown', onPaletteKey);
  refs.paletteInput.addEventListener('input', () => renderPalette());
  refs.paletteClose.addEventListener('click', () => closePalette());
  refs.paletteScrim.addEventListener('click', () => closePalette());
  // A row is chosen without the field losing the focus (it is the one stop of the dialog that names the row).
  refs.paletteList.addEventListener('mousedown', (event) => event.preventDefault());
  refs.paletteList.addEventListener('click', (event) => {
    const option = event.target instanceof Element ? event.target.closest('.palette-option') : null;
    if (option) goToFile(option.dataset.path);
  });
  refs.paletteList.addEventListener('mousemove', (event) => {
    const option = event.target instanceof Element ? event.target.closest('.palette-option') : null;
    if (!option) return;
    const index = [...refs.paletteList.children].indexOf(option);
    if (index !== state.palette.active) setPaletteActive(index);
  });
  bindBrowseEvents();
  // Only #sidebar, #content and #info scroll; the document never does (its scroll events are the
  // only ones that reach window, element scrolls do not bubble). If something moves it anyway, put
  // it back, so the top bar cannot be left out of sight.
  window.addEventListener('scroll', () => {
    const root = document.scrollingElement;
    if (root && (root.scrollTop !== 0 || root.scrollLeft !== 0)) root.scrollTo(0, 0);
  });
}

async function boot() {
  cacheRefs();
  initLang();
  applyStaticI18n();
  // The browser would restore a scroll position of the document, which never scrolls: the page keeps the
  // reading place itself (see `savePosition`).
  if ('scrollRestoration' in history) history.scrollRestoration = 'manual';
  bindEvents();
  state.route = parseRoute(window.location.hash);
  updateLayoutMode();
  // A reload comes back to its own history entry, which has the place the reader was at. The live connection is made by the
  // page that opens a source (see `openRoute`): with no source open there is nothing to keep up to date.
  await loadFresh();
  window.setInterval(renderLastChange, AGE_TICK_MS);
  window.setInterval(updateAges, AGE_TICK_MS);
}

boot();
