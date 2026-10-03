// Unit tests for src/Pusula/wwwroot/js/core.js (run with: node --test "tests/web/*.test.mjs").
// No dependencies: Node's built-in test runner and the vendored markdown-it bundle only.
// All inputs are synthetic.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

import * as core from '../../src/Pusula/wwwroot/js/core.js';

const require = createRequire(import.meta.url);
const markdownit = require('../../src/Pusula/wwwroot/vendor/markdown-it/markdown-it.umd.min.js');

const md = core.createMarkdown(markdownit);

// ---------------------------------------------------------------------------------------------
// Helpers that re-implement the SERVER's extraction rules (spec 3.2) so the tests can check that
// the client derives the same `raw` values from markdown-it's output.
// ---------------------------------------------------------------------------------------------

/** Spec 3.2-1: blank out fenced blocks line by line. */
function maskFences(text) {
  let fence = null;
  return text.split('\n').map((line) => {
    if (fence === null) {
      const open = /^ {0,3}(`{3,}|~{3,})/.exec(line);
      if (!open) return line;
      fence = { char: open[1][0], length: open[1].length };
      return ' '.repeat(line.length);
    }
    const closing = new RegExp(`^ {0,3}\\${fence.char}{${fence.length},}\\s*$`);
    if (closing.test(line)) fence = null;
    return ' '.repeat(line.length);
  }).join('\n');
}

/** Spec 3.2-2: inline code spans of one line. Returns `{ text, spans }` (text with spans blanked). */
function maskCodeSpans(text) {
  const spans = [];
  const masked = text.split('\n').map((line) => line.replace(
    /(?<!`)(`+)(?!`)(.+?)(?<!`)\1(?!`)/g,
    (whole, ticks, content) => {
      spans.push(content.trim());
      return ' '.repeat(whole.length);
    },
  )).join('\n');
  return { text: masked, spans };
}

/** Spec 3.2-4: `MarkdownLink` raw values. */
function serverMarkdownRaws(source) {
  const { text } = maskCodeSpans(maskFences(source));
  const re = /(?<!!)\[(?:[^[\]\n]|\[[^[\]\n]*\])*\]\((?:<([^<>\n]*)>|([^()\s]+))(?:\s+(?:"[^"\n]*"|'[^'\n]*'))?\s*\)/g;
  const raws = [];
  for (const match of text.matchAll(re)) {
    const raw = match[1] ?? match[2];
    if (raw.startsWith('#')) continue;
    if (/^[A-Za-z][A-Za-z0-9+.-]*:/.test(raw) && !/^file:/i.test(raw)) continue;
    raws.push(raw);
  }
  return raws;
}

/** Spec 3.2-5: `WikiLink` raw values. */
function serverWikiRaws(source) {
  const { text } = maskCodeSpans(maskFences(source));
  return [...text.matchAll(/(?<![![])\[\[([^[\]\n]+?)\]\]/g)].map((match) => match[1]);
}

function unescapeHtml(text) {
  return text.replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&quot;', '"').replaceAll('&amp;', '&');
}

/** `data-raw` values of the wikilink placeholders in markdown-it's output, in order. */
function clientWikiRaws(source) {
  return [...md.render(source).matchAll(/<a class="wikilink" data-raw="([^"]*)"/g)]
    .map((match) => unescapeHtml(match[1]));
}

/** `data-raw` values of the embed placeholders (`![[x]]`) in markdown-it's output, in order. */
function clientEmbedRaws(source) {
  return [...md.render(source).matchAll(/<a class="wikilink" data-embed="1" data-raw="([^"]*)"/g)]
    .map((match) => unescapeHtml(match[1]));
}

/** `href` values of the anchors in markdown-it's output, in order. */
function clientHrefs(source) {
  return [...md.render(source).matchAll(/<a href="([^"]*)"/g)].map((match) => unescapeHtml(match[1]));
}

// ---------------------------------------------------------------------------------------------

describe('enumName', () => {
  it('uppercases the first letter and keeps PascalCase as is', () => {
    assert.equal(core.enumName('claudeMd'), 'ClaudeMd');
    assert.equal(core.enumName('ClaudeMd'), 'ClaudeMd');
  });

  it('returns an empty string for non-strings and empty strings', () => {
    assert.equal(core.enumName(undefined), '');
    assert.equal(core.enumName(null), '');
    assert.equal(core.enumName(''), '');
  });
});

describe('link keys', () => {
  it('linkKey joins kind and raw with a pipe', () => {
    assert.equal(core.linkKey('WikiLink', 'Note#Head|alias'), 'WikiLink|Note#Head|alias');
    assert.equal(core.linkKey('wikiLink', 'x'), 'WikiLink|x');
  });

  it('buildLinkMap keys links by Kind|Raw and the first occurrence wins', () => {
    const map = core.buildLinkMap([
      { kind: 'WikiLink', raw: 'a', line: 3, status: 'Resolved', target: 'a.md' },
      { kind: 'WikiLink', raw: 'a', line: 9, status: 'Broken' },
      { kind: 'ClaudePath', raw: '~/.claude/x.md', line: 4, status: 'Resolved', target: 'x.md' },
    ]);
    assert.equal(map.size, 2);
    assert.equal(map.get('WikiLink|a').line, 3);
    assert.equal(map.get('ClaudePath|~/.claude/x.md').target, 'x.md');
  });

  it('buildLinkMap tolerates a missing list', () => {
    assert.equal(core.buildLinkMap(undefined).size, 0);
  });
});

describe('href decoding', () => {
  it('decodeHref percent-decodes', () => {
    assert.equal(core.decodeHref('my%20file.md'), 'my file.md');
    assert.equal(core.decodeHref('%C3%A4.md'), 'ä.md');
    assert.equal(core.decodeHref('x.md#Some%20Heading'), 'x.md#Some Heading');
  });

  it('decodeHref returns the input when it is not valid percent-encoding', () => {
    assert.equal(core.decodeHref('100%.md'), '100%.md');
    assert.equal(core.decodeHref('%E0%A4%A'), '%E0%A4%A');
  });

  it('decodeHref does not treat + as a space', () => {
    assert.equal(core.decodeHref('a+b.md'), 'a+b.md');
  });

  it('markdownLinkKey is MarkdownLink|decode(href)', () => {
    assert.equal(core.markdownLinkKey('my%20file.md'), 'MarkdownLink|my file.md');
    assert.equal(core.markdownLinkKey('plain.md'), 'MarkdownLink|plain.md');
  });

  it('markdownLinkKeys lists the decoded key first, then the href as rendered', () => {
    assert.deepEqual(core.markdownLinkKeys('my%20file.md'), ['MarkdownLink|my file.md', 'MarkdownLink|my%20file.md']);
    assert.deepEqual(core.markdownLinkKeys('plain.md'), ['MarkdownLink|plain.md']);
  });

  it('findMarkdownLink finds a destination written with a space (<my file.md>)', () => {
    const map = core.buildLinkMap([{ kind: 'MarkdownLink', raw: 'my file.md', status: 'Resolved', target: 'my file.md' }]);
    assert.equal(core.findMarkdownLink(map, 'my%20file.md').target, 'my file.md');
  });

  it('findMarkdownLink finds a destination that was already percent-encoded in the source', () => {
    const map = core.buildLinkMap([{ kind: 'MarkdownLink', raw: 'my%20file.md', status: 'Resolved', target: 'my file.md' }]);
    assert.equal(core.findMarkdownLink(map, 'my%20file.md').target, 'my file.md');
  });

  it('findMarkdownLink finds a non-ASCII destination written literally', () => {
    const map = core.buildLinkMap([{ kind: 'MarkdownLink', raw: 'ä.md', status: 'Resolved', target: 'ä.md' }]);
    assert.ok(core.findMarkdownLink(map, '%C3%A4.md'));
  });

  it('findMarkdownLink finds a lone percent sign (markdown-it renders it as %25)', () => {
    const map = core.buildLinkMap([{ kind: 'MarkdownLink', raw: '100%.md', status: 'Broken' }]);
    assert.ok(core.findMarkdownLink(map, '100%25.md'));
  });

  it('findMarkdownLink returns undefined for an unknown href', () => {
    assert.equal(core.findMarkdownLink(new Map(), 'nope.md'), undefined);
  });
});

describe('findWikiLink', () => {
  const map = core.buildLinkMap([
    { kind: 'WikiLink', raw: 'Note#Head|alias', status: 'Resolved', target: 'n.md' },
    { kind: 'WikiLink', raw: 'a\\|b', status: 'Broken' },
  ]);

  it('finds a wikilink by the raw text', () => {
    assert.equal(core.findWikiLink(map, 'Note#Head|alias').target, 'n.md');
  });

  it('finds a wikilink whose escaped pipe lost its backslash in a table cell', () => {
    assert.equal(core.findWikiLink(map, 'a|b').status, 'Broken');
  });

  it('returns undefined for an unknown wikilink', () => {
    assert.equal(core.findWikiLink(map, 'nope'), undefined);
    assert.equal(core.findWikiLink(map, 'x|y'), undefined);
  });

  it('matches what markdown-it renders for [[a\\|b]] inside a table cell', () => {
    const source = '| x |\n|---|\n| [[a\\|b]] |';
    assert.deepEqual(serverWikiRaws(source), ['a\\|b']);
    assert.deepEqual(clientWikiRaws(source), ['a|b']);
    assert.equal(core.findWikiLink(map, clientWikiRaws(source)[0]).status, 'Broken');
  });
});

describe('MarkdownLink parity with the server (raw values)', () => {
  const corpus = [
    '[a](x.md)',
    '[a](<my file.md>)',
    '[a](my%20file.md)',
    '[a](ä.md)',
    '[a](%C3%A4.md)',
    '[a](x.md#Some%20Heading)',
    '[a](<x.md#Some Heading>)',
    '[a](../rules/x.md "title")',
    "[a](x.md 'title')",
    '[a](~/.claude/x.md)',
    '[a](/abs/x.md)',
    '[a](file:///home/user/x.md)',
    '[a](a/b/c.md) and [b](d.md)',
    '[link with `code`](x.md)',
    '[a **b**](x.md)',
    '`[a](in-code.md)` [b](out.md)',
    '```\n[a](fenced.md)\n```\n[b](after.md)',
    '![img](pic.png) [a](x.md)',
    '[a](http://example.com/x) [b](y.md)',
    '[a](#heading) [b](y.md)',
    '> [a](quoted.md)\n\n- [b](listed.md)',
    '| a |\n|---|\n| [b](cell.md) |',
    '[a](dir/) [b](100%25.md)',
  ];

  for (const source of corpus) {
    it(`finds the server's links through the rendered hrefs: ${JSON.stringify(source)}`, () => {
      const serverRaws = serverMarkdownRaws(source);
      const map = core.buildLinkMap(serverRaws.map((raw) => ({ kind: 'MarkdownLink', raw, status: 'Resolved', target: 't.md' })));
      const found = clientHrefs(source)
        .map((href) => core.findMarkdownLink(map, href))
        .filter(Boolean)
        .map((link) => link.raw);
      assert.deepEqual(found, serverRaws);
    });
  }
});

describe('ClaudePath', () => {
  const raws = (text) => core.findClaudePaths(text).map((found) => found.raw);

  it('matches a path and leaves the sentence period out', () => {
    assert.deepEqual(raws('See ~/.claude/rules/x.md.'), ['~/.claude/rules/x.md']);
  });

  it('leaves trailing punctuation out: , ; : and closing brackets', () => {
    assert.deepEqual(raws('~/.claude/a.md, ~/.claude/b.md; ~/.claude/c.md: (~/.claude/d.md)'), [
      '~/.claude/a.md', '~/.claude/b.md', '~/.claude/c.md', '~/.claude/d.md',
    ]);
  });

  it('stops at backticks, quotes, angle brackets, pipes and braces', () => {
    assert.deepEqual(raws('`~/.claude/skills/foo/SKILL.md`'), ['~/.claude/skills/foo/SKILL.md']);
    assert.deepEqual(raws('"~/.claude/a.md" \'~/.claude/b.md\''), ['~/.claude/a.md', '~/.claude/b.md']);
    assert.deepEqual(raws('| ~/.claude/a.md | x |'), ['~/.claude/a.md']);
    assert.deepEqual(raws('{~/.claude/a.md}'), ['~/.claude/a.md']);
  });

  it('stops at a placeholder: ~/.claude/rules/<name>.md gives ~/.claude/rules/', () => {
    assert.deepEqual(raws('~/.claude/rules/<name>.md'), ['~/.claude/rules/']);
  });

  it('stops at a glob: ~/.claude/skills/*/SKILL.md gives ~/.claude/skills/', () => {
    assert.deepEqual(raws('~/.claude/skills/*/SKILL.md'), ['~/.claude/skills/']);
  });

  it('stops at a shell variable: ~/.claude/skills/$s/SKILL.md gives ~/.claude/skills/', () => {
    assert.deepEqual(raws('~/.claude/skills/$s/SKILL.md'), ['~/.claude/skills/']);
    assert.deepEqual(raws('`~/.claude/skills/$s/SKILL.md`'), ['~/.claude/skills/']);
    assert.deepEqual(raws('~/.claude/skills/$s/SKILL.md and ~/.claude/rules/x.md'), [
      '~/.claude/skills/', '~/.claude/rules/x.md',
    ]);
  });

  it('never puts a dollar sign into a path, inside or at the end', () => {
    assert.deepEqual(raws('~/.claude/a/${name}.md'), ['~/.claude/a/']);
    assert.deepEqual(raws('~/.claude/rules/foo$bar.md'), ['~/.claude/rules/foo']);
    assert.deepEqual(raws('~/.claude/x.md$'), ['~/.claude/x.md']);
  });

  it('finds nothing when a dollar sign comes right after the prefix', () => {
    assert.deepEqual(raws('~/.claude/$s/x.md'), []);
    assert.deepEqual(raws('~/.claude/$'), []);
  });

  it('stops at emphasis markers', () => {
    assert.deepEqual(raws('**~/.claude/x.md**'), ['~/.claude/x.md']);
  });

  it('needs at least one character after ~/.claude/', () => {
    assert.deepEqual(raws('~/.claude/ and ~/.claude/.'), []);
    assert.deepEqual(raws('~/.claude'), []);
  });

  it('does not match a longer directory name', () => {
    assert.deepEqual(raws('~/.claudefoo/x.md'), []);
  });

  it('keeps an inner colon (only a trailing one is dropped)', () => {
    assert.deepEqual(raws('~/.claude/x.md:12'), ['~/.claude/x.md:12']);
    assert.deepEqual(raws('~/.claude/x.md:'), ['~/.claude/x.md']);
  });

  it('reports the position of each match', () => {
    assert.deepEqual(core.findClaudePaths('a ~/.claude/x.md b'), [{ index: 2, raw: '~/.claude/x.md' }]);
  });

  it('claudePathRegExp is the spec 3.2-6 pattern', () => {
    const spec = "~/\\.claude/[^\\s`'\"<>()\\[\\]{}*|,;$]*[^\\s`'\"<>()\\[\\]{}*|,;$.:]";
    assert.equal(core.claudePathRegExp().source, spec.replaceAll('/', '\\/'));
  });

  it('claudePathRegExp returns an independent global regex each time', () => {
    const a = core.claudePathRegExp();
    const b = core.claudePathRegExp();
    assert.notEqual(a, b);
    assert.equal(a.flags, 'g');
    a.lastIndex = 5;
    assert.equal(b.lastIndex, 0);
  });
});

describe('RelativePath', () => {
  it('accepts paths with at least one slash', () => {
    for (const text of ['rules/x.md', './rules/x.md', 'skills/foo/', 'a/b', 'src/Pusula/', 'İçerik/not_1.md', 'a.b/c-d/e']) {
      assert.equal(core.isRelativePath(text), true, text);
    }
  });

  it('rejects everything else', () => {
    for (const text of ['foo', 'foo.md', '~/x/y', '/etc/x', '../x/y', 'a b/c', 'a/b c', 'http://x/y', 'x/y:z', '', '   ']) {
      assert.equal(core.isRelativePath(text), false, JSON.stringify(text));
    }
  });

  it('trims the text first, like the server does for a code span', () => {
    assert.equal(core.isRelativePath('  rules/x.md '), true);
  });

  it('RELATIVE_PATH_RE is the spec pattern', () => {
    assert.equal(core.RELATIVE_PATH_RE.source, String.raw`^(?:\.\/)?(?:[\p{L}\p{N}_.\-]+\/)+[\p{L}\p{N}_.\-]*$`);
    assert.equal(core.RELATIVE_PATH_RE.flags, 'u');
  });
});

describe('parseWikilink', () => {
  it('plain target', () => {
    assert.deepEqual(core.parseWikilink('Note'), { target: 'Note', heading: null, alias: null, label: 'Note' });
  });

  it('target with heading', () => {
    assert.deepEqual(core.parseWikilink('Note#Some Heading'), {
      target: 'Note', heading: 'Some Heading', alias: null, label: 'Note#Some Heading',
    });
  });

  it('target with alias', () => {
    assert.deepEqual(core.parseWikilink('Note|the note'), {
      target: 'Note', heading: null, alias: 'the note', label: 'the note',
    });
  });

  it('target with heading and alias: the alias is the label', () => {
    assert.deepEqual(core.parseWikilink('Note#Head|alias'), {
      target: 'Note', heading: 'Head', alias: 'alias', label: 'alias',
    });
  });

  it('heading only (link inside the same file)', () => {
    assert.deepEqual(core.parseWikilink('#Head'), { target: '', heading: 'Head', alias: null, label: '#Head' });
  });

  it('the alias starts at the first pipe and may contain # and pipes', () => {
    const parsed = core.parseWikilink('a|b#c|d');
    assert.equal(parsed.target, 'a');
    assert.equal(parsed.heading, null);
    assert.equal(parsed.alias, 'b#c|d');
  });

  it('the heading starts at the first hash', () => {
    const parsed = core.parseWikilink('a#b#c');
    assert.equal(parsed.target, 'a');
    assert.equal(parsed.heading, 'b#c');
  });

  it('an empty heading or alias falls back to the target', () => {
    assert.equal(core.parseWikilink('Note#').label, 'Note');
    assert.equal(core.parseWikilink('Note|').label, 'Note');
  });

  it('never returns an empty label', () => {
    assert.equal(core.parseWikilink('|').label.length > 0, true);
    assert.equal(core.parseWikilink(' ').label, ' ');
  });
});

describe('wikilink inline rule (markdown-it output)', () => {
  it('renders a plain wikilink', () => {
    assert.equal(md.render('[[Note]]'), '<p><a class="wikilink" data-raw="Note">Note</a></p>\n');
  });

  it('data-raw is the inner text as written; the label is alias ?? target#heading ?? target', () => {
    assert.equal(
      md.render('See [[Note#Head|alias]].'),
      '<p>See <a class="wikilink" data-raw="Note#Head|alias">alias</a>.</p>\n',
    );
    assert.equal(md.render('[[Note#Head]]'), '<p><a class="wikilink" data-raw="Note#Head">Note#Head</a></p>\n');
    assert.equal(md.render('[[#Head]]'), '<p><a class="wikilink" data-raw="#Head">#Head</a></p>\n');
  });

  it('keeps spaces in data-raw exactly as written', () => {
    assert.match(md.render('[[ spaced name ]]'), /data-raw=" spaced name "/);
  });

  it('escapes HTML in data-raw and in the label', () => {
    assert.equal(
      md.render('[[A & B "q" <t>]]'),
      '<p><a class="wikilink" data-raw="A &amp; B &quot;q&quot; &lt;t&gt;">A &amp; B &quot;q&quot; &lt;t&gt;</a></p>\n',
    );
  });

  it('renders several wikilinks in one paragraph', () => {
    assert.deepEqual(clientWikiRaws('[[a]] and [[b#c]] and [[a]][[d]]'), ['a', 'b#c', 'a', 'd']);
  });

  it('an embed ![[x]] is its own placeholder (data-embed), and not a wikilink', () => {
    assert.equal(md.render('![[embed]]'), '<p><a class="wikilink" data-embed="1" data-raw="embed">embed</a></p>\n');
    assert.deepEqual(clientWikiRaws('![[embed]] and [[real]]'), ['real']);
    assert.deepEqual(clientEmbedRaws('![[embed]] and [[real]]'), ['embed']);
  });

  it('an embed keeps what is inside the brackets as data-raw, as the server reports it: heading, alias and all', () => {
    assert.deepEqual(clientEmbedRaws('![[Note#Head|alias]]'), ['Note#Head|alias']);
    assert.match(md.render('![[folder/Note#Head|alias]]'), />alias<\/a>/, 'the label is the alias');
    assert.match(md.render('![[a & "b"]]'), /data-raw="a &amp; &quot;b&quot;"/);
  });

  it('an embed is found in the same places a wikilink is, and in running text', () => {
    assert.deepEqual(clientEmbedRaws('see ![[a]] and ![[b]]'), ['a', 'b']);
    assert.deepEqual(clientEmbedRaws('- ![[a]]\n> ![[b]]'), ['a', 'b']);
    assert.deepEqual(clientEmbedRaws('| x |\n|---|\n| ![[a]] |'), ['a']);
  });

  it('empty, multi-line, unclosed and code embeds are left as text; an image is still an image', () => {
    assert.equal(md.render('![[]]'), '<p>![[]]</p>\n');
    assert.equal(md.render('![[a\nb]]'), '<p>![[a\nb]]</p>\n');
    assert.equal(md.render('![[a]'), '<p>![[a]</p>\n');
    assert.equal(md.render('`![[code]]`'), '<p><code>![[code]]</code></p>\n');
    assert.deepEqual(clientEmbedRaws('```\n![[fenced]]\n```'), []);
    assert.match(md.render('![alt](pic.png)'), /class="md-image"/);
    assert.deepEqual(clientEmbedRaws('![alt](pic.png)'), []);
  });

  it('nothing is made of an embed inside the label of a link: the link stays, the text is as written', () => {
    assert.equal(md.render('[see ![[x]]](y.md)'), '<p><a href="y.md">see ![[x]]</a></p>\n');
    assert.deepEqual(clientEmbedRaws('[see ![[x]]](y.md)'), []);
  });

  it('leaves triple brackets, empty bodies and multi-line bodies as text', () => {
    assert.equal(md.render('[[[x]]]'), '<p>[[[x]]]</p>\n');
    assert.equal(md.render('[[]]'), '<p>[[]]</p>\n');
    assert.equal(md.render('[[a\nb]]'), '<p>[[a\nb]]</p>\n');
    assert.equal(md.render('[[a] b]]'), '<p>[[a] b]]</p>\n');
  });

  it('does not touch inline code or fenced code', () => {
    assert.equal(md.render('`[[code]]`'), '<p><code>[[code]]</code></p>\n');
    assert.equal(md.render('```\n[[fenced]]\n```'), '<pre><code>[[fenced]]\n</code></pre>\n');
  });

  it('works in headings, lists, quotes, tables and emphasis', () => {
    assert.deepEqual(clientWikiRaws('# H [[a]]'), ['a']);
    assert.deepEqual(clientWikiRaws('- [[a]]'), ['a']);
    assert.deepEqual(clientWikiRaws('> [[a]]'), ['a']);
    assert.deepEqual(clientWikiRaws('| x |\n|---|\n| [[a]] |'), ['a']);
    assert.deepEqual(clientWikiRaws('**[[a]]** _[[b]]_'), ['a', 'b']);
  });

  it('a wikilink inside a link label breaks the outer link (links do not nest)', () => {
    const html = md.render('[see [[x]]](y.md)');
    assert.ok(html.includes('data-raw="x"'));
    assert.ok(!html.includes('href='));
  });

  const corpus = [
    'See [[Note]] and [[Other#Head|alias]].',
    '![[embed]] and [[real]]',
    '[[[triple]]]',
    '`[[in code]]` and [[out]]',
    '``[[double tick]]`` [[x]]',
    '```\n[[fenced]]\n```\n[[after]]',
    '~~~\n[[tilde]]\n~~~\n[[after]]',
    '[[unclosed\nlink]] [[ok]]',
    '[[]] [[ ]] [[a b]]',
    '> quote [[in quote]]',
    '- list [[in list]]',
    '| a | [[in table]] |\n|---|---|\n| 1 | 2 |',
    '# Heading [[in heading]]',
    '[[a]][[b]]',
    '[[a#b#c|d|e]]',
    '[[Ünicode Başlık]]',
    '[[a&b "q"]]',
    'text [[x]]\n\n[[y]]',
    '[[a]]](x)',
  ];

  for (const source of corpus) {
    it(`finds the same raw values as the server: ${JSON.stringify(source)}`, () => {
      assert.deepEqual(clientWikiRaws(source), serverWikiRaws(source));
    });
  }
});

describe('allowFileLinks', () => {
  it('renders a file: link that markdown-it alone refuses', () => {
    assert.equal(markdownit().render('[a](file:///x.md)'), '<p>[a](file:///x.md)</p>\n');
    assert.equal(md.render('[a](file:///home/user/x.md)'), '<p><a href="file:///home/user/x.md">a</a></p>\n');
  });

  it('still refuses javascript:, vbscript: and data: destinations', () => {
    assert.ok(!md.render('[a](javascript:alert(1))').includes('<a '));
    assert.ok(!md.render('[a](JaVaScRiPt:alert(1))').includes('<a '));
    assert.ok(!md.render('[a](vbscript:x)').includes('<a '));
    assert.ok(!md.render('[a](data:text/html;base64,AAAA)').includes('<a '));
  });

  it('does not allow raw HTML', () => {
    assert.equal(md.render('<script>alert(1)</script>'), '<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>\n');
    assert.equal(md.render('a <b onclick="x">b</b>'), '<p>a &lt;b onclick=&quot;x&quot;&gt;b&lt;/b&gt;</p>\n');
  });
});

describe('cspSafeAttrs', () => {
  it('turns table alignment into classes and emits no style attribute', () => {
    const html = md.render('| a | b | c |\n|:--|:-:|--:|\n| 1 | 2 | 3 |');
    assert.ok(!html.includes('style='));
    assert.ok(html.includes('<th class="align-left">a</th>'));
    assert.ok(html.includes('<th class="align-center">b</th>'));
    assert.ok(html.includes('<td class="align-right">3</td>'));
  });

  it('leaves tables without alignment alone', () => {
    const html = md.render('| a |\n|---|\n| 1 |');
    assert.ok(html.includes('<th>a</th>'));
  });
});

describe('imagePlaceholders', () => {
  it('renders a placeholder span, never an img element', () => {
    const html = md.render('![a logo](pics/logo.png)');
    assert.equal(html, '<p><span class="md-image" data-alt="a logo" title="pics/logo.png"></span></p>\n');
    assert.ok(!html.includes('<img'));
  });

  it('never creates an img for a remote or data image either', () => {
    assert.ok(!md.render('![x](https://example.com/x.png)').includes('<img'));
    assert.ok(!md.render('![x](data:image/png;base64,AAAA)').includes('<img'));
  });

  it('escapes the alt text and the source', () => {
    const html = md.render('![a "b" <c>](x&y.png)');
    assert.ok(html.includes('data-alt="a &quot;b&quot; &lt;c&gt;"'));
    assert.ok(html.includes('title="x&amp;y.png"'));
  });

  it('an image is not a MarkdownLink', () => {
    assert.deepEqual(serverMarkdownRaws('![a](x.png)'), []);
    assert.ok(!md.render('![a](x.png)').includes('<a '));
  });
});

describe('callouts', () => {
  it('parseCalloutMarker reads the type and the title', () => {
    assert.deepEqual(core.parseCalloutMarker('[!note] My title'), { type: 'note', fold: '', rest: 'My title' });
  });

  it('parseCalloutMarker lower-cases the type and accepts fold markers', () => {
    assert.deepEqual(core.parseCalloutMarker('[!Warning]- Careful'), { type: 'warning', fold: '-', rest: 'Careful' });
    assert.deepEqual(core.parseCalloutMarker('[!tip]+ Open'), { type: 'tip', fold: '+', rest: 'Open' });
  });

  it('parseCalloutMarker keeps what follows the first line in rest', () => {
    assert.deepEqual(core.parseCalloutMarker('[!note]\nbody'), { type: 'note', fold: '', rest: '\nbody' });
    assert.deepEqual(core.parseCalloutMarker('[!note] T\nbody'), { type: 'note', fold: '', rest: 'T\nbody' });
  });

  it('parseCalloutMarker rejects text that is not a marker', () => {
    for (const text of ['[!]', '[! note]', 'plain', ' [!note]', '[note]', '[!1x]']) {
      assert.equal(core.parseCalloutMarker(text), null, text);
    }
  });

  it('calloutClass maps aliases to a canonical type and unknown types to note', () => {
    assert.equal(core.calloutClass('summary'), 'abstract');
    assert.equal(core.calloutClass('tldr'), 'abstract');
    assert.equal(core.calloutClass('hint'), 'tip');
    assert.equal(core.calloutClass('important'), 'tip');
    assert.equal(core.calloutClass('check'), 'success');
    assert.equal(core.calloutClass('faq'), 'question');
    assert.equal(core.calloutClass('caution'), 'warning');
    assert.equal(core.calloutClass('missing'), 'failure');
    assert.equal(core.calloutClass('error'), 'danger');
    assert.equal(core.calloutClass('cite'), 'quote');
    assert.equal(core.calloutClass('WARNING'), 'warning');
    assert.equal(core.calloutClass('whatever'), 'note');
  });

  it('capitalize', () => {
    assert.equal(core.capitalize('warning'), 'Warning');
    assert.equal(core.capitalize(''), '');
  });

  it('markdown-it keeps the marker as the start of the first text, which is what the UI relies on', () => {
    assert.equal(md.render('> [!note] Title\n> body'), '<blockquote>\n<p>[!note] Title\nbody</p>\n</blockquote>\n');
  });
});

describe('slugify and createSlugger', () => {
  it('lower-cases, turns spaces into hyphens and drops other punctuation', () => {
    assert.equal(core.slugify('Hello, World!'), 'hello-world');
    assert.equal(core.slugify('  Some Heading  '), 'some-heading');
    assert.equal(core.slugify('a  b'), 'a--b');
  });

  it('keeps letters, digits, hyphens and underscores (any script)', () => {
    assert.equal(core.slugify('snake_case-and-2nd'), 'snake_case-and-2nd');
    assert.equal(core.slugify('Başlık Örneği'), 'başlık-örneği');
    assert.equal(core.slugify('日本語 見出し'), '日本語-見出し');
  });

  it('handles the Turkish dotted capital I without leaving a stray mark', () => {
    assert.equal(core.slugify('İçindekiler'), 'i̇çindekiler'.normalize('NFC').replace(/[^\p{L}\p{N}_-]/gu, ''));
  });

  it('drops symbols and returns an empty string when nothing is left', () => {
    assert.equal(core.slugify('`code` (x)'), 'code-x');
    assert.equal(core.slugify('***'), '');
  });

  it('a link heading and the heading it points at give the same slug', () => {
    assert.equal(core.slugify('Some Heading'), core.slugify('some heading'));
    assert.equal(core.slugify('my-heading'), 'my-heading');
  });

  it('createSlugger numbers repeats: x, x-2, x-3', () => {
    const slug = core.createSlugger();
    assert.deepEqual(['Intro', 'Intro', 'Other', 'intro'].map(slug), ['intro', 'intro-2', 'other', 'intro-3']);
  });

  it('createSlugger avoids a clash with an existing numbered slug', () => {
    const slug = core.createSlugger();
    assert.deepEqual(['a', 'a-2', 'a'].map(slug), ['a', 'a-2', 'a-3']);
  });

  it('createSlugger returns an empty string for an empty slug and keeps state per instance', () => {
    const one = core.createSlugger();
    const two = core.createSlugger();
    assert.equal(one('!!!'), '');
    assert.equal(one('x'), 'x');
    assert.equal(two('x'), 'x');
  });
});

describe('formatTokens', () => {
  it('shows small numbers as they are', () => {
    assert.equal(core.formatTokens(1), '~1');
    assert.equal(core.formatTokens(850), '~850');
    assert.equal(core.formatTokens(999), '~999');
  });

  it('shows nothing as a plain 0, never ~0', () => {
    assert.equal(core.formatTokens(0), '0');
    assert.equal(core.formatTokens(0.4), '0', 'rounds to nothing');
    assert.equal(core.formatTokens(0.6), '~1');
  });

  it('uses K with one decimal and drops a trailing .0', () => {
    assert.equal(core.formatTokens(1000), '~1K');
    assert.equal(core.formatTokens(1234), '~1.2K');
    assert.equal(core.formatTokens(2000), '~2K');
    assert.equal(core.formatTokens(12345), '~12.3K');
    assert.equal(core.formatTokens(999_949), '~999.9K');
  });

  it('rolls over to M instead of showing 1000K', () => {
    assert.equal(core.formatTokens(999_950), '~1M');
    assert.equal(core.formatTokens(1_200_000), '~1.2M');
  });

  it('rounds fractions and treats bad input as zero', () => {
    assert.equal(core.formatTokens(849.6), '~850');
    assert.equal(core.formatTokens(undefined), '0');
    assert.equal(core.formatTokens(Number.NaN), '0');
    assert.equal(core.formatTokens(-5), '0');
  });
});

describe('percent', () => {
  it('computes and clamps', () => {
    assert.equal(core.percent(25, 100), 25);
    assert.equal(core.percent(150, 100), 100);
    assert.equal(core.percent(1, 0), 0);
    assert.equal(core.percent(0, 100), 0);
    assert.equal(core.percent(-3, 100), 0);
  });
});

describe('formatPercent', () => {
  it('rounds to a whole percent', () => {
    assert.equal(core.formatPercent(28.4), '28%');
    assert.equal(core.formatPercent(28.5), '29%');
    assert.equal(core.formatPercent(100), '100%');
  });

  it('says <1% for a share that exists but rounds to nothing, and 0% only for none', () => {
    assert.equal(core.formatPercent(0.4), '<1%');
    assert.equal(core.formatPercent(0.99), '<1%');
    assert.equal(core.formatPercent(1), '1%');
    assert.equal(core.formatPercent(0), '0%');
    assert.equal(core.formatPercent(-2), '0%');
    assert.equal(core.formatPercent(Number.NaN), '0%');
    assert.equal(core.formatPercent(undefined), '0%');
  });

  it('agrees with percent() on real shares', () => {
    assert.equal(core.formatPercent(core.percent(93, 12365)), '<1%');
    assert.equal(core.formatPercent(core.percent(3495, 12365)), '28%');
  });
});

describe('routes', () => {
  it('fileHash puts the source first, then encodes the path, the heading and the line', () => {
    assert.equal(core.fileHash('claude', 'rules/a b.md'), '#/s/claude/f/rules%2Fa%20b.md');
    assert.equal(core.fileHash('claude', 'a.md', 'Some Heading'), '#/s/claude/f/a.md?h=Some%20Heading');
    assert.equal(core.fileHash('claude', 'a.md', ''), '#/s/claude/f/a.md');
    assert.equal(core.fileHash('claude', 'a.md', null), '#/s/claude/f/a.md');
    assert.equal(core.fileHash('claude', 'a.md', null, 78), '#/s/claude/f/a.md?l=78');
    assert.equal(core.fileHash('notes-2', 'a.md', 'H 1', 5), '#/s/notes-2/f/a.md?h=H%201&l=5');
  });

  it('fileHash leaves out a line that is not a positive whole number', () => {
    for (const line of [0, -3, 1.5, Number.NaN, undefined, null, '7']) {
      assert.equal(core.fileHash('claude', 'a.md', null, line), '#/s/claude/f/a.md', String(line));
    }
  });

  it('a page that has no source is addressed the old way (it is the page an old address stands for)', () => {
    for (const source of [null, undefined, '']) {
      assert.equal(core.fileHash(source, 'a.md', 'H', 3), '#/f/a.md?h=H&l=3', String(source));
      assert.equal(core.overviewHash(source), '#/', String(source));
      assert.equal(core.overviewHash(source, { issues: 'broken' }), '#/?issues=broken', String(source));
    }
  });

  it('overviewHash names the source, and the issue group, the memory table or the tag', () => {
    assert.equal(core.overviewHash('claude'), '#/s/claude/');
    assert.equal(core.overviewHash('claude', {}), '#/s/claude/');
    assert.equal(core.overviewHash('claude', { issues: 'broken' }), '#/s/claude/?issues=broken');
    assert.equal(core.overviewHash('claude', { memory: true }), '#/s/claude/?memory=1');
    assert.equal(core.overviewHash('notes', { tag: 'alan/alt' }), '#/s/notes/?tag=alan%2Falt');
    assert.equal(core.overviewHash('claude', { issues: 'pending', memory: true }), '#/s/claude/?issues=pending', 'an issue group wins');
    assert.equal(core.overviewHash('notes', { memory: true, tag: 'x' }), '#/s/notes/?memory=1', 'then the memory table');
  });

  it('SOURCES_HASH is the sources page', () => {
    assert.equal(core.SOURCES_HASH, '#/sources');
    assert.deepEqual(core.parseRoute(core.SOURCES_HASH), { view: 'sources', source: null });
  });

  const overview = (extra = {}) => ({ view: 'overview', source: null, issues: null, memory: false, tag: null, ...extra });
  const file = (extra = {}) => ({ view: 'file', source: null, path: 'a.md', heading: null, line: null, ...extra });

  it('parseRoute returns the overview of no source for #/, an empty hash and unknown routes', () => {
    for (const hash of ['', '#', '#/', '#/nope', '#/f/', '#/f', undefined, '#/s', '#/s/', '#/s//']) {
      assert.deepEqual(core.parseRoute(hash), overview(), String(hash));
    }
  });

  it('parseRoute reads the sources page, with or without a slash', () => {
    for (const hash of ['#/sources', '#/sources/', '/sources']) assert.deepEqual(core.parseRoute(hash), { view: 'sources', source: null }, hash);
    assert.deepEqual(core.parseRoute('#/sourcesx'), overview(), 'only the page itself');
    assert.deepEqual(core.parseRoute('#/s/sources'), overview({ source: 'sources' }), 'a source may be called sources');
  });

  it('parseRoute reads the source of a page of a source', () => {
    assert.deepEqual(core.parseRoute('#/s/claude/'), overview({ source: 'claude' }));
    assert.deepEqual(core.parseRoute('#/s/claude'), overview({ source: 'claude' }), 'the closing slash is not needed');
    assert.deepEqual(core.parseRoute('#/s/my-notes/f/a.md'), file({ source: 'my-notes' }));
    assert.deepEqual(core.parseRoute('#/s/claude/f/rules%2Fa.md?h=H&l=5'), file({ source: 'claude', path: 'rules/a.md', heading: 'H', line: 5 }));
  });

  it('parseRoute reads the issue group, the memory table and the tag of the overview', () => {
    for (const group of core.ISSUE_GROUPS) {
      assert.deepEqual(core.parseRoute(`#/s/c/?issues=${group}`), overview({ source: 'c', issues: group }), group);
      assert.deepEqual(core.parseRoute(core.overviewHash('c', { issues: group })), overview({ source: 'c', issues: group }), group);
    }
    assert.deepEqual(core.parseRoute('#/s/c/?memory=1'), overview({ source: 'c', memory: true }));
    assert.deepEqual(core.parseRoute(core.overviewHash('c', { memory: true })), overview({ source: 'c', memory: true }));
    assert.deepEqual(core.parseRoute('#/s/n/?tag=proje'), overview({ source: 'n', tag: 'proje' }));
    assert.deepEqual(core.parseRoute('#/s/n/?tag=%23proje'), overview({ source: 'n', tag: 'proje' }), 'a # in front is not part of the tag');
    assert.equal(core.parseRoute('#/s/n/?tag=%23').tag, null);
    assert.deepEqual(core.parseRoute(core.overviewHash('n', { tag: 'alan/alt ş' })), overview({ source: 'n', tag: 'alan/alt ş' }));
  });

  it('parseRoute reads the old addresses as pages that name no source', () => {
    assert.deepEqual(core.parseRoute('#/?issues=broken'), overview({ issues: 'broken' }));
    assert.deepEqual(core.parseRoute('#/?memory=1'), overview({ memory: true }));
    assert.deepEqual(core.parseRoute('#/f/rules%2Fa.md'), file({ path: 'rules/a.md' }));
    assert.deepEqual(core.parseRoute('#/f/a.md?h=Some%20Heading'), file({ heading: 'Some Heading' }));
    assert.deepEqual(core.parseRoute('#/f/a.md?l=78'), file({ line: 78 }));
    assert.deepEqual(core.parseRoute('#/f/a.md?h=H&l=5'), file({ heading: 'H', line: 5 }));
    assert.deepEqual(core.parseRoute('#/f/a.md?l=5&h=H'), file({ heading: 'H', line: 5 }));
  });

  it('parseRoute ignores an issue group it does not know and anything but memory=1', () => {
    for (const hash of ['#/s/c/?issues=', '#/s/c/?issues=nope', '#/s/c/?issues=BROKEN', '#/s/c/?issues=__proto__', '#/s/c/?issues=constructor']) {
      assert.deepEqual(core.parseRoute(hash), overview({ source: 'c' }), hash);
    }
    for (const hash of ['#/s/c/?memory', '#/s/c/?memory=0', '#/s/c/?memory=true', '#/s/c/?memory=']) {
      assert.equal(core.parseRoute(hash).memory, false, hash);
    }
    for (const hash of ['#/s/c/?tag', '#/s/c/?tag=', '#/s/c/?Tag=x']) assert.equal(core.parseRoute(hash).tag, null, hash);
  });

  it('parseRoute takes only a positive whole number as a line', () => {
    for (const value of ['0', '-4', '1.5', 'x', '', '12a', '0x10', '1e3', '99999999999']) {
      assert.equal(core.parseRoute(`#/s/c/f/a.md?l=${value}`).line, null, value);
    }
    assert.equal(core.parseRoute('#/s/c/f/a.md?l=000012').line, 12);
  });

  it('parseRoute inverts fileHash for awkward sources, paths, headings and lines', () => {
    const sources = [null, 'claude', 'my-notes-2'];
    const paths = ['a.md', 'rules/x y.md', 'projects/-home-u-p/memory/MEMORY.md', 'İçerik/ä #1 ?.md', 'a%20b.md', 'x&y=z.md', 'plus+sign.md', 'f/x.md', 'sources/s.md'];
    const headings = [null, 'Head', 'Some Heading', 'a&b=c', '100% sure', 'Ünicode #1?', 'x+y'];
    const lines = [null, 1, 78, 100000];
    for (const source of sources) {
      for (const path of paths) {
        for (const heading of headings) {
          for (const line of lines) {
            assert.deepEqual(core.parseRoute(core.fileHash(source, path, heading, line)), file({ source, path, heading, line }), `${source} ${path} ${heading} ${line}`);
          }
        }
      }
    }
  });

  it('parseRoute survives malformed percent-encoding', () => {
    assert.deepEqual(core.parseRoute('#/s/c/f/100%.md'), file({ source: 'c', path: '100%.md' }));
    assert.deepEqual(core.parseRoute('#/s/c/f/a.md?h=%E0%A4%A'), file({ source: 'c', path: 'a.md', heading: '%E0%A4%A' }));
    assert.deepEqual(core.parseRoute('#/s/%E0%A4%A/'), overview({ source: '%E0%A4%A' }));
  });

  it('parseRoute ignores other query keys, and the last h or l wins', () => {
    assert.deepEqual(core.parseRoute('#/s/c/f/a.md?x=1&h=H&y=2'), file({ source: 'c', heading: 'H' }));
    assert.deepEqual(core.parseRoute('#/s/c/f/a.md?h=A&h=B&l=1&l=2'), file({ source: 'c', heading: 'B', line: 2 }));
  });

  it('routeHash is what parseRoute reads: every kind of page, with and without a source', () => {
    const routes = [
      { view: 'sources', source: null },
      overview(), overview({ source: 'c' }), overview({ source: 'c', issues: 'orphans' }), overview({ source: 'c', memory: true }), overview({ source: 'n', tag: 'a/b c' }),
      file(), file({ source: 'c', path: 'rules/a b.md', heading: 'H 1', line: 5 }),
    ];
    for (const route of routes) assert.deepEqual(core.parseRoute(core.routeHash(route)), route, JSON.stringify(route));
    assert.equal(core.routeHash(overview({ source: 'c' })), '#/s/c/');
    assert.equal(core.routeHash(file({ source: 'c', path: 'a.md', line: 2 })), '#/s/c/f/a.md?l=2');
  });

  it('linkHref only links resolved links that have a target, in the source it is asked for', () => {
    assert.equal(core.linkHref('claude', { status: 'Resolved', target: 'rules/a.md' }), '#/s/claude/f/rules%2Fa.md');
    assert.equal(core.linkHref('notes', { status: 'Resolved', target: 'a.md', heading: 'H 1' }), '#/s/notes/f/a.md?h=H%201');
    assert.equal(core.linkHref('claude', { status: 'Resolved' }), null);
    for (const status of ['NonMarkdown', 'Broken', 'Pending', 'External']) {
      assert.equal(core.linkHref('claude', { status, target: 'a.md' }), null, status);
    }
  });

  it('isOpenable is the question linkHref asks, without a source', () => {
    assert.equal(core.isOpenable({ status: 'Resolved', target: 'a.md' }), true);
    assert.equal(core.isOpenable({ status: 'resolved', target: 'a.md' }), true, 'camelCase works');
    assert.equal(core.isOpenable({ status: 'Resolved' }), false);
    assert.equal(core.isOpenable({ status: 'Pending', target: 'a.md' }), false);
  });
});

describe('which source a page opens (chooseSource, resolveRoute)', () => {
  const source = (id, extra = {}) => ({ id, name: id, path: `/x/${id}`, profile: 'Claude', available: true, fileCount: 1, ...extra });
  const claude = source('claude');
  const notes = source('notes', { profile: 'Vault' });
  const gone = source('gone', { available: false, error: 'not found' });
  const list = [gone, claude, notes];

  it('chooseSource takes the one opened last when it can be read, otherwise the first that can', () => {
    assert.equal(core.chooseSource(list, 'notes').id, 'notes');
    assert.equal(core.chooseSource(list, 'claude').id, 'claude');
    assert.equal(core.chooseSource(list, null).id, 'claude', 'the first that can be read, not the first listed');
    assert.equal(core.chooseSource(list, 'nope').id, 'claude', 'a source that is not listed any more');
    assert.equal(core.chooseSource(list, 'gone').id, 'claude', 'one that cannot be read is not chosen, even when it was the last');
  });

  it('chooseSource finds nothing in nothing, or where nothing can be read', () => {
    assert.equal(core.chooseSource([], 'claude'), null);
    assert.equal(core.chooseSource([gone], null), null);
    for (const junk of [null, undefined, 'x', 7, {}]) assert.equal(core.chooseSource(junk, 'claude'), null, String(junk));
  });

  it('a source that does not say whether it can be read is read as readable', () => {
    assert.equal(core.chooseSource([{ id: 'a', name: 'a' }], null).id, 'a');
  });

  const parse = (hash) => core.parseRoute(hash);

  it('an address that names no source opens the same page in the source the page picks, and is written over', () => {
    const resolved = core.resolveRoute(parse('#/'), list, 'notes');
    assert.deepEqual(resolved, { route: { view: 'overview', source: 'notes', issues: null, memory: false, tag: null }, redirect: '#/s/notes/', missing: null });
    assert.deepEqual(core.resolveRoute(parse(''), list, null).redirect, '#/s/claude/');
  });

  it('an old file address keeps its path, heading and line under the source: a bookmark made before sources keeps working', () => {
    const resolved = core.resolveRoute(parse('#/f/rules%2Fa.md?h=Intro&l=12'), list, 'claude');
    assert.equal(resolved.redirect, '#/s/claude/f/rules%2Fa.md?h=Intro&l=12');
    assert.deepEqual(resolved.route, { view: 'file', source: 'claude', path: 'rules/a.md', heading: 'Intro', line: 12 });
    assert.equal(core.resolveRoute(parse('#/f/a.md'), list, 'notes').redirect, '#/s/notes/f/a.md');
  });

  it('an old overview address keeps the issue group or the memory table', () => {
    assert.equal(core.resolveRoute(parse('#/?issues=broken'), list, 'claude').redirect, '#/s/claude/?issues=broken');
    assert.equal(core.resolveRoute(parse('#/?memory=1'), list, 'claude').redirect, '#/s/claude/?memory=1');
  });

  it('with no source to pick the address is the sources page', () => {
    for (const sources of [[], [gone]]) {
      assert.deepEqual(core.resolveRoute(parse('#/'), sources, null), { route: { view: 'sources', source: null }, redirect: '#/sources', missing: null });
      assert.equal(core.resolveRoute(parse('#/f/a.md'), sources, null).redirect, '#/sources');
    }
  });

  it('a page of a source the list has is shown as it is, and the address stays', () => {
    for (const hash of ['#/s/claude/', '#/s/notes/f/a.md?l=3', '#/s/notes/?tag=x', '#/s/claude/?issues=pending']) {
      const resolved = core.resolveRoute(parse(hash), list, 'notes');
      assert.deepEqual(resolved, { route: parse(hash), redirect: null, missing: null }, hash);
    }
  });

  it('a source that cannot be read is still opened when the address names it: the page says why', () => {
    assert.deepEqual(core.resolveRoute(parse('#/s/gone/'), list, 'claude'), { route: parse('#/s/gone/'), redirect: null, missing: null });
  });

  it('a page of a source the list has not is the sources page, which says which one is missing', () => {
    const resolved = core.resolveRoute(parse('#/s/old-one/f/a.md'), list, 'claude');
    assert.deepEqual(resolved, { route: { view: 'sources', source: null }, redirect: '#/sources', missing: 'old-one' });
  });

  it('the sources page is the sources page', () => {
    assert.deepEqual(core.resolveRoute(parse('#/sources'), list, 'claude'), { route: { view: 'sources', source: null }, redirect: null, missing: null });
    assert.equal(core.resolveRoute(parse('#/sources'), [], null).redirect, null);
  });

  it('what a redirect writes is an address that resolves to itself (Back does not loop)', () => {
    for (const hash of ['#/', '#/f/a.md?l=2', '#/?issues=orphans', '']) {
      const first = core.resolveRoute(parse(hash), list, 'claude');
      const second = core.resolveRoute(parse(first.redirect), list, 'claude');
      assert.equal(second.redirect, null, hash);
      assert.deepEqual(second.route, first.route, hash);
    }
  });

  it('a list that is no list has no source', () => {
    for (const junk of [null, undefined, 'x']) assert.equal(core.resolveRoute(parse('#/'), junk, null).redirect, '#/sources', String(junk));
  });

  it('sourceKey keys a setting by the source', () => {
    assert.equal(core.sourceKey('pusula.openDirs', 'claude'), 'pusula.openDirs.claude');
    assert.notEqual(core.sourceKey('pusula.recent', 'a'), core.sourceKey('pusula.recent', 'b'));
  });
});

describe('tree helpers', () => {
  const nodes = [
    { name: 'rules', path: 'rules', type: 'Directory', fileCount: 2, tokens: 30, children: [
      { name: 'a.md', path: 'rules/a.md', type: 'File', layer: 'Rule', loadMode: 'EverySession', tokens: 10, brokenLinks: 0, orphan: false },
      { name: 'b.md', path: 'rules/b.md', type: 'File', layer: 'PathRule', loadMode: 'Conditional', tokens: 20, brokenLinks: 1, orphan: false },
    ] },
    { name: 'skills', path: 'skills', type: 'Directory', fileCount: 2, tokens: 9, children: [
      { name: 'x', path: 'skills/x', type: 'Directory', fileCount: 2, tokens: 9, children: [
        { name: 'SKILL.md', path: 'skills/x/SKILL.md', type: 'File', layer: 'Skill', loadMode: 'DescriptionEverySession', tokens: 5, brokenLinks: 0, orphan: false },
        { name: 'other.md', path: 'skills/x/other.md', type: 'File', layer: 'Skill', loadMode: 'UserInvoked', tokens: 4, brokenLinks: 0, orphan: false },
      ] },
    ] },
    { name: 'CLAUDE.md', path: 'CLAUDE.md', type: 'File', layer: 'ClaudeMd', loadMode: 'EverySession', tokens: 7, brokenLinks: 0, orphan: false },
  ];

  it('ancestorDirs lists the directories above a file', () => {
    assert.deepEqual(core.ancestorDirs('a/b/c.md'), ['a', 'a/b']);
    assert.deepEqual(core.ancestorDirs('c.md'), []);
    assert.deepEqual(core.ancestorDirs('projects/p/memory/MEMORY.md'), ['projects', 'projects/p', 'projects/p/memory']);
  });

  it('isDirectory reads the node type', () => {
    assert.equal(core.isDirectory(nodes[0]), true);
    assert.equal(core.isDirectory(nodes[2]), false);
    assert.equal(core.isDirectory({ type: 'directory' }), true);
  });

  it('flattenFiles returns every file in tree order', () => {
    assert.deepEqual(core.flattenFiles(nodes).map((file) => file.path), [
      'rules/a.md', 'rules/b.md', 'skills/x/SKILL.md', 'skills/x/other.md', 'CLAUDE.md',
    ]);
    assert.deepEqual(core.flattenFiles(undefined), []);
  });

  it('loadModesByLayer lists the modes of each layer in LOAD_MODES order with counts', () => {
    const modes = core.loadModesByLayer(core.flattenFiles(nodes));
    assert.deepEqual(modes.get('Skill'), [
      { mode: 'DescriptionEverySession', count: 1 },
      { mode: 'UserInvoked', count: 1 },
    ]);
    assert.deepEqual(modes.get('Rule'), [{ mode: 'EverySession', count: 1 }]);
    assert.deepEqual(modes.get('PathRule'), [{ mode: 'Conditional', count: 1 }]);
    assert.equal(modes.has('Agent'), false);
  });
});

describe('splitPath', () => {
  it('splits into upper folders, the folder next to the file, and the file name', () => {
    assert.deepEqual(core.splitPath('skills/synced/abc-def/docs/SKILL.md'), {
      head: 'skills/synced/abc-def/', parent: 'docs/', name: 'SKILL.md',
    });
    assert.deepEqual(core.splitPath('a/b/c.md'), { head: 'a/', parent: 'b/', name: 'c.md' });
  });

  it('a file in a top-level folder has no head', () => {
    assert.deepEqual(core.splitPath('rules/general.md'), { head: '', parent: 'rules/', name: 'general.md' });
  });

  it('a file in the root has only a name', () => {
    assert.deepEqual(core.splitPath('CLAUDE.md'), { head: '', parent: '', name: 'CLAUDE.md' });
  });

  it('the three parts always join back to the path', () => {
    for (const path of ['a.md', 'a/b.md', 'a/b/c.md', 'projects/-home-u-p/memory/MEMORY.md', 'İçerik/ä #1 ?.md', 'a//b.md']) {
      const { head, parent, name } = core.splitPath(path);
      assert.equal(head + parent + name, path, path);
    }
  });

  it('keeps a trailing slash in the parent when the path ends with a folder', () => {
    assert.deepEqual(core.splitPath('a/b/'), { head: 'a/', parent: 'b/', name: '' });
  });
});

describe('budgetParts (the pieces of the context budget band)', () => {
  const file = (path, layer, loadMode, everySessionTokens, extra = {}) => ({
    type: 'File', path, name: path.slice(path.lastIndexOf('/') + 1), layer, loadMode, tokens: everySessionTokens + 100, everySessionTokens, ...extra,
  });
  const files = [
    file('CLAUDE.md', 'ClaudeMd', 'EverySession', 3000),
    file('rules/general.md', 'Rule', 'EverySession', 2000),
    file('rules/style.md', 'Rule', 'EverySession', 500),
    file('rules/frontend.md', 'PathRule', 'Conditional', 0),
    file('output-styles/terse.md', 'OutputStyle', 'EverySession', 300),
    file('skills/a/SKILL.md', 'Skill', 'DescriptionEverySession', 40),
    file('skills/b/SKILL.md', 'Skill', 'DescriptionEverySession', 90),
    file('skills/c/SKILL.md', 'Skill', 'DescriptionEverySession', 70),
    file('skills/c/notes.md', 'SkillResource', 'OnDemand', 0),
    file('agents/reviewer.md', 'Agent', 'DescriptionEverySession', 60),
    file('commands/sync.md', 'Command', 'UserInvoked', 0),
    file('projects/p/memory/MEMORY.md', 'MemoryIndex', 'ProjectSession', 0),
  ];

  it('returns null when no file carries everySessionTokens, so the page can leave the band out', () => {
    assert.equal(core.budgetParts([]), null);
    assert.equal(core.budgetParts(undefined), null);
    assert.equal(core.budgetParts(null), null);
    assert.equal(core.budgetParts([{ path: 'a.md', layer: 'Rule', loadMode: 'EverySession', tokens: 10 }]), null);
    assert.equal(core.budgetParts([{ path: 'a.md', everySessionTokens: '10' }]), null, 'a string is not a count');
  });

  it('with the field present but nothing above zero there are no parts', () => {
    assert.deepEqual(core.budgetParts([file('a.md', 'Other', 'OnDemand', 0)]), { total: 0, parts: [] });
  });

  it('every file that loads every session is its own piece; files that load nothing are left out', () => {
    const { parts } = core.budgetParts(files);
    const singles = parts.filter((part) => part.kind === 'file').map((part) => part.path);
    assert.deepEqual(singles, ['CLAUDE.md', 'rules/general.md', 'rules/style.md', 'output-styles/terse.md']);
    assert.ok(!parts.some((part) => part.id.includes('frontend') || part.id.includes('MEMORY')));
  });

  it('the descriptions of one layer are one group: 3 skills are one piece, an agent another', () => {
    const { parts } = core.budgetParts(files);
    const groups = parts.filter((part) => part.kind === 'group');
    assert.deepEqual(groups.map((group) => [group.id, group.layer, group.count, group.tokens]), [
      ['group:Skill', 'Skill', 3, 200],
      ['group:Agent', 'Agent', 1, 60],
    ]);
    assert.deepEqual(groups[0].files.map((entry) => [entry.path, entry.tokens]), [
      ['skills/b/SKILL.md', 90], ['skills/c/SKILL.md', 70], ['skills/a/SKILL.md', 40],
    ], 'files biggest first');
    assert.deepEqual(groups[0].files[0], {
      path: 'skills/b/SKILL.md', name: 'SKILL.md', layer: 'Skill', loadMode: 'DescriptionEverySession', tokens: 90,
    });
  });

  it('orders whole files first, then the groups, each biggest first', () => {
    const { parts } = core.budgetParts(files);
    assert.deepEqual(parts.map((part) => part.id), [
      'file:CLAUDE.md', 'file:rules/general.md', 'file:rules/style.md', 'file:output-styles/terse.md', 'group:Skill', 'group:Agent',
    ]);
    assert.deepEqual(parts.map((part) => part.tokens), [3000, 2000, 500, 300, 200, 60]);
  });

  it('a tie is broken by path, so the order never flickers between refreshes', () => {
    const tied = [file('rules/b.md', 'Rule', 'EverySession', 10), file('rules/a.md', 'Rule', 'EverySession', 10)];
    assert.deepEqual(core.budgetParts(tied).parts.map((part) => part.path), ['rules/a.md', 'rules/b.md']);
    assert.deepEqual(core.budgetParts([...tied].reverse()).parts.map((part) => part.path), ['rules/a.md', 'rules/b.md']);
  });

  it('total is the sum of the parts and the shares add up to 100', () => {
    const budget = core.budgetParts(files);
    assert.equal(budget.total, 3000 + 2000 + 500 + 300 + 200 + 60);
    assert.ok(Math.abs(budget.parts.reduce((sum, part) => sum + part.percent, 0) - 100) < 1e-9);
    assert.ok(Math.abs(budget.parts[0].percent - (3000 / 6060) * 100) < 1e-9);
  });

  it('a part carries what the page needs: layer, load mode and tokens', () => {
    const [first] = core.budgetParts(files).parts;
    assert.equal(first.kind, 'file');
    assert.equal(first.layer, 'ClaudeMd');
    assert.equal(first.loadMode, 'EverySession');
    assert.equal(first.name, 'CLAUDE.md');
    assert.equal(first.tokens, 3000);
  });

  it('a file without the field counts as nothing when others have it', () => {
    const mixed = [file('a.md', 'Rule', 'EverySession', 7), { path: 'b.md', name: 'b.md', layer: 'Rule', loadMode: 'EverySession', tokens: 99 }];
    assert.deepEqual(core.budgetParts(mixed).parts.map((part) => part.path), ['a.md']);
  });

  it('groups descriptions of any layer by that layer, and tolerates camelCase enum names', () => {
    const odd = [
      file('x/one.md', 'Other', 'DescriptionEverySession', 5),
      file('x/two.md', 'Other', 'DescriptionEverySession', 6),
      { path: 'r.md', name: 'r.md', layer: 'rule', loadMode: 'everySession', tokens: 9, everySessionTokens: 9 },
    ];
    const { parts } = core.budgetParts(odd);
    assert.deepEqual(parts.map((part) => [part.id, part.layer, part.loadMode, part.tokens]), [
      ['file:r.md', 'Rule', 'EverySession', 9],
      ['group:Other', 'Other', 'DescriptionEverySession', 11],
    ]);
  });

  it('falls back to the last path segment when a file has no name', () => {
    const { parts } = core.budgetParts([{ path: 'rules/x.md', layer: 'Rule', loadMode: 'EverySession', everySessionTokens: 4 }]);
    assert.equal(parts[0].name, 'x.md');
  });

  it('works on the output of flattenFiles(tree)', () => {
    const tree = [
      { type: 'Directory', name: 'rules', path: 'rules', children: [file('rules/a.md', 'Rule', 'EverySession', 11)] },
      file('CLAUDE.md', 'ClaudeMd', 'EverySession', 22),
    ];
    assert.deepEqual(core.budgetParts(core.flattenFiles(tree)).parts.map((part) => part.path), ['CLAUDE.md', 'rules/a.md']);
  });
});

describe('skillFile', () => {
  it('names the skill first for a SKILL.md', () => {
    assert.deepEqual(core.skillFile('skills/proje-vault/SKILL.md'), { skill: 'proje-vault', file: 'SKILL.md' });
    assert.deepEqual(core.skillFile('skills/synced/abc-123/docs/SKILL.md'), { skill: 'docs', file: 'SKILL.md' });
    assert.deepEqual(core.skillFile('x/SKILL.md'), { skill: 'x', file: 'SKILL.md' });
  });

  it('is null for every other file', () => {
    for (const path of ['SKILL.md', 'skills/x/skill.md', 'skills/x/SKILL.md.bak', 'skills/x/references/ADR.md', 'rules/a.md', '', '/SKILL.md']) {
      assert.equal(core.skillFile(path), null, path);
    }
  });
});

describe('homeSlug', () => {
  it('is the slug of the parent of the folder being read', () => {
    assert.equal(core.homeSlug('/home/ana/.claude'), '-home-ana');
    assert.equal(core.homeSlug('/home/ana/.claude/'), '-home-ana');
    assert.equal(core.homeSlug('C:\\Users\\ana\\.claude'), 'C--Users-ana');
    assert.equal(core.homeSlug('/home/ana-maria/.claude'), '-home-ana-maria');
  });

  it('is empty when there is no parent to name', () => {
    for (const root of ['', '/', '/x', 'relative', undefined, null]) {
      assert.equal(core.homeSlug(root), '', String(root));
    }
  });
});

describe('splitSlug', () => {
  const split = (slug, home) => {
    const { prefix, name } = core.splitSlug(slug, home);
    assert.equal(prefix + name, slug, `${slug} must be rebuilt by prefix + name`);
    return [prefix, name];
  };
  const home = '-home-ana';

  it('after a conventional container folder the project name starts', () => {
    assert.deepEqual(split('-home-ana-Workspace-shop-api', home), ['-home-ana-Workspace-', 'shop-api']);
    assert.deepEqual(split('-home-ana-Workspace-shop-api'), ['-home-ana-Workspace-', 'shop-api'], 'also without knowing the home folder');
    assert.deepEqual(split('C--Workspace-blog-engine'), ['C--Workspace-', 'blog-engine']);
    assert.deepEqual(split('C--Users-Ana-Desktop-Notes'), ['C--Users-Ana-Desktop-', 'Notes']);
    assert.deepEqual(split('-mnt-data-storage-Archive'), ['-mnt-data-storage-', 'Archive']);
    assert.deepEqual(split('-home-bob-Documents-thesis'), ['-home-bob-Documents-', 'thesis']);
  });

  it('keeps hyphens inside the project name', () => {
    assert.deepEqual(split('-home-ana-Workspace-a-b-c-d', home), ['-home-ana-Workspace-', 'a-b-c-d']);
    assert.deepEqual(split('C--Workspace-sample-app'), ['C--Workspace-', 'sample-app']);
  });

  it('a folder straight under the home folder is told apart from the user name by the home slug', () => {
    assert.deepEqual(split('-home-ana-visual-lab', home), ['-home-ana-', 'visual-lab']);
    assert.deepEqual(split('-home-ana-Downloads', home), ['-home-ana-', 'Downloads']);
  });

  it('without the home slug, a folder under a home folder keeps the user in its name', () => {
    assert.deepEqual(split('-home-ana-visual-lab'), ['-home-', 'ana-visual-lab']);
  });

  it('a home folder itself is named by its user', () => {
    assert.deepEqual(split('-home-ana', home), ['-home-', 'ana']);
    assert.deepEqual(split('-home-ana-maria'), ['-home-', 'ana-maria']);
    assert.deepEqual(split('C--Users-Ana'), ['C--Users-', 'Ana']);
  });

  it('only the first container counts, and one that ends the slug is not a prefix', () => {
    assert.deepEqual(split('-home-ana-Workspace-my-Downloads-tool', home), ['-home-ana-Workspace-', 'my-Downloads-tool']);
    assert.deepEqual(split('-home-ana-Workspace', home), ['-home-ana-', 'Workspace']);
    assert.deepEqual(split('-home-ana-Workspace-', home), ['-home-ana-', 'Workspace-']);
  });

  it('a slug it cannot read is all name', () => {
    assert.deepEqual(split('plain'), ['', 'plain']);
    assert.deepEqual(split(''), ['', '']);
    assert.deepEqual(split('-srv-app'), ['', '-srv-app']);
  });

  it('the home slug of another machine does not confuse it', () => {
    assert.deepEqual(split('-home-bob-Workspace-api', home), ['-home-bob-Workspace-', 'api']);
    assert.deepEqual(split('-home-anabel-Workspace-api', home), ['-home-anabel-Workspace-', 'api'], 'a longer user name is not this home');
  });
});

describe('projectLabels (the names the project folders are shown by)', () => {
  const home = '-home-dev';
  const slugs = [
    'C--Workspace-alpha', '-home-dev-Workspace-alpha', '-home-dev-Workspace-beta-gamma', '-home-dev',
    'C--Users-tester', '-mnt-storage-ARCHIVE', '-tmp',
  ];
  const labels = core.projectLabels(slugs, home);
  const read = (slug, from = labels) => {
    const { name, context, shared } = from.get(slug);
    return { name, context, shared };
  };

  it('is one entry per distinct slug, in the order given, and `full` is the slug as it is on disk', () => {
    assert.deepEqual([...labels.keys()], slugs);
    for (const [slug, label] of labels) assert.equal(label.full, slug);
    assert.deepEqual([...core.projectLabels(['-tmp', '-tmp', '', null, 7, 'x']).keys()], ['-tmp', 'x']);
    assert.equal(core.projectLabels().size, 0);
    assert.equal(core.projectLabels(new Set(['-tmp'])).size, 1, 'any iterable will do');
  });

  it('the name is what follows the container folder (hyphens inside it stay), the context is where that folder is', () => {
    assert.deepEqual(read('-home-dev-Workspace-beta-gamma'), { name: 'beta-gamma', context: '~/Workspace', shared: false });
    assert.deepEqual(read('-mnt-storage-ARCHIVE'), { name: 'ARCHIVE', context: '/mnt/storage', shared: false });
    assert.deepEqual(read('C--Users-tester'), { name: 'tester', context: 'C:\\Users', shared: false });
  });

  it('a Windows path is written with its drive and backslashes', () => {
    const windows = core.projectLabels(['C--Users-tester-Desktop-notes', 'D--Workspace-alpha', 'C--Workspace-gamma-delta']);
    assert.deepEqual(read('C--Users-tester-Desktop-notes', windows), { name: 'notes', context: 'C:\\Users\\tester\\Desktop', shared: false });
    assert.deepEqual(read('D--Workspace-alpha', windows), { name: 'alpha', context: 'D:\\Workspace', shared: false });
    assert.deepEqual(read('C--Workspace-gamma-delta', windows), { name: 'gamma-delta', context: 'C:\\Workspace', shared: false });
  });

  it("this machine's home folder is ~, with the path it stands for; a folder straight under it has ~ as its place", () => {
    assert.deepEqual(read('-home-dev'), { name: '~', context: '/home/dev', shared: false });
    const under = core.projectLabels(['-home-dev-visual-lab', '-home-dev-Downloads', '-home-dev-Workspace-alpha'], home);
    assert.deepEqual(read('-home-dev-visual-lab', under), { name: 'visual-lab', context: '~', shared: false });
    assert.deepEqual(read('-home-dev-Downloads', under), { name: 'Downloads', context: '~', shared: false });
    assert.deepEqual(read('-home-dev-Workspace-alpha', under), { name: 'alpha', context: '~/Workspace', shared: false });
  });

  it('the home folder of a Windows machine, and a user name with a hyphen, are read the same way', () => {
    assert.deepEqual(read('C--Users-tester', core.projectLabels(['C--Users-tester'], 'C--Users-tester')), { name: '~', context: 'C:\\Users\\tester', shared: false });
    assert.deepEqual(read('-home-ana-maria', core.projectLabels(['-home-ana-maria'], '-home-ana-maria')), { name: '~', context: '/home/ana-maria', shared: false });
    const own = core.projectLabels(['-home-ana-maria-Workspace-alpha'], '-home-ana-maria');
    assert.deepEqual(read('-home-ana-maria-Workspace-alpha', own), { name: 'alpha', context: '~/Workspace', shared: false });
  });

  it("without the home slug, or in another user's home, the user is part of the place: ~user/Container (a hyphen in the user name stays)", () => {
    const other = core.projectLabels(['-home-dev-Workspace-alpha', '-home-sam-lee-Workspace-alpha', '-home-sam-lee-Documents-thesis'], '-home-ana');
    assert.deepEqual(read('-home-dev-Workspace-alpha', other), { name: 'alpha', context: '~dev/Workspace', shared: true });
    assert.equal(other.get('-home-sam-lee-Workspace-alpha').context, '~sam-lee/Workspace');
    assert.equal(other.get('-home-sam-lee-Documents-thesis').context, '~sam-lee/Documents');
  });

  it("another user's home folder itself is named by its user, as splitSlug does, and lives in /home", () => {
    assert.deepEqual(read('-home-sam-lee', core.projectLabels(['-home-sam-lee'], home)), { name: 'sam-lee', context: '/home', shared: false });
  });

  it('a slug of no known shape is the slug without its leading hyphen, and says nothing about where it is', () => {
    assert.deepEqual(read('-tmp'), { name: 'tmp', context: '', shared: false });
    const odd = core.projectLabels(['plain', '-srv-app', '-', '--'], home);
    assert.deepEqual([...odd.values()].map((label) => [label.name, label.context]), [['plain', ''], ['srv-app', ''], ['-', ''], ['--', '']]);
    const scratch = '-tmp-claude-1000--home-dev-0a1b2c3d-scratchpad-eval-cwd';
    assert.equal(core.projectLabels([scratch], home).get(scratch).name, 'tmp-claude-1000--home-dev-0a1b2c3d-scratchpad-eval-cwd');
  });

  it('a name that two folders have is shared, and each of them then has a context of its own to be told by', () => {
    assert.equal(read('C--Workspace-alpha').shared, true);
    assert.equal(read('-home-dev-Workspace-alpha').shared, true);
    assert.equal(read('-home-dev-Workspace-beta-gamma').shared, false);
    assert.equal(read('C--Workspace-alpha').context, 'C:\\Workspace');
    assert.equal(read('-home-dev-Workspace-alpha').context, '~/Workspace');

    const three = core.projectLabels(['C--Workspace-alpha', '-home-dev-Workspace-alpha', '-home-sam-lee-Workspace-alpha', 'D--Workspace-alpha'], home);
    assert.deepEqual([...three.values()].map((label) => label.context), ['C:\\Workspace', '~/Workspace', '~sam-lee/Workspace', 'D:\\Workspace']);
    assert.ok([...three.values()].every((label) => label.shared));
  });

  it('names are compared without regard to case: the same project typed two ways is one name', () => {
    const typed = core.projectLabels(['C--Workspace-Alpha', '-home-dev-Workspace-alpha'], home);
    assert.deepEqual([...typed.values()].map((label) => [label.name, label.shared]), [['Alpha', true], ['alpha', true]]);
  });

  it('when the places do not tell two folders apart, or there is none to read, the slug is the context', () => {
    const same = core.projectLabels(['-tmp', 'tmp']);
    assert.deepEqual([...same.values()].map((label) => [label.name, label.context, label.shared]), [['tmp', '-tmp', true], ['tmp', 'tmp', true]]);
    const none = core.projectLabels(['-tmp', 'C--Workspace-tmp']);
    assert.deepEqual([...none.values()].map((label) => label.context), ['-tmp', 'C:\\Workspace'], 'the folder with no place of its own is not left without one');
  });

  it('every shared label has a context, and no other label of its name has the same one', () => {
    const mixed = core.projectLabels([
      'C--Workspace-alpha', 'D--Workspace-alpha', '-home-dev-Workspace-alpha', '-home-dev-alpha', '-mnt-storage-alpha',
      '-tmp', 'tmp', 'C--Workspace-tmp', '-home-dev-Workspace-solo',
    ], home);
    const byName = new Map();
    for (const label of mixed.values()) byName.set(label.name.toLowerCase(), [...(byName.get(label.name.toLowerCase()) ?? []), label]);
    for (const group of byName.values()) {
      assert.equal(group.every((label) => label.shared), group.length > 1);
      if (group.length < 2) continue;
      const contexts = group.map((label) => label.context);
      assert.ok(contexts.every((context) => context !== ''), `${group[0].name}: a shared label without a context`);
      assert.equal(new Set(contexts).size, contexts.length, `${group[0].name}: two labels read the same`);
    }
  });
});

describe('projectFolders (the project slugs of a /api/tree response)', () => {
  const folder = (name, path, children = []) => ({ type: 'Directory', name, path, children });
  const nodes = [
    folder('projects', 'projects', [
      folder('C--Workspace-alpha', 'projects/C--Workspace-alpha', [folder('memory', 'projects/C--Workspace-alpha/memory')]),
      { type: 'File', name: 'stray.md', path: 'projects/stray.md' },
      folder('-tmp', 'projects/-tmp'),
    ]),
    folder('rules', 'rules', [folder('nested', 'rules/nested')]),
  ];

  it('is the folders directly under projects/, not their own folders and not files', () => {
    assert.deepEqual(core.projectFolders(nodes), ['C--Workspace-alpha', '-tmp']);
  });

  it('is empty when there is no projects folder, or no tree at all', () => {
    for (const none of [[], undefined, null, [nodes[1]], [folder('x', 'x', [folder('projects', 'x/projects', [folder('p', 'x/projects/p')])])]]) {
      assert.deepEqual(core.projectFolders(none), []);
    }
  });

  it('tolerates a trailing slash in the path and camelCase node types', () => {
    assert.deepEqual(core.projectFolders([{ type: 'directory', name: 'projects', path: 'projects/', children: [{ type: 'directory', name: 'a', path: 'projects/a/' }] }]), ['a']);
  });

  it('feeds projectLabels the way the page does: the real tree gives one label per folder', () => {
    assert.deepEqual([...core.projectLabels(core.projectFolders(nodes), '-home-dev').keys()], ['C--Workspace-alpha', '-tmp']);
  });
});

describe('displayPath (what a list shows for a path)', () => {
  const labels = core.projectLabels(['C--Workspace-alpha', '-home-dev-Workspace-alpha', '-home-dev-Workspace-beta'], '-home-dev');

  it('a memory file is its project and its file: the memory/ folder is left out', () => {
    const shown = core.displayPath('projects/C--Workspace-alpha/memory/feedback_x.md', labels);
    assert.equal(shown.kind, 'project');
    assert.equal(shown.project, labels.get('C--Workspace-alpha'), 'the entry of projectLabels itself');
    assert.equal(shown.file, 'feedback_x.md');
    assert.equal(core.displayPath('projects/-home-dev-Workspace-beta/memory/MEMORY.md', labels).project.name, 'beta');
  });

  it('folders under memory/ stay in the file', () => {
    assert.equal(core.displayPath('projects/C--Workspace-alpha/memory/sub/dir/x.md', labels).file, 'sub/dir/x.md');
  });

  it('any other path is a path, split as splitPath splits it', () => {
    for (const path of ['rules/a.md', 'skills/x/SKILL.md', 'CLAUDE.md', 'a/b/c/d.md', 'memory/x.md']) {
      assert.deepEqual(core.displayPath(path, labels), { kind: 'path', ...core.splitPath(path) }, path);
    }
    assert.deepEqual(core.displayPath('rules/a.md', labels), { kind: 'path', head: '', parent: 'rules/', name: 'a.md' });
  });

  it('a path under projects/ that is not a memory file stays a path', () => {
    for (const path of [
      'projects/C--Workspace-alpha/notes.md', 'projects/C--Workspace-alpha/memory/', 'projects/C--Workspace-alpha/memory',
      'projects/C--Workspace-alpha/', 'projects/C--Workspace-alpha', 'projects/', 'projects//memory/x.md', 'other/projects/C--Workspace-alpha/memory/x.md',
    ]) {
      assert.equal(core.displayPath(path, labels).kind, 'path', path);
    }
  });

  it('a memory file of a project the labels do not know is a path: nothing is guessed', () => {
    assert.deepEqual(core.displayPath('projects/-unknown/memory/x.md', labels), { kind: 'path', head: 'projects/-unknown/', parent: 'memory/', name: 'x.md' });
  });

  it('works without labels', () => {
    for (const none of [undefined, null, new Map()]) {
      assert.equal(core.displayPath('projects/C--Workspace-alpha/memory/x.md', none).kind, 'path');
    }
  });
});

describe('fileEnds (a file name for a cut in its middle)', () => {
  it('keeps the last 10 characters apart: the extension and a little of the name before it', () => {
    assert.deepEqual(core.fileEnds('project_response_contract_problemdetails.md'), { start: 'project_response_contract_problem', end: 'details.md' });
    assert.deepEqual(core.fileEnds('abcdefghijk'), { start: 'a', end: 'bcdefghijk' });
  });

  it('a name of 10 characters or less is all end, so it is never cut', () => {
    for (const name of ['MEMORY.md', 'x.md', 'abcdefghij', '']) assert.deepEqual(core.fileEnds(name), { start: '', end: name }, name);
  });

  it('start and end always make the name again, for any length kept', () => {
    for (const name of ['', 'a', 'a.md', 'user_profile.md', 'sub/dir/some_long_file_name.md', 'ä #1 ?.md']) {
      for (const keep of [0, 1, 3, 10, 100]) {
        const { start, end } = core.fileEnds(name, keep);
        assert.equal(start + end, name, `${name} / ${keep}`);
      }
    }
    assert.deepEqual(core.fileEnds('abcdefgh', 3), { start: 'abcde', end: 'fgh' });
  });

  it('does not split a character made of two code units', () => {
    const { start, end } = core.fileEnds('\u{1F680}'.repeat(12));
    assert.equal(start, '\u{1F680}'.repeat(2));
    assert.equal(end, '\u{1F680}'.repeat(10));
  });

  it('tolerates a missing name', () => {
    assert.deepEqual(core.fileEnds(undefined), { start: '', end: '' });
    assert.deepEqual(core.fileEnds(null), { start: '', end: '' });
  });
});

describe('scrollTargetTop', () => {
  const view = { scrollTop: 100, viewHeight: 400 }; // shows 100..500

  it("'start' puts the item at the top, minus the margin, never above the first pixel", () => {
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 900, itemHeight: 20, block: 'start' }), 900);
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 900, itemHeight: 20, block: 'start', margin: 12 }), 888);
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 5, itemHeight: 20, block: 'start', margin: 12 }), 0);
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 0, itemHeight: 20, block: 'start' }), 0);
  });

  it("'start' scrolls even when the item is already in view", () => {
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 200, itemHeight: 20, block: 'start' }), 200);
  });

  it("'nearest' leaves the position alone when the item is in view", () => {
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 200, itemHeight: 20 }), 100);
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 100, itemHeight: 20 }), 100, 'flush with the top');
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 480, itemHeight: 20 }), 100, 'flush with the bottom');
  });

  it("'nearest' brings an item above the view to the top", () => {
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 40, itemHeight: 20 }), 40);
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 40, itemHeight: 20, margin: 8 }), 32);
  });

  it("'nearest' brings an item below the view to the bottom edge", () => {
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 700, itemHeight: 20 }), 320);
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 700, itemHeight: 20, margin: 4 }), 324, 'the margin stays below the item');
  });

  it("'nearest' aligns an item taller than the view at its top", () => {
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 700, itemHeight: 1000 }), 700);
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 700, itemHeight: 1000, margin: 10 }), 690);
  });

  it('is never negative', () => {
    assert.equal(core.scrollTargetTop({ scrollTop: 0, viewHeight: 400, itemTop: 2, itemHeight: 20, margin: 30 }), 0);
    assert.equal(core.scrollTargetTop({ scrollTop: 50, viewHeight: 400, itemTop: 2, itemHeight: 20, margin: 30 }), 0);
  });

  it('defaults to nearest with no margin', () => {
    assert.equal(core.scrollTargetTop({ scrollTop: 0, viewHeight: 100, itemTop: 150, itemHeight: 10 }), 60);
  });
});

describe('enum lists match the API contract', () => {
  it('has the layers, load modes, link kinds and link statuses of spec 2.1 and 3.1', () => {
    assert.deepEqual(core.LAYERS, [
      'ClaudeMd', 'Rule', 'PathRule', 'Skill', 'SkillResource', 'Agent', 'Command', 'OutputStyle',
      'MemoryIndex', 'Memory', 'Reference', 'Shared', 'Other', 'Note',
    ]);
    assert.deepEqual(core.LOAD_MODES, [
      'EverySession', 'DescriptionEverySession', 'ProjectSession', 'Conditional', 'OnDemand', 'UserInvoked', 'Inactive',
    ]);
    assert.deepEqual(core.LINK_KINDS, ['MarkdownLink', 'WikiLink', 'ClaudePath', 'RelativePath', 'Embed']);
    assert.deepEqual(core.LINK_STATUSES, ['Resolved', 'NonMarkdown', 'Broken', 'Pending', 'External']);
    assert.deepEqual(core.PROFILES, ['Claude', 'Vault', 'Markdown']);
  });
});

// ---------------------------------------------------------------------------------------------
// F2: from a problem to its line, the side panel, the tree's token pair
// ---------------------------------------------------------------------------------------------

/** What the page's `data-line` values are for the elements `tag` of `html`, in order. */
function linesOf(html, tag) {
  return [...html.matchAll(new RegExp(`<${tag}\\b[^>]*\\bdata-line="(\\d+)"`, 'g'))].map((match) => Number(match[1]));
}

describe('lineNumbers (data-line on the blocks)', () => {
  const source = [
    '# H1', //                  1
    '', //                      2
    'para one', //              3
    'line two', //              4
    '', //                      5
    '- a', //                   6
    '- b', //                   7
    '  - nested', //            8
    '', //                      9
    '> quote', //               10
    '', //                      11
    '```js', //                 12
    'code', //                  13
    '```', //                   14
    '', //                      15
    '| h |', //                 16
    '|---|', //                 17
    '| c |', //                 18
    '', //                      19
    '---', //                   20
    '', //                      21
    '1. first', //              22
    '', //                      23
    'closing text', //          24
    '', //                      25
    '    indented code', //     26
  ].join('\n');
  const html = md.render(source, { bodyStartLine: 1 });

  it('a heading, a paragraph (its first line), a quote and a rule carry the line they start on', () => {
    assert.deepEqual(linesOf(html, 'h1'), [1]);
    assert.deepEqual(linesOf(html, 'p'), [3, 10, 24]);
    assert.deepEqual(linesOf(html, 'blockquote'), [10]);
    assert.deepEqual(linesOf(html, 'hr'), [20]);
  });

  it('a list and each of its items, nested ones included, carry their own lines (a tight list has no paragraphs)', () => {
    assert.deepEqual(linesOf(html, 'ul'), [6, 8]);
    assert.deepEqual(linesOf(html, 'li'), [6, 7, 8, 22]);
    assert.deepEqual(linesOf(html, 'ol'), [22]);
  });

  it('a table and each of its rows carry theirs (a cell has no source map and gets nothing)', () => {
    assert.deepEqual(linesOf(html, 'table'), [16]);
    assert.deepEqual(linesOf(html, 'tr'), [16, 18]);
    assert.deepEqual(linesOf(html, 'th'), []);
    assert.deepEqual(linesOf(html, 'td'), []);
  });

  it('a fenced block has it on its <code> (what markdown-it renders; the page lifts it to the <pre>), an indented block on its <pre>', () => {
    assert.match(html, /<pre><code[^>]*\bdata-line="12"/);
    assert.match(html, /<pre data-line="26"><code>/);
  });

  it('the lines are the same in document order, so the last block that starts at or before a line is the one that holds it', () => {
    const all = [...html.matchAll(/\bdata-line="(\d+)"/g)].map((match) => Number(match[1]));
    assert.deepEqual(all, [...all].sort((a, b) => a - b));
  });

  it('is the line of the FILE: bodyStartLine moves every line, and a file with frontmatter finds its blocks', () => {
    const file = ['---', 'name: x', 'description: y', '---', '', '# Title', '', 'text with [[a link]]', '', '- item'];
    const bodyStartLine = 5; // what the server reports: the first body line is the one after the closing ---
    const body = file.slice(bodyStartLine - 1).join('\n');
    const rendered = md.render(body, { bodyStartLine });
    assert.deepEqual(linesOf(rendered, 'h1'), [6]);
    assert.deepEqual(linesOf(rendered, 'p'), [8]);
    assert.deepEqual(linesOf(rendered, 'li'), [10]);
    assert.equal(file[6 - 1], '# Title');
    assert.equal(file[8 - 1], 'text with [[a link]]');
    assert.equal(file[10 - 1], '- item');
  });

  it('without bodyStartLine (text that is not a file) nothing is added, so existing output is unchanged', () => {
    for (const env of [undefined, {}, { bodyStartLine: 0 }, { bodyStartLine: -3 }, { bodyStartLine: 1.5 }, { bodyStartLine: '2' }]) {
      assert.ok(!md.render('# a\n\ntext\n\n- b', env).includes('data-line'), JSON.stringify(env));
    }
    assert.equal(md.render('[[Note]]'), '<p><a class="wikilink" data-raw="Note">Note</a></p>\n');
  });

  it('adds no element, no markup and no script: the output differs only by the attribute', () => {
    const plain = md.render(source);
    const numbered = md.render(source, { bodyStartLine: 1 });
    assert.equal(numbered.replace(/ data-line="\d+"/g, ''), plain);
  });

  it('is correct inside a quote and a list', () => {
    const rendered = md.render('intro\n\n> a\n>\n> b\n\n- x\n\n  para in item\n- y', { bodyStartLine: 10 });
    assert.deepEqual(linesOf(rendered, 'blockquote'), [12]);
    assert.deepEqual(linesOf(rendered, 'p'), [10, 12, 14, 16, 18, 19], 'a loose list has a paragraph in every item');
    assert.deepEqual(linesOf(rendered, 'li'), [16, 19]);
  });
});

describe('blockIndexForLine', () => {
  const lines = [1, 3, 6, 6, 7, 8, 8, 10, 16, 16, 18, 20];

  it('is the block that starts closest before the line, or at it', () => {
    assert.equal(core.blockIndexForLine(lines, 3), 1);
    assert.equal(core.blockIndexForLine(lines, 4), 1, 'the second line of a paragraph');
    assert.equal(core.blockIndexForLine(lines, 5), 1, 'a blank line belongs to the block before it');
    assert.equal(core.blockIndexForLine(lines, 9), 6, 'a blank line after a list item is in that item');
    assert.equal(core.blockIndexForLine(lines, 10), 7);
    assert.equal(core.blockIndexForLine(lines, 17), 9, 'the separator row of a table is the header row');
  });

  it('of several blocks that start on the same line it is the last, the innermost', () => {
    assert.equal(core.blockIndexForLine(lines, 6), 3);
    assert.equal(core.blockIndexForLine(lines, 8), 6);
    assert.equal(core.blockIndexForLine(lines, 16), 9);
  });

  it('a line past the last block is in the last block', () => {
    assert.equal(core.blockIndexForLine(lines, 20), 11);
    assert.equal(core.blockIndexForLine(lines, 5000), 11);
  });

  it('a line above every block (the frontmatter), no blocks at all, and junk give -1', () => {
    assert.equal(core.blockIndexForLine([5, 7], 4), -1);
    assert.equal(core.blockIndexForLine([], 4), -1);
    assert.equal(core.blockIndexForLine([Number.NaN, Number.NaN], 4), -1);
    assert.equal(core.blockIndexForLine([5], Number.NaN), -1);
  });

  it('does not rely on the order of the input: the closest start wins, ties go to the later one', () => {
    assert.equal(core.blockIndexForLine([9, 2, 5, 5, 1], 6), 3);
    assert.equal(core.blockIndexForLine([9, 2, 5, 5, 1], 1), 4);
  });

  it('finds the block of a real render: the broken link on a line of a frontmatter file', () => {
    const file = ['---', 'name: s', '---', '', '# Skill', '', 'Intro.', '', '- see `rules/gone.md`', '- see [[other]]', '', 'Last paragraph.'];
    const bodyStartLine = 4;
    const html = md.render(file.slice(bodyStartLine - 1).join('\n'), { bodyStartLine });
    const found = [...html.matchAll(/<(\w+)\b[^>]*\bdata-line="(\d+)"/g)].map((match) => ({ tag: match[1], line: Number(match[2]) }));
    const at = (line) => found[core.blockIndexForLine(found.map((block) => block.line), line)];
    assert.deepEqual(at(9), { tag: 'li', line: 9 }, 'the line of the code span');
    assert.deepEqual(at(10), { tag: 'li', line: 10 }, 'the line of the wikilink');
    assert.deepEqual(at(5), { tag: 'h1', line: 5 });
    assert.deepEqual(at(2), undefined, 'a line in the frontmatter has no block');
  });
});

describe('tokenParts (the tree: every session first, the total quietly after)', () => {
  it('a file that loads every session: its share leads, the total follows without a second ~', () => {
    assert.deepEqual(core.tokenParts(3495, 3495), { lead: '~3.5K', total: '/ 3.5K' });
    assert.deepEqual(core.tokenParts(93, 1168), { lead: '~93', total: '/ 1.2K' });
    assert.deepEqual(core.tokenParts(1, 850), { lead: '~1', total: '/ 850' });
    assert.deepEqual(core.tokenParts(12_000, 1_200_000), { lead: '~12K', total: '/ 1.2M' });
  });

  it('nothing in every session: only the total, written as everywhere else, and no lead', () => {
    assert.deepEqual(core.tokenParts(0, 1234), { lead: null, total: '~1.2K' });
    assert.deepEqual(core.tokenParts(0, 80), { lead: null, total: '~80' });
    assert.deepEqual(core.tokenParts(0, 0), { lead: null, total: '0' });
  });

  it('a server that does not send the share, or sends rubbish, is the same as none', () => {
    for (const value of [undefined, null, Number.NaN, -4, '12']) {
      assert.deepEqual(core.tokenParts(value, 900), { lead: null, total: '~900' }, String(value));
    }
  });

  it('a folder works the same: its sub-totals', () => {
    assert.deepEqual(core.tokenParts(4920, 10_500), { lead: '~4.9K', total: '/ 10.5K' });
    assert.deepEqual(core.tokenParts(0, 89_800), { lead: null, total: '~89.8K' });
  });
});

describe('loadedTokens (the number under the path of a file)', () => {
  const tokens = { total: 1168, everySession: 93, projectSession: 0 };

  it('every session and description-every-session: the every-session share (the description, for a skill)', () => {
    assert.equal(core.loadedTokens('EverySession', { total: 3495, everySession: 3495, projectSession: 0 }), 3495);
    assert.equal(core.loadedTokens('DescriptionEverySession', tokens), 93);
  });

  it('in its project: the project share', () => {
    assert.equal(core.loadedTokens('ProjectSession', { total: 1700, everySession: 0, projectSession: 1650 }), 1650);
  });

  it('every other mode loads the whole file when it loads at all', () => {
    for (const mode of ['Conditional', 'OnDemand', 'UserInvoked', 'Inactive']) {
      assert.equal(core.loadedTokens(mode, tokens), 1168, mode);
    }
  });

  it('tolerates camelCase modes and a missing or broken count', () => {
    assert.equal(core.loadedTokens('everySession', tokens), 93);
    assert.equal(core.loadedTokens('EverySession', undefined), 0);
    assert.equal(core.loadedTokens('OnDemand', { total: Number.NaN }), 0);
    assert.equal(core.loadedTokens(undefined, tokens), 1168);
  });
});

describe('folderPaths and displayStatus (a link to a folder is a folder)', () => {
  const tree = [
    { type: 'Directory', name: 'rules', path: 'rules', children: [{ type: 'File', name: 'a.md', path: 'rules/a.md' }] },
    { type: 'Directory', name: 'skills', path: 'skills/', children: [
      { type: 'Directory', name: 'x', path: 'skills/x', children: [] },
    ] },
    { type: 'File', name: 'CLAUDE.md', path: 'CLAUDE.md' },
  ];
  const folders = core.folderPaths(tree);

  it('lists every folder of the tree without a trailing slash', () => {
    assert.deepEqual([...folders].sort(), ['rules', 'skills', 'skills/x']);
    assert.equal(core.folderPaths(undefined).size, 0);
  });

  it('a NonMarkdown link whose raw text ends in a slash is a folder, even a folder the tree does not know', () => {
    assert.equal(core.displayStatus({ status: 'NonMarkdown', raw: 'state/', target: 'state' }, folders), 'Folder');
    assert.equal(core.displayStatus({ status: 'NonMarkdown', raw: '~/.claude/projects/', target: 'projects' }, folders), 'Folder');
    assert.equal(core.displayStatus({ status: 'NonMarkdown', raw: 'a/b', target: 'a/b/' }, folders), 'Folder');
  });

  it('or one that names a folder of the tree without the slash', () => {
    assert.equal(core.displayStatus({ status: 'NonMarkdown', raw: '~/.claude/rules', target: 'rules' }, folders), 'Folder');
    assert.equal(core.displayStatus({ status: 'nonMarkdown', raw: 'skills', target: 'skills' }, folders), 'Folder', 'camelCase works');
  });

  it('a file that is not Markdown stays "not Markdown"', () => {
    assert.equal(core.displayStatus({ status: 'NonMarkdown', raw: '~/.claude/statusline.sh', target: 'statusline.sh' }, folders), 'NonMarkdown');
    assert.equal(core.displayStatus({ status: 'NonMarkdown', raw: 'bin/claude-sync', target: 'bin/claude-sync' }, folders), 'NonMarkdown');
    assert.equal(core.displayStatus({ status: 'NonMarkdown', raw: 'x' }, undefined), 'NonMarkdown', 'no tree, no guess');
  });

  it('every other status is passed through, even with a slash', () => {
    for (const status of ['Resolved', 'Broken', 'Pending', 'External']) {
      assert.equal(core.displayStatus({ status, raw: 'a/', target: 'a' }, folders), status);
    }
  });
});

describe('linkLabel and mergeLinks (one row per target in the side panel)', () => {
  const link = (extra) => ({ kind: 'ClaudePath', status: 'Resolved', raw: 'x', line: 1, ...extra });

  it('a resolved link is called by its path and heading, any other by what was written', () => {
    assert.equal(core.linkLabel(link({ target: 'rules/a.md', raw: '~/.claude/rules/a.md' })), 'rules/a.md');
    assert.equal(core.linkLabel(link({ target: 'rules/a.md', heading: 'Intro', raw: '[[a#Intro]]' })), 'rules/a.md#Intro');
    assert.equal(core.linkLabel(link({ status: 'Broken', target: 'gone.md', raw: '~/.claude/gone.md' })), '~/.claude/gone.md');
    assert.equal(core.linkLabel(link({ status: 'Pending', raw: 'some-memory' })), 'some-memory');
  });

  it('links to the same target are one row with their lines (sorted, distinct), like "line 15, 58"', () => {
    const rows = core.mergeLinks([
      link({ target: 'rules/a.md', line: 58 }),
      link({ target: 'b.md', line: 20 }),
      link({ target: 'rules/a.md', line: 15 }),
      link({ target: 'rules/a.md', line: 58 }),
    ]);
    assert.deepEqual(rows.map((row) => [row.label, row.lines]), [['rules/a.md', [15, 58]], ['b.md', [20]]]);
    assert.equal(rows[0].link.line, 58, 'link is the first of the merged ones');
  });

  it('keeps the order of first appearance, and a heading makes another target', () => {
    const rows = core.mergeLinks([
      link({ target: 'a.md', heading: 'X', line: 3 }),
      link({ target: 'a.md', line: 4 }),
      link({ target: 'a.md', heading: 'X', line: 9 }),
    ]);
    assert.deepEqual(rows.map((row) => [row.label, row.lines]), [['a.md#X', [3, 9]], ['a.md', [4]]]);
  });

  it('unresolved links merge by what was written', () => {
    const rows = core.mergeLinks([
      link({ status: 'Broken', raw: '~/.claude/gone.md', target: 'gone.md', line: 8 }),
      link({ status: 'Broken', raw: 'rules/gone.md', target: 'gone.md', line: 9 }),
      link({ status: 'Broken', raw: '~/.claude/gone.md', target: 'gone.md', line: 12 }),
    ]);
    assert.deepEqual(rows.map((row) => [row.label, row.lines]), [['~/.claude/gone.md', [8, 12]], ['rules/gone.md', [9]]]);
  });

  it('a link without a usable line still makes its row, with no number to show', () => {
    const rows = core.mergeLinks([link({ target: 'a.md', line: undefined }), link({ target: 'a.md', line: 0 }), link({ target: 'a.md', line: 6 })]);
    assert.deepEqual(rows.map((row) => row.lines), [[6]]);
  });

  it('no links, no rows; the same list merges the same way twice', () => {
    assert.deepEqual(core.mergeLinks([]), []);
    const input = [link({ target: 'a.md', line: 2 }), link({ target: 'a.md', line: 1 })];
    assert.deepEqual(core.mergeLinks(input), core.mergeLinks(input));
    assert.deepEqual(input.map((item) => item.line), [2, 1], 'the input is not reordered');
  });

  it('twelve links to one file are one row (the real rules/infrastructure-rules.md case)', () => {
    const many = Array.from({ length: 12 }, (_, index) => link({ target: 'rules/altyapi-teshis.md', line: 10 + index * 3 }));
    const rows = core.mergeLinks(many);
    assert.equal(rows.length, 1);
    assert.equal(rows[0].lines.length, 12);
  });
});

describe('the issue rows and the side panel build their links from fileHash', () => {
  it('a line link is a route that parseRoute reads back', () => {
    for (const [path, line] of [['rules/a.md', 78], ['projects/-home-u-p/memory/MEMORY.md', 1], ['İçerik/ä #1 ?.md', 100000]]) {
      assert.deepEqual(core.parseRoute(core.fileHash('claude', path, null, line)), { view: 'file', source: 'claude', path, heading: null, line });
    }
  });
});

describe('scrollTargetTop with a bottom margin (a sticky legend covers the bottom of the tree)', () => {
  const view = { scrollTop: 100, viewHeight: 400 }; // shows 100..500

  it('keeps room below the item: an item that fits but sits under the covered part is brought up', () => {
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 470, itemHeight: 20, margin: 4 }), 100, 'without the cover it is in view');
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 470, itemHeight: 20, margin: 4, bottomMargin: 54 }), 144, '470 + 20 + 54 - 400');
  });

  it('only the bottom changes: the top margin still applies to an item above the view', () => {
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 40, itemHeight: 20, margin: 4, bottomMargin: 54 }), 36);
  });

  it('defaults to the margin, so every existing call is unchanged; start ignores it', () => {
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 700, itemHeight: 20, margin: 4 }), 324);
    assert.equal(core.scrollTargetTop({ ...view, itemTop: 900, itemHeight: 20, block: 'start', margin: 12, bottomMargin: 99 }), 888);
  });
});

// ---------------------------------------------------------------------------------------------
// R4-1: where the reader was (history entries)
// ---------------------------------------------------------------------------------------------

describe('savedPosition (the reading place a history entry carries)', () => {
  it('reads { scroll, treeScroll, infoScroll } and rounds to whole pixels', () => {
    assert.deepEqual(core.savedPosition({ scroll: 768, treeScroll: 420, infoScroll: 96 }), { scroll: 768, treeScroll: 420, infoScroll: 96 });
    assert.deepEqual(core.savedPosition({ scroll: 767.6, treeScroll: 0.4, infoScroll: 12.5 }), { scroll: 768, treeScroll: 0, infoScroll: 13 });
  });

  it('an entry with the top of the page is still a place: it was shown before', () => {
    assert.deepEqual(core.savedPosition({ scroll: 0, treeScroll: 0, infoScroll: 0 }), { scroll: 0, treeScroll: 0, infoScroll: 0 });
  });

  it('a tree or panel scroll that is missing or wrong counts as the top, the content scroll is what makes a place', () => {
    assert.deepEqual(core.savedPosition({ scroll: 90 }), { scroll: 90, treeScroll: 0, infoScroll: 0 });
    assert.deepEqual(core.savedPosition({ scroll: 90, treeScroll: -4, infoScroll: -1 }), { scroll: 90, treeScroll: 0, infoScroll: 0 });
    assert.deepEqual(core.savedPosition({ scroll: 90, treeScroll: 'x', infoScroll: NaN }), { scroll: 90, treeScroll: 0, infoScroll: 0 });
  });

  it('an entry written before the info panel had a place is read as the panel at its top', () => {
    assert.deepEqual(core.savedPosition({ scroll: 300, treeScroll: 40 }), { scroll: 300, treeScroll: 40, infoScroll: 0 });
  });

  it('an entry without a usable place has none: a link just followed, junk, a negative or non-numeric scroll', () => {
    for (const state of [null, undefined, 'x', 7, true, {}, { scroll: '768' }, { scroll: -1 }, { scroll: NaN }, { scroll: Infinity }, { treeScroll: 40 }]) {
      assert.equal(core.savedPosition(state), null, JSON.stringify(state));
    }
  });
});

describe('withPosition (writing the place back)', () => {
  it('sets the three numbers and keeps whatever else the entry carries', () => {
    assert.deepEqual(core.withPosition({ other: 1, scroll: 5 }, 300, 12, 77), { other: 1, scroll: 300, treeScroll: 12, infoScroll: 77 });
  });

  it('starts from nothing when the entry has no object state', () => {
    for (const state of [null, undefined, 'x', 3, []]) assert.deepEqual(core.withPosition(state, 10, 20, 30), { scroll: 10, treeScroll: 20, infoScroll: 30 });
  });

  it('the panel is the top when it is not given', () => {
    assert.deepEqual(core.withPosition(null, 10, 20), { scroll: 10, treeScroll: 20, infoScroll: 0 });
  });

  it('never writes a fractional, negative or non-finite number', () => {
    assert.deepEqual(core.withPosition(null, 10.6, 3.2, 8.5), { scroll: 11, treeScroll: 3, infoScroll: 9 });
    assert.deepEqual(core.withPosition(null, -5, NaN, -2), { scroll: 0, treeScroll: 0, infoScroll: 0 });
    assert.deepEqual(core.withPosition(null, Infinity, undefined, Infinity), { scroll: 0, treeScroll: 0, infoScroll: 0 });
  });

  it('what it writes, savedPosition reads', () => {
    assert.deepEqual(core.savedPosition(core.withPosition(null, 768, 33, 120)), { scroll: 768, treeScroll: 33, infoScroll: 120 });
  });
});

describe('arrivalTarget (where a route puts the reader)', () => {
  const file = (extra = {}) => ({ view: 'file', path: 'a.md', heading: null, line: null, ...extra });
  const overview = (extra = {}) => ({ view: 'overview', issues: null, memory: false, ...extra });
  const saved = { scroll: 768, treeScroll: 40, infoScroll: 96 };

  it('a page reached the first time goes to its line, else its heading, else its top', () => {
    assert.deepEqual(core.arrivalTarget(file({ line: 78, heading: 'x' }), null), { kind: 'line', line: 78 });
    assert.deepEqual(core.arrivalTarget(file({ heading: 'x' }), null), { kind: 'heading', heading: 'x' });
    assert.deepEqual(core.arrivalTarget(file(), null), { kind: 'top' });
  });

  it('the overview is the page\'s to work out (an issue group, the memory table or its top)', () => {
    for (const route of [overview(), overview({ issues: 'broken' }), overview({ memory: true })]) {
      assert.deepEqual(core.arrivalTarget(route, null), { kind: 'overview' });
    }
  });

  it('an entry that comes back goes where the reader left it, whatever its address names', () => {
    for (const route of [file(), file({ line: 78 }), file({ heading: 'x' }), file({ line: 78, heading: 'x' }), overview(), overview({ issues: 'pending' }), overview({ memory: true })]) {
      assert.deepEqual(core.arrivalTarget(route, saved), { kind: 'restore', scroll: 768, treeScroll: 40, infoScroll: 96 }, JSON.stringify(route));
    }
  });

  it('works on what parseRoute and savedPosition return, and a state with no place is a fresh arrival', () => {
    assert.deepEqual(core.arrivalTarget(core.parseRoute('#/f/a.md?l=12'), core.savedPosition(null)), { kind: 'line', line: 12 });
    assert.deepEqual(core.arrivalTarget(core.parseRoute('#/f/a.md?l=12'), core.savedPosition({ scroll: 5, treeScroll: 6, infoScroll: 7 })), { kind: 'restore', scroll: 5, treeScroll: 6, infoScroll: 7 });
    assert.deepEqual(core.arrivalTarget(core.parseRoute('#/?issues=orphans'), core.savedPosition({ scroll: 'x' })), { kind: 'overview' });
    assert.deepEqual(core.arrivalTarget(core.parseRoute('#/'), core.savedPosition({ scroll: 0, treeScroll: 0 })), { kind: 'restore', scroll: 0, treeScroll: 0, infoScroll: 0 });
  });
});

describe('pageIsAtAddress (a page writes its reading place only at its own address)', () => {
  const at = (hash) => core.parseRoute(hash);

  it('a page is at its address: the same file, heading and line', () => {
    assert.equal(core.pageIsAtAddress(at('#/f/a.md'), '#/f/a.md'), true);
    assert.equal(core.pageIsAtAddress(at('#/f/a.md?h=intro'), '#/f/a.md?h=intro'), true);
    assert.equal(core.pageIsAtAddress(at('#/f/a.md?l=12'), '#/f/a.md?l=12'), true);
    assert.equal(core.pageIsAtAddress(at('#/f/a.md?h=x&l=3'), '#/f/a.md?l=3&h=x'), true, 'the order of the parameters is no difference');
  });

  it('another file, or the overview, is another page', () => {
    assert.equal(core.pageIsAtAddress(at('#/f/a.md'), '#/f/b.md'), false);
    assert.equal(core.pageIsAtAddress(at('#/f/a.md'), '#/'), false);
    assert.equal(core.pageIsAtAddress(at('#/'), '#/f/a.md'), false);
  });

  it('the same file at another heading or line is another entry: the browser has moved on and the page has not', () => {
    assert.equal(core.pageIsAtAddress(at('#/f/a.md'), '#/f/a.md?h=intro'), false);
    assert.equal(core.pageIsAtAddress(at('#/f/a.md?h=intro'), '#/f/a.md'), false);
    assert.equal(core.pageIsAtAddress(at('#/f/a.md?l=12'), '#/f/a.md?l=13'), false);
  });

  it('the overview: it is the same page for as long as its group or its memory table is the same', () => {
    assert.equal(core.pageIsAtAddress(at('#/'), ''), true, 'no address at all is the overview');
    assert.equal(core.pageIsAtAddress(at('#/'), '#/'), true);
    assert.equal(core.pageIsAtAddress(at('#/?issues=pending'), '#/?issues=pending'), true);
    assert.equal(core.pageIsAtAddress(at('#/?memory=1'), '#/?memory=1'), true);
    assert.equal(core.pageIsAtAddress(at('#/?issues=pending'), '#/?issues=orphans'), false);
    assert.equal(core.pageIsAtAddress(at('#/?issues=pending'), '#/'), false);
    assert.equal(core.pageIsAtAddress(at('#/'), '#/?memory=1'), false);
  });

  it('a path is the same path however it is written: the page was asked for through parseRoute, and the address is read through it', () => {
    assert.equal(core.pageIsAtAddress(at('#/f/dir%2Fa%20b.md'), '#/f/dir%2Fa%20b.md'), true);
    assert.equal(core.pageIsAtAddress(at('#/f/dir%2Fa%20b.md'), '#/f/dir%2Fa b.md'), true);
    assert.equal(core.pageIsAtAddress(at('#/f/dir%2Fa.md'), '#/f/dir%2Fb.md'), false);
  });

  it('a route that is not one, or an address that is not a route, is no page at that address', () => {
    for (const route of [null, undefined, 'x', 7]) assert.equal(core.pageIsAtAddress(route, '#/'), false, String(route));
    assert.equal(core.pageIsAtAddress({ view: 'file', path: 'a.md', heading: null, line: null }, 'junk'), false);
    assert.equal(core.pageIsAtAddress({ view: 'overview', issues: null, memory: false }, 'junk'), true, 'junk is the overview, as parseRoute says');
  });

  it('a route may leave out what is null (the initial route of the page): it is the same page as the address that has none', () => {
    assert.equal(core.pageIsAtAddress({ view: 'file', path: 'a.md' }, '#/f/a.md'), true);
    assert.equal(core.pageIsAtAddress({ view: 'overview' }, '#/'), true);
  });

  it('replays the race: Forward and Back at once. A page that finishes loading after Back must not write over the entry Back went to', () => {
    const run = (guarded) => {
      // History: entry 0 is A, which the reader scrolled; entry 1 is B, fresh. The reader goes Forward to B, and Back to A at once.
      const entries = [
        { hash: '#/f/a.md', state: core.withPosition(null, 1500, 40, 96) },
        { hash: '#/f/b.md', state: null },
      ];
      let current = 1; // Forward: the browser is on B, B is on its way
      const pageB = { route: at(entries[1].hash), scroll: 0, treeScroll: 0, infoScroll: 0 };
      current = 0; // Back, at once: the browser is on A, and the `hashchange` of A has not run yet
      // B finishes loading now and settles: it writes its place into the CURRENT entry (which is A's)
      const writes = !guarded || core.pageIsAtAddress(pageB.route, entries[current].hash);
      if (writes) entries[current].state = core.withPosition(entries[current].state, pageB.scroll, pageB.treeScroll, pageB.infoScroll);
      return entries[0].state;
    };
    assert.deepEqual(run(false), { scroll: 0, treeScroll: 0, infoScroll: 0 }, 'without the guard A is at the top: the bug');
    assert.deepEqual(run(true), { scroll: 1500, treeScroll: 40, infoScroll: 96 }, 'with it A keeps its place');
  });

  it('the page that IS at the entry\'s address still writes: the guard takes nothing from a page in its own entry', () => {
    const entries = [{ hash: '#/f/a.md', state: core.withPosition(null, 100, 0, 0) }];
    const pageA = { route: at('#/f/a.md'), scroll: 1500, treeScroll: 40, infoScroll: 96 };
    assert.equal(core.pageIsAtAddress(pageA.route, entries[0].hash), true);
    entries[0].state = core.withPosition(entries[0].state, pageA.scroll, pageA.treeScroll, pageA.infoScroll);
    assert.deepEqual(core.savedPosition(entries[0].state), { scroll: 1500, treeScroll: 40, infoScroll: 96 });
  });
});

describe('parseUiState and uiStateJson (what stays open across a reload)', () => {
  const full = {
    issueOpen: new Map([['pending', true], ['orphans', false]]),
    moreOpen: new Set(['issues:pending', 'band:Skill']),
    openBand: new Set(['group:Skill']),
    memoryOpen: true,
    layersRestOpen: true,
    legendOpen: true,
    propertiesOpen: false,
  };

  it('what is written is read back', () => {
    const back = core.parseUiState(core.uiStateJson(full));
    assert.deepEqual([...back.issueOpen], [['pending', true], ['orphans', false]]);
    assert.deepEqual([...back.moreOpen], ['issues:pending', 'band:Skill']);
    assert.deepEqual([...back.openBand], ['group:Skill']);
    assert.deepEqual([back.memoryOpen, back.layersRestOpen, back.legendOpen, back.propertiesOpen], [true, true, true, false]);
  });

  it('nothing, junk or the wrong shape is the page the way it starts: folded, the properties open', () => {
    for (const text of [null, undefined, '', 'not json', '[]', '7', 'null', '"x"']) {
      const ui = core.parseUiState(text);
      assert.equal(ui.issueOpen.size, 0, String(text));
      assert.equal(ui.moreOpen.size, 0);
      assert.equal(ui.openBand.size, 0);
      assert.deepEqual([ui.memoryOpen, ui.layersRestOpen, ui.legendOpen, ui.propertiesOpen], [false, false, false, true]);
    }
  });

  it('keeps only what has the right type: known issue groups with a boolean, string names, boolean flags', () => {
    const ui = core.parseUiState(JSON.stringify({
      issueOpen: [['broken', true], ['nope', true], ['pending', 'yes'], 'x', ['orphans']],
      moreOpen: ['a', 3, null, 'b'],
      openBand: 'group:Skill',
      memoryOpen: 1,
      layersRestOpen: 'true',
      legendOpen: true,
      propertiesOpen: null,
    }));
    assert.deepEqual([...ui.issueOpen], [['broken', true]]);
    assert.deepEqual([...ui.moreOpen], ['a', 'b']);
    assert.equal(ui.openBand.size, 0);
    assert.deepEqual([ui.memoryOpen, ui.layersRestOpen, ui.legendOpen, ui.propertiesOpen], [false, false, true, true]);
  });
});

// ---------------------------------------------------------------------------------------------
// R4-3: the distilled overview
// ---------------------------------------------------------------------------------------------

describe('splitLayers (the layers table in two)', () => {
  const layer = (name, files, tokens, everySessionTokens) => ({ layer: name, files, tokens, everySessionTokens });
  const layers = [
    layer('ClaudeMd', 1, 3500, 3500),
    layer('Rule', 3, 4900, 4900),
    layer('PathRule', 4, 5500, 0),
    layer('Skill', 36, 78800, 3900),
    layer('SkillResource', 30, 100200, 0),
    layer('Agent', 1, 1200, 93),
    layer('Command', 1, 499, 0),
    layer('OutputStyle', 1, 524, 0),
    layer('MemoryIndex', 24, 7900, 0),
    layer('Memory', 192, 81900, 0),
    layer('Other', 3, 3700, 0),
  ];

  it('the layers that load every session stay in the table, and so does the memory index, which loads in every project', () => {
    const { shown } = core.splitLayers(layers);
    assert.deepEqual(shown.map((entry) => entry.layer), ['ClaudeMd', 'Rule', 'Skill', 'Agent', 'MemoryIndex']);
  });

  it('every other layer is folded, in the order given, and the fold says how many tokens it holds', () => {
    const { rest, restTokens } = core.splitLayers(layers);
    assert.deepEqual(rest.map((entry) => entry.layer), ['PathRule', 'SkillResource', 'Command', 'OutputStyle', 'Memory', 'Other']);
    assert.equal(restTokens, 5500 + 100200 + 499 + 524 + 81900 + 3700);
  });

  it('no layer is lost or counted twice', () => {
    const { shown, rest } = core.splitLayers(layers);
    assert.equal(shown.length + rest.length, layers.length);
    assert.deepEqual([...shown, ...rest].map((entry) => entry.layer).sort(), layers.map((entry) => entry.layer).sort());
  });

  it('a layer is kept by its share, not by its size: a huge layer that loads on demand is folded, a tiny one that loads every session is not', () => {
    const { shown, rest } = core.splitLayers([layer('Reference', 1, 999999, 0), layer('Rule', 1, 3, 1)]);
    assert.deepEqual(shown.map((entry) => entry.layer), ['Rule']);
    assert.deepEqual(rest.map((entry) => entry.layer), ['Reference']);
  });

  it('knows the memory index by any spelling of the enum, like the rest of the page', () => {
    assert.deepEqual(core.splitLayers([layer('memoryIndex', 2, 10, 0)]).shown.length, 1);
  });

  it('nothing to split, or nothing in a field, is not an error', () => {
    for (const input of [undefined, null, [], 'x']) assert.deepEqual(core.splitLayers(input), { shown: [], rest: [], restTokens: 0 });
    const odd = core.splitLayers([{ layer: 'Other' }, { layer: 'Memory', tokens: NaN, everySessionTokens: undefined }, { layer: 'Reference', tokens: -5 }]);
    assert.equal(odd.shown.length, 0);
    assert.equal(odd.rest.length, 3);
    assert.equal(odd.restTokens, 0);
  });

  it('nothing loads every session: everything is folded (the page then shows the fold alone)', () => {
    const { shown, rest } = core.splitLayers([layer('Memory', 1, 5, 0), layer('Other', 1, 6, 0)]);
    assert.equal(shown.length, 0);
    assert.equal(rest.length, 2);
  });
});

describe('trLocative (the suffix of a time in Turkish)', () => {
  const cases = [
    // [hour, minute, suffix, how the last number is said]
    [8, 29, 'da', 'dokuz'], [10, 19, 'da', 'dokuz'], [10, 42, 'de', 'iki'], [9, 15, 'te', 'beş'], [14, 43, 'te', 'üç'], [7, 54, 'te', 'dört'],
    [12, 36, 'da', 'altı'], [12, 17, 'de', 'yedi'], [12, 48, 'de', 'sekiz'], [12, 51, 'de', 'bir'],
    [12, 10, 'da', 'on'], [12, 20, 'de', 'yirmi'], [12, 30, 'da', 'otuz'], [12, 40, 'ta', 'kırk'], [12, 50, 'de', 'elli'],
    // on the hour the hour is what is said
    [8, 0, 'de', 'sekiz'], [14, 0, 'te', 'on dört'], [10, 0, 'da', 'on'], [20, 0, 'de', 'yirmi'], [6, 0, 'da', 'altı'], [5, 0, 'te', 'beş'], [0, 0, 'da', 'sıfır'], [1, 0, 'de', 'bir'], [23, 0, 'te', 'yirmi üç'], [21, 0, 'de', 'yirmi bir'], [19, 0, 'da', 'on dokuz'],
    [23, 59, 'da', 'elli dokuz'], [0, 5, 'te', 'beş'],
  ];
  for (const [hour, minute, suffix, said] of cases) {
    it(`${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}'${suffix} (${said})`, () => {
      assert.equal(core.trLocative(hour, minute), suffix);
    });
  }

  it('is one of da, de, ta, te for every minute of the day, and ta only after "kırk"', () => {
    for (let hour = 0; hour < 24; hour += 1) {
      for (let minute = 0; minute < 60; minute += 1) {
        const suffix = core.trLocative(hour, minute);
        assert.ok(['da', 'de', 'ta', 'te'].includes(suffix), `${hour}:${minute} -> ${suffix}`);
        const spoken = minute === 0 ? hour : minute;
        assert.equal(suffix === 'ta', spoken === 40, `${hour}:${minute} -> ${suffix}`);
      }
    }
  });
});

// ---------------------------------------------------------------------------------------------
// R4-2: live changes
// ---------------------------------------------------------------------------------------------

describe('summarizeChange (what the top bar says about a changed event)', () => {
  it('names the first file and counts the others', () => {
    assert.deepEqual(core.summarizeChange({ changed: ['CLAUDE.md', 'a.md', 'b.md'] }), {
      path: 'CLAUDE.md', kind: 'changed', extra: 2, paths: ['CLAUDE.md', 'a.md', 'b.md'],
    });
  });

  it('a file that appeared comes before one that changed, which comes before one that went', () => {
    assert.equal(core.summarizeChange({ added: ['n.md'], changed: ['c.md'], removed: ['r.md'] }).path, 'n.md');
    assert.equal(core.summarizeChange({ changed: ['c.md'], removed: ['r.md'] }).path, 'c.md');
    const gone = core.summarizeChange({ removed: ['r.md', 's.md'] });
    assert.deepEqual([gone.path, gone.kind, gone.extra], ['r.md', 'removed', 1]);
  });

  it('inside a list the order of the server is kept (it sends them sorted)', () => {
    assert.equal(core.summarizeChange({ changed: ['b.md', 'a.md'] }).path, 'b.md');
  });

  it('one path in two lists is counted once', () => {
    const summary = core.summarizeChange({ added: ['x.md'], changed: ['x.md', 'y.md'] });
    assert.deepEqual(summary.paths, ['x.md', 'y.md']);
    assert.equal(summary.extra, 1);
  });

  it('a lone file has nothing extra', () => {
    assert.equal(core.summarizeChange({ changed: ['only.md'] }).extra, 0);
  });

  it('an event with no path says nothing; what is not a list or not a path is ignored', () => {
    for (const event of [undefined, {}, { added: [], changed: [], removed: [] }, { added: 'x.md' }, { changed: [null, 3, ''] }]) {
      assert.equal(core.summarizeChange(event), null, JSON.stringify(event));
    }
    assert.equal(core.summarizeChange({ changed: [null, 'real.md', 7] }).path, 'real.md');
  });
});

describe('changeName (a changed file as the top bar names it)', () => {
  const labels = core.projectLabels(['-home-dev-Workspace-alpha', '-home-dev-Workspace-beta'], '-home-dev');

  it('a plain file by its name', () => {
    assert.equal(core.changeName('CLAUDE.md', labels), 'CLAUDE.md');
    assert.equal(core.changeName('rules/general.md', labels), 'general.md');
  });

  it('a skill\'s file by its skill: every skill has a SKILL.md', () => {
    assert.equal(core.changeName('skills/alpha/SKILL.md', labels), 'alpha · SKILL.md');
  });

  it('a project\'s memory file by its project, not its slug', () => {
    assert.equal(core.changeName('projects/-home-dev-Workspace-alpha/memory/MEMORY.md', labels), 'alpha · MEMORY.md');
    assert.equal(core.changeName('projects/-home-dev-Workspace-beta/memory/notes/x.md', labels), 'beta · notes/x.md');
  });

  it('a memory path of a project the page does not know is a plain file', () => {
    assert.equal(core.changeName('projects/-unknown/memory/MEMORY.md', labels), 'MEMORY.md');
    assert.equal(core.changeName('projects/-unknown/memory/MEMORY.md', undefined), 'MEMORY.md');
  });
});

describe('ageParts (how long ago)', () => {
  const cases = [
    [0, 'now', 0], [4_999, 'now', 0], [5_000, 's', 5], [12_400, 's', 12], [59_999, 's', 59],
    [60_000, 'm', 1], [150_000, 'm', 2], [3_599_999, 'm', 59],
    [3_600_000, 'h', 1], [7_300_000, 'h', 2], [86_399_999, 'h', 23],
    [86_400_000, 'd', 1], [200_000_000, 'd', 2],
  ];
  for (const [ms, unit, n] of cases) {
    it(`${ms} ms is ${unit} ${n}`, () => {
      assert.deepEqual(core.ageParts(ms), { unit, n });
    });
  }

  it('nothing, a time in the future and what is not a number are "now"', () => {
    for (const value of [-5, -86_400_000, NaN, Infinity, undefined, null, '5000']) assert.deepEqual(core.ageParts(value), { unit: 'now', n: 0 }, String(value));
  });
});

describe('formatDelta (the badge beside a number that changed)', () => {
  it('counts: a plus or a real minus sign (U+2212) and the size', () => {
    assert.equal(core.formatDelta(1), '+1');
    assert.equal(core.formatDelta(-1), '−1');
    assert.equal(core.formatDelta(36), '+36');
    assert.equal(core.formatDelta(-12), '−12');
    assert.ok(!core.formatDelta(-1).includes('-'), 'the minus sign is not a hyphen');
  });

  it('tokens: written as tokens are everywhere, without the tilde', () => {
    assert.equal(core.formatDelta(120, { tokens: true }), '+120');
    assert.equal(core.formatDelta(-1200, { tokens: true }), '−1.2K');
    assert.equal(core.formatDelta(2_500_000, { tokens: true }), '+2.5M');
    assert.equal(core.formatDelta(-93, { tokens: true }), '−93');
  });

  it('no change, less than one, or no number is no badge', () => {
    for (const value of [0, 0.4, -0.4, NaN, Infinity, -Infinity, undefined, null]) assert.equal(core.formatDelta(value), '', String(value));
    assert.equal(core.formatDelta(0.4, { tokens: true }), '');
  });
});

describe('overviewDiff (what changed between two overviews)', () => {
  const overview = (extra = {}) => ({ everySessionTokens: 12400, broken: [1, 2, 3], pending: [1], orphans: [], frontmatterErrors: [1, 2], ...extra });

  it('two equal overviews differ in nothing', () => {
    assert.deepEqual(core.overviewDiff(overview(), overview()), []);
  });

  it('a count that changed, with where it was and where it is', () => {
    assert.deepEqual(core.overviewDiff(overview(), overview({ broken: [1, 2] })), [{ key: 'broken', from: 3, to: 2 }]);
    assert.deepEqual(core.overviewDiff(overview(), overview({ orphans: [1] })), [{ key: 'orphans', from: 0, to: 1 }]);
    assert.deepEqual(core.overviewDiff(overview(), overview({ frontmatterErrors: [] })), [{ key: 'frontmatter', from: 2, to: 0 }]);
  });

  it('what every session loads is the budget, in tokens, and comes first; then the groups in the order of the page: the errors, then what is to be reviewed', () => {
    const diffs = core.overviewDiff(overview(), overview({ everySessionTokens: 12520, pending: [], broken: [], frontmatterErrors: [1], orphans: [1] }));
    assert.deepEqual(diffs, [
      { key: 'budget', from: 12400, to: 12520 },
      { key: 'broken', from: 3, to: 0 },
      { key: 'frontmatter', from: 2, to: 1 },
      { key: 'pending', from: 1, to: 0 },
      { key: 'orphans', from: 0, to: 1 },
    ]);
  });

  it('with nothing to compare there is no difference; a field that is missing counts as nothing', () => {
    assert.deepEqual(core.overviewDiff(null, overview()), []);
    assert.deepEqual(core.overviewDiff(overview(), undefined), []);
    assert.deepEqual(core.overviewDiff({}, {}), []);
    assert.deepEqual(core.overviewDiff({}, overview()), [
      { key: 'budget', from: 0, to: 12400 }, { key: 'broken', from: 0, to: 3 }, { key: 'frontmatter', from: 0, to: 2 }, { key: 'pending', from: 0, to: 1 },
    ]);
  });
});

describe('changedBlocks (which blocks of a file are new)', () => {
  const blocks = (text) => text.split(' ');

  it('nothing changed, nothing is new', () => {
    assert.deepEqual(core.changedBlocks(blocks('a b c'), blocks('a b c')), []);
    assert.deepEqual(core.changedBlocks([], []), []);
  });

  it('an edited block is the one new block', () => {
    assert.deepEqual(core.changedBlocks(blocks('a b c d'), blocks('a X c d')), [1]);
    assert.deepEqual(core.changedBlocks(blocks('a b c'), blocks('X b c')), [0], 'the first');
    assert.deepEqual(core.changedBlocks(blocks('a b c'), blocks('a b X')), [2], 'the last');
  });

  it('a block added at the end, at the start or in the middle', () => {
    assert.deepEqual(core.changedBlocks(blocks('a b'), blocks('a b N')), [2]);
    assert.deepEqual(core.changedBlocks(blocks('a b'), blocks('N a b')), [0]);
    assert.deepEqual(core.changedBlocks(blocks('a b'), blocks('a N b')), [1]);
  });

  it('several blocks added together, or edited in one place', () => {
    assert.deepEqual(core.changedBlocks(blocks('a b'), blocks('a N1 N2 N3 b')), [1, 2, 3]);
    assert.deepEqual(core.changedBlocks(blocks('a b c d e'), blocks('a X Y d e')), [1, 2]);
  });

  it('a block that went leaves nothing new behind', () => {
    assert.deepEqual(core.changedBlocks(blocks('a b c'), blocks('a c')), []);
    assert.deepEqual(core.changedBlocks(blocks('a b c'), []), []);
  });

  it('a block that moved is not new (its text is in the old middle), one that is new is', () => {
    assert.deepEqual(core.changedBlocks(blocks('a b c d'), blocks('a c b d')), []);
    assert.deepEqual(core.changedBlocks(blocks('a b c d'), blocks('d c b N a')), [3]);
  });

  it('a copy is new: a paragraph that was once there and is now twice is marked once, the copy', () => {
    assert.deepEqual(core.changedBlocks(blocks('a b c'), blocks('a b b c')), [2]);
    assert.deepEqual(core.changedBlocks(blocks('a x b'), blocks('a x x x b')), [2, 3]);
  });

  it('a rewrite marks nothing: more than `max` blocks are new', () => {
    const old = Array.from({ length: 60 }, (_, index) => `old ${index}`);
    const rewritten = Array.from({ length: 60 }, (_, index) => `new ${index}`);
    assert.deepEqual(core.changedBlocks(old, rewritten), []);
    assert.equal(core.changedBlocks(old, rewritten, 100).length, 60);
    assert.equal(core.changedBlocks(old, [...old.slice(0, 20), ...rewritten.slice(0, 40)]).length, 40, 'exactly max is still shown');
  });

  it('the indices are of the new list, in order, and every one is a block that is not in the old text', () => {
    const before = blocks('intro a b c outro');
    const after = blocks('intro a Q c R outro');
    const found = core.changedBlocks(before, after);
    assert.deepEqual(found, [2, 4]);
    for (const index of found) assert.ok(!before.includes(after[index]));
  });
});

describe('pillTarget (where to point at a change that is off screen)', () => {
  const view = { top: 100, bottom: 500 };

  it('a change in view, even partly, needs no pill', () => {
    assert.equal(core.pillTarget([{ top: 200, bottom: 220 }], view), null);
    assert.equal(core.pillTarget([{ top: 80, bottom: 120 }], view), null, 'partly above');
    assert.equal(core.pillTarget([{ top: 480, bottom: 540 }], view), null, 'partly below');
    assert.equal(core.pillTarget([{ top: 0, bottom: 900 }], view), null, 'taller than the view');
  });

  it('a change above points up, a change below points down', () => {
    assert.deepEqual(core.pillTarget([{ top: 10, bottom: 40 }], view), { dir: 'up', index: 0 });
    assert.deepEqual(core.pillTarget([{ top: 700, bottom: 730 }], view), { dir: 'down', index: 0 });
  });

  it('a block that touches the edge is out of view', () => {
    assert.deepEqual(core.pillTarget([{ top: 60, bottom: 100 }], view), { dir: 'up', index: 0 });
    assert.deepEqual(core.pillTarget([{ top: 500, bottom: 540 }], view), { dir: 'down', index: 0 });
  });

  it('with changes on both sides the nearest one wins, whichever side it is on', () => {
    assert.deepEqual(core.pillTarget([{ top: 0, bottom: 50 }, { top: 600, bottom: 640 }], view), { dir: 'up', index: 0 }, '50 above against 100 below');
    assert.deepEqual(core.pillTarget([{ top: 0, bottom: 50 }, { top: 520, bottom: 560 }], view), { dir: 'down', index: 1 }, '50 above against 20 below');
    assert.deepEqual(core.pillTarget([{ top: 0, bottom: 90 }, { top: 900, bottom: 940 }], view), { dir: 'up', index: 0 });
  });

  it('one block in view is enough for no pill at all, whatever else changed', () => {
    assert.equal(core.pillTarget([{ top: 0, bottom: 50 }, { top: 300, bottom: 320 }, { top: 900, bottom: 940 }], view), null);
  });

  it('nothing changed, nothing to point at', () => {
    assert.equal(core.pillTarget([], view), null);
  });
});

describe('closedAncestor (the folder row that wears the dot)', () => {
  it('the outermost folder above the file that is closed', () => {
    assert.equal(core.closedAncestor('docs/deep/inner/a.md', new Set()), 'docs');
    assert.equal(core.closedAncestor('docs/deep/inner/a.md', new Set(['docs'])), 'docs/deep');
    assert.equal(core.closedAncestor('docs/deep/inner/a.md', new Set(['docs', 'docs/deep'])), 'docs/deep/inner');
  });

  it('every folder open: the file\'s own row shows it, no dot', () => {
    assert.equal(core.closedAncestor('docs/deep/a.md', new Set(['docs', 'docs/deep'])), null);
  });

  it('a file at the top has no folder above it', () => {
    assert.equal(core.closedAncestor('CLAUDE.md', new Set()), null);
  });

  it('an open folder below a closed one does not matter: the closed one hides it', () => {
    assert.equal(core.closedAncestor('docs/deep/a.md', new Set(['docs/deep'])), 'docs');
  });
});

// ---------------------------------------------------------------------------------------------
// R5-A: the folders under projects/ in the order of the names they are shown by
// ---------------------------------------------------------------------------------------------

describe('orderProjectFolders (the projects folder, by the name each project is shown by)', () => {
  const folder = (name) => ({ type: 'Directory', name, path: `projects/${name}`, children: [] });
  const file = (name) => ({ type: 'File', name, path: `projects/${name}` });
  const labelsOf = (slugs, home = '-home-dev') => core.projectLabels(slugs, home);
  const names = (list, labels) => list.map((node) => labels.get(node.name)?.name ?? node.name);

  it('is by the name shown, not by the slug: a Windows folder and a Linux folder of other projects are mixed', () => {
    const slugs = ['-home-dev-Workspace-zeta', 'C--Workspace-beta', '-home-dev-Workspace-alpha', 'C--Workspace-gamma'];
    const labels = labelsOf(slugs);
    assert.deepEqual(names(core.orderProjectFolders(slugs.map(folder), labels), labels), ['alpha', 'beta', 'gamma', 'zeta']);
  });

  it('is by Turkish rules, without regard to case: ç after c, ı before i, upper and lower case together', () => {
    const slugs = ['-home-dev-Workspace-zeta', '-home-dev-Workspace-Cetvel', '-home-dev-Workspace-çizgi', '-home-dev-Workspace-dolap', '-home-dev-Workspace-ırmak', '-home-dev-Workspace-isik', '-home-dev-Workspace-Alfa', '-home-dev-Workspace-beta'];
    const labels = labelsOf(slugs);
    assert.deepEqual(names(core.orderProjectFolders(slugs.map(folder), labels), labels), ['Alfa', 'beta', 'Cetvel', 'çizgi', 'dolap', 'ırmak', 'isik', 'zeta']);
  });

  it('this machine\'s home folder (~) comes first: a symbol is before every letter', () => {
    const slugs = ['-home-dev-Workspace-alpha', '-home-dev'];
    const labels = labelsOf(slugs);
    assert.deepEqual(names(core.orderProjectFolders(slugs.map(folder), labels), labels), ['~', 'alpha']);
  });

  it('one name on two machines: the place tells them apart, the same way every time', () => {
    const slugs = ['C--Workspace-alpha', '-home-dev-Workspace-alpha', '-home-dev-Workspace-beta'];
    const labels = labelsOf(slugs);
    const order = core.orderProjectFolders(slugs.map(folder), labels).map((node) => node.name);
    assert.deepEqual(order, ['-home-dev-Workspace-alpha', 'C--Workspace-alpha', '-home-dev-Workspace-beta'], '~/Workspace before C:\\Workspace');
    assert.deepEqual(core.orderProjectFolders([...slugs].reverse().map(folder), labels).map((node) => node.name), order, 'the order of the input does not matter');
  });

  it('the place decides before the slug does: /mnt/storage is before ~/Workspace although its slug is after', () => {
    const slugs = ['-home-dev-Workspace-alpha', '-mnt-storage-alpha', 'C--Workspace-alpha'];
    const labels = labelsOf(slugs);
    assert.deepEqual(slugs.map((slug) => labels.get(slug).context), ['~/Workspace', '/mnt/storage', 'C:\\Workspace']);
    const order = core.orderProjectFolders(slugs.map(folder), labels).map((node) => node.name);
    assert.deepEqual(order, ['-mnt-storage-alpha', '-home-dev-Workspace-alpha', 'C--Workspace-alpha']);
    assert.notDeepEqual(order, [...slugs].sort(), 'not the order of the slugs');
  });

  it('folders come before files; a file keeps its place among the files', () => {
    const slugs = ['-home-dev-Workspace-beta', '-home-dev-Workspace-alpha'];
    const labels = labelsOf(slugs);
    const result = core.orderProjectFolders([file('b.md'), folder(slugs[0]), file('a.md'), folder(slugs[1])], labels);
    assert.deepEqual(result.map((node) => node.name), ['-home-dev-Workspace-alpha', '-home-dev-Workspace-beta', 'b.md', 'a.md']);
  });

  it('a folder the labels do not know is ordered by its own name, and nothing is written to the input', () => {
    const input = [folder('zz'), folder('-home-dev-Workspace-alpha'), folder('mm')];
    const before = input.map((node) => node.name);
    const result = core.orderProjectFolders(input, labelsOf(['-home-dev-Workspace-alpha']));
    assert.deepEqual(result.map((node) => node.name), ['-home-dev-Workspace-alpha', 'mm', 'zz']);
    assert.deepEqual(input.map((node) => node.name), before);
    assert.notEqual(result, input);
  });

  it('no nodes, no labels, junk', () => {
    assert.deepEqual(core.orderProjectFolders([], new Map()), []);
    assert.deepEqual(core.orderProjectFolders(undefined, undefined), []);
    assert.deepEqual(core.orderProjectFolders([folder('b'), folder('a')], null).map((node) => node.name), ['a', 'b']);
  });

  it('works on what the page has: the real tree of projects gives one order, and it is a permutation of the folders', () => {
    const slugs = ['-home-dev-Workspace-zeta', 'C--Workspace-alpha', '-home-dev-Workspace-alpha', '-home-dev', '-mnt-storage-Omega', 'C--Users-Ana-Desktop-Mavi'];
    const labels = labelsOf(slugs);
    const result = core.orderProjectFolders(slugs.map(folder), labels);
    assert.deepEqual([...result.map((node) => node.name)].sort(), [...slugs].sort());
    assert.deepEqual(names(result, labels), ['~', 'alpha', 'alpha', 'Mavi', 'Omega', 'zeta']);
  });
});

// ---------------------------------------------------------------------------------------------
// R5-B: errors, and what is only to be reviewed
// ---------------------------------------------------------------------------------------------

describe('the two sections of issues', () => {
  it('ISSUE_GROUPS is the order of the page: the errors, then what is to be reviewed', () => {
    assert.deepEqual(core.ISSUE_GROUPS, ['broken', 'frontmatter', 'pending', 'orphans']);
    assert.deepEqual(core.ISSUE_GROUPS, [...core.ISSUE_SECTIONS.errors, ...core.ISSUE_SECTIONS.review]);
  });

  it('every group is in exactly one section: broken links and frontmatter are errors, links not written yet and orphans are not', () => {
    assert.deepEqual(core.ISSUE_SECTIONS, { errors: ['broken', 'frontmatter'], review: ['pending', 'orphans'] });
    for (const group of core.ISSUE_GROUPS) {
      const sections = Object.entries(core.ISSUE_SECTIONS).filter(([, groups]) => groups.includes(group));
      assert.equal(sections.length, 1, group);
      assert.equal(core.issueSection(group), sections[0][0], group);
    }
  });

  it('a group that is not known is not an error: it is something to look at', () => {
    assert.equal(core.issueSection('something'), 'review');
    assert.equal(core.issueSection(undefined), 'review');
  });

  it('an errors group starts open when it has something in it; every group to review starts folded, whatever it holds', () => {
    assert.equal(core.issueStartsOpen('broken', 13), true);
    assert.equal(core.issueStartsOpen('frontmatter', 1), true);
    assert.equal(core.issueStartsOpen('broken', 0), false);
    assert.equal(core.issueStartsOpen('pending', 36), false);
    assert.equal(core.issueStartsOpen('orphans', 7), false);
    assert.equal(core.issueStartsOpen('pending', 0), false);
  });

  const overview = (extra = {}) => ({
    broken: [{ source: 'a.md' }, { source: 'b.md' }], pending: [{ source: 'c.md' }], orphans: [], frontmatterErrors: [{ path: 'd.md' }], ...extra,
  });

  it('splitIssues puts an overview in its two sections, each group in the order of ISSUE_SECTIONS', () => {
    const { errors, review, errorCount, reviewCount } = core.splitIssues(overview());
    assert.deepEqual(errors.map((group) => group.key), ['broken', 'frontmatter']);
    assert.deepEqual(review.map((group) => group.key), ['pending', 'orphans']);
    assert.deepEqual(errors.map((group) => group.items.length), [2, 1]);
    assert.deepEqual(review.map((group) => group.items.length), [1, 0]);
    assert.equal(errorCount, 3);
    assert.equal(reviewCount, 1);
  });

  it('the groups carry the overview\'s own items (the frontmatter ones from `frontmatterErrors`)', () => {
    const data = overview();
    const { errors, review } = core.splitIssues(data);
    assert.equal(errors[0].items, data.broken);
    assert.equal(errors[1].items, data.frontmatterErrors);
    assert.equal(review[0].items, data.pending);
  });

  it('nothing wrong and nothing to review: empty groups, zero counts', () => {
    const { errors, review, errorCount, reviewCount } = core.splitIssues(overview({ broken: [], pending: [], frontmatterErrors: [] }));
    assert.deepEqual([errorCount, reviewCount], [0, 0]);
    assert.ok(errors.every((group) => group.items.length === 0) && review.every((group) => group.items.length === 0));
  });

  it('a server that sends nothing for a group, or nothing at all, has empty groups', () => {
    for (const data of [{}, null, undefined, { broken: 'x', pending: null }]) {
      const { errorCount, reviewCount, errors, review } = core.splitIssues(data);
      assert.deepEqual([errorCount, reviewCount, errors.length, review.length], [0, 0, 2, 2], JSON.stringify(data));
    }
  });

  it('positiveLine: a line is a positive whole number, anything else is no line', () => {
    assert.equal(core.positiveLine(3), 3);
    assert.equal(core.positiveLine(1), 1);
    for (const value of [0, -1, 2.5, '3', null, undefined, NaN, Infinity, {}]) assert.equal(core.positiveLine(value), null, String(value));
  });
});

// ---------------------------------------------------------------------------------------------
// R5-C: errors in words
// ---------------------------------------------------------------------------------------------

describe('loadFailure (what kind of failure a load error is)', () => {
  const apiError = (status) => Object.assign(new Error(`HTTP ${status}`), { status });

  it('no answer at all is the network: what `fetch` throws is a TypeError ("Failed to fetch", "Load failed", ...)', () => {
    assert.deepEqual(core.loadFailure(new TypeError('Failed to fetch')), { kind: 'network', status: 0 });
    assert.deepEqual(core.loadFailure(new TypeError('Load failed')), { kind: 'network', status: 0 });
    assert.deepEqual(core.loadFailure(new TypeError('NetworkError when attempting to fetch resource.')), { kind: 'network', status: 0 });
    assert.deepEqual(core.loadFailure(Object.assign(new Error('x'), { name: 'NetworkError' })), { kind: 'network', status: 0 });
  });

  it('a 5xx answer is the server\'s error, with its status', () => {
    for (const status of [500, 502, 503, 504, 599]) assert.deepEqual(core.loadFailure(apiError(status)), { kind: 'server', status });
  });

  it('any other failing answer is a request the server did not accept, with its status', () => {
    for (const status of [400, 401, 403, 408, 429, 499]) assert.deepEqual(core.loadFailure(apiError(status)), { kind: 'request', status });
  });

  it('an answer that could not be read (JSON that is not JSON) or anything unknown is "unexpected", and says nothing of a status', () => {
    assert.deepEqual(core.loadFailure(new SyntaxError('Unexpected token < in JSON at position 0')), { kind: 'unexpected', status: 0 });
    assert.deepEqual(core.loadFailure(new Error('boom')), { kind: 'unexpected', status: 0 });
    for (const junk of [null, undefined, 'Failed to fetch', 42, {}, { status: 'x' }, { status: 304 }]) {
      assert.deepEqual(core.loadFailure(junk), { kind: 'unexpected', status: 0 }, JSON.stringify(junk));
    }
  });

  it('the message of the error is never what decides: the same words in a TypeError and in an Error are not the same kind', () => {
    assert.equal(core.loadFailure(new TypeError('anything')).kind, 'network');
    assert.equal(core.loadFailure(new Error('Failed to fetch')).kind, 'unexpected');
  });
});

describe('frontmatterProblem (what the page says about a frontmatter with an error)', () => {
  const MESSAGE = 'Line 3, column 34: While scanning a plain scalar value, found invalid mapping.';

  it('a file without an error has no problem', () => {
    assert.equal(core.frontmatterProblem({ frontmatter: { name: 'x' } }), null);
    assert.equal(core.frontmatterProblem({ frontmatter: null }), null);
    assert.equal(core.frontmatterProblem({ frontmatterError: '' }), null);
    assert.equal(core.frontmatterProblem({ frontmatterError: null, frontmatterErrorLine: 3 }), null, 'a line alone is no error');
    for (const junk of [null, undefined, 'x', 7]) assert.equal(core.frontmatterProblem(junk), null, String(junk));
  });

  it('keys were read although there is an error: partly read', () => {
    const problem = core.frontmatterProblem({ frontmatter: { name: 'x', type: 'note' }, frontmatterError: MESSAGE });
    assert.equal(problem.partial, true);
    assert.equal(problem.message, MESSAGE);
  });

  it('nothing was read (null, no keys, not an object): not read at all', () => {
    for (const frontmatter of [null, undefined, {}, [], 'text', 7, ['a']]) {
      assert.equal(core.frontmatterProblem({ frontmatter, frontmatterError: MESSAGE }).partial, false, JSON.stringify(frontmatter));
    }
  });

  it('the line and its text come from the server\'s new fields; the message stays what it was', () => {
    const problem = core.frontmatterProblem({ frontmatter: { name: 'x' }, frontmatterError: MESSAGE, frontmatterErrorLine: 3, frontmatterErrorText: 'description: A value: with a colon' });
    assert.deepEqual(problem, { partial: true, line: 3, text: 'description: A value: with a colon', message: MESSAGE });
  });

  it('a server that does not send them (an older one) has no line and no text, and the page falls back to the message', () => {
    assert.deepEqual(core.frontmatterProblem({ frontmatter: { name: 'x' }, frontmatterError: MESSAGE }), { partial: true, line: null, text: null, message: MESSAGE });
  });

  it('a line that is not a line number, and a text that is empty or not text, are left out', () => {
    for (const line of [0, -2, 2.5, '3', null]) {
      assert.equal(core.frontmatterProblem({ frontmatterError: MESSAGE, frontmatterErrorLine: line }).line, null, String(line));
    }
    for (const text of ['', '   ', 5, null, {}]) {
      assert.equal(core.frontmatterProblem({ frontmatterError: MESSAGE, frontmatterErrorLine: 3, frontmatterErrorText: text }).text, null, String(text));
    }
  });

  it('the text is not trimmed or changed here: the server wrote it, the page shows it as text', () => {
    const raw = '<img src=x onerror=1>  key: value';
    assert.equal(core.frontmatterProblem({ frontmatterError: MESSAGE, frontmatterErrorLine: 2, frontmatterErrorText: raw }).text, raw);
  });
});

// ---------------------------------------------------------------------------------------------
// R5-D: the quick opener (Ctrl+O): finding a file by its name, its path or its project
// ---------------------------------------------------------------------------------------------

describe('foldChar and foldText (what is compared in a search)', () => {
  it('lower case, without accents: what is on the keyboard', () => {
    assert.equal(core.foldChar('A'), 'a');
    assert.equal(core.foldChar('Z'), 'z');
    for (const [typed, folded] of [['ç', 'c'], ['Ç', 'c'], ['ş', 's'], ['Ş', 's'], ['ğ', 'g'], ['Ğ', 'g'], ['ö', 'o'], ['Ö', 'o'], ['ü', 'u'], ['Ü', 'u'], ['é', 'e']]) {
      assert.equal(core.foldChar(typed), folded, typed);
    }
  });

  it('every i is an i: the dotted capital İ, the dotless ı and the capital I', () => {
    for (const letter of ['i', 'I', 'İ', 'ı']) assert.equal(core.foldChar(letter), 'i', letter);
  });

  it('what is not a letter stays: digits, signs, a hyphen', () => {
    for (const sign of ['1', '-', '_', '.', '/', '~']) assert.equal(core.foldChar(sign), sign);
  });

  it('foldText folds whole characters: an emoji is one, and the length of the text is kept', () => {
    assert.deepEqual(core.foldText('İş-ÇAĞ'), ['i', 's', '-', 'c', 'a', 'g']);
    assert.deepEqual(core.foldText('a😀b'), ['a', '😀', 'b']);
    assert.deepEqual(core.foldText(''), []);
    assert.deepEqual(core.foldText(undefined), []);
  });
});

describe('matchText (how a word of the search matches a text)', () => {
  const match = (query, text, options) => core.matchText(core.foldText(query), text, options);
  /** The matched letters in [brackets]. */
  const marked = (query, text, options) => {
    const found = match(query, text, options);
    return found && `${found.tier}:${core.highlightParts(text, found.indices).map((part) => (part.hit ? `[${part.text}]` : part.text)).join('')}`;
  };

  it('a text that starts with it: prefix', () => {
    assert.equal(marked('deploy', 'deployment-rules.md'), 'prefix:[deploy]ment-rules.md');
  });

  it('a word of the text that starts with it, after - _ . / or a space, or at a capital: word', () => {
    assert.equal(marked('rules', 'deployment-rules.md'), 'word:deployment-[rules].md');
    assert.equal(marked('state', 'BUILD_STATE.md'), 'word:BUILD_[STATE].md');
    assert.equal(marked('md', 'notes.md'), 'word:notes.[md]');
    assert.equal(marked('mark', 'diagramMarker'), 'word:diagram[Mark]er');
    assert.equal(marked('b', 'a b'), 'word:a [b]');
  });

  it('inside a word: substring', () => {
    assert.equal(marked('ploy', 'deployment-rules.md'), 'substring:de[ploy]ment-rules.md');
  });

  it('the letters in order with others between them: fuzzy', () => {
    assert.equal(marked('dloy', 'deployment-rules.md'), 'fuzzy:[d]ep[loy]ment-rules.md');
    assert.equal(marked('dpoy', 'deploy'), 'fuzzy:[d]e[p]l[oy]');
  });

  it('the initials of words are a fuzzy match however far apart they are', () => {
    assert.equal(marked('drm', 'deployment-rules.md'), 'fuzzy:[d]eployment-[r]ules.[m]d');
    assert.equal(marked('sm', 'SKILL_MANAGER'), 'fuzzy:[S]KILL_[M]ANAGER');
  });

  it('letters scattered over a long text are no match: a fuzzy match is close, or it is the initials', () => {
    assert.equal(match('drm', 'diamreallyrandommd'), null);
    assert.equal(match('h-wm', 'home-user-Workspace-notes/memory/MEMORY.md'), null);
    assert.equal(match('abc', 'axxxxxxbc'), null, 'six letters between, and the word has three');
    assert.equal(marked('abc', 'axxxxbc'), 'fuzzy:[a]xxxx[bc]', 'four between is as far as a word of three may go');
  });

  it('`fuzzy: false` leaves the loose tier out and keeps the others', () => {
    assert.equal(match('drm', 'deployment-rules.md', { fuzzy: false }), null);
    assert.equal(marked('rules', 'deployment-rules.md', { fuzzy: false }), 'word:deployment-[rules].md');
    assert.equal(marked('deploy', 'deployment-rules.md', { fuzzy: false }), 'prefix:[deploy]ment-rules.md');
  });

  it('no match: other letters, a word longer than the text, an empty word', () => {
    assert.equal(match('xyz', 'abc'), null);
    assert.equal(match('abcd', 'abc'), null);
    assert.equal(match('', 'abc'), null);
    assert.equal(core.matchText([], 'abc'), null);
  });

  it('case, accents and the Turkish i do not matter, and the marks are where the text is', () => {
    assert.equal(marked('calisma', 'ÇALIŞMA-notları.md'), 'prefix:[ÇALIŞMA]-notları.md');
    assert.equal(marked('ISIM', 'İsim.md'), 'prefix:[İsim].md');
    assert.equal(marked('irmak', 'ırmak'), 'prefix:[ırmak]');
    assert.equal(marked('sis', 'Işık-Şişe'), 'word:Işık-[Şiş]e');
  });

  it('indices are of characters, not of code units: an emoji before the match does not move the marks', () => {
    assert.equal(marked('b', 'a😀b'), 'word:a😀[b]', 'a sign before it makes it a word of its own');
    assert.equal(marked('bc', 'a😀xbc'), 'substring:a😀x[bc]');
  });

  it('a better tier always beats a worse one, whatever the lengths', () => {
    const prefix = match('abc', `abc${'x'.repeat(250)}`).score;
    const word = match('abc', 'x-abc').score;
    const substring = match('abc', 'xabc').score;
    const fuzzy = match('abc', 'a-b-c').score;
    assert.ok(prefix > word && word > substring && substring > fuzzy, JSON.stringify({ prefix, word, substring, fuzzy }));
  });

  it('a score is never negative, however far apart the initials are: a weaker place of an entry must not win by being multiplied by a weight under 1', () => {
    const far = `a${' x'.repeat(250)} b`;
    const found = match('ab', far);
    assert.equal(found.tier, 'fuzzy');
    assert.ok(found.score >= 1 && found.score < 1400, String(found.score));
    assert.ok(found.score < match('ab', 'xab').score, 'still below every better tier');
  });

  it('inside a tier a shorter text comes first, and so does an earlier match', () => {
    assert.ok(match('abc', 'abc').score > match('abc', 'abcdef').score);
    assert.ok(match('abc', 'x-abc').score > match('abc', 'x-y-abc').score);
  });
});

describe('highlightParts (a text in runs, for <mark>)', () => {
  it('marks the characters at the places given, in runs', () => {
    assert.deepEqual(core.highlightParts('deployment', [0, 1, 2, 3, 4, 5]), [{ text: 'deploy', hit: true }, { text: 'ment', hit: false }]);
    assert.deepEqual(core.highlightParts('abcdef', [1, 4]), [
      { text: 'a', hit: false }, { text: 'b', hit: true }, { text: 'cd', hit: false }, { text: 'e', hit: true }, { text: 'f', hit: false },
    ]);
  });

  it('the runs are the text again, whatever is marked', () => {
    for (const marks of [[], [0], [0, 1, 2, 3, 4, 5], [2, 3], [5]]) {
      assert.equal(core.highlightParts('abcdef', marks).map((part) => part.text).join(''), 'abcdef');
    }
  });

  it('nothing marked is one run; nothing at all is no run', () => {
    assert.deepEqual(core.highlightParts('abc', []), [{ text: 'abc', hit: false }]);
    assert.deepEqual(core.highlightParts('abc', undefined), [{ text: 'abc', hit: false }]);
    assert.deepEqual(core.highlightParts('', [0]), []);
    assert.deepEqual(core.highlightParts(undefined, [0]), []);
  });

  it('counts characters: an emoji is one place', () => {
    assert.deepEqual(core.highlightParts('a😀b', [1]), [{ text: 'a', hit: false }, { text: '😀', hit: true }, { text: 'b', hit: false }]);
  });

  it('is only text: a name that looks like markup is not made into any', () => {
    const parts = core.highlightParts('<b>x</b>', [1]);
    assert.equal(parts.map((part) => part.text).join(''), '<b>x</b>');
    assert.ok(parts.every((part) => typeof part.text === 'string'));
  });
});

describe('quickEntries and quickSearch (the files the opener finds)', () => {
  const file = (path, layer = 'Other', loadMode = 'OnDemand') => ({ type: 'File', path, name: path.slice(path.lastIndexOf('/') + 1), layer, loadMode });
  const files = [
    file('CLAUDE.md', 'ClaudeMd', 'EverySession'),
    file('rules/deployment-rules.md', 'Rule', 'EverySession'),
    file('rules/style-guide-rules.md', 'Rule', 'EverySession'),
    file('rules/notes.md', 'Rule', 'EverySession'),
    file('skills/diagram-maker/SKILL.md', 'Skill', 'DescriptionEverySession'),
    file('skills/diagram-viewer/SKILL.md', 'Skill', 'DescriptionEverySession'),
    file('skills/diagram-maker/references/shapes.md', 'SkillResource', 'OnDemand'),
    file('projects/-home-dev-Workspace-alpha/memory/MEMORY.md', 'MemoryIndex', 'ProjectSession'),
    file('projects/-home-dev-Workspace-alpha/memory/feedback_tests.md', 'Memory', 'OnDemand'),
    file('projects/C--Workspace-alpha/memory/MEMORY.md', 'MemoryIndex', 'ProjectSession'),
    file('projects/-home-dev-Workspace-beta/memory/MEMORY.md', 'MemoryIndex', 'ProjectSession'),
    file('projects/-home-dev-Workspace-beta/memory/notes.md', 'Memory', 'OnDemand'),
  ];
  const labels = core.projectLabels(['-home-dev-Workspace-alpha', 'C--Workspace-alpha', '-home-dev-Workspace-beta'], '-home-dev');
  const entries = core.quickEntries(files, labels);
  const find = (query, options) => core.quickSearch(entries, query, options);
  const rows = (result) => result.items.map((item) => `${item.entry.name}@${item.entry.context}`);

  it('a file is its name and the folder it is in', () => {
    const entry = entries.find((e) => e.path === 'rules/deployment-rules.md');
    assert.deepEqual(entry, { path: 'rules/deployment-rules.md', name: 'deployment-rules.md', context: 'rules', layer: 'Rule', loadMode: 'EverySession' });
    assert.equal(entries.find((e) => e.path === 'CLAUDE.md').context, '', 'a file at the top is in no folder');
    assert.equal(entries.find((e) => e.path === 'skills/diagram-maker/references/shapes.md').context, 'skills/diagram-maker/references');
  });

  it('a skill is the skill, not SKILL.md, and the folder the skill is in', () => {
    const entry = entries.find((e) => e.path === 'skills/diagram-maker/SKILL.md');
    assert.equal(entry.name, 'diagram-maker');
    assert.equal(entry.context, 'skills');
    assert.equal(entry.loadMode, 'DescriptionEverySession');
  });

  it('a project\'s memory file is its file and its project, by the name the page calls it; the place only where another project has the same name', () => {
    const entry = (path) => entries.find((e) => e.path === path);
    assert.deepEqual([entry('projects/-home-dev-Workspace-beta/memory/notes.md').name, entry('projects/-home-dev-Workspace-beta/memory/notes.md').context], ['notes.md', 'beta']);
    assert.equal(entry('projects/-home-dev-Workspace-alpha/memory/MEMORY.md').context, 'alpha \u00b7 ~/Workspace');
    assert.equal(entry('projects/C--Workspace-alpha/memory/MEMORY.md').context, 'alpha \u00b7 C:\\Workspace');
  });

  it('every file of the tree is an entry; what is not a file with a path is left out; no labels, no files', () => {
    assert.equal(entries.length, files.length);
    assert.deepEqual(core.quickEntries([{ name: 'x' }, null, { path: '' }, { path: 5 }, file('a.md')], labels).map((e) => e.path), ['a.md']);
    assert.deepEqual(core.quickEntries(undefined, labels), []);
    assert.equal(core.quickEntries([file('projects/-home-dev-Workspace-beta/memory/notes.md')], undefined)[0].context, 'projects/-home-dev-Workspace-beta/memory');
  });

  it('an empty search finds nothing (the recent files are another list)', () => {
    for (const query of ['', '   ', undefined, null]) assert.deepEqual(find(query), { items: [], total: 0 });
  });

  it('finds a file by its name: the best kind of match leads', () => {
    assert.deepEqual(rows(find('deploy')), ['deployment-rules.md@rules']);
    assert.equal(rows(find('notes'))[0].startsWith('notes.md@'), true);
    assert.deepEqual(rows(find('diagram')).slice(0, 2).sort(), ['diagram-maker@skills', 'diagram-viewer@skills']);
  });

  it('finds a skill by its name, and by SKILL.md through its path', () => {
    assert.equal(find('diagram-maker').items[0].entry.path, 'skills/diagram-maker/SKILL.md');
    assert.ok(find('skill.md').items.some((item) => item.entry.path === 'skills/diagram-viewer/SKILL.md'));
  });

  it('finds the files of a project by its name', () => {
    const result = find('beta');
    assert.deepEqual(result.items.map((item) => item.entry.path).sort(), ['projects/-home-dev-Workspace-beta/memory/MEMORY.md', 'projects/-home-dev-Workspace-beta/memory/notes.md']);
    assert.ok(result.items.every((item) => item.contextMarks.length === 4), 'the place is marked: beta');
  });

  it('every word must match, in any place: the project and the file', () => {
    assert.deepEqual(find('beta notes').items.map((item) => item.entry.path), ['projects/-home-dev-Workspace-beta/memory/notes.md']);
    assert.deepEqual(rows(find('rules guide')), ['style-guide-rules.md@rules']);
    assert.equal(find('beta nothing-like-this').total, 0);
  });

  it('the name is worth more than the place: a file named after a project comes before the project\'s files', () => {
    const named = [...entries, ...core.quickEntries([file('docs/alpha-plan.md')], labels)];
    const result = core.quickSearch(named, 'alpha');
    assert.equal(result.items[0].entry.path, 'docs/alpha-plan.md');
    assert.ok(result.total > 1);
  });

  it('the marks are the letters that matched in what a row shows: the name, and the place', () => {
    const [first] = find('deploy').items;
    assert.deepEqual(first.nameMarks, [0, 1, 2, 3, 4, 5]);
    assert.deepEqual(first.contextMarks, []);
    const [rules] = find('rules style').items;
    assert.equal(rules.entry.name, 'style-guide-rules.md');
    assert.deepEqual(core.highlightParts(rules.entry.name, rules.nameMarks).filter((part) => part.hit).map((part) => part.text).sort(), ['rules', 'style']);
    assert.deepEqual(core.highlightParts(rules.entry.context, rules.contextMarks).filter((part) => part.hit).map((part) => part.text), ['rules']);
  });

  it('a match in the path that is not shown marks nothing', () => {
    const [first] = find('memory').items;
    assert.ok(first.entry.name === 'MEMORY.md' || first.nameMarks.length === 0);
    const viaPath = find('memory').items.find((item) => item.entry.name === 'feedback_tests.md');
    assert.ok(viaPath, 'found through its path, projects/.../memory/');
    assert.deepEqual([viaPath.nameMarks, viaPath.contextMarks], [[], []]);
  });

  it('the loose kind of match never reaches the path: a long path is not a match of its scattered letters', () => {
    assert.equal(find('xhrj').total, 0);
    assert.equal(find('h-wm').total, 0);
  });

  it('what matches equally well comes the file opened last first, then the shorter name, then the place', () => {
    const tied = core.quickEntries([file('a/x.md'), file('b/x.md'), file('c/x.md')], labels);
    const order = (recent) => core.quickSearch(tied, 'x.md', { recent }).items.map((item) => item.entry.path);
    assert.deepEqual(order([]), ['a/x.md', 'b/x.md', 'c/x.md']);
    assert.deepEqual(order(['c/x.md', 'b/x.md']), ['c/x.md', 'b/x.md', 'a/x.md']);
  });

  it('at most `limit` items, and the total says how many there are', () => {
    const many = core.quickEntries(Array.from({ length: 120 }, (_, i) => file(`docs/note-${i}.md`)), labels);
    const result = core.quickSearch(many, 'note');
    assert.equal(result.items.length, core.QUICK_LIMIT);
    assert.equal(result.total, 120);
    assert.equal(core.quickSearch(many, 'note', { limit: 5 }).items.length, 5);
    assert.equal(core.QUICK_LIMIT, 50);
  });

  it('a word that matches nothing finds nothing', () => {
    assert.deepEqual(find('zzzzqq'), { items: [], total: 0 });
  });

  it('case, accents and the Turkish i do not matter in the search either', () => {
    const turkish = core.quickEntries([file('docs/ÇALIŞMA-notları.md'), file('docs/İlk-Işık.md')], labels);
    assert.equal(core.quickSearch(turkish, 'calisma').items[0].entry.name, 'ÇALIŞMA-notları.md');
    assert.equal(core.quickSearch(turkish, 'ILK').items[0].entry.name, 'İlk-Işık.md');
    assert.equal(core.quickSearch(turkish, 'isik').items[0].entry.name, 'İlk-Işık.md');
  });

  it('the files that make the real tree: 3000 of them are searched without trouble', () => {
    const big = core.quickEntries(Array.from({ length: 3000 }, (_, i) => file(`projects/-home-dev-Workspace-p${i % 40}/memory/some_long_memory_file_name_${i}.md`)), core.projectLabels(Array.from({ length: 40 }, (_, i) => `-home-dev-Workspace-p${i}`), '-home-dev'));
    const started = performance.now();
    for (const query of ['a', 'memory', 'p12 name', 'xqz', 'smfn']) core.quickSearch(big, query);
    assert.ok(performance.now() - started < 1500, `five searches over 3000 files took ${Math.round(performance.now() - started)} ms`);
  });
});

describe('recent files (what an empty search lists)', () => {
  const entries = ['a.md', 'b.md', 'c.md', 'd.md'].map((path) => ({ path, name: path, context: '', layer: 'Other', loadMode: 'OnDemand' }));

  it('pushRecent puts a file first, once', () => {
    assert.deepEqual(core.pushRecent([], 'a.md'), ['a.md']);
    assert.deepEqual(core.pushRecent(['b.md', 'a.md'], 'c.md'), ['c.md', 'b.md', 'a.md']);
    assert.deepEqual(core.pushRecent(['b.md', 'a.md', 'c.md'], 'a.md'), ['a.md', 'b.md', 'c.md'], 'a file opened again moves up');
    assert.deepEqual(core.pushRecent(['a.md'], 'a.md'), ['a.md']);
  });

  it('it remembers at most `max`, the newest', () => {
    const list = Array.from({ length: 12 }, (_, i) => `f${i}.md`);
    const next = core.pushRecent(list, 'new.md');
    assert.equal(next.length, core.RECENT_MAX);
    assert.equal(next[0], 'new.md');
    assert.ok(!next.includes('f11.md'));
    assert.deepEqual(core.pushRecent(['b', 'c'], 'a', 2), ['a', 'b']);
  });

  it('it does not write to the list it is given, and takes what is not a list as none', () => {
    const list = ['a.md'];
    core.pushRecent(list, 'b.md');
    assert.deepEqual(list, ['a.md']);
    assert.deepEqual(core.pushRecent(null, 'a.md'), ['a.md']);
    assert.deepEqual(core.pushRecent('junk', 'a.md'), ['a.md']);
  });

  it('what is written is read back', () => {
    assert.deepEqual(core.parseRecent(core.recentJson(['b.md', 'a.md'])), ['b.md', 'a.md']);
  });

  it('nothing, junk or the wrong shape is no recent files', () => {
    for (const text of [null, undefined, '', 'not json', '{}', '7', '"x"', 'null']) assert.deepEqual(core.parseRecent(text), [], String(text));
  });

  it('only paths are kept, once each, and no more than `RECENT_MAX`', () => {
    assert.deepEqual(core.parseRecent(JSON.stringify(['a.md', 3, null, '', 'a.md', 'b.md', {}])), ['a.md', 'b.md']);
    assert.equal(core.parseRecent(JSON.stringify(Array.from({ length: 40 }, (_, i) => `f${i}.md`))).length, core.RECENT_MAX);
  });

  it('recentResults lists them newest first, as the rows of a search, with nothing marked', () => {
    const result = core.recentResults(entries, ['c.md', 'a.md']);
    assert.deepEqual(result.map((item) => item.entry.path), ['c.md', 'a.md']);
    assert.deepEqual(result[0], { entry: entries[2], score: 0, nameMarks: [], contextMarks: [] });
  });

  it('a file that is no longer in the tree is not listed, and nothing recent is an empty list', () => {
    assert.deepEqual(core.recentResults(entries, ['gone.md', 'b.md']).map((item) => item.entry.path), ['b.md']);
    assert.deepEqual(core.recentResults(entries, []), []);
    assert.deepEqual(core.recentResults(entries, undefined), []);
    assert.deepEqual(core.recentResults(undefined, ['a.md']), []);
  });

  it('at most `limit` of them', () => {
    assert.equal(core.recentResults(entries, ['a.md', 'b.md', 'c.md', 'd.md'], 2).length, 2);
  });
});

describe('the keys that open the quick opener', () => {
  const key = (event, options) => core.opensQuickOpen({ key: '', code: '', ctrlKey: false, metaKey: false, altKey: false, shiftKey: false, isComposing: false, repeat: false, ...event }, options);

  it('Ctrl+O and Ctrl+K open it, and so do Cmd+O and Cmd+K', () => {
    for (const letter of ['o', 'k', 'O', 'K']) {
      assert.equal(key({ key: letter, ctrlKey: true }), true, `Ctrl+${letter}`);
      assert.equal(key({ key: letter, metaKey: true }), true, `Cmd+${letter}`);
    }
  });

  it('other letters with Ctrl do not: the browser keeps Ctrl+P, Ctrl+F, Ctrl+L', () => {
    for (const letter of ['p', 'f', 'l', 's', 'a', 'j']) assert.equal(key({ key: letter, ctrlKey: true }), false, letter);
  });

  it('Shift or Alt turns them into other commands of the browser: not ours', () => {
    assert.equal(key({ key: 'O', ctrlKey: true, shiftKey: true }), false, 'Ctrl+Shift+O: bookmarks');
    assert.equal(key({ key: 'K', ctrlKey: true, shiftKey: true }), false, 'Ctrl+Shift+K: the console');
    assert.equal(key({ key: 'k', ctrlKey: true, altKey: true }), false, 'Ctrl+Alt (AltGr) is a character');
  });

  it('a layout without the letter (another alphabet) is read from the physical key', () => {
    assert.equal(key({ key: 'щ', code: 'KeyO', ctrlKey: true }), true);
    assert.equal(key({ key: 'л', code: 'KeyK', metaKey: true }), true);
    assert.equal(key({ key: 'з', code: 'KeyP', ctrlKey: true }), false);
  });

  it('`/` opens it when the focus is not in a text field', () => {
    assert.equal(key({ key: '/' }), true);
    assert.equal(key({ key: '/' }, { editable: false }), true);
  });

  it('`/` typed in a text field is text', () => {
    assert.equal(key({ key: '/' }, { editable: true }), false);
  });

  it('`/` with Shift opens it (a Turkish keyboard has it on 7), with Ctrl, Cmd or Alt it does not', () => {
    assert.equal(key({ key: '/', shiftKey: true }), true);
    assert.equal(key({ key: '/', ctrlKey: true }), false);
    assert.equal(key({ key: '/', metaKey: true }), false);
    assert.equal(key({ key: '/', altKey: true }), false);
  });

  it('a key that is held down opens it once, and a key of an input method is not a shortcut', () => {
    assert.equal(key({ key: '/', repeat: true }), false);
    assert.equal(key({ key: 'k', ctrlKey: true, isComposing: true }), false);
    assert.equal(key({ key: '/', isComposing: true }), false);
  });

  it('plain letters and other keys do nothing', () => {
    for (const plain of ['o', 'k', 'a', '?', 'Enter', 'Escape', 'Tab']) assert.equal(key({ key: plain }), false, plain);
    assert.equal(key({}), false);
  });

  it('isApplePlatform: Mac and iOS use Cmd', () => {
    for (const platform of ['MacIntel', 'MacPPC', 'iPhone', 'iPad', 'iPod', 'macOS']) assert.equal(core.isApplePlatform(platform), true, platform);
    for (const platform of ['Win32', 'Linux x86_64', 'Android', '', undefined, null]) assert.equal(core.isApplePlatform(platform), false, String(platform));
  });
});

// ---------------------------------------------------------------------------------------------
// Sources: what is read as notes (a vault, a Markdown folder), their tags and embeds
// ---------------------------------------------------------------------------------------------

describe('profiles (a Claude configuration or notes)', () => {
  it('Claude is read as a Claude configuration; a vault and a Markdown folder as notes', () => {
    assert.equal(core.isNoteProfile('Claude'), false);
    assert.equal(core.isNoteProfile('Vault'), true);
    assert.equal(core.isNoteProfile('Markdown'), true);
    assert.equal(core.isNoteProfile('vault'), true, 'camelCase works');
  });

  it('a profile that is none, or not known, is a Claude configuration (an older server sends none)', () => {
    for (const junk of [undefined, null, '', 'Other', 7, {}]) {
      assert.equal(core.isNoteProfile(junk), false, String(junk));
      assert.equal(core.profileName(junk), 'Claude', String(junk));
    }
    assert.equal(core.profileName('markdown'), 'Markdown');
  });

  it('noteName is a file name without .md', () => {
    assert.equal(core.noteName('Project plan.md'), 'Project plan');
    assert.equal(core.noteName('README.MD'), 'README');
    assert.equal(core.noteName('a.md.md'), 'a.md', 'only the last extension');
    assert.equal(core.noteName('plan.markdown'), 'plan.markdown');
    assert.equal(core.noteName('md'), 'md');
    assert.equal(core.noteName(undefined), '');
  });
});

describe('tags (notes)', () => {
  it('tagKey is the name in lower case, without a leading #', () => {
    assert.equal(core.tagKey('#Proje'), 'proje');
    assert.equal(core.tagKey('Alan/Alt'), 'alan/alt');
    assert.equal(core.tagKey(undefined), '');
  });

  it('tagKey reads a Turkish İ as an i: lower-casing it leaves a combining dot behind, which is dropped', () => {
    assert.equal(core.tagKey('İş'), 'iş');
    assert.equal(core.tagKey('İSTANBUL'), 'istanbul');
    assert.equal(core.tagKey('ışık'), 'ışık', 'a dotless ı stays itself');
  });

  it('sortTags puts the tag with the most notes first and equal counts by name', () => {
    const sorted = core.sortTags([{ name: 'b', count: 2 }, { name: 'a', count: 2 }, { name: 'z', count: 9 }, { name: 'çay', count: 2 }, { name: 'c', count: 1 }]);
    assert.deepEqual(sorted.map((tag) => tag.name), ['z', 'a', 'b', 'çay', 'c']);
  });

  it('sortTags drops what is not a tag, makes a bad count 0, and returns a new list', () => {
    const input = [{ name: 'a', count: 'x' }, null, { count: 3 }, { name: '', count: 3 }, 'b', { name: 'c', count: 2.6 }];
    assert.deepEqual(core.sortTags(input), [{ name: 'c', count: 3 }, { name: 'a', count: 0 }]);
    assert.equal(input.length, 6);
    for (const junk of [undefined, null, 'x', {}]) assert.deepEqual(core.sortTags(junk), [], String(junk));
  });

  const files = [
    { path: 'a.md', tags: ['proje', 'alan/alt'] },
    { path: 'b.md', tags: ['Proje'] },
    { path: 'c.md', tags: [] },
    { path: 'd.md' },
    { path: 'e.md', tags: ['projeler'] },
  ];

  it('filesWithTag lists the files that carry a tag, whatever its case, in the order given', () => {
    assert.deepEqual(core.filesWithTag(files, 'proje').map((file) => file.path), ['a.md', 'b.md']);
    assert.deepEqual(core.filesWithTag(files, 'PROJE').map((file) => file.path), ['a.md', 'b.md']);
    assert.deepEqual(core.filesWithTag(files, '#proje').map((file) => file.path), ['a.md', 'b.md'], 'the # is not part of the name');
    assert.deepEqual(core.filesWithTag(files, 'alan/alt').map((file) => file.path), ['a.md']);
  });

  it('filesWithTag matches the whole tag: a longer or a parent tag is another tag', () => {
    assert.deepEqual(core.filesWithTag(files, 'alan'), []);
    assert.deepEqual(core.filesWithTag(files, 'projeler').map((file) => file.path), ['e.md']);
  });

  it('filesWithTag finds nothing for no tag, no files, or files without tags', () => {
    assert.deepEqual(core.filesWithTag(files, ''), []);
    assert.deepEqual(core.filesWithTag(files, null), []);
    assert.deepEqual(core.filesWithTag(undefined, 'proje'), []);
    assert.deepEqual(core.filesWithTag([{ path: 'x.md' }, null], 'proje'), []);
  });

  it('filesWithTag works on the files of a tree (flattenFiles)', () => {
    const tree = [{ type: 'Directory', name: 'd', path: 'd', children: [{ type: 'File', name: 'x.md', path: 'd/x.md', tags: ['t'] }] }, { type: 'File', name: 'y.md', path: 'y.md', tags: ['t'] }];
    assert.deepEqual(core.filesWithTag(core.flattenFiles(tree), 't').map((file) => file.path), ['d/x.md', 'y.md']);
  });

  const inline = (text, tags) => core.findInlineTags(text, tags).map(({ index, raw, tag }) => [index, raw, tag]);

  it('findInlineTags finds the #tags of the text that are tags of the file, as written and as listed', () => {
    assert.deepEqual(inline('Bu #Proje için #alan/alt ve #yok', ['proje', 'alan/alt']), [[3, '#Proje', 'proje'], [15, '#alan/alt', 'alan/alt']]);
    assert.deepEqual(inline('#proje at the start', ['proje']), [[0, '#proje', 'proje']]);
    assert.deepEqual(inline('(#proje)', ['proje']), [[1, '#proje', 'proje']]);
  });

  it('findInlineTags leaves a #word that is not one of the file\'s tags, however it looks', () => {
    assert.deepEqual(inline('a #nope and #other', ['proje']), []);
    assert.deepEqual(inline('#1 is not a tag', ['proje']), []);
  });

  it('findInlineTags does not read a # inside a word or a path as a tag', () => {
    assert.deepEqual(inline('a#proje and see/#proje and x_#proje and &#proje', ['proje']), []);
    assert.deepEqual(inline('##proje', ['proje']), []);
  });

  it('findInlineTags takes the tag up to where it stops: punctuation after it is the text\'s', () => {
    assert.deepEqual(inline('Etiket: #proje, #alan.', ['proje', 'alan']), [[8, '#proje', 'proje'], [16, '#alan', 'alan']]);
    assert.deepEqual(inline('#proje- and #alan/', ['proje', 'alan']), [[0, '#proje', 'proje'], [12, '#alan', 'alan']], 'a trailing dash or slash is not the tag\'s');
  });

  it('findInlineTags knows Turkish and other letters', () => {
    assert.deepEqual(inline('Bir #çalışma #İş', ['çalışma', 'iş']), [[4, '#çalışma', 'çalışma']].concat([[13, '#İş', 'iş']]));
  });

  it('findInlineTags finds nothing without tags or text', () => {
    assert.deepEqual(inline('#proje', []), []);
    assert.deepEqual(inline('#proje', undefined), []);
    assert.deepEqual(inline('#proje', ['', 3, null]), []);
    assert.deepEqual(inline('', ['proje']), []);
    assert.deepEqual(inline(undefined, ['proje']), []);
  });

  it('findInlineTags can be called again and again (no state is kept between calls)', () => {
    for (let round = 0; round < 3; round += 1) assert.deepEqual(inline('x #proje y #proje', ['proje']).length, 2);
  });
});

describe('embeds (![[x]])', () => {
  it('embedName is the note\'s name without .md, and #heading when it names one', () => {
    assert.equal(core.embedName('Project plan'), 'Project plan');
    assert.equal(core.embedName('folder/sub/Note'), 'Note');
    assert.equal(core.embedName('folder/Note.md'), 'Note');
    assert.equal(core.embedName('Note#Heading'), 'Note#Heading');
    assert.equal(core.embedName('Note|alias'), 'Note', 'the alias is a size or a label, not the name');
    assert.equal(core.embedName('#Heading'), '#Heading');
    assert.equal(core.embedName(' '), '');
  });

  it('a file that is not Markdown keeps its extension', () => {
    assert.equal(core.embedName('attachments/diagram.png|300'), 'diagram.png');
    assert.equal(core.embedName('docs/spec.pdf'), 'spec.pdf');
  });

  it('embedAssetKind says picture or file', () => {
    for (const target of ['a.png', 'x/b.JPG', 'c.jpeg', 'd.gif', 'e.svg', 'f.webp', 'g.avif', ' h.bmp ']) assert.equal(core.embedAssetKind(target), 'image', target);
    for (const target of ['a.pdf', 'b.mp3', 'c.mp4', 'd', 'png', 'e.png.txt', '', undefined]) assert.equal(core.embedAssetKind(target), 'file', String(target));
  });

  it('findWikiLink looks an embed up as Embed|raw, and a wikilink as WikiLink|raw: the same raw text is two links', () => {
    const map = core.buildLinkMap([
      { kind: 'WikiLink', raw: 'Note', status: 'Resolved', target: 'Note.md' },
      { kind: 'Embed', raw: 'Note', status: 'Pending' },
      { kind: 'Embed', raw: 'a\\|b', status: 'Resolved', target: 'a.md' },
    ]);
    assert.equal(core.findWikiLink(map, 'Note').status, 'Resolved');
    assert.equal(core.findWikiLink(map, 'Note', 'Embed').status, 'Pending');
    assert.equal(core.findWikiLink(map, 'a|b', 'Embed').target, 'a.md', 'a table cell loses the backslash of an escaped pipe');
    assert.equal(core.findWikiLink(map, 'other', 'Embed'), undefined);
  });
});

describe('a source of notes in the lists the page makes', () => {
  const files = [
    { path: 'Daily/2026-10-01.md', layer: 'Note', loadMode: 'OnDemand' },
    { path: 'projects/-home-u-app/memory/MEMORY.md', layer: 'Note', loadMode: 'OnDemand' },
    { path: 'skills/x/SKILL.md', layer: 'Note', loadMode: 'OnDemand' },
  ];

  it('quickEntries of notes: the name without .md and the folder; no skills and no projects', () => {
    const entries = core.quickEntries(files, new Map(), { notes: true });
    assert.deepEqual(entries.map((entry) => [entry.name, entry.context]), [
      ['2026-10-01', 'Daily'], ['MEMORY', 'projects/-home-u-app/memory'], ['SKILL', 'skills/x'],
    ]);
  });

  it('even with the labels of a Claude configuration, a source of notes has no projects', () => {
    const labels = core.projectLabels(['-home-u-app'], '-home-u');
    const [, memory] = core.quickEntries(files, labels, { notes: true });
    assert.equal(memory.name, 'MEMORY');
    const [, claudeMemory] = core.quickEntries(files, labels);
    assert.equal(claudeMemory.name, 'MEMORY.md');
    assert.equal(claudeMemory.context, 'app');
  });

  it('quickEntries without the option is as it was', () => {
    const entries = core.quickEntries(files, new Map());
    assert.equal(entries[0].name, '2026-10-01.md');
    assert.equal(entries[2].name, 'x', 'a skill is its folder');
  });

  it('changeName of a note is its name without .md; of a Claude configuration it is as it was', () => {
    assert.equal(core.changeName('Daily/2026-10-01.md', new Map(), { notes: true }), '2026-10-01');
    assert.equal(core.changeName('skills/x/SKILL.md', new Map(), { notes: true }), 'SKILL');
    assert.equal(core.changeName('skills/x/SKILL.md', new Map()), 'x · SKILL.md');
    assert.equal(core.changeName('Daily/a.md', new Map()), 'a.md');
  });
});

describe('where a route puts the reader, and where a page is (sources, tags)', () => {
  const at = (hash) => core.parseRoute(hash);

  it('the sources page starts at its top', () => {
    assert.deepEqual(core.arrivalTarget(at('#/sources'), null), { kind: 'top' });
    assert.deepEqual(core.arrivalTarget(at('#/sources'), { scroll: 5, treeScroll: 1, infoScroll: 0 }), { kind: 'restore', scroll: 5, treeScroll: 1, infoScroll: 0 });
  });

  it('the overview of a source, with or without a tag, is the page\'s to work out', () => {
    assert.deepEqual(core.arrivalTarget(at('#/s/n/'), null), { kind: 'overview' });
    assert.deepEqual(core.arrivalTarget(at('#/s/n/?tag=x'), null), { kind: 'overview' });
  });

  it('pageIsAtAddress: the sources page is at the sources address and nowhere else', () => {
    assert.equal(core.pageIsAtAddress(at('#/sources'), '#/sources'), true);
    assert.equal(core.pageIsAtAddress(at('#/sources'), '#/sources/'), true);
    assert.equal(core.pageIsAtAddress(at('#/sources'), '#/s/c/'), false);
    assert.equal(core.pageIsAtAddress(at('#/s/c/'), '#/sources'), false);
  });

  it('pageIsAtAddress: the same page of another source is another page', () => {
    assert.equal(core.pageIsAtAddress(at('#/s/a/f/x.md'), '#/s/b/f/x.md'), false);
    assert.equal(core.pageIsAtAddress(at('#/s/a/'), '#/s/b/'), false);
    assert.equal(core.pageIsAtAddress(at('#/s/a/f/x.md'), '#/s/a/f/x.md'), true);
    assert.equal(core.pageIsAtAddress(at('#/s/a/'), '#/s/a'), true);
  });

  it('pageIsAtAddress: another tag is another page, and a page with no source is not the page of a source', () => {
    assert.equal(core.pageIsAtAddress(at('#/s/n/?tag=a'), '#/s/n/?tag=a'), true);
    assert.equal(core.pageIsAtAddress(at('#/s/n/?tag=a'), '#/s/n/?tag=b'), false);
    assert.equal(core.pageIsAtAddress(at('#/s/n/?tag=a'), '#/s/n/'), false);
    assert.equal(core.pageIsAtAddress(at('#/s/n/'), '#/s/n/?tag=a'), false);
    assert.equal(core.pageIsAtAddress(at('#/'), '#/s/n/'), false, 'the address has been replaced by the source\'s own');
    assert.equal(core.pageIsAtAddress(at('#/s/n/'), '#/'), false);
  });
});

describe('loadFailure of a source whose folder cannot be read', () => {
  const apiError = (status, detail, code) => Object.assign(new Error(`HTTP ${status}`), { status, detail, code });

  it('a 503 that carries a code the page knows is "unavailable", with that code and not the server\'s English sentence', () => {
    assert.deepEqual(core.loadFailure(apiError(503, 'Directory not found: /mnt/notes', 'FolderMissing')), { kind: 'unavailable', status: 503, code: 'FolderMissing' });
    assert.deepEqual(core.loadFailure(apiError(503, '', 'NotReadable')), { kind: 'unavailable', status: 503, code: 'NotReadable' });
    assert.deepEqual(core.loadFailure(apiError(503, undefined, 'TooLarge')), { kind: 'unavailable', status: 503, code: 'TooLarge' });
    assert.ok(!('detail' in core.loadFailure(apiError(503, 'x', 'TooLarge'))), 'the English sentence is never carried on to the page');
  });

  it('the code is read whatever the case of its first letter', () => {
    assert.equal(core.loadFailure(apiError(503, '', 'folderMissing')).code, 'FolderMissing');
  });

  it('an older server says why in words only: still "unavailable", with no code, so the page says it plainly', () => {
    assert.deepEqual(core.loadFailure(apiError(503, '  no access \n')), { kind: 'unavailable', status: 503, code: '' });
    assert.deepEqual(core.loadFailure(apiError(503, 'x', 'SomethingNew')), { kind: 'unavailable', status: 503, code: '' }, 'a code the page has no words for');
  });

  it('a 503 that says nothing is the server\'s error, as before; so is any other 5xx', () => {
    for (const detail of [undefined, '', '   ', 3, null]) assert.deepEqual(core.loadFailure(apiError(503, detail)), { kind: 'server', status: 503 }, String(detail));
    for (const code of ['', 'Unknown', 3, null]) assert.deepEqual(core.loadFailure(apiError(503, '', code)), { kind: 'server', status: 503 }, String(code));
    assert.deepEqual(core.loadFailure(apiError(500, 'boom', 'FolderMissing')), { kind: 'server', status: 500 }, 'only a 503 is a folder that cannot be read');
    assert.deepEqual(core.loadFailure(apiError(404, 'no such source')), { kind: 'request', status: 404 });
  });
});

describe('sourceErrorCode (why a folder cannot be read)', () => {
  it('the three codes the server sends', () => {
    assert.deepEqual([...core.SOURCE_ERROR_CODES], ['FolderMissing', 'NotReadable', 'TooLarge']);
    for (const code of core.SOURCE_ERROR_CODES) assert.equal(core.sourceErrorCode(code), code);
  });

  it('anything else is "", which the page says plainly', () => {
    for (const junk of [undefined, null, '', 'Other', 'folder missing', 7, {}]) assert.equal(core.sourceErrorCode(junk), '', String(junk));
    assert.equal(core.sourceErrorCode('tooLarge'), 'TooLarge');
  });
});

describe('sourcesAccess (what the sources page may do, from the answer of /api/sources)', () => {
  it('where the server allows this browser to change the list: canEdit, and no reason to give', () => {
    assert.deepEqual(core.sourcesAccess({ sources: [], sourcesFile: '/x/sources.json', canEdit: true }), { canEdit: true, closed: null, fileError: null });
    assert.deepEqual(core.sourcesAccess({ canEdit: true, editBlocked: 'Remote', sourcesFile: '/x/sources.json' }), { canEdit: true, closed: null, fileError: null }, 'canEdit is what counts');
  });

  it('asked over the network: Remote, which the sources file can still be edited by hand for; started with the folders: CommandLine', () => {
    assert.deepEqual(core.sourcesAccess({ canEdit: false, editBlocked: 'Remote', sourcesFile: '/x/sources.json' }), { canEdit: false, closed: 'Remote', fileError: null });
    assert.deepEqual(core.sourcesAccess({ canEdit: false, editBlocked: 'CommandLine' }), { canEdit: false, closed: 'CommandLine', fileError: null });
    assert.deepEqual(core.sourcesAccess({ canEdit: false, editBlocked: 'CommandLine', sourcesFile: '/x/sources.json' }), { canEdit: false, closed: 'CommandLine', fileError: null });
  });

  it('Remote from a server that names no sources file is the command line: there is no file to edit by hand', () => {
    for (const sourcesFile of [undefined, null, '']) {
      assert.equal(core.sourcesAccess({ canEdit: false, editBlocked: 'Remote', sourcesFile }).closed, 'CommandLine', String(sourcesFile));
    }
  });

  it('a server that does not say why (an older one) or says something else closes the page without a note', () => {
    for (const data of [{}, { canEdit: false }, { canEdit: false, editBlocked: 'Other' }, { canEdit: false, editBlocked: null }]) {
      assert.deepEqual(core.sourcesAccess(data), { canEdit: false, closed: null, fileError: null }, JSON.stringify(data));
    }
  });

  it('only true is "may": a word that sounds like it is not taken for it', () => {
    for (const canEdit of ['true', 'yes', 1, {}, [true]]) assert.equal(core.sourcesAccess({ canEdit }).canEdit, false, String(canEdit));
  });

  it('the reason is read whatever the case of its first letter (the server sends PascalCase; a naming policy may turn it into camelCase)', () => {
    assert.equal(core.sourcesAccess({ canEdit: false, editBlocked: 'remote', sourcesFile: '/x' }).closed, 'Remote');
    assert.equal(core.sourcesAccess({ canEdit: false, editBlocked: 'commandLine' }).closed, 'CommandLine');
  });

  it('a damaged sources file: what is wrong with it, without the spaces around it; nothing when it is all right', () => {
    assert.equal(core.sourcesAccess({ canEdit: true, sourcesFileError: '  Unexpected character at line 3.\n' }).fileError, 'Unexpected character at line 3.');
    for (const sourcesFileError of [undefined, null, '', '   ', 3, {}]) assert.equal(core.sourcesAccess({ canEdit: true, sourcesFileError }).fileError, null, String(sourcesFileError));
  });

  it('anything that is not an answer is a page that may do nothing and has nothing to say', () => {
    for (const junk of [null, undefined, 'x', 42, []]) assert.deepEqual(core.sourcesAccess(junk), { canEdit: false, closed: null, fileError: null }, String(junk));
  });
});

describe('addSourceBody (what the form that adds a source sends)', () => {
  it('the path, and the name only when one was typed: it is optional', () => {
    assert.deepEqual(core.addSourceBody('~/Documents/notlar', ''), { path: '~/Documents/notlar' });
    assert.deepEqual(core.addSourceBody('~/Documents/notlar', 'Notlarım'), { path: '~/Documents/notlar', name: 'Notlarım' });
    assert.ok(!('name' in core.addSourceBody('/x', '')), 'no empty name is sent');
  });

  it('the spaces around both are dropped (a path pasted with a newline is the path), and a name of spaces is no name', () => {
    assert.deepEqual(core.addSourceBody('  ~/Documents/notlar \n', '  Notlarım  '), { path: '~/Documents/notlar', name: 'Notlarım' });
    assert.deepEqual(core.addSourceBody('/x', '   '), { path: '/x' });
    assert.equal(core.addSourceBody('/a b/c ', '').path, '/a b/c', 'a space inside is part of the path');
  });

  it('what is not typed at all, or only spaces, is an empty path: the server says what is wrong (PathRequired), the page does not guess', () => {
    assert.deepEqual(core.addSourceBody('', ''), { path: '' });
    assert.deepEqual(core.addSourceBody('   ', undefined), { path: '' });
    assert.deepEqual(core.addSourceBody(null, null), { path: '' });
  });

  it('a name is sent as it is: how long it may be is the server\'s to say (InvalidName)', () => {
    assert.equal(core.addSourceBody('/x', 'a'.repeat(81)).name.length, 81);
  });
});

describe('sourceFailure (what adding or removing a source failed with, in the words of the page)', () => {
  const refused = (status, code, detail = '') => Object.assign(new Error(`HTTP ${status}`), { status, code, detail });
  const FILE = '/x/sources.json';

  it('a refusal that is about the path is about the path box, and the page has words for it', () => {
    for (const code of ['PathRequired', 'PathNotAbsolute', 'FolderNotFound', 'TooBroad', 'AlreadyListed']) {
      assert.deepEqual(core.sourceFailure(refused(400, code, 'detail the page does not show'), { file: FILE }), { kind: code, params: {}, field: 'path' }, code);
    }
  });

  it('a name that is not allowed is about the name box', () => {
    assert.deepEqual(core.sourceFailure(refused(400, 'InvalidName'), { file: FILE }), { kind: 'InvalidName', params: {}, field: 'name' });
  });

  it('a sources file that cannot be read names the file the server named; a file that cannot be written gives the reason; neither is about a box', () => {
    assert.deepEqual(core.sourceFailure(refused(409, 'FileInvalid'), { file: FILE }), { kind: 'FileInvalid', params: { file: FILE }, field: null });
    assert.deepEqual(core.sourceFailure(refused(500, 'WriteFailed', '  Permission denied: sources.json \n')), { kind: 'WriteFailed', params: { detail: 'Permission denied: sources.json' }, field: null });
  });

  it('a code that needs the file, or the reason, to be said and has none is said as the failure it is: no sentence is left with a hole in it', () => {
    for (const file of [undefined, null, '']) assert.deepEqual(core.sourceFailure(refused(409, 'FileInvalid'), { file }), { kind: 'request', params: { status: 409 }, field: null }, String(file));
    assert.deepEqual(core.sourceFailure(refused(409, 'FileInvalid')), { kind: 'request', params: { status: 409 }, field: null });
    for (const detail of [undefined, '', '   ']) assert.deepEqual(core.sourceFailure(refused(500, 'WriteFailed', detail)), { kind: 'server', params: { status: 500 }, field: null }, String(detail));
  });

  it('asked over the network (Remote), or from another origin (CrossOrigin, which is the same to a reader), is the note of a page that is closed, with the file', () => {
    assert.deepEqual(core.sourceFailure(refused(403, 'Remote'), { file: FILE }), { kind: 'Remote', params: { file: FILE }, field: null });
    assert.deepEqual(core.sourceFailure(refused(403, 'CrossOrigin'), { file: FILE }), { kind: 'Remote', params: { file: FILE }, field: null });
  });

  it('a server that was started with the folders says so; and with no file to name, Remote and CrossOrigin read as that too', () => {
    assert.deepEqual(core.sourceFailure(refused(403, 'CommandLine'), { file: FILE }), { kind: 'CommandLine', params: {}, field: null });
    assert.deepEqual(core.sourceFailure(refused(403, 'Remote')), { kind: 'CommandLine', params: {}, field: null });
    assert.deepEqual(core.sourceFailure(refused(403, 'CrossOrigin'), { file: null }), { kind: 'CommandLine', params: {}, field: null });
  });

  it('a code the page has no words for is the failure of a page that did not load: the server\'s error for a 5xx, a request it did not accept for any other, with the status', () => {
    assert.deepEqual(core.sourceFailure(refused(500, 'Boom')), { kind: 'server', params: { status: 500 }, field: null });
    assert.deepEqual(core.sourceFailure(refused(400, 'InvalidProfile')), { kind: 'request', params: { status: 400 }, field: null });
    assert.deepEqual(core.sourceFailure(refused(404, 'NotFound')), { kind: 'request', params: { status: 404 }, field: null });
    assert.deepEqual(core.sourceFailure(refused(403, undefined)), { kind: 'request', params: { status: 403 }, field: null });
    assert.deepEqual(core.sourceFailure(refused(502, '')), { kind: 'server', params: { status: 502 }, field: null });
  });

  it('no answer at all is the network, and an answer that could not be read is "unexpected"', () => {
    assert.deepEqual(core.sourceFailure(new TypeError('Failed to fetch')), { kind: 'network', params: { status: 0 }, field: null });
    assert.deepEqual(core.sourceFailure(new SyntaxError('Unexpected token')), { kind: 'unexpected', params: { status: 0 }, field: null });
    for (const junk of [null, undefined, 'boom', 7, {}]) assert.equal(core.sourceFailure(junk).kind, 'unexpected', String(junk));
  });

  it('a 503 here is the server\'s error, not a folder that cannot be read (that is what a page of a source says)', () => {
    assert.deepEqual(core.sourceFailure(refused(503, 'Unavailable', 'Directory not found: /x')), { kind: 'server', params: { status: 503 }, field: null });
  });

  it('the code decides, not the words: a message or a detail that mentions a code is not that code', () => {
    const error = Object.assign(new Error('FolderNotFound'), { status: 400, detail: 'FolderNotFound' });
    assert.equal(core.sourceFailure(error).kind, 'request');
  });

  it('every kind it says for a code the page has words for is in SOURCE_REFUSALS, Remote or CommandLine; the rest are the kinds of loadFailure', () => {
    const kinds = new Set();
    for (const code of ['PathRequired', 'PathNotAbsolute', 'FolderNotFound', 'TooBroad', 'AlreadyListed', 'InvalidName', 'FileInvalid', 'WriteFailed', 'Remote', 'CrossOrigin', 'CommandLine', 'Other']) {
      kinds.add(core.sourceFailure(refused(409, code, 'why'), { file: FILE }).kind);
    }
    for (const kind of kinds) assert.ok(core.SOURCE_REFUSALS.includes(kind) || ['Remote', 'CommandLine', 'request'].includes(kind), kind);
    assert.deepEqual([...core.SOURCE_REFUSALS], ['PathRequired', 'PathNotAbsolute', 'FolderNotFound', 'TooBroad', 'AlreadyListed', 'InvalidName', 'FileInvalid', 'WriteFailed']);
  });
});

// ---------------------------------------------------------------------------------------------
// Where the server runs, and what the "add a folder" box says where the list may not be changed
// ---------------------------------------------------------------------------------------------

describe('sourcesHost (the machine and whether the network may change the list)', () => {
  it('the name of the computer pusula runs on, without the spaces around it, and whether the server accepts changes from the network', () => {
    assert.deepEqual(core.sourcesHost({ machine: 'devbox', remoteEdit: false }), { machine: 'devbox', remoteEdit: false });
    assert.deepEqual(core.sourcesHost({ machine: '  devbox \n', remoteEdit: true }), { machine: 'devbox', remoteEdit: true });
  });

  it('an older server says neither: no machine, and no "yes"', () => {
    for (const data of [{}, { canEdit: false }, null, undefined, 'x', []]) assert.deepEqual(core.sourcesHost(data), { machine: null, remoteEdit: false }, JSON.stringify(data));
  });

  it('a machine that is no name is none, and only true is "yes"', () => {
    for (const machine of ['', '   ', 3, null, {}]) assert.equal(core.sourcesHost({ machine }).machine, null, String(machine));
    for (const remoteEdit of ['true', 'yes', 1, {}, [true]]) assert.equal(core.sourcesHost({ remoteEdit }).remoteEdit, false, String(remoteEdit));
  });
});

describe('sourceLock (what the "add a folder" box says where this browser may not change the list)', () => {
  const FILE = '/x/sources.json';
  const host = { machine: 'devbox', remoteEdit: false };
  const lockOf = (data, hostData = host) => core.sourceLock(core.sourcesAccess(data), hostData, data.sourcesFile ?? null);

  it('where the browser may change the list there is nothing to say', () => {
    assert.equal(lockOf({ canEdit: true, sourcesFile: FILE }), null);
    assert.equal(core.sourceLock({ canEdit: true, closed: null }, host, FILE), null);
  });

  it('asked over the network: Remote, with the machine, the flag to start the server with and the file to edit; the chip says it is the network', () => {
    assert.deepEqual(lockOf({ canEdit: false, editBlocked: 'Remote', sourcesFile: FILE }), { kind: 'Remote', machine: 'devbox', flag: true, file: FILE, chip: 'Remote' });
  });

  it('a server that already accepts changes from the network does not say how to make it', () => {
    assert.equal(lockOf({ canEdit: false, editBlocked: 'Remote', sourcesFile: FILE }, { machine: 'devbox', remoteEdit: true }).flag, false);
  });

  it('a server that names no machine leaves it to the page ("the computer pusula runs on")', () => {
    assert.equal(lockOf({ canEdit: false, editBlocked: 'Remote', sourcesFile: FILE }, { machine: null, remoteEdit: false }).machine, null);
    assert.equal(core.sourceLock({ canEdit: false, closed: 'Remote' }, undefined, FILE).machine, null);
  });

  it('started with the folders: one line, no way left, no flag and no file; the chip says only "read-only"', () => {
    assert.deepEqual(lockOf({ canEdit: false, editBlocked: 'CommandLine' }), { kind: 'CommandLine', machine: 'devbox', flag: false, file: null, chip: 'Plain' });
    assert.deepEqual(lockOf({ canEdit: false, editBlocked: 'CommandLine', sourcesFile: FILE }).file, null);
  });

  it('a Remote answer that names no sources file is the command line (there is no file to edit)', () => {
    assert.equal(lockOf({ canEdit: false, editBlocked: 'Remote' }).kind, 'CommandLine');
  });

  it('a server that does not say why not (an older one) still gets a box, with a reason of its own: Unknown', () => {
    for (const data of [{ canEdit: false }, { canEdit: false, editBlocked: 'Other', sourcesFile: FILE }, {}]) {
      assert.deepEqual(lockOf(data), { kind: 'Unknown', machine: 'devbox', flag: false, file: null, chip: 'Plain' }, JSON.stringify(data));
    }
  });
});

describe('copyMethod (how the page can copy a path)', () => {
  it('the clipboard API where the browser has it (a secure context)', () => {
    assert.equal(core.copyMethod({ clipboard: true, command: true }), 'clipboard');
    assert.equal(core.copyMethod({ clipboard: true }), 'clipboard');
  });

  it('a selected field and execCommand where it has no clipboard but can copy that way (a page opened by an IP address over http)', () => {
    assert.equal(core.copyMethod({ clipboard: false, command: true }), 'selection');
  });

  it('no button where it can do neither', () => {
    assert.equal(core.copyMethod({ clipboard: false, command: false }), 'none');
    assert.equal(core.copyMethod({}), 'none');
    assert.equal(core.copyMethod(), 'none');
  });
});

// ---------------------------------------------------------------------------------------------
// Backlink excerpts, properties, the front of a source of notes, the upper folders of a path
// ---------------------------------------------------------------------------------------------

describe('readableWikilinks (a backlink excerpt without the syntax of a link)', () => {
  const read = core.readableWikilinks;

  it('[[a|b]] is b, [[a#h]] is a, [[a]] is a', () => {
    assert.equal(read('see [[a|b]]'), 'see b');
    assert.equal(read('see [[a#h]]'), 'see a');
    assert.equal(read('see [[a]]'), 'see a');
  });

  it('the alias wins over the heading, and a path stays as written', () => {
    assert.equal(read('[[a#h|b]]'), 'b');
    assert.equal(read('[[folder/Note]] and [[folder/Note#h]]'), 'folder/Note and folder/Note');
  });

  it('a link to a heading of the same note is the heading', () => {
    assert.equal(read('[[#Başlık]]'), 'Başlık');
  });

  it('an embed is the note or the file it puts in, without its ! and without its size (what follows | in an embed is a width, not a name)', () => {
    assert.equal(read('![[Plan]]'), 'Plan');
    assert.equal(read('![[Plan#Q4]] ve ![[a.png|300]]'), 'Plan ve a.png');
    assert.equal(read('![[a#h|b]]'), 'a');
  });

  it('every link of a line, and the text between them untouched', () => {
    assert.equal(read('Bu [[A|bir]] ve [[B#x]] ile [[C]] #etiket `kod`'), 'Bu bir ve B ile C #etiket `kod`');
  });

  it('what is not a wikilink is left as it is: a Markdown link, single brackets, triple brackets, an unclosed one, an empty one', () => {
    for (const text of ['[a](b.md)', '[not a link]', '[x] [y]', '[[[a]]]', '[[unclosed', '[[]]', 'plain text', '']) assert.equal(read(text), text, text);
  });

  it('a link that spans a line is no link (the server extracts only within one line)', () => {
    assert.equal(read('[[a\nb]]'), '[[a\nb]]');
  });

  it('whatever is not text is no text: the excerpt of a line that has none', () => {
    assert.equal(read(undefined), '');
    assert.equal(read(null), '');
  });

  it('whitespace inside the brackets is not part of the name', () => {
    assert.equal(read('[[ a | b ]]'), 'b');
    assert.equal(read('[[ a ]]'), 'a');
  });

  it('can be called again and again (no state is kept between calls)', () => {
    for (let round = 0; round < 3; round += 1) assert.equal(read('[[a]] [[b]]'), 'a b');
  });
});

describe('propertiesWithoutTags (the properties of a note: its tags are chips already)', () => {
  const without = core.propertiesWithoutTags;

  it('leaves out tags and tag, whatever the case, and keeps the others in their order', () => {
    assert.deepEqual(without({ title: 'x', tags: ['a'], status: 'ok', Tag: 'b' }), { title: 'x', status: 'ok' });
    assert.deepEqual(Object.keys(without({ b: 1, tags: [], a: 2, TAGS: [] })), ['b', 'a']);
  });

  it('a note whose properties were only its tags has none left', () => {
    assert.deepEqual(without({ tags: ['a', 'b'] }), {});
    assert.deepEqual(without({ tag: 'a' }), {});
  });

  it('a key that only contains "tags" is another property', () => {
    assert.deepEqual(without({ 'tags-extra': 1, tagged: 2, hashtags: 3 }), { 'tags-extra': 1, tagged: 2, hashtags: 3 });
  });

  it('returns a new object and leaves the input alone', () => {
    const input = { tags: ['a'], title: 'x' };
    const output = without(input);
    assert.notEqual(output, input);
    assert.deepEqual(input, { tags: ['a'], title: 'x' });
  });

  it('what is no object comes back as it is (a note with no frontmatter)', () => {
    for (const value of [undefined, null, 'x', 3, ['tags']]) assert.equal(without(value), value, String(value));
  });
});

describe('noteFront (where the overview of a source of notes starts)', () => {
  const data = {
    entry: { path: 'Home.md', title: 'Home' },
    recent: [{ path: 'a.md', modifiedAt: '2026-10-02T09:00:00Z' }, { path: 'b/c.md', modifiedAt: '2026-10-01T09:00:00Z' }],
    mostLinked: [{ path: 'Home.md', count: 5 }, { path: 'a.md', count: 2 }],
  };

  it('the entry, the notes changed last as the server ordered them, the notes linked to most', () => {
    assert.deepEqual(core.noteFront(data), data);
  });

  it('a Claude folder, or an older server: nothing', () => {
    for (const input of [{}, { recent: [], mostLinked: [] }, null, undefined, 'x']) assert.deepEqual(core.noteFront(input), { entry: null, recent: [], mostLinked: [] }, JSON.stringify(input));
  });

  it('an entry with no title is called by its file name without .md; one with no path is none', () => {
    assert.deepEqual(core.noteFront({ entry: { path: 'x/Start here.md' } }).entry, { path: 'x/Start here.md', title: 'Start here' });
    assert.deepEqual(core.noteFront({ entry: { path: 'Home.md', title: '   ' } }).entry, { path: 'Home.md', title: 'Home' });
    for (const entry of [{ title: 'x' }, { path: '' }, { path: 3 }, null, 'Home.md']) assert.equal(core.noteFront({ entry }).entry, null, JSON.stringify(entry));
  });

  it('a row that is no path is left out of its list, and a note that nothing links to is not "most linked"', () => {
    const front = core.noteFront({
      recent: [{ path: 'a.md', modifiedAt: 'x' }, { modifiedAt: 'y' }, null, { path: '' }],
      mostLinked: [{ path: 'a.md', count: 0 }, { path: 'b.md', count: 3.4 }, { path: 'c.md', count: 'many' }, { path: 'd.md' }],
    });
    assert.deepEqual(front.recent, [{ path: 'a.md', modifiedAt: 'x' }]);
    assert.deepEqual(front.mostLinked, [{ path: 'b.md', count: 3 }]);
  });

  it('a time that is no text is no time (the row then says none)', () => {
    assert.deepEqual(core.noteFront({ recent: [{ path: 'a.md', modifiedAt: 5 }, { path: 'b.md' }] }).recent, [{ path: 'a.md', modifiedAt: '' }, { path: 'b.md', modifiedAt: '' }]);
  });

  it('the lists it returns are new', () => {
    const front = core.noteFront(data);
    assert.notEqual(front.recent, data.recent);
    assert.notEqual(front.recent[0], data.recent[0]);
  });
});

describe('ageSince (how long ago an ISO date was)', () => {
  const NOW = Date.parse('2026-10-02T12:00:00Z');

  it('is ageParts of the time that has passed', () => {
    assert.deepEqual(core.ageSince('2026-10-02T11:59:58Z', NOW), { unit: 'now', n: 0 });
    assert.deepEqual(core.ageSince('2026-10-02T11:59:30Z', NOW), { unit: 's', n: 30 });
    assert.deepEqual(core.ageSince('2026-10-02T11:56:00Z', NOW), { unit: 'm', n: 4 });
    assert.deepEqual(core.ageSince('2026-10-02T09:00:00Z', NOW), { unit: 'h', n: 3 });
    assert.deepEqual(core.ageSince('2026-09-27T12:00:00Z', NOW), { unit: 'd', n: 5 });
  });

  it('a time in the future is "now"; a date with an offset is read as the moment it is', () => {
    assert.deepEqual(core.ageSince('2026-10-03T12:00:00Z', NOW), { unit: 'now', n: 0 });
    assert.deepEqual(core.ageSince('2026-10-02T14:56:00+03:00', NOW), { unit: 'm', n: 4 });
  });

  it('what is no date is none: the row says nothing of time', () => {
    for (const value of ['', 'yesterday', undefined, null, 5, {}]) assert.equal(core.ageSince(value, NOW), null, String(value));
  });

  it('asks the clock when it is not told', () => {
    assert.equal(core.ageSince(new Date().toISOString()).unit, 'now');
  });
});

describe('pathHeadCut (the upper folders of a path that no longer fit)', () => {
  // Every character is 8px wide and the ellipsis 8px: "0Kararlar/" with room for 30px would be "0K…".
  const widths = [8, 8, 8, 8, 8, 8];
  const cut = (room, extra = {}) => core.pathHeadCut({ room, whole: 200, widths, ellipsis: 8, ...extra });

  it('a head that fits whole is never cut, however small it is', () => {
    assert.equal(core.pathHeadCut({ room: 40, whole: 40, widths, ellipsis: 8 }), false);
    assert.equal(core.pathHeadCut({ room: 100, whole: 24, widths: [8, 8, 8], ellipsis: 8 }), false);
    assert.equal(core.pathHeadCut({ room: 0, whole: 0, widths: [], ellipsis: 8 }), false);
  });

  it('with less than three characters left to read (before the ellipsis) it is left out for a plain "\u2026/"', () => {
    assert.equal(cut(0), true);
    assert.equal(cut(8), true, 'only the ellipsis');
    assert.equal(cut(16), true, 'one character and the ellipsis');
    assert.equal(cut(31), true, 'two characters and the ellipsis; the third does not fit');
  });

  it('with three characters or more it is cut by the ellipsis as before', () => {
    assert.equal(cut(32), false, 'three characters and the ellipsis');
    assert.equal(cut(60), false);
  });

  it('a head with fewer than three characters is cut as soon as it does not fit whole (there is no third to wait for)', () => {
    assert.equal(core.pathHeadCut({ room: 10, whole: 16, widths: [8, 8], ellipsis: 8 }), true);
    assert.equal(core.pathHeadCut({ room: 24, whole: 26, widths: [8, 8], ellipsis: 8 }), false, 'both fit before the ellipsis');
  });

  it('the narrow letters of a name count as they are: "iii/" is three characters in less room than "WWW/"', () => {
    assert.equal(core.pathHeadCut({ room: 24, whole: 100, widths: [3, 3, 3, 4], ellipsis: 8 }), false);
    assert.equal(core.pathHeadCut({ room: 24, whole: 100, widths: [14, 14, 14, 14], ellipsis: 8 }), true);
  });

  it('the number of characters it asks for can be changed, and a missing list of widths is an empty one', () => {
    assert.equal(cut(32, { min: 5 }), true);
    assert.equal(cut(48, { min: 5 }), false);
    assert.equal(core.pathHeadCut({ room: 1, whole: 20, widths: undefined }), false);
  });
});

// ---------------------------------------------------------------------------------------------
// The folder picker ("Choose a folder..." on the sources page)
// ---------------------------------------------------------------------------------------------

describe('browseUrl (the address that lists the folders inside a folder)', () => {
  it('no folder is the home folder: the server says where that is', () => {
    assert.equal(core.browseUrl(), '/api/browse');
    assert.equal(core.browseUrl(null), '/api/browse');
    assert.equal(core.browseUrl(''), '/api/browse');
  });

  it('a folder is encoded whole, never taken apart: a space, a # or a non-ASCII letter cannot break the query', () => {
    assert.equal(core.browseUrl('/home/ana/belgeler'), '/api/browse?path=%2Fhome%2Fana%2Fbelgeler');
    assert.equal(core.browseUrl('/x/a b#c/çay'), '/api/browse?path=%2Fx%2Fa%20b%23c%2F%C3%A7ay');
  });

  it('the hidden folders are asked for with hidden=1, with or without a folder', () => {
    assert.equal(core.browseUrl('/x', { hidden: true }), '/api/browse?path=%2Fx&hidden=1');
    assert.equal(core.browseUrl(null, { hidden: true }), '/api/browse?hidden=1');
    assert.equal(core.browseUrl('/x', { hidden: false }), '/api/browse?path=%2Fx');
  });
});

describe('browseFolder, browseFolders, folderKind and folderHolds (one folder of an answer of the picker)', () => {
  it('a folder is read as the page draws it; the kind comes in either case, and any other kind is no kind', () => {
    assert.deepEqual(
      core.browseFolder({ name: 'notlar', path: '/home/ana/notlar', display: '~/notlar', kind: 'Vault', markdownCount: 16, listed: true }),
      { name: 'notlar', path: '/home/ana/notlar', display: '~/notlar', kind: 'Vault', count: 16, more: false, listed: true },
    );
    assert.equal(core.folderKind('claude'), 'Claude');
    assert.equal(core.folderKind('Vault'), 'Vault');
    assert.equal(core.folderKind('Markdown'), '', 'the picker marks a vault and a Claude configuration only');
    assert.equal(core.folderKind(undefined), '');
  });

  it('what the server leaves out is what the page says nothing of: no count, not listed, no kind; the name is the last one of the path, the display the path', () => {
    assert.deepEqual(
      core.browseFolder({ path: '/srv/veri/belgeler' }),
      { name: 'belgeler', path: '/srv/veri/belgeler', display: '/srv/veri/belgeler', kind: '', count: null, more: false, listed: false },
    );
    assert.equal(core.browseFolder({ path: 'C:\\Veri\\Notlar' }).name, 'Notlar');
  });

  it('"more" means the count stopped there, and only a folder with a count can say so', () => {
    assert.deepEqual(
      [core.browseFolder({ path: '/a', markdownCount: 999, more: true }).more, core.browseFolder({ path: '/a', more: true }).more, core.browseFolder({ path: '/a', markdownCount: 3, more: 'yes' }).more],
      [true, false, false],
    );
    assert.equal(core.browseFolder({ path: '/a', markdownCount: -4 }).count, null);
    assert.equal(core.browseFolder({ path: '/a', markdownCount: 7.9 }).count, 7);
  });

  it('what names no folder is left out of a list, and what is not a list is an empty one', () => {
    assert.deepEqual(core.browseFolders([{ path: '/a' }, null, 'x', { name: 'no path' }, { path: '' }, { path: '/b' }]).map((folder) => folder.path), ['/a', '/b']);
    assert.deepEqual(core.browseFolders(undefined), []);
    assert.deepEqual(core.browseFolders({ path: '/a' }), []);
  });

  it('a row says what a folder holds only when there is something: no count and no Markdown say nothing; a count that stopped says so', () => {
    assert.equal(core.folderHolds({ count: null, more: false }), null);
    assert.equal(core.folderHolds({ count: 0, more: false }), null);
    assert.equal(core.folderHolds({ count: 0, more: true }), null);
    assert.deepEqual(core.folderHolds({ count: 12, more: false }), { n: 12, more: false });
    assert.deepEqual(core.folderHolds({ count: 999, more: true }), { n: 999, more: true });
    assert.equal(core.folderHolds(undefined), null);
  });
});

describe('browseListing and browseFound (the answers of the picker)', () => {
  it('a listing carries where it is, how to go up, the home folder, the folders and whether the list was cut', () => {
    const listing = core.browseListing({
      path: '/home/ana/belgeler', display: '~/belgeler', parent: '/home/ana', home: '/home/ana', truncated: true,
      folders: [{ path: '/home/ana/belgeler/a', name: 'a' }],
    });
    assert.deepEqual(
      { ...listing, folders: listing.folders.map((folder) => folder.name) },
      { path: '/home/ana/belgeler', display: '~/belgeler', parent: '/home/ana', home: '/home/ana', folders: ['a'], truncated: true },
    );
  });

  it('the root has no parent; a server that says nothing of the home folder or the display is read as it is; an answer with no path is no listing', () => {
    const root = core.browseListing({ path: '/', folders: [] });
    assert.deepEqual(root, { path: '/', display: '/', parent: null, home: null, folders: [], truncated: false });
    assert.equal(core.browseListing({ folders: [] }), null);
    assert.equal(core.browseListing(null), null);
    assert.equal(core.browseListing({ path: '' }), null);
  });

  it('the folders the server found are complete unless it says otherwise', () => {
    assert.deepEqual(core.browseFound({ folders: [{ path: '/a' }], complete: false }).complete, false);
    assert.deepEqual(core.browseFound({ folders: [] }), { folders: [], complete: true });
    assert.deepEqual(core.browseFound(undefined), { folders: [], complete: true });
  });
});

describe('browseCrumbs (the path of the folder, one place to go to each)', () => {
  const labels = (crumbs) => crumbs.map((crumb) => crumb.label);

  it('inside the home folder it starts with ~ (which is the home folder), and every place knows its own path', () => {
    const crumbs = core.browseCrumbs('/home/ana/belgeler/raporlar', '/home/ana');
    assert.deepEqual(labels(crumbs), ['~', 'belgeler', 'raporlar']);
    assert.deepEqual(crumbs.map((crumb) => crumb.path), ['/home/ana', '/home/ana/belgeler', '/home/ana/belgeler/raporlar']);
    assert.deepEqual(crumbs.map((crumb) => crumb.kind), ['home', 'folder', 'folder']);
  });

  it('the home folder itself is ~ alone, with or without a separator at the end of either path', () => {
    assert.deepEqual(labels(core.browseCrumbs('/home/ana', '/home/ana')), ['~']);
    assert.deepEqual(labels(core.browseCrumbs('/home/ana/', '/home/ana')), ['~']);
    assert.deepEqual(labels(core.browseCrumbs('/home/ana', '/home/ana/')), ['~']);
  });

  it('outside it the path starts at the root; a folder whose name only begins like the home folder is not inside it', () => {
    const crumbs = core.browseCrumbs('/mnt/depo/arsiv', '/home/ana');
    assert.deepEqual(labels(crumbs), ['/', 'mnt', 'depo', 'arsiv']);
    assert.deepEqual(crumbs.map((crumb) => crumb.path), ['/', '/mnt', '/mnt/depo', '/mnt/depo/arsiv']);
    assert.equal(crumbs[0].kind, 'root');
    assert.deepEqual(labels(core.browseCrumbs('/home/ana-maria/x', '/home/ana')), ['/', 'home', 'ana-maria', 'x']);
    assert.deepEqual(labels(core.browseCrumbs('/', '/home/ana')), ['/']);
  });

  it('a server that does not say where home is has no ~; a Windows path has its drive for a root', () => {
    assert.deepEqual(labels(core.browseCrumbs('/home/ana/x', null)), ['/', 'home', 'ana', 'x']);
    const windows = core.browseCrumbs('D:\\veri\\notlar', 'C:\\Users\\ana');
    assert.deepEqual(labels(windows), ['D:', 'veri', 'notlar']);
    assert.deepEqual(windows.map((crumb) => crumb.path), ['D:\\', 'D:\\veri', 'D:\\veri\\notlar']);
    assert.deepEqual(core.browseCrumbs('C:\\Users\\ana\\Belgeler', 'C:\\Users\\ana').map((crumb) => crumb.path), ['C:\\Users\\ana', 'C:\\Users\\ana\\Belgeler']);
    assert.deepEqual(core.browseCrumbs('', '/home/ana'), []);
  });
});

describe('sameFolder and addBlock (why the folder that is shown cannot be added)', () => {
  const listing = (path, extra = {}) => ({ path, parent: path === '/' ? null : '/x', home: '/home/ana', folders: [], truncated: false, display: path, ...extra });

  it('two paths are the same folder when they differ only by the separator at the end', () => {
    assert.equal(core.sameFolder('/a/b', '/a/b/'), true);
    assert.equal(core.sameFolder('/a/b', '/a/c'), false);
    assert.equal(core.sameFolder('/', '/'), true);
    assert.equal(core.sameFolder('', ''), false);
    assert.equal(core.sameFolder('/a', undefined), false);
  });

  it('the home folder itself, the root and a folder that is listed already cannot be added; any other can', () => {
    assert.equal(core.addBlock(listing('/home/ana'), []), 'home');
    assert.equal(core.addBlock(listing('/'), []), 'root');
    assert.equal(core.addBlock(listing('/srv/veri'), [{ path: '/srv/veri/' }]), 'listed');
    assert.equal(core.addBlock(listing('/srv/veri'), [{ path: '/srv/baska' }, { path: '/home/ana' }]), null);
    assert.equal(core.addBlock(listing('/srv/veri'), undefined), null);
  });

  it('there is nothing to say before a folder has come, and a server that does not say where home is blocks the root only', () => {
    assert.equal(core.addBlock(null, []), null);
    assert.equal(core.addBlock(undefined, []), null);
    assert.equal(core.addBlock(listing('/home/ana', { home: null }), []), null);
    assert.equal(core.addBlock(listing('/', { home: null }), []), 'root');
  });

  it('a user who has no home folder starts at the root, and it is the root that cannot be added, not "the home folder itself"', () => {
    assert.equal(core.addBlock(listing('/', { home: '/' }), []), 'root');
  });
});

describe('listboxIndex (where the selected row of the list of folders goes)', () => {
  it('the arrows move by one, Home and End go to the ends, the page keys by a page', () => {
    assert.equal(core.listboxIndex('ArrowDown', 2, 10), 3);
    assert.equal(core.listboxIndex('ArrowUp', 2, 10), 1);
    assert.equal(core.listboxIndex('Home', 5, 10), 0);
    assert.equal(core.listboxIndex('End', 5, 10), 9);
    assert.equal(core.listboxIndex('PageDown', 2, 30), 10);
    assert.equal(core.listboxIndex('PageUp', 12, 30), 4);
    assert.equal(core.listboxIndex('PageDown', 2, 30, 5), 7);
  });

  it('the ends do not wrap: a list can be hundreds long, and the down arrow on the last must not jump to the first', () => {
    assert.equal(core.listboxIndex('ArrowDown', 9, 10), 9);
    assert.equal(core.listboxIndex('ArrowUp', 0, 10), 0);
    assert.equal(core.listboxIndex('PageDown', 8, 10), 9);
    assert.equal(core.listboxIndex('PageUp', 3, 10), 0);
  });

  it('a key that moves nothing, and a list with no row, give null', () => {
    assert.equal(core.listboxIndex('a', 2, 10), null);
    assert.equal(core.listboxIndex('Enter', 2, 10), null);
    assert.equal(core.listboxIndex('ArrowDown', 0, 0), null);
    assert.equal(core.listboxIndex('Home', 0, 0), null);
  });
});

describe('parseBrowseState and browseStateJson (what the picker remembers for the tab)', () => {
  it('what was written is read back', () => {
    const json = core.browseStateJson({ path: '/home/ana/belgeler', hidden: true });
    assert.deepEqual(core.parseBrowseState(json), { path: '/home/ana/belgeler', hidden: true });
    assert.deepEqual(core.parseBrowseState(core.browseStateJson({ path: null, hidden: false })), { path: null, hidden: false });
  });

  it('nothing, blocked storage, a text that is not JSON or JSON of another shape is the start: no folder, nothing hidden', () => {
    const start = { path: null, hidden: false };
    for (const text of [null, undefined, '', 'not json', '[]', '42', '"x"', '{"path":7,"hidden":"yes"}', '{"path":"","hidden":1}']) {
      assert.deepEqual(core.parseBrowseState(text), start, String(text));
    }
  });

  it('only a real boolean is a switch that is on, and only a text is a folder', () => {
    assert.deepEqual(core.parseBrowseState('{"path":"/x","hidden":true}'), { path: '/x', hidden: true });
    assert.deepEqual(core.parseBrowseState('{"path":"/x","hidden":"true"}'), { path: '/x', hidden: false });
    assert.equal(core.browseStateJson({ path: '', hidden: 1 }), '{"path":null,"hidden":false}');
  });
});
