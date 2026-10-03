// Tests for the UI dictionary (src/Pusula/wwwroot/js/i18n.js): both languages are complete,
// they agree with the spec's display-name table, and every key the page uses exists.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import * as i18n from '../../src/Pusula/wwwroot/js/i18n.js';
import * as core from '../../src/Pusula/wwwroot/js/core.js';

const wwwroot = fileURLToPath(new URL('../../src/Pusula/wwwroot/', import.meta.url));
const appSource = readFileSync(`${wwwroot}js/app.js`, 'utf8');
const htmlSource = readFileSync(`${wwwroot}index.html`, 'utf8');

const tr = i18n.DICT.tr;
const en = i18n.DICT.en;

function placeholders(text) {
  return [...text.matchAll(/\{(\w+)\}/g)].map((match) => match[1]).sort();
}

describe('dictionary completeness', () => {
  it('has exactly the languages tr and en', () => {
    assert.deepEqual(i18n.LANGS, ['tr', 'en']);
    assert.deepEqual(Object.keys(i18n.DICT).sort(), ['en', 'tr']);
  });

  it('tr and en define the same keys', () => {
    const onlyTr = Object.keys(tr).filter((key) => !(key in en));
    const onlyEn = Object.keys(en).filter((key) => !(key in tr));
    assert.deepEqual(onlyTr, [], 'keys missing in en');
    assert.deepEqual(onlyEn, [], 'keys missing in tr');
  });

  it('every value is a non-empty string without surrounding whitespace', () => {
    for (const [lang, dict] of Object.entries(i18n.DICT)) {
      for (const [key, value] of Object.entries(dict)) {
        assert.equal(typeof value, 'string', `${lang}:${key}`);
        assert.ok(value.length > 0, `${lang}:${key} is empty`);
        assert.equal(value, value.trim(), `${lang}:${key} has surrounding whitespace`);
      }
    }
  });

  it('both languages use the same placeholders for each key', () => {
    for (const key of Object.keys(tr)) {
      assert.deepEqual(placeholders(tr[key]), placeholders(en[key]), key);
    }
  });

  it('every enum value the API can send has a name, a tooltip or a label', () => {
    for (const dict of [tr, en]) {
      for (const layer of core.LAYERS) assert.ok(`layer.${layer}` in dict, `layer.${layer}`);
      for (const mode of core.LOAD_MODES) {
        assert.ok(`load.${mode}` in dict, `load.${mode}`);
        assert.ok(`loadShort.${mode}` in dict, `loadShort.${mode}`);
      }
      for (const status of core.LINK_STATUSES) {
        assert.ok(`status.${status}` in dict, `status.${status}`);
        assert.ok(`tip.${status}` in dict, `tip.${status}`);
      }
      for (const kind of core.LINK_KINDS) assert.ok(`kind.${kind}` in dict, `kind.${kind}`);
    }
  });
});

describe('display names from spec 7', () => {
  const table = {
    'layer.ClaudeMd': ['CLAUDE.md', 'CLAUDE.md'],
    'layer.Rule': ['Kural', 'Rule'],
    'layer.PathRule': ['Koşullu kural', 'Path rule'],
    'layer.Skill': ['Skill', 'Skill'],
    'layer.SkillResource': ['Skill eki', 'Skill resource'],
    'layer.Agent': ['Ajan', 'Agent'],
    'layer.Command': ['Komut', 'Command'],
    'layer.OutputStyle': ['Çıktı stili', 'Output style'],
    'layer.MemoryIndex': ['Memory indeksi', 'Memory index'],
    'layer.Memory': ['Memory kaydı', 'Memory'],
    'layer.Reference': ['Referans', 'Reference'],
    'layer.Shared': ['Paylaşılan', 'Shared'],
    'layer.Other': ['Diğer', 'Other'],
    'load.EverySession': ['Her oturum', 'Every session'],
    'load.DescriptionEverySession': ['Açıklaması her oturum, gövdesi çağrılınca', 'Description every session, body on use'],
    'load.ProjectSession': ['Projede her oturum', 'Every session in its project'],
    'load.Conditional': ['Eşleşen dosya okununca', 'When a matching file is read'],
    'load.OnDemand': ['İstenince', 'On demand'],
    'load.UserInvoked': ['Yalnız kullanıcı çağırınca', 'Only when the user invokes it'],
    'load.Inactive': ['Etkin değil', 'Inactive'],
    'status.Resolved': ['bağlı', 'linked'],
    'status.NonMarkdown': ['Markdown değil', 'not Markdown'],
    'status.Broken': ['kırık', 'broken'],
    // The spec's "henüz yazılmamış" says what is missing only next to the word for a link (clarify, F2).
    'status.Pending': ['henüz yazılmamış bağ', 'link not written yet'],
    'status.External': ['kök dışında', 'outside root'],
    // Not a status the server sends: how a NonMarkdown link to a folder is shown.
    'status.Folder': ['klasör', 'folder'],
  };

  for (const [key, [turkish, english]] of Object.entries(table)) {
    it(`${key}`, () => {
      assert.equal(tr[key], turkish);
      assert.equal(en[key], english);
    });
  }
});

describe('overview wording', () => {
  it('the budget headline reads like the spec in both languages', () => {
    assert.equal(i18n.format(tr['budget.title'], { tokens: '~12.4K' }), 'Her oturum ~12.4K token (tahmini)');
    assert.equal(i18n.format(en['budget.title'], { tokens: '~12.4K' }), 'Every session ~12.4K tokens (estimated)');
  });

  it('the note under the band and the group names', () => {
    assert.equal(i18n.format(tr['budget.memoryNote'], { tokens: '~1.6K' }), '+ her projede MEMORY.md (en büyük ~1.6K)');
    assert.equal(i18n.format(tr['budget.group.Skill'], { n: 33 }), 'Skill açıklamaları (33)');
    assert.equal(i18n.format(tr['budget.group.Agent'], { n: 1 }), 'Ajan açıklamaları (1)');
    assert.equal(i18n.format(tr['budget.group.Command'], { n: 2 }), 'Komut açıklamaları (2)');
    assert.equal(i18n.format(en['budget.group.Skill'], { n: 33 }), 'Skill descriptions (33)');
  });

  it('the issue counts read "13 kırık bağ · 36 henüz yazılmamış bağ · 7 yetim dosya · 2 frontmatter hatası"', () => {
    i18n.setLang('tr');
    const chips = [['broken', 13], ['pending', 36], ['orphans', 7], ['frontmatter', 2]].map(([key, count]) => `${count} ${i18n.tn(`issues.chip.${key}`, count)}`);
    assert.equal(chips.join(' · '), '13 kırık bağ · 36 henüz yazılmamış bağ · 7 yetim dosya · 2 frontmatter hatası');
    i18n.setLang('en');
  });

  it('"show more" carries the number of hidden rows', () => {
    assert.equal(i18n.format(tr['more.show'], { n: 25 }), '+25 daha göster');
    assert.equal(i18n.format(en['more.show'], { n: 25 }), 'Show 25 more');
  });

  it('the old stat-card strings are gone', () => {
    for (const key of Object.keys(tr)) assert.ok(!key.startsWith('stat.'), key);
  });

  it('one sentence under the band says what is loaded and that ~ is an estimate', () => {
    for (const [dict, words] of [[tr, ['her oturum', 'açıklamalarını', '~', 'tahmin']], [en, ['every session', 'descriptions', '~', 'estimate']]]) {
      const text = dict['budget.explain'];
      assert.equal(text.split(/[.!?](?:\s|$)/).filter(Boolean).length, 1, `one sentence: ${text}`);
      for (const word of words) assert.ok(text.includes(word), `${word} in ${text}`);
    }
  });

  it('the number is an upper limit (the skill list can be shortened over its budget), in one sentence under the band and in one sentence in the tooltip of the title', () => {
    for (const [dict, words] of [[tr, ['üst sınır', 'skill listesi', 'bütçeyi aşınca', 'kısalabil']], [en, ['upper limit', 'skill list', 'budget', 'shortened']]]) {
      for (const key of ['budget.explain', 'budget.hint']) {
        const text = dict[key];
        assert.equal(text.split(/[.!?](?:\s|$)/).filter(Boolean).length, 1, `one sentence: ${text}`);
        for (const word of words.slice(0, 3)) assert.ok(text.includes(word), `${word} in ${key}: ${text}`);
      }
    }
    assert.ok(tr['budget.hint'].includes('Tahmini') && en['budget.hint'].includes('Estimated'), 'the tooltip still says it is an estimate');
  });

  it('the meta line reads "311 dosya · 08:29\'da indekslendi" and "311 files · indexed at 08:29", without a version or the folder', () => {
    assert.equal(i18n.format(tr['overview.indexed'], { time: "08:29'da" }), "08:29'da indekslendi");
    assert.equal(i18n.format(en['overview.indexed'], { time: '08:29' }), 'indexed at 08:29');
    for (const key of ['overview.title', 'overview.root', 'overview.version']) {
      assert.ok(!(key in tr) && !(key in en), `${key} is gone: the title is the budget sentence, the folder is in the top bar, the version means nothing to a reader`);
    }
  });

  it('the "10 heaviest files" table is gone with its strings', () => {
    for (const key of ['heaviest.title', 'heaviest.none', 'col.everySession']) assert.ok(!(key in tr) && !(key in en), key);
    assert.equal(tr['budget.none'], 'Her oturum yüklenen dosya yok');
    assert.equal(en['budget.none'], 'No file loads every session');
  });

  it('the folded layers say how many there are and how many tokens they hold', () => {
    i18n.setLang('tr');
    assert.equal(i18n.tn('layers.rest', 8, { tokens: '~231K' }), 'Yalnız gerektiğinde yüklenenler (8 katman, ~231K token)');
    assert.equal(i18n.tn('layers.rest', 1, { tokens: '~5' }), 'Yalnız gerektiğinde yüklenenler (1 katman, ~5 token)');
    i18n.setLang('en');
    assert.equal(i18n.tn('layers.rest', 8, { tokens: '~231K' }), 'Loaded only when needed (8 layers, ~231K tokens)');
    assert.equal(i18n.tn('layers.rest', 1, { tokens: '~5' }), 'Loaded only when needed (1 layer, ~5 tokens)');
  });
});

describe('clarify wording (F2)', () => {

  it('a file reads "Her oturum · ~3.5K token" under its path', () => {
    assert.equal(i18n.format(tr['file.loadLine'], { load: tr['load.EverySession'], tokens: '~3.5K' }), 'Her oturum · ~3.5K token');
    assert.equal(i18n.format(en['file.loadLine'], { load: en['load.EverySession'], tokens: '~3.5K' }), 'Every session · ~3.5K tokens');
  });

  it('the tree tooltip says both numbers in full, or only the total', () => {
    assert.equal(i18n.format(tr['tokens.pair'], { every: '~3.5K', total: '~3.5K' }), 'Her oturum ~3.5K, toplam ~3.5K token (tahmini)');
    assert.equal(i18n.format(tr['tokens.total'], { total: '~1.2K' }), 'Toplam ~1.2K token (tahmini)');
    assert.equal(i18n.format(en['tokens.pair'], { every: '~93', total: '~1.2K' }), 'Every session ~93, total ~1.2K tokens (estimated)');
    assert.equal(i18n.format(en['tokens.total'], { total: '0' }), 'Total 0 tokens (estimated)');
  });

  it('a folder is a folder, with its own tooltip', () => {
    assert.equal(i18n.format(tr['tip.Folder'], { target: 'rules' }), 'Klasör: rules');
    assert.equal(i18n.format(en['tip.Folder'], { target: 'rules' }), 'Folder: rules');
  });

  it('the line wording keeps {lines} for the page to fill with links, and tells one from many in English only', () => {
    i18n.setLang('en');
    assert.equal(i18n.tn('info.lines', 1), 'line {lines}');
    assert.equal(i18n.tn('info.lines', 2), 'lines {lines}');
    i18n.setLang('tr');
    for (const count of [1, 2, 12]) assert.equal(i18n.tn('info.lines', count), 'satır {lines}', String(count));
    i18n.setLang('en');
    assert.ok(!('info.line' in en) && !('info.line' in tr), 'the single-line key was replaced by info.lines');
  });

  it('the overview link has a name for the icon that stands in for its text', () => {
    assert.ok(htmlSource.includes('data-i18n-aria-label="nav.overview"'));
    assert.ok(htmlSource.includes('data-i18n-title="nav.overview"'));
    assert.equal(tr['nav.overview'], 'Genel bakış');
    assert.equal(tr['nav.skip'], 'İçeriğe geç');
    assert.equal(en['nav.skip'], 'Skip to content');
  });
});

describe('live change wording (R4-2)', () => {
  it('a time ago is short, in both languages', () => {
    assert.equal(tr['age.now'], 'şimdi');
    assert.equal(i18n.format(tr['age.s'], { n: 12 }), '12 sn önce');
    assert.equal(i18n.format(tr['age.m'], { n: 3 }), '3 dk önce');
    assert.equal(i18n.format(tr['age.h'], { n: 2 }), '2 sa önce');
    assert.equal(i18n.format(tr['age.d'], { n: 1 }), '1 gün önce');
    assert.equal(en['age.now'], 'just now');
    assert.equal(i18n.format(en['age.s'], { n: 12 }), '12 s ago');
    assert.equal(i18n.format(en['age.m'], { n: 3 }), '3 min ago');
    assert.equal(i18n.format(en['age.h'], { n: 2 }), '2 h ago');
    assert.equal(i18n.format(en['age.d'], { n: 1 }), '1 d ago');
  });

  it('every unit of ageParts has a string in both languages', () => {
    for (const unit of ['now', 's', 'm', 'h', 'd']) {
      assert.ok(`age.${unit}` in tr && `age.${unit}` in en, unit);
      assert.ok(core.ageParts(unit === 'now' ? 0 : { s: 5_000, m: 60_000, h: 3_600_000, d: 86_400_000 }[unit]).unit === unit);
    }
  });

  it('the top bar reads "son: CLAUDE.md · 12 sn önce": the label, the name, the time', () => {
    assert.equal(tr['last.label'], 'son:');
    assert.equal(en['last.label'], 'last:');
    assert.equal(tr['last.removed'], 'silindi');
    assert.equal(en['last.removed'], 'deleted');
  });

  it('the open file says "güncellendi 10:42" / "updated 10:42"', () => {
    assert.equal(i18n.format(tr['file.updated'], { time: '10:42' }), 'güncellendi 10:42');
    assert.equal(i18n.format(en['file.updated'], { time: '10:42' }), 'updated 10:42');
  });

  it('the pill says which side the change is on', () => {
    assert.equal(tr['pill.below'], 'değişiklik aşağıda');
    assert.equal(tr['pill.above'], 'değişiklik yukarıda');
    assert.equal(en['pill.below'], 'change below');
    assert.equal(en['pill.above'], 'change above');
  });

  it('what a screen reader is told: "Kırık bağlar 13 → 12", "CLAUDE.md güncellendi 10:42"', () => {
    assert.equal(i18n.format(tr['announce.count'], { label: tr['issues.broken'], from: 13, to: 12 }), 'Kırık bağlar 13 → 12');
    assert.equal(i18n.format(en['announce.count'], { label: en['issues.broken'], from: 13, to: 12 }), 'Broken links 13 → 12');
    assert.equal(i18n.format(tr['announce.updated'], { name: 'CLAUDE.md', time: '10:42' }), 'CLAUDE.md güncellendi 10:42');
    assert.equal(i18n.format(en['announce.updated'], { name: 'CLAUDE.md', time: '10:42' }), 'CLAUDE.md updated 10:42');
    assert.equal(i18n.format(tr['announce.budget'], { from: 12365, to: 12485 }), 'Her oturum 12365 → 12485 token');
    assert.equal(i18n.format(en['announce.budget'], { from: 12365, to: 12485 }), 'Every session 12365 → 12485 tokens');
  });
});

describe('short load-mode labels (table cells)', () => {
  const short = {
    'loadShort.EverySession': ['Her oturum', 'Every session'],
    'loadShort.DescriptionEverySession': ['Açıklaması her oturum', 'Description every session'],
    'loadShort.ProjectSession': ['Projede her oturum', 'Every session in project'],
    'loadShort.Conditional': ['Koşullu', 'Conditional'],
    'loadShort.OnDemand': ['İstenince', 'On demand'],
    'loadShort.UserInvoked': ['Kullanıcı çağırınca', 'User invoked'],
    'loadShort.Inactive': ['Etkin değil', 'Inactive'],
  };

  for (const [key, [turkish, english]] of Object.entries(short)) {
    it(`${key}`, () => {
      assert.equal(tr[key], turkish);
      assert.equal(en[key], english);
    });
  }

  it('each short label is shorter than (or as short as) the full label, so it can replace it in a table', () => {
    for (const mode of core.LOAD_MODES) {
      for (const dict of [tr, en]) {
        assert.ok(dict[`loadShort.${mode}`].length <= dict[`load.${mode}`].length, mode);
      }
    }
  });

  it('the full labels are unchanged (legend and info panel keep using them)', () => {
    assert.equal(tr['load.DescriptionEverySession'], 'Açıklaması her oturum, gövdesi çağrılınca');
    assert.equal(en['load.ProjectSession'], 'Every session in its project');
  });
});

describe('every key is defined and every key is used', () => {
  // Keys used literally: t('key', ...) in app.js and data-i18n*="key" in index.html.
  const literalKeys = new Set([
    ...[...appSource.matchAll(/\bt\('([\w.]+)'/g)].map((match) => match[1]),
    ...[...htmlSource.matchAll(/data-i18n(?:-title|-aria-label)?="([\w.]+)"/g)].map((match) => match[1]),
  ]);
  // Keys built from an enum value or a name: t(`layer.${...}`), t(`budget.group.${...}`).
  const dynamicPrefixes = new Set([...appSource.matchAll(/\bt\(`([\w.]+)\.\$\{/g)].map((match) => match[1]));
  // Keys with a count are read with tn(): each is stored as `<key>.one` and `<key>.other`.
  const pluralKeys = new Set([...appSource.matchAll(/\btn\('([\w.]+)'/g)].map((match) => match[1]));
  const pluralPrefixes = new Set([...appSource.matchAll(/\btn\(`([\w.]+)\.\$\{/g)].map((match) => match[1]));

  const pluralBase = (key) => (/\.(?:one|other)$/.test(key) ? key.replace(/\.(?:one|other)$/, '') : null);
  const isUsed = (key) => {
    const base = pluralBase(key);
    return literalKeys.has(key)
      || [...dynamicPrefixes].some((prefix) => key.startsWith(`${prefix}.`))
      || (base !== null && (pluralKeys.has(base) || [...pluralPrefixes].some((prefix) => base.startsWith(`${prefix}.`))));
  };

  it('finds the usages (guards the scan itself)', () => {
    assert.ok(literalKeys.size > 40, `found ${literalKeys.size} literal keys`);
    for (const prefix of ['layer', 'load', 'loadShort', 'status', 'tip', 'kind', 'live', 'lang', 'budget.group', 'issues']) {
      assert.ok(dynamicPrefixes.has(prefix), `dynamic prefix ${prefix}`);
    }
    assert.ok(pluralKeys.has('overview.files'), 'plural key overview.files');
    assert.ok(pluralPrefixes.has('issues.chip'), 'plural prefix issues.chip');
  });

  it('every key used by the page exists in the dictionary', () => {
    for (const key of literalKeys) assert.ok(key in en, `${key} is used but not defined`);
    for (const base of pluralKeys) {
      assert.ok(`${base}.one` in en && `${base}.other` in en, `${base} is read with tn() but has no .one / .other`);
    }
  });

  it('every dictionary key is used by the page (a note variant is used where the key it replaces is)', () => {
    for (const key of Object.keys(en)) assert.ok(isUsed(key.replace(/\.note$/, '')), `${key} is defined but never used`);
  });
});

describe('plural keys (tn)', () => {
  const bases = [...new Set(Object.keys(en).map((key) => key.match(/^(.*)\.(?:one|other)$/)?.[1]).filter(Boolean))];

  it('there are some, and each has both forms in both languages', () => {
    assert.ok(bases.length >= 5, bases.join(', '));
    for (const base of bases) {
      for (const dict of [tr, en]) {
        assert.ok(`${base}.one` in dict && `${base}.other` in dict, `${base} needs .one and .other`);
      }
    }
  });

  it('the two forms of a key use the same placeholders in both languages', () => {
    for (const base of bases) {
      const reference = placeholders(en[`${base}.other`]);
      for (const dict of [tr, en]) {
        for (const form of ['one', 'other']) assert.deepEqual(placeholders(dict[`${base}.${form}`]), reference, `${base}.${form}`);
      }
    }
  });

  it('English tells one from many', () => {
    i18n.setLang('en');
    assert.equal(i18n.tn('overview.files', 1), '1 file');
    assert.equal(i18n.tn('overview.files', 311), '311 files');
    assert.equal(i18n.tn('overview.files', 0), '0 files');
    assert.equal(i18n.tn('issues.chip.broken', 1), 'broken link');
    assert.equal(i18n.tn('issues.chip.broken', 13), 'broken links');
    assert.equal(i18n.tn('issues.chip.orphans', 1), 'orphan file');
    assert.equal(i18n.tn('issues.chip.pending', 2), 'links not written yet');
    assert.equal(i18n.tn('issues.chip.frontmatter', 1), 'frontmatter error');
  });

  it('Turkish does not inflect after a number', () => {
    i18n.setLang('tr');
    for (const count of [0, 1, 2, 13, 311]) {
      assert.equal(i18n.tn('overview.files', count), `${count} dosya`);
      assert.equal(i18n.tn('issues.chip.broken', count), 'kırık bağ');
    }
    i18n.setLang('en');
  });

  it('{n} is the count and other placeholders are filled in', () => {
    i18n.setLang('en');
    assert.equal(i18n.tn('overview.files', 7, { n: 'ignored' }), '7 files', 'the count wins over a stray n');
    assert.equal(i18n.tn('no.such.key', 3), 'no.such.key', 'an unknown key comes back as the key');
  });
});

describe('language selection', () => {
  it('detectLang picks tr for Turkish browsers and en otherwise', () => {
    assert.equal(i18n.detectLang('tr'), 'tr');
    assert.equal(i18n.detectLang('tr-TR'), 'tr');
    assert.equal(i18n.detectLang('TR-tr'), 'tr');
    assert.equal(i18n.detectLang('en-US'), 'en');
    assert.equal(i18n.detectLang('de'), 'en');
    assert.equal(i18n.detectLang(''), 'en');
    assert.equal(i18n.detectLang(undefined), 'en');
  });

  it('format fills placeholders and leaves unknown ones as written', () => {
    assert.equal(i18n.format('a {x} b {y}', { x: 1 }), 'a 1 b {y}');
    assert.equal(i18n.format('plain'), 'plain');
    assert.equal(i18n.format('{n} items', { n: 0 }), '0 items');
  });

  it('t follows setLang and works without storage', () => {
    i18n.setLang('tr');
    assert.equal(i18n.getLang(), 'tr');
    assert.equal(i18n.t('nav.overview'), 'Genel bakış');
    assert.equal(i18n.t('tree.broken', { n: 3 }), 'Kırık bağ: 3');
    i18n.setLang('en');
    assert.equal(i18n.getLang(), 'en');
    assert.equal(i18n.t('nav.overview'), 'Overview');
    assert.equal(i18n.t('tree.broken', { n: 3 }), 'Broken links: 3');
  });

  it('t returns the key for an unknown key', () => {
    assert.equal(i18n.t('no.such.key'), 'no.such.key');
  });

  it('setLang ignores an unsupported language', () => {
    i18n.setLang('en');
    i18n.setLang('xx');
    assert.equal(i18n.getLang(), 'en');
  });

  it('initLang falls back to the browser language when nothing is stored', () => {
    const lang = i18n.initLang();
    assert.ok(['tr', 'en'].includes(lang));
    assert.equal(i18n.getLang(), lang);
  });
});

// ---------------------------------------------------------------------------------------------
// R5: errors and what is only to be reviewed, error messages, the quick opener
// ---------------------------------------------------------------------------------------------

describe('errors and what is to be reviewed (R5-B)', () => {
  it('the two sections are called Hatalar / Errors and İncelenecekler / To review', () => {
    assert.equal(tr['issues.errors'], 'Hatalar');
    assert.equal(tr['issues.review'], 'İncelenecekler');
    assert.equal(en['issues.errors'], 'Errors');
    assert.equal(en['issues.review'], 'To review');
  });

  it('nothing wrong is said calmly', () => {
    assert.equal(tr['issues.errorsNone'], 'Hata yok');
    assert.equal(en['issues.errorsNone'], 'No errors');
  });

  it('the groups keep their names', () => {
    assert.deepEqual([tr['issues.broken'], tr['issues.pending'], tr['issues.orphans'], tr['issues.frontmatter']], ['Kırık bağlar', 'Henüz yazılmamış bağlar', 'Yetim dosyalar', 'Frontmatter hataları']);
    assert.deepEqual([en['issues.broken'], en['issues.pending'], en['issues.orphans'], en['issues.frontmatter']], ['Broken links', 'Links not written yet', 'Orphan files', 'Frontmatter errors']);
  });

  it('a group to review says what it is in one sentence, and that it is not an error', () => {
    for (const [dict, notError] of [[tr, 'hata değil'], [en, 'not an error']]) {
      for (const key of ['issues.help.pending', 'issues.help.orphans']) {
        const text = dict[key];
        assert.equal(text.split(/[.!?](?:\s|$)/).filter(Boolean).length, 1, `one sentence: ${text}`);
      }
      assert.ok(dict['issues.help.pending'].includes(notError), dict['issues.help.pending']);
    }
    assert.equal(tr['issues.help.pending'], "Henüz yazılmamış bağ: Claude'un memory'de andığı ama henüz yazmadığı not — hata değil, yapılacak listesi.");
    assert.equal(tr['issues.help.orphans'], 'Yetim: hiçbir yerden bağ almayan, yalnız istenince yüklenen dosya.');
  });

  it('the words of the old single section and of the empty group are gone', () => {
    for (const key of ['issues.title', 'issues.none', 'issues.allClear']) assert.ok(!(key in tr) && !(key in en), key);
  });
});

describe('error messages in words (R5-C)', () => {
  it('no connection: what happened, why it may be, that the page refreshes by itself (Turkish as the planner wrote it)', () => {
    assert.equal(tr['error.network'], 'pusula sunucusuna ulaşılamıyor — sunucu kapalı olabilir ya da ağ bağlantısı yok. Bağlantı gelince sayfa kendiliğinden yenilenir.');
    assert.ok(en['error.network'].includes("Can't reach the pusula server") && en['error.network'].includes('refreshes by itself'));
  });

  it('the words of a failure never carry the browser\'s own message', () => {
    for (const dict of [tr, en]) {
      for (const key of ['error.network', 'error.server', 'error.request', 'error.unexpected', 'stale.text']) {
        assert.ok(!/Failed to fetch|NetworkError|Load failed|\{message\}/i.test(dict[key]), `${key}: ${dict[key]}`);
      }
    }
    assert.ok(!('error.load' in tr) && !('error.load' in en), 'the message-carrying string is gone');
  });

  it('a 5xx names its status and says to try again', () => {
    assert.equal(i18n.format(tr['error.server'], { status: 500 }), 'Sunucu bir hata döndü (HTTP 500). Tekrar deneyin.');
    assert.equal(i18n.format(en['error.server'], { status: 503 }), 'The server returned an error (HTTP 503). Try again.');
  });

  it('a refused request names its status; an unreadable answer needs none', () => {
    assert.equal(i18n.format(tr['error.request'], { status: 403 }), 'Sunucu isteği kabul etmedi (HTTP 403).');
    assert.equal(i18n.format(en['error.request'], { status: 403 }), 'The server did not accept the request (HTTP 403).');
    assert.deepEqual(placeholders(tr['error.unexpected']), []);
    assert.deepEqual(placeholders(en['error.unexpected']), []);
  });

  it('every kind of failure loadFailure can say has a string in both languages', () => {
    for (const kind of ['network', 'server', 'request', 'unexpected']) assert.ok(`error.${kind}` in tr && `error.${kind}` in en, kind);
  });

  it('the retry button and the time of the last attempt', () => {
    assert.equal(tr['error.retry'], 'Yeniden dene');
    assert.equal(i18n.format(tr['error.attempt'], { time: '11:55' }), 'Son deneme 11:55');
    assert.equal(i18n.format(en['error.attempt'], { time: '11:55' }), 'Last attempt 11:55');
  });

  it('the strip of stale content: "Bağlantı koptu · içerik 11:53 itibarıyla"', () => {
    assert.equal(i18n.format(tr['stale.text'], { time: '11:53' }), 'Bağlantı koptu · içerik 11:53 itibarıyla');
    assert.equal(i18n.format(en['stale.text'], { time: '11:53' }), 'Connection lost · content as of 11:53');
  });

  it('a frontmatter that was read in part, or not at all, with its line, its hint and its details', () => {
    assert.equal(tr['fm.partial'], 'Frontmatter kısmen okundu');
    assert.equal(tr['fm.failed'], 'Frontmatter okunamadı');
    assert.equal(i18n.format(tr['fm.line'], { line: 3 }), 'Satır 3');
    assert.equal(i18n.format(en['fm.line'], { line: 3 }), 'Line 3');
    assert.equal(tr['fm.hint'], "Değerde ':' ya da '#' varsa değeri tırnak içine alın.");
    assert.equal(en['fm.hint'], "If a value contains ':' or '#', put the value in quotes.");
    assert.equal(tr['fm.detail'], 'Ayrıntı');
    assert.equal(en['fm.detail'], 'Details');
    assert.ok(!('file.frontmatterError' in tr) && !('file.frontmatterError' in en), 'the old one-string wording is gone');
  });
});

describe('the tree tools and the quick opener (R5-A, R5-D)', () => {
  it('the tools of the tree strip have names that say their action', () => {
    assert.equal(tr['tree.collapseAll'], 'Tümünü daralt');
    assert.equal(tr['tree.revealOpen'], 'Açık dosyayı göster');
    assert.equal(en['tree.collapseAll'], 'Collapse all');
    assert.equal(en['tree.revealOpen'], 'Show open file');
  });

  it('the search button names its shortcut', () => {
    assert.equal(i18n.format(tr['quick.open'], { key: 'Ctrl+K' }), 'Dosya ara (Ctrl+K)');
    assert.equal(i18n.format(en['quick.open'], { key: 'Cmd+K' }), 'Find file (Cmd+K)');
  });

  it('the dialog, its field and its close button have names', () => {
    assert.equal(tr['quick.title'], 'Dosya aç');
    assert.equal(en['quick.title'], 'Open file');
    assert.ok(tr['quick.label'].length > 0 && en['quick.label'].length > 0);
    assert.equal(tr['quick.close'], 'Kapat');
    assert.equal(en['quick.close'], 'Close');
    assert.ok(htmlSource.includes('data-i18n-aria-label="quick.title"') && htmlSource.includes('data-i18n-aria-label="quick.label"') && htmlSource.includes('data-i18n-aria-label="quick.close"'));
  });

  it('the count of results tells one from many in English, and does not inflect in Turkish', () => {
    i18n.setLang('en');
    assert.equal(i18n.tn('quick.count', 1), '1 result');
    assert.equal(i18n.tn('quick.count', 12), '12 results');
    i18n.setLang('tr');
    for (const count of [1, 2, 12]) assert.equal(i18n.tn('quick.count', count), `${count} sonuç`);
    i18n.setLang('en');
  });

  it('what the list says when there is nothing to list: recent files, no match, more than shown, nothing opened yet', () => {
    assert.equal(tr['quick.recent'], 'Son açılanlar');
    assert.equal(en['quick.recent'], 'Recently opened');
    assert.equal(tr['quick.none'], 'Eşleşen dosya yok');
    assert.equal(i18n.format(tr['quick.more'], { n: 240 }), 'Başka 240 sonuç var, aramayı daraltın');
    assert.equal(i18n.format(en['quick.more'], { n: 240 }), '240 more results, narrow the search');
    assert.ok(tr['quick.empty'].includes('Henüz') && en['quick.empty'].includes('No file opened'));
    assert.equal(tr['quick.placeholder'], 'Dosya ara…');
  });

  it('it finds files by their name, path or project, it does not search what files say: no string promises more', () => {
    for (const key of Object.keys(tr).filter((name) => name.startsWith('quick.'))) {
      assert.ok(!/içerik ara|tam metin|full.?text|search the text/i.test(`${tr[key]} ${en[key]}`), key);
    }
  });
});

// ---------------------------------------------------------------------------------------------
// Sources and notes
// ---------------------------------------------------------------------------------------------

describe('note variants (what reads differently in a source of notes)', () => {
  const variants = Object.keys(en).filter((key) => key.endsWith('.note'));

  it('there are some, in both languages', () => {
    assert.ok(variants.length >= 10, variants.join(', '));
    assert.deepEqual(Object.keys(tr).filter((key) => key.endsWith('.note')).sort(), [...variants].sort());
  });

  it('each replaces a key that exists, and says it with the same placeholders', () => {
    for (const key of variants) {
      const base = key.replace(/\.note$/, '');
      for (const dict of [tr, en]) {
        assert.ok(base in dict, `${key} replaces ${base}, which is not defined`);
        assert.deepEqual(placeholders(dict[key]), placeholders(dict[base]), key);
        assert.notEqual(dict[key], dict[base], `${key} reads the same as ${base}: it is not a variant`);
      }
    }
  });

  it('a plural key has a variant for both forms or for none', () => {
    for (const dict of [tr, en]) {
      for (const key of variants) {
        const match = /^(.*)\.(one|other)\.note$/.exec(key);
        if (!match) continue;
        const other = match[2] === 'one' ? 'other' : 'one';
        assert.ok(`${match[1]}.${other}.note` in dict, `${key} has no ${other} form`);
      }
    }
  });

  it('t and tn read the variant while the page shows notes, and the plain key otherwise', () => {
    i18n.setLang('tr');
    try {
      assert.equal(i18n.getVariant(), '');
      assert.equal(i18n.t('issues.pending'), 'Henüz yazılmamış bağlar');
      assert.equal(i18n.tn('overview.files', 161), '161 dosya');
      i18n.setVariant('note');
      assert.equal(i18n.getVariant(), 'note');
      assert.equal(i18n.t('issues.pending'), 'Henüz oluşturulmamış notlar');
      assert.equal(i18n.t('issues.orphans'), 'Yetim notlar');
      assert.equal(i18n.tn('overview.files', 161), '161 not');
      assert.equal(i18n.tn('issues.chip.pending', 3), 'henüz oluşturulmamış not');
      assert.equal(i18n.t('tip.Pending', { target: 'x' }), 'Henüz oluşturulmamış not: bu adla bir not yok (x)');
      assert.equal(i18n.t('status.Pending'), 'henüz oluşturulmamış not');
    } finally {
      i18n.setVariant('');
      i18n.setLang('en');
    }
    assert.equal(i18n.t('issues.pending'), 'Links not written yet');
  });

  it('a key that has no variant reads the same while the page shows notes', () => {
    i18n.setVariant('note');
    try {
      assert.equal(i18n.t('issues.broken'), 'Broken links');
      assert.equal(i18n.t('nav.overview'), 'Overview');
      assert.equal(i18n.tn('overview.folders', 3), '3 folders');
    } finally {
      i18n.setVariant('');
    }
  });

  it('English tells one note from many in the variants as well', () => {
    i18n.setLang('en');
    i18n.setVariant('note');
    try {
      assert.equal(i18n.tn('overview.files', 1), '1 note');
      assert.equal(i18n.tn('overview.files', 161), '161 notes');
      assert.equal(i18n.tn('issues.chip.orphans', 1), 'orphan note');
      assert.equal(i18n.tn('issues.chip.orphans', 7), 'orphan notes');
    } finally {
      i18n.setVariant('');
    }
  });

  it('anything but "note" is no variant', () => {
    i18n.setVariant('note');
    i18n.setVariant('nonsense');
    assert.equal(i18n.getVariant(), '');
  });

  it('a variant says what the source is: notes, not files, and what the review groups are in Obsidian\'s words', () => {
    assert.equal(tr['issues.help.pending.note'], "Bağı yazılmış ama notu henüz oluşturulmamış; Obsidian'da normaldir, yapılacaklar listesi gibi okunur.");
    assert.equal(tr['issues.help.orphans.note'], 'Hiç bağı olmayan notlar.');
    assert.equal(en['issues.help.orphans.note'], 'Notes with no links at all.');
    assert.equal(tr['issues.pending.note'], 'Henüz oluşturulmamış notlar');
    assert.equal(tr['issues.orphans.note'], 'Yetim notlar');
    for (const key of ['issues.help.pending.note', 'issues.help.orphans.note']) {
      for (const dict of [tr, en]) assert.equal(dict[key].split(/[.!?](?:\s|$)/).filter(Boolean).length, 1, `one sentence: ${dict[key]}`);
    }
  });
});

describe('the sources page and the source switch', () => {
  it('the page, and what a source is called in the top bar', () => {
    assert.equal(tr['sources.title'], 'Kaynaklar');
    assert.equal(en['sources.title'], 'Sources');
    assert.equal(i18n.format(tr['source.switch'], { name: '~/.claude' }), 'Kaynak: ~/.claude — değiştir');
    assert.equal(i18n.format(en['source.switch'], { name: '~/.claude' }), 'Source: ~/.claude — change');
    assert.ok(tr['source.pick'].length > 0 && en['source.pick'].length > 0);
  });

  it('a source is called by what it holds: files for a Claude configuration, notes for the others', () => {
    i18n.setLang('tr');
    assert.equal(i18n.tn('source.count.files', 311), '311 dosya');
    assert.equal(i18n.tn('source.count.notes', 161), '161 not');
    i18n.setLang('en');
    assert.equal(i18n.tn('source.count.files', 1), '1 file');
    assert.equal(i18n.tn('source.count.files', 311), '311 files');
    assert.equal(i18n.tn('source.count.notes', 1), '1 note');
    assert.equal(i18n.tn('source.count.notes', 161), '161 notes');
  });

  it('a count on the sources page is of the source that is listed, not of the one that is open: it has no note variant', () => {
    for (const key of Object.keys(en).filter((name) => name.startsWith('source.count.') || name.startsWith('source.profile.') || name.startsWith('sources.'))) {
      assert.ok(!key.endsWith('.note'), key);
      assert.ok(!(`${key}.note` in en), `${key} has a note variant, which would change it on the sources page`);
    }
    i18n.setVariant('note');
    try {
      assert.equal(i18n.tn('source.count.files', 311), '311 files');
    } finally {
      i18n.setVariant('');
    }
  });

  it('the profiles have their names: Claude configuration, Obsidian vault, Markdown folder', () => {
    assert.deepEqual(core.PROFILES.map((profile) => tr[`source.profile.${profile}`]), ['Claude yapılandırması', 'Obsidian vault', 'Markdown klasörü']);
    assert.deepEqual(core.PROFILES.map((profile) => en[`source.profile.${profile}`]), ['Claude configuration', 'Obsidian vault', 'Markdown folder']);
  });

  it('open and last opened', () => {
    assert.equal(tr['source.open'], 'açık');
    assert.equal(tr['source.last'], 'son açılan');
  });

  it('a source that cannot be read: its card says why in a few words, from the code the server gives, and plainly when there is none', () => {
    assert.equal(tr['source.unavailable'], 'Klasöre erişilemiyor.');
    assert.deepEqual(['FolderMissing', 'NotReadable', 'TooLarge'].map((code) => tr[`source.whyShort.${code}`]), ['Klasör bulunamadı.', 'Klasör okunamıyor.', 'Klasör gösterilemeyecek kadar büyük.']);
    assert.deepEqual(['FolderMissing', 'NotReadable', 'TooLarge'].map((code) => en[`source.whyShort.${code}`]), ['Folder not found.', "Can't read the folder.", 'The folder is too large to show.']);
    assert.ok(!('source.unavailableWhy' in tr) && !('source.unavailableWhy' in en), 'the server\'s own sentence is never shown: no string carries it');
    assert.equal(i18n.format(tr['sources.missing'], { id: 'eski' }), 'Bu kaynak listede yok: eski');
    assert.equal(i18n.format(en['sources.missing'], { id: 'old' }), 'This source is not in the list: old');
  });

  it('what the page of such a source says: what is the matter, the folder, and what to do (as the planner wrote it)', () => {
    assert.equal(i18n.format(tr['source.why.FolderMissing'], { path: '/x/notlar' }), 'Klasör bulunamadı: /x/notlar. Taşındıysa bu kaynağı kaldırıp yeni yolu ekleyin.');
    assert.equal(i18n.format(tr['source.why.NotReadable'], { path: '/x/notlar' }), 'Klasör okunamıyor: /x/notlar. Dosya izinlerini kontrol edin.');
    assert.equal(tr['source.why.TooLarge'], 'Klasör gösterilemeyecek kadar büyük. İçindeki daha küçük bir klasörü ekleyin.');
    assert.equal(tr['error.unavailable'], 'Bu kaynağın klasörüne erişilemiyor.');
    assert.equal(i18n.format(en['source.why.FolderMissing'], { path: '/x/notes' }), 'Folder not found: /x/notes. If it was moved, remove this source and add the new path.');
    assert.equal(i18n.format(en['source.why.NotReadable'], { path: '/x/notes' }), "Can't read the folder: /x/notes. Check the file permissions.");
    assert.equal(en['source.why.TooLarge'], 'The folder is too large to show. Add a smaller folder inside it.');
    assert.deepEqual(placeholders(tr['error.unavailable']), []);
    assert.deepEqual(placeholders(en['error.unavailable']), []);
  });

  it('every code the server sends has both forms in both languages', () => {
    for (const code of core.SOURCE_ERROR_CODES) {
      for (const key of [`source.why.${code}`, `source.whyShort.${code}`]) assert.ok(tr[key] && en[key], key);
    }
  });

  it('the indicator of a source that cannot be read is its own word, neither "offline" nor red', () => {
    assert.equal(tr['live.unavailable'], 'kaynak erişilemez');
    assert.equal(en['live.unavailable'], 'source unavailable');
    assert.equal(tr['live.offline'], 'çevrimdışı', 'a lost connection keeps its word');
    assert.equal(en['live.offline'], 'offline');
  });

  it('the help says how to add a source by hand, once: it is folded under "other ways", and the one sentence about the server is that it needs no restart', () => {
    assert.equal(tr['sources.more'], 'Başka yollar (dosya, komut satırı)');
    assert.equal(en['sources.more'], 'Other ways (file, command line)');
    assert.equal(tr['sources.help.file'], 'Kaynakları şu dosyadan elle de düzenleyebilirsiniz:');
    assert.equal(tr['sources.help.refresh'], 'Sunucuyu yeniden başlatmanız gerekmez; sayfayı yenileyin.');
    assert.equal(en['sources.help.refresh'], "You don't need to restart the server; refresh the page.");
    assert.ok(!('sources.help.cli' in tr) && !('sources.help.cli' in en), 'that the folders came from the command line is the box\'s line now');
    const sayingRefresh = Object.keys(tr).filter((key) => /yenile/i.test(tr[key]) && key.startsWith('sources.'));
    assert.deepEqual(sayingRefresh, ['sources.help.refresh'], 'one sentence says it, and no other says it differently');
    for (const dict of [tr, en]) {
      const example = JSON.parse(dict['sources.example']);
      assert.deepEqual(Object.keys(example), ['sources']);
      assert.deepEqual(example.sources.map((entry) => Object.keys(entry)), [['name', 'path'], ['name', 'path']]);
      assert.equal(example.sources[0].path, '~/.claude');
      assert.match(dict['sources.command'], /^pusula ~\/\.claude ~\/Documents\/\w+$/);
      assert.ok(dict['sources.help.auto'].includes('.obsidian/'));
    }
  });

  it('no sentence of the sources tells the reader to restart the server (only that they need not)', () => {
    for (const key of Object.keys(tr).filter((name) => name.startsWith('sources.'))) {
      const turkish = tr[key].replace(/yeniden başlatmanız gerekmez/gi, '');
      const english = en[key].replace(/(?:don't|do not) need to restart/gi, '');
      assert.ok(!/yeniden başlat/i.test(turkish) && !/restart/i.test(english), `${key} sends the reader to restart the server, which a change of the list does not need`);
    }
  });

  it('the help names no file or path of a real machine', () => {
    for (const key of Object.keys(tr).filter((name) => name.startsWith('sources.'))) {
      assert.ok(!/\/home\/|\/mnt\/|C:\\/.test(`${tr[key]} ${en[key]}`), key);
    }
  });

  it('the way out of a page that is an error about a folder: the sources, and a retry', () => {
    assert.ok(tr['error.toSources'].length > 0 && en['error.toSources'].length > 0);
    assert.equal(tr['error.retry'], 'Yeniden dene');
  });
});

describe('adding and removing sources (the sources page)', () => {
  it('the form: its title, its two boxes, the example path in the box of the path, and the button, which says what it is doing while it does it', () => {
    assert.deepEqual(['title', 'path', 'name', 'optional', 'submit', 'busy'].map((key) => tr[`sources.add.${key}`]), ['Klasör ekle', 'Klasör yolu', 'Ad', 'isteğe bağlı', 'Ekle', 'Ekleniyor…']);
    assert.deepEqual(['title', 'path', 'name', 'optional', 'submit', 'busy'].map((key) => en[`sources.add.${key}`]), ['Add a folder', 'Folder path', 'Name', 'optional', 'Add', 'Adding…']);
    assert.equal(tr['sources.add.pathPlaceholder'], 'örn. ~/Documents/notlar');
    assert.equal(en['sources.add.pathPlaceholder'], 'e.g. ~/Documents/notes');
  });

  it('the refusals, as the planner wrote them', () => {
    const words = {
      PathRequired: 'Bir klasör yolu yazın.',
      PathNotAbsolute: 'Tam yol yazın: / ya da ~ ile başlamalı.',
      FolderNotFound: 'Bu klasör bulunamadı.',
      TooBroad: 'Kök ya da ev klasörünün kendisi eklenemez; içindeki bir klasörü seçin.',
      AlreadyListed: 'Bu klasör zaten listede.',
      InvalidName: 'Ad en çok 80 karakter olabilir.',
    };
    for (const [code, text] of Object.entries(words)) assert.equal(tr[`sources.error.${code}`], text, code);
    assert.equal(i18n.format(tr['sources.error.FileInvalid'], { file: '/x/sources.json' }), 'Kaynak dosyası okunamıyor; önce dosyayı düzeltin: /x/sources.json');
    assert.equal(i18n.format(tr['sources.error.WriteFailed'], { detail: 'izin yok' }), 'Kaynak dosyası yazılamadı: izin yok');
    assert.equal(i18n.format(en['sources.error.FileInvalid'], { file: '/x/sources.json' }), 'The sources file cannot be read; fix the file first: /x/sources.json');
    assert.equal(i18n.format(en['sources.error.WriteFailed'], { detail: 'no access' }), 'The sources file could not be written: no access');
  });

  it('every refusal the page has words for has them in both languages, and the other failures have the words of a page that did not load', () => {
    for (const code of core.SOURCE_REFUSALS) {
      assert.ok(tr[`sources.error.${code}`] && en[`sources.error.${code}`], code);
    }
    for (const kind of ['network', 'server', 'request', 'unexpected']) assert.ok(tr[`error.${kind}`] && en[`error.${kind}`], kind);
    assert.deepEqual(Object.keys(tr).filter((key) => key.startsWith('sources.error.')).map((key) => key.slice('sources.error.'.length)).sort(), [...core.SOURCE_REFUSALS].sort(), 'no words for a code the page does not know');
  });

  it('where the list cannot be changed the box says so in one line, as the planner wrote it: the machine goes where {machine} is; without one it is the computer pusula runs on', () => {
    assert.equal(
      i18n.format(tr['sources.locked.Remote'], { machine: 'devbox' }),
      'Bu cihazdan kapalı — kaynaklar yalnız devbox bilgisayarının kendi tarayıcısından eklenip kaldırılır.',
    );
    assert.equal(
      tr['sources.locked.RemoteAnon'],
      "Bu cihazdan kapalı — kaynaklar yalnız pusula'nın çalıştığı bilgisayarın kendi tarayıcısından eklenip kaldırılır.",
    );
    assert.equal(tr['sources.locked.CommandLine'], 'Bu sunucu komut satırındaki klasörlerle başlatıldı; liste buradan değiştirilemez.');
    assert.equal(en['sources.locked.CommandLine'], 'This server was started with the folders from the command line; the list cannot be changed from here.');
    assert.ok(tr['sources.locked.Unknown'].length > 0 && en['sources.locked.Unknown'].length > 0, 'a server that does not say why still gets a line');
    assert.deepEqual(placeholders(tr['sources.locked.Remote']), ['machine']);
    for (const key of ['sources.locked.RemoteAnon', 'sources.locked.CommandLine', 'sources.locked.Unknown']) assert.deepEqual(placeholders(tr[key]), [], key);
    assert.ok(!('sources.closed.Remote' in tr) && !('sources.closed.CommandLine' in tr), 'the old notes are gone');
  });

  it('what can be done instead, as the planner wrote it: open it on the machine, start the server so that the network may, or edit the file (the flag and the file go where the page puts them as code)', () => {
    assert.equal(i18n.format(tr['sources.way.here'], { machine: 'devbox' }), 'Bu sayfayı devbox bilgisayarında açın');
    assert.equal(tr['sources.way.hereAnon'], "Bu sayfayı pusula'nın çalıştığı bilgisayarda açın");
    assert.equal(tr['sources.way.flag'], 'Ağdan da açmak için sunucuyu {flag} ile başlatın');
    assert.equal(tr['sources.way.file'], 'Ya da dosyayı düzenleyin: {file}');
    assert.equal(en['sources.way.flag'], 'To allow it over the network too, start the server with {flag}');
    assert.equal(en['sources.way.file'], 'Or edit the file: {file}');
    for (const dict of [tr, en]) {
      assert.equal(dict['sources.way.flag'].split('{flag}').length, 2, 'the page splits the wording at {flag} to put the flag in as code');
      assert.equal(dict['sources.way.file'].split('{file}').length, 2, 'and at {file} to put the file in as code');
    }
  });

  it('a Turkish word is never put after the name of a machine with a suffix: the name of a computer has a vowel of its own, which no rule of the page knows', () => {
    for (const key of ['sources.locked.Remote', 'sources.way.here']) assert.ok(!/\{machine\}['’]/.test(tr[key]), key);
  });

  it('the chip in the heading says the page is read-only, and, over the network, that the network is why', () => {
    assert.equal(tr['sources.readonly'], 'Salt okunur');
    assert.equal(tr['sources.readonly.Remote'], 'Salt okunur · ağ');
    assert.equal(en['sources.readonly'], 'Read-only');
    assert.equal(en['sources.readonly.Remote'], 'Read-only · network');
  });

  it('copying the path of the file: the button, its name, and what it says for a moment', () => {
    assert.equal(tr['sources.copy'], 'Kopyala');
    assert.equal(tr['sources.copied'], 'Kopyalandı');
    assert.ok(tr['sources.copyFailed'].length > 0 && en['sources.copyFailed'].length > 0);
    for (const dict of [tr, en]) assert.ok(dict['sources.copy.named'].toLowerCase().includes(dict['sources.copy'].toLowerCase()), 'the name of the button contains the word on it');
  });

  it('a sources file that is damaged, as the planner wrote it', () => {
    assert.equal(i18n.format(tr['sources.fileError'], { error: 'satır 3' }), 'Kaynak dosyası okunamadı, son geçerli liste kullanılıyor: satır 3');
    assert.ok(en['sources.fileError'].includes('{error}'));
  });

  it('removing: the quiet button is named by its source (every card has one), the question says that the folder is not touched, and the answers', () => {
    assert.equal(tr['sources.remove'], 'Kaldır');
    assert.equal(i18n.format(tr['sources.remove.named'], { name: '~/.claude' }), 'Kaldır: ~/.claude');
    assert.equal(i18n.format(en['sources.remove.named'], { name: '~/.claude' }), 'Remove ~/.claude');
    for (const dict of [tr, en]) assert.ok(dict['sources.remove.named'].startsWith(dict['sources.remove']), 'the name of the button starts with what it says on it');
    assert.equal(tr['sources.remove.ask'], 'Listeden kaldırılsın mı? Klasöre dokunulmaz.');
    assert.equal(en['sources.remove.ask'], 'Remove it from the list? The folder is not touched.');
    assert.equal(tr['sources.remove.cancel'], 'Vazgeç');
    assert.equal(tr['sources.remove.busy'], 'Kaldırılıyor…');
    assert.equal(i18n.format(tr['sources.removed'], { name: 'Notlarım' }), 'Notlarım listeden kaldırıldı.');
    assert.equal(i18n.format(en['sources.removed'], { name: 'Notes' }), 'Notes was removed from the list.');
  });

  it('none of it is a note variant, so it reads the same whichever source is open', () => {
    i18n.setLang('en');
    i18n.setVariant('note');
    try {
      for (const key of Object.keys(en).filter((name) => /^sources\.(?:add|remove|error|locked|readonly|way|copy|fileError)/.test(name))) {
        assert.equal(i18n.t(key), en[key], key);
      }
    } finally {
      i18n.setVariant('');
    }
  });
});

describe('notes: tags, embeds and the new enum values', () => {
  it('tags have a title, an empty line, a list name and a way to close the filter', () => {
    assert.equal(tr['tags.title'], 'Etiketler');
    assert.equal(en['tags.title'], 'Tags');
    assert.ok(tr['tags.none'].length > 0 && tr['tags.of'].length > 0 && tr['tagged.none'].length > 0);
    assert.equal(tr['tagged.close'], 'Filtreyi kapat');
  });

  it('"#etiket · 12 not": the count of a tag tells one note from many in English', () => {
    i18n.setLang('tr');
    assert.equal(`#etiket · ${i18n.tn('tagged.count', 12)}`, '#etiket · 12 not');
    i18n.setLang('en');
    assert.equal(i18n.tn('tagged.count', 1), '1 note');
    assert.equal(i18n.tn('tagged.count', 12), '12 notes');
  });

  it('the overview of notes: "161 not · 34 klasör"', () => {
    i18n.setLang('tr');
    i18n.setVariant('note');
    try {
      assert.equal(`${i18n.tn('overview.files', 161)} · ${i18n.tn('overview.folders', 34)}`, '161 not · 34 klasör');
    } finally {
      i18n.setVariant('');
      i18n.setLang('en');
    }
    assert.equal(`${i18n.tn('overview.folders', 1)}, ${i18n.tn('overview.folders', 2)}`, '1 folder, 2 folders');
  });

  it('an embedded note: what the card says, and the placeholders for a picture and a file', () => {
    assert.equal(tr['embed.kind'], 'gömülü not');
    assert.equal(en['embed.kind'], 'embedded note');
    assert.equal(i18n.format(tr['md.image'], { alt: 'a.png' }), 'görsel: a.png');
    assert.equal(i18n.format(tr['md.file'], { name: 'a.pdf' }), 'dosya: a.pdf');
    assert.equal(i18n.format(en['md.file'], { name: 'a.pdf' }), 'file: a.pdf');
  });

  it('a link to a note that is not created yet is "henüz oluşturulmamış not" in the tooltip and the label of a source of notes', () => {
    assert.equal(tr['status.Pending.note'], 'henüz oluşturulmamış not');
    assert.ok(tr['tip.Pending.note'].startsWith('Henüz oluşturulmamış not'));
    assert.equal(tr['status.Pending'], 'henüz yazılmamış bağ', 'a Claude configuration keeps its words');
  });

  it('the Note layer and the Embed kind have names', () => {
    assert.equal(tr['layer.Note'], 'Not');
    assert.equal(en['layer.Note'], 'Note');
    assert.equal(tr['kind.Embed'], 'Gömme');
    assert.equal(en['kind.Embed'], 'Embed');
  });

  it('the folder being read is no more a string: the source switch says it', () => {
    assert.ok(!('root.title' in tr) && !('root.title' in en));
  });
});

describe('the menu of sources', () => {
  it('"manage sources" leads to the sources page, and the status of a folder is said in words', () => {
    assert.equal(tr['source.manage'], 'Kaynakları yönet…');
    assert.equal(en['source.manage'], 'Manage sources…');
    assert.equal(tr['source.status.off'], 'erişilemez');
    assert.equal(tr['source.status.ok'], 'erişilebilir');
    assert.ok(en['source.status.off'].length > 0 && en['source.status.ok'].length > 0);
  });

  it('the language, where the top bar has no room for it: "Dil:" and the two buttons', () => {
    assert.equal(tr['lang.menu'], 'Dil:');
    assert.equal(en['lang.menu'], 'Language:');
    assert.ok(htmlSource.includes('data-i18n="lang.menu"') && htmlSource.includes('data-i18n="source.manage"'));
    assert.ok(htmlSource.includes('role="menuitemradio"'));
  });

  it('the menu is named, in the page, by the title of the sources', () => {
    assert.match(htmlSource, /<div id="source-menu"[^>]*data-i18n-aria-label="sources\.title"/);
  });
});

describe('where to start in a source of notes', () => {
  it('the entry note, and what it is: of a vault, of a Markdown folder', () => {
    assert.equal(tr['entry.title'], 'Giriş notu');
    assert.equal(en['entry.title'], 'Entry note');
    assert.equal(tr['entry.hint.Vault'], "Vault'un başlangıç notu");
    assert.ok(tr['entry.hint.Markdown'].length > 0 && en['entry.hint.Markdown'].length > 0);
    assert.notEqual(tr['entry.hint.Vault'], tr['entry.hint.Markdown'], 'a folder is not a vault');
  });

  it('the two lists have their names, and a note that is linked to says how many notes link to it', () => {
    assert.equal(tr['recent.title'], 'Son değişen notlar');
    assert.equal(tr['linked.title'], 'En çok bağ alan notlar');
    assert.ok(en['recent.title'].length > 0 && en['linked.title'].length > 0);
    i18n.setLang('tr');
    for (const count of [1, 2, 12]) assert.equal(i18n.tn('overview.backlinks', count), `${count} geri bağ`);
    i18n.setLang('en');
    assert.equal(i18n.tn('overview.backlinks', 1), '1 backlink');
    assert.equal(i18n.tn('overview.backlinks', 5), '5 backlinks');
  });

  it('an orphan note is one with no links at all: its tooltip, its line in the panel and its group say so', () => {
    assert.equal(tr['tree.orphan.note'], 'Yetim: bu notun hiç bağı yok');
    assert.equal(tr['info.orphan.note'], 'Yetim: bu notun gelen ya da giden hiçbir bağı yok.');
    assert.equal(en['tree.orphan.note'], 'Orphan: this note has no links at all');
    assert.equal(en['info.orphan.note'], 'Orphan: this note has no incoming or outgoing links.');
    for (const dict of [tr, en]) {
      for (const key of ['tree.orphan.note', 'info.orphan.note', 'issues.help.orphans.note']) assert.ok(!/başka hiçbir|no other|hiçbir notun/i.test(dict[key]), `${key} still says "no other note"`);
    }
  });
});

describe('the folder picker', () => {
  it('the button on the sources page and the line under it: the first way to add a folder is to choose it, the second to type the path', () => {
    assert.equal(tr['sources.add.pick'], 'Klasör seç…');
    assert.equal(en['sources.add.pick'], 'Choose a folder…');
    assert.equal(tr['sources.add.or'], 'ya da yolu yazın');
    assert.equal(en['sources.add.or'], 'or type the path');
  });

  it('the window, the folders the server found, and what is said while it searches, when it stopped early and when it could not say', () => {
    assert.deepEqual(['title', 'found', 'found.searching', 'found.partial'].map((key) => tr[`browse.${key}`]), [
      'Klasör seç', 'Bulunan klasörler', 'Klasörler aranıyor…', 'Arama yarıda kesildi; aşağıdan göz atabilirsiniz.',
    ]);
    assert.deepEqual(['title', 'found', 'found.searching', 'found.partial'].map((key) => en[`browse.${key}`]), [
      'Choose a folder', 'Folders found', 'Searching for folders…', 'The search stopped early; you can browse below.',
    ]);
    for (const dict of [tr, en]) assert.ok(dict['browse.found.failed'].length > 0, 'a failed search says the reader can browse');
  });

  it('what a row says: what the folder is (vault, Claude), what it holds (a count that stopped says "999+"), and that it is in the list', () => {
    assert.deepEqual(['Vault', 'Claude'].map((kind) => tr[`browse.kind.${kind}`]), ['vault', 'Claude']);
    assert.deepEqual(['Vault', 'Claude'].map((kind) => en[`browse.kind.${kind}`]), ['vault', 'Claude']);
    assert.equal(i18n.format(tr['browse.count.moreNotes'], { n: 999 }), '999+ not');
    assert.equal(i18n.format(en['browse.count.moreNotes'], { n: 999 }), '999+ notes');
    assert.equal(i18n.format(tr['browse.count.moreFiles'], { n: 999 }), '999+ dosya');
    assert.equal(tr['browse.listed'], 'listede');
    assert.ok(en['browse.listed'].length > 0);
  });

  it('the folders have a count, and say that there are none, that the list was cut and that it is loading', () => {
    i18n.setLang('tr');
    assert.equal(i18n.tn('browse.count', 12), '12 klasör');
    assert.equal(tr['browse.empty'], 'Bu klasörde alt klasör yok.');
    assert.equal(i18n.format(tr['browse.truncated'], { n: 500 }), 'İlk 500 klasör gösteriliyor.');
    assert.equal(tr['browse.loading'], 'Klasörler yükleniyor…');
    i18n.setLang('en');
    assert.equal(i18n.tn('browse.count', 1), '1 folder');
    assert.equal(i18n.tn('browse.count', 12), '12 folders');
    assert.equal(en['browse.empty'], 'This folder has no subfolders.');
    assert.equal(i18n.format(en['browse.truncated'], { n: 500 }), 'Showing the first 500 folders.');
  });

  it('the controls: the way up, the hidden folders, the two buttons at the bottom (the one that adds is named by what it adds)', () => {
    assert.deepEqual(['up', 'hidden', 'cancel', 'add'].map((key) => tr[`browse.${key}`]), ['Üst', 'Gizli klasörleri göster', 'Vazgeç', 'Bu klasörü ekle']);
    assert.deepEqual(['up', 'hidden', 'cancel', 'add'].map((key) => en[`browse.${key}`]), ['Up', 'Show hidden folders', 'Cancel', 'Add this folder']);
    for (const dict of [tr, en]) {
      assert.ok(dict['browse.up.named'].length > dict['browse.up'].length, 'the way up is named in full for a screen reader');
      assert.ok(dict['browse.add.named'].includes('{name}'), 'the button of a found folder says which folder it adds');
    }
  });

  it('why the folder that is shown cannot be added: the home folder, the root (the third reason, that it is in the list, is the sources page\'s own sentence)', () => {
    assert.equal(tr['browse.why.home'], 'Ev klasörünün kendisi eklenemez; bir alt klasör seçin.');
    assert.equal(en['browse.why.home'], 'The home folder itself cannot be added; choose a subfolder.');
    assert.ok(/kök/i.test(tr['browse.why.root']) && /root/i.test(en['browse.why.root']));
    assert.equal(tr['sources.error.AlreadyListed'], 'Bu klasör zaten listede.');
  });

  it('a network that is down is said in the picker\'s own words: the page cannot refresh itself there, so it does not say it will', () => {
    for (const dict of [tr, en]) assert.ok(!/refresh|yenilen/i.test(dict['browse.error.network']), dict['browse.error.network']);
    assert.ok(/yeniden deneyin/.test(tr['browse.error.network']) && /Try again/.test(en['browse.error.network']));
  });

  it('none of it names a folder of a real machine, and none of it is a note variant', () => {
    for (const key of Object.keys(tr).filter((name) => name.startsWith('browse.'))) {
      assert.ok(!/\/home\/|\/mnt\/|C:\\/.test(`${tr[key]} ${en[key]}`), key);
      assert.ok(!key.endsWith('.note') && !(`${key}.note` in en), key);
    }
  });
});
