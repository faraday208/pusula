# pusula

AI ajan config klasörlerini — öncelikle `~/.claude`, sonra Antigravity (`~/.gemini/antigravity`) —
tarayıcıda okunur hâlde gösteren ve dosyalar arasındaki bağları çıkaran yerel araç.

Görünüm ve kullanım Obsidian'ı örnek alır (dosya ağacı, render, backlink, grafik, arama). Veri
modeli ve bağ çözme ise Obsidian'a göre değil, okunan klasörün kendi yapısına göre kurulur.

**Durum:** kararlar verildi (2026-10-01), kod yok; sıradaki iş `.claude` ve Antigravity yapısını
ölçmek. Kararlar: `CLAUDE.md` → *Kararlar*.

---

## Neden `.claude`'u Obsidian'da açmak yetmiyor?

- **Bu dosyaları çoğunlukla Claude yazıyor, biz okuyoruz.** Gereken şey iyi bir editör değil;
  iyi bir okuyucu, bağlar arasında gezinme ve Claude yazarken kendiliğinden yenilenen bir görünüm.
- **Claude'un kendi bağ türleri var.** Yol atıfları (`~/.claude/rules/x.md`), `@import` ve
  memory'nin frontmatter adına göre kurduğu `[[bağ]]`'lar. Obsidian bunları göremez:
  `~/.claude`'daki 192 memory dosyasının 102'sinde frontmatter adı dosya adından farklı, bu yüzden
  memory'deki 45 bağ Obsidian'da kırık görünüyor (ölçüm, 2026-09-30).
- **Asıl soru "ne yükleniyor?"** Bir dosyanın her oturum mu, `paths:` eşleşince mi, skill
  tetiklenince mi, yoksa yalnız istenince mi yüklendiği; kaç token yediği; hangi atıfın kırık,
  hangi dosyanın yetim olduğu. Bunlar bir not uygulamasının değil, bir denetim aracının işi.
- **Antigravity Markdown vault'u değil.** `brain/` altında Markdown dosyaları ve bunların
  `.resolved` sürümleri, yanında protobuf (`.pb`) konuşma kayıtları var. Bir not uygulaması bu
  düzeni anlamaz.
- **Her cihazdan okunur.** pusula dosyaların durduğu makinede koşar; tablet ve öteki bilgisayarlar
  özel ağ üzerinden tarayıcıyla okur, cihaza kopya gerekmez.

## Artılar

1. **Okumaya göre tasarlanır.** Temiz görünüm, backlink'ler, grafik, arama, canlı yenileme.
2. **Claude'un bağlarını anlar.** Yol atıfları, `@import` ve memory adları gerçek bağ olarak görünür.
3. **Config denetimi.** Yükleme katmanı, token payı, kırık ve yetim atıflar tek bakışta görünür;
   "hep ekleniyor, hiç eksilmiyor" sorunu ölçülebilir olur.
4. **Klasörü bozmaz.** Okuduğu klasöre yazmaz (karar 1).

## Eksiler

1. **Yazma ve bakım bizde.** Her özellik bizim işimiz; topluluk, plugin, tema yok.
2. **Düzenleme yok (ilk sürüm).** Bir dosyayı düzeltmek için yine bir editör gerekir.
3. **Ağa bağımlı.** Dosyaların durduğu makine kapalıysa okunamaz.
4. **Yığına bir araç daha.** Kendi amacına ters düşmemesi için çekirdeği küçük tutulmalı.

## Değerlendirilen alternatifler

- **Obsidian (+ plugin):** Claude bağlarını ayrı bir görünümde çözebilirdi; ama çekirdek grafiği
  yalnız kendi çözdüğü bağları gösterir, dosyaların cihazda bulunması gerekir ve yükleme katmanı
  kavramı yok.
- **Quartz:** Obsidian vault'unu web sitesine çevirir; güçlü ama Obsidian odaklı ve Claude
  bağlarını anlamaz. Denenmedi, motor sıfırdan yazılıyor (karar 3).
- **VS Code + Foam:** editör içinde grafik ve backlink; Claude bağlarını anlamaz, masaüstüne bağlı.
