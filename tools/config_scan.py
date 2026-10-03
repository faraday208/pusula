#!/usr/bin/env python3
"""~/.claude ve ~/.gemini (Antigravity) yapısını sayar: dosya katmanları, bağ biçimleri,
frontmatter alanları, kırık atıflar. Salt-okunur; dosya içeriği yazdırmaz.

Kullanım: python3 tools/config_scan.py [claude_kökü] [gemini_kökü]
"""
import collections, json, os, re, sys

CLAUDE = os.path.expanduser(sys.argv[1] if len(sys.argv) > 1 else "~/.claude")
GEMINI = os.path.expanduser(sys.argv[2] if len(sys.argv) > 2 else "~/.gemini")

# Config olmayan, çalışma zamanı dizinleri
SKIP_TOP = {"plugins", "cache", "file-history", "sessions", "debug", "backups", "paste-cache",
            "chrome", "ide", "jobs", "daemon", "downloads", "session-env", "security",
            "usage-data", "todos", "shell-snapshots", "statsig", "telemetry", ".git"}

LINK = {
    "@import": re.compile(r"^@(~?[\w./-]+)", re.M),
    "yol ~/.claude/…": re.compile(r"~/\.claude/[\w./-]+"),
    "göreli yol `x/y.md`": re.compile(r"`((?:rules|shared|skills|agents|commands|hooks|output-styles|references)/[\w./-]+)`"),
    "md link [](x)": re.compile(r"(?<!!)\[[^\]]*\]\((?!https?:|#|mailto:)([^)\s]+)\)"),
    "wikilink [[x]]": re.compile(r"(?<!!)\[\[([^\]|#]+)(?:[#|][^\]]*)?\]\]"),
    "http link": re.compile(r"\]\(https?:[^)]+\)"),
}
FM = re.compile(r"\A---\r?\n(.*?)\r?\n---", re.S)


def frontmatter(text):
    m = FM.match(text)
    if not m:
        return None
    keys = {}
    for line in m.group(1).splitlines():
        mm = re.match(r"^([A-Za-z_][\w-]*):\s*(.*)$", line)
        if mm:
            keys[mm.group(1)] = mm.group(2).strip().strip("'\"")
    return keys


def read(p):
    try:
        return open(p, encoding="utf-8", errors="replace").read()
    except OSError:
        return ""


def layer(rel, fm):
    parts = rel.split(os.sep)
    top = parts[0]
    if rel == "CLAUDE.md":
        return "her oturum: CLAUDE.md"
    if top == "rules":
        return "koşullu: rules (paths:)" if fm and "paths" in fm else "her oturum: rules"
    if top == "skills" and len(parts) > 1 and parts[1] == "synced":
        return "skill: synced (claude.ai)"
    if top == "skills":
        return "skill: SKILL.md" if parts[-1] == "SKILL.md" else "skill: ek dosya"
    if top == "projects":
        return "memory: MEMORY.md indeksi" if parts[-1] == "MEMORY.md" else "memory: kayıt"
    if top in ("agents", "commands", "output-styles", "shared", "hooks"):
        return f"istenince: {top}"
    if len(parts) == 1:
        return "istenince: kök *.md"
    return f"diğer: {top}"


def scan_claude():
    files = []
    other_ext = collections.Counter()
    for d, dirs, fs in os.walk(CLAUDE):
        rel_d = os.path.relpath(d, CLAUDE)
        top = rel_d.split(os.sep)[0]
        if rel_d == ".":
            dirs[:] = [x for x in dirs if x not in SKIP_TOP]
        elif top == "projects":
            # projects/ altında yalnız memory dizinleri config sayılır
            depth = rel_d.count(os.sep)
            if depth == 1:
                dirs[:] = [x for x in dirs if x == "memory"]
                fs = []
        for f in fs:
            p = os.path.join(d, f)
            rel = os.path.relpath(p, CLAUDE)
            if f.endswith(".md"):
                files.append(rel)
            elif not rel.startswith("projects"):
                other_ext[os.path.splitext(f)[1] or "(uzantısız)"] += 1

    by_layer = collections.defaultdict(lambda: [0, 0])  # dosya, karakter
    links = collections.Counter()
    link_files = collections.Counter()
    fm_keys = collections.defaultdict(collections.Counter)
    fm_count = collections.Counter()
    broken = collections.Counter()
    broken_ex = collections.defaultdict(list)
    wl_total = wl_name = wl_file = wl_none = 0
    mem_name_differs = mem_total = 0
    targets = collections.Counter()  # hangi dosyaya kaç atıf geliyor (yetim tespiti için)

    # memory ad çözümü: dizin başına frontmatter name -> dosya
    mem_names = collections.defaultdict(dict)
    for rel in files:
        if rel.startswith("projects") and not rel.endswith("MEMORY.md"):
            fm = frontmatter(read(os.path.join(CLAUDE, rel))) or {}
            if "name" in fm:
                mem_names[os.path.dirname(rel)][fm["name"]] = rel

    for rel in files:
        t = read(os.path.join(CLAUDE, rel))
        fm = frontmatter(t)
        lay = layer(rel, fm)
        by_layer[lay][0] += 1
        by_layer[lay][1] += len(t)
        grp = lay.split(":")[0]
        if fm is not None:
            fm_count[grp] += 1
            for k in fm:
                fm_keys[grp][k] += 1
        if lay == "memory: kayıt":
            mem_total += 1
            if fm and fm.get("name") and fm["name"] != os.path.splitext(os.path.basename(rel))[0]:
                mem_name_differs += 1

        # kod blokları atıf sayılmaz
        body = re.sub(r"```.*?```", "", t, flags=re.S)
        # satır içi kod: yol atıfları orada yaşar, ama [[x]] ve [](x) orada örnektir
        plain = re.sub(r"`[^`\n]*`", "", body)
        for kind, pat in LINK.items():
            found = pat.findall(plain if kind in ("wikilink [[x]]", "md link [](x)") else body)
            if found:
                links[kind] += len(found)
                link_files[kind] += 1
            for target in found if kind != "http link" else []:
                resolved = resolve(kind, target, rel, mem_names)
                if resolved is True:
                    continue
                if resolved:
                    targets[resolved] += 1
                    if kind == "wikilink [[x]]":
                        wl_total += 1
                        if resolved.endswith("#ad"):
                            wl_name += 1
                        else:
                            wl_file += 1
                else:
                    broken[kind] += 1
                    if kind == "wikilink [[x]]":
                        wl_total += 1
                        wl_none += 1
                    if len(broken_ex[kind]) < 4:
                        broken_ex[kind].append(f"{rel} → {target}")

    orphans = [f for f in files if f.startswith("projects") and not f.endswith("MEMORY.md")
               and targets[f] == 0 and targets[f + "#ad"] == 0]
    return dict(files=files, by_layer=by_layer, links=links, link_files=link_files,
                fm_keys=fm_keys, fm_count=fm_count, broken=broken, broken_ex=broken_ex,
                wl=(wl_total, wl_name, wl_file, wl_none), mem=(mem_total, mem_name_differs),
                other_ext=other_ext, orphans=len(orphans))


def resolve(kind, target, rel, mem_names):
    """Çözülürse hedef rel yolu (ya da True: dış/önemsiz), çözülemezse None."""
    base = os.path.dirname(os.path.join(CLAUDE, rel))
    t = target.rstrip(".,;:)")
    if kind == "wikilink [[x]]":
        d = os.path.dirname(rel)
        if t in mem_names.get(d, {}):
            return mem_names[d][t] + "#ad"
        for cand in (os.path.join(base, t), os.path.join(base, t + ".md")):
            if os.path.isfile(cand):
                return os.path.relpath(cand, CLAUDE)
        return None
    if kind in ("@import", "yol ~/.claude/…"):
        p = os.path.expanduser(t)
        if not p.startswith("/"):
            p = os.path.join(base, p)
    elif kind == "göreli yol `x/y.md`":
        p = os.path.join(CLAUDE, t)
        if not os.path.exists(p):
            p = os.path.join(base, t)
    else:  # md link
        t = t.split("#")[0]
        if not t:
            return True
        if t.startswith("file://"):
            t = t[7:]
        p = os.path.expanduser(t) if t.startswith(("~", "/")) else os.path.join(base, t)
    if "<" in t or "*" in t or "{" in t:
        return True  # şablon/örnek yol
    if os.path.exists(p):
        p = os.path.realpath(p)
        return os.path.relpath(p, CLAUDE) if p.startswith(os.path.realpath(CLAUDE)) else True
    return None


def scan_gemini():
    ag = os.path.join(GEMINI, "antigravity")
    out = collections.Counter()
    names = collections.Counter()
    links = collections.Counter()
    types = collections.Counter()
    fm = 0
    for d, dirs, fs in os.walk(ag):
        top = os.path.relpath(d, ag).split(os.sep)[0]
        for f in fs:
            p = os.path.join(d, f)
            if top == "brain":
                if f.endswith(".md"):
                    out["brain: .md (güncel)"] += 1
                    names[f] += 1
                    t = read(p)
                    fm += bool(FM.match(t))
                    for m in re.findall(r"\]\(([a-z]+):", t):
                        links[m + ":"] += 1
                    links["[[x]]"] += len(re.findall(r"\[\[", t))
                elif re.search(r"\.resolved\.\d+$", f):
                    out["brain: .resolved.N (eski sürüm)"] += 1
                elif f.endswith(".resolved"):
                    out["brain: .resolved"] += 1
                elif f.endswith(".metadata.json"):
                    out["brain: .metadata.json"] += 1
                    try:
                        types[json.load(open(p)).get("artifactType", "?")] += 1
                    except (OSError, ValueError):
                        pass
                else:
                    out["brain: diğer (" + (os.path.splitext(f)[1] or "-") + ")"] += 1
            else:
                out[f"{top}: {os.path.splitext(f)[1] or '(uzantısız)'}"] += 1
    brains = len(os.listdir(os.path.join(ag, "brain"))) if os.path.isdir(os.path.join(ag, "brain")) else 0
    return dict(out=out, names=names, links=links, types=types, fm=fm, brains=brains,
                gemini_md=len(read(os.path.join(GEMINI, "GEMINI.md"))))


def main():
    c = scan_claude()
    print(f"# ~/.claude ({CLAUDE}) — {len(c['files'])} .md\n")
    print("## Katmanlar  (token ≈ karakter/4, kaba)")
    for lay, (n, ch) in sorted(c["by_layer"].items()):
        print(f"  {lay:32} {n:4} dosya  ~{ch // 4:7,} token")
    print("\n## Bağ biçimleri  (kullanım / dosya / kırık)")
    for k in LINK:
        print(f"  {k:24} {c['links'][k]:5} / {c['link_files'][k]:4} / {c['broken'][k]:4}")
    t, n, f, x = c["wl"]
    print(f"\n  wikilink çözümü: {t} → memory adıyla {n}, dosya adıyla {f}, çözülemeyen {x}")
    mt, md = c["mem"]
    print(f"  memory kaydı {mt}; frontmatter adı dosya adından farklı: {md}; hiç atıf almayan: {c['orphans']}")
    print("\n  kırık örnekleri:")
    for k, ex in c["broken_ex"].items():
        for e in ex:
            print(f"    [{k}] {e}")
    print("\n## Frontmatter  (grup: frontmatter'lı dosya — alan sayıları)")
    for g, keys in sorted(c["fm_keys"].items()):
        print(f"  {g:10} {c['fm_count'][g]:4}  " + ", ".join(f"{k}={v}" for k, v in keys.most_common(10)))
    print("\n## .md dışı dosyalar (projects/ hariç)")
    print("  " + ", ".join(f"{k}={v}" for k, v in c["other_ext"].most_common()))

    g = scan_gemini()
    print(f"\n# ~/.gemini ({GEMINI})\n")
    print(f"  GEMINI.md ~{g['gemini_md'] // 4:,} token; brain oturumu: {g['brains']}")
    for k, v in sorted(g["out"].items()):
        print(f"  {k:36} {v:4}")
    print("  brain .md adları: " + ", ".join(f"{k}={v}" for k, v in g["names"].most_common()))
    print("  artifactType: " + ", ".join(f"{k}={v}" for k, v in g["types"].most_common()))
    print(f"  brain .md bağları: " + ", ".join(f"{k}={v}" for k, v in g["links"].most_common())
          + f"; frontmatter'lı: {g['fm']}")


main()
