# PC-F63: Nightly client-e2e: WebKit chart-bidi canvas tests fail

## Implementer notes

### Root cause

A WebKit canvas text bug that the chart's strings exposed, not fonts, bidi order or the marks.
Reproduced on local Windows WebKit (Playwright 1.61.1, webkit-2311) with the same three failures
as nightly 44745573605 on Linux (`code-arabic rtl`; `facade-column-ar "-35 ألف"` ltr;
`facade-column-ar "1100 - النقدية"` rtl), so it does not depend on the runner's newly installed
fonts. Chromium and Firefox pass.

WebKit draws a canvas string through `GraphicsContext::drawBidiText`: it splits the string into
bidi runs in visual (left-to-right) order, draws each with `FontCascade::drawText`, and moves the
pen by the size `drawText` returns (`currentPoint.move(advance)`). That size carries the run's
*initial advance*, height included. For a right-to-left run, the first glyph drawn is the leftmost,
that is the logically last character. When it carries a vertical GPOS offset (Arabic final forms
joined cursively, such as the teh marbuta of "النقدية", the feh of "ألف", the teh of "شركات"),
WebKit shifts the whole run's baseline by it and carries the shift into every run drawn after it.

Measured (scratch scripts PC-F63-exp1..6.mjs in the orchestrator's scratch folder):

- In `1100 النقدية` drawn whole in an RTL canvas, the digits sit exactly 1 px higher than `1100`
  drawn alone (a vertical scan of the reference finds 0 differing pixels at dy = -1 and none at
  any x offset); the Arabic word matches. `1100 - Cash` matches at dy = 0. The shift follows any
  run drawn after an Arabic run: `Cash النقدية` rtl and `النقدية Cash` ltr both shift Cash; Latin
  first in visual order never shifts.
- It scales with the font size and the word: النقدية -1/-2/-3 px at 16/32/48 px, ألف +1/+2/+2,
  شركات and المبيعات more than the ±4 px scanned at 48 px.
- The Arabic word itself is off the line too. Ink rows at 48 px, against the Latin "Cash" (rows
  44-79 in all engines): Chromium and Firefox draw شركات at 46-90, النقدية at 45-88, ألف at 35-80.
  WebKit draws them at 51-98, 43-85 and 43-82.
- A zero-width ARABIC LETTER MARK (U+061C) after the word puts it right: WebKit then draws
  شركات, النقدية and ألف with the bottoms Chromium draws (90, 88, 80) and stops the carry. An RLM
  there stops the carry but leaves the word off the line (still 51-98): RLM comes from the Latin
  face, ALM from the Arabic one, so only ALM becomes the Arabic run's first drawn glyph.
- RLM after an Arabic letter also changes bidi resolution (W2: the following European digits stop
  being Arabic numbers, so "35%" and "1-2" reorder): 694 and 288 differing pixels in Chromium.
  ALM, of the class AL like the letter, changes nothing: 0 differing pixels in Chromium and Firefox
  on every pair tried, `rtl`/`ltr` with Latin, digits, percent and ranges after the word.

So both readings in the brief hold in part: it is a product bug (Arabic labels and the Latin codes
or names beside them are drawn off the baseline in WebKit, by more at larger sizes), and the
test's reference ("each run drawn apart") is itself exposed to the same bug whenever a reference
segment ends a right-to-left run (`-35 ألف` drawn alone has its digits after the word).

### What was built

- `client/projects/core/tellma-core-ui/chart/internal/chart-text.ts`: `ɵtmCanvasText` closes every
  word of an AL-class script (Arabic, Syriac, Thaana, Hanifi Rohingya) with a zero-width ALM, once,
  on the finished string (so after `tmStripBidiControls`, which still strips an incoming ALM, after
  the run marks and after `NUMBER_NEUTRAL`'s LRMs, which therefore never wrap it). The mark goes
  after the word's harakat, tatweel and ZWJ (between a letter and its ZWJ it breaks the join in
  Firefox and WebKit) and before a ZWNJ, which ends the word (ALM is transparent to joining, so
  the non-joiner still does its job). Closing the whole string once also avoids closing a word
  split across two adjacent pieces in the middle. The header comment explains the WebKit mechanism
  and the deviation. `ɵtmRunDirection` and the RTL letter class were compacted (short `sc=` script
  aliases, one `exec` instead of a loop; same behaviour) to keep `./chart` inside its ceiling.
- `client/projects/internal/showcase/src/app/chart/chart-bidi-probe.ts`: new `onTheLine(text)`,
  the reference's own (regex-based, independent) closing of Arabic words with ALM. The probe's
  `drawApart` draws and measures each segment through it; `MARKS` also strips ALM from probed
  texts.
- `client/projects/internal/showcase/src/app/chart/chart-bidi-story.ts`: the "apart" canvases draw
  each piece through `onTheLine`. "Plain" is unchanged.
- Unit tests: `chart-text.spec.ts` (new: words closed with ALM after harakat, tatweel and ZWJ,
  before ZWNJ; in base pieces and wrapped runs; ALM keeps "35%" as it was; Hebrew left as it is;
  Arabic-Indic digits close nothing; the mark is not wrapped in a number's run; incoming ALM
  stripped; existing expectations updated), `chart-option.spec.ts` and `tm-chart.spec.ts` (mark
  regexes include ALM, one expectation gains the ALM).
- `ARCHITECTURE.md`: the canvas-text sentence names the closing mark.
- Bundle size: `./chart` measured 6.827 KB gzipped before, 6.868 KB after, ceiling 6.87 unchanged
  (a first version that also closed Hebrew-class words with RLM measured 6.94 and was cut).

The e2e spec `chart-bidi.spec.ts` is unchanged: its assertions stay pixel-exact.

### Deviations

- Spec 0034 §7.4 and §14 ("the marks are LRM and RLM only"; the unit test pins "only LRM and RLM")
  and spec 0024 §7.9 ("with LRM and RLM"): the adapter also writes ALM (U+061C), closing Arabic
  words, because WebKit draws an Arabic run off the baseline otherwise and RLM in that place both
  fails to fix the word and changes how a following number reads. ALM is in spec 0024 §7.9's
  control set, so `tmStripBidiControls`, `tmValidateChartSpec`'s `bidi-control` and every "no
  bidi control outside canvas" check already cover it; it stays inside the option strings.

### Verification

- WebKit, Windows, before: the three nightly failures reproduce (`chart-bidi.spec.ts`,
  `--project=webkit`). After (final version): WebKit 4/4 and Firefox 4/4 against the dev server,
  Chromium 4/4 against the production build (`CI=1`; the dev-server start timed out twice under
  machine load, before any test ran).
- Negative check (earlier version of the same closing): with only the product's closing disabled
  (reference still closed), WebKit fails 3 tests (`code-arabic ltr`, both facade `-35 ألف`), so the
  spec still catches the bug.
- Linux WebKit: Docker `ubuntu:26.04`, Playwright 1.61.1 WebKit installed `--with-deps`, replaying
  the spec's assertions (Desktop Safari context, all 14 together/apart pairs, both marks checks,
  the four facade charts in both directions) against a production showcase build served in the
  container: all pass.
- core-ui chart unit tests (`ng test core-ui --include "projects/core/tellma-core-ui/chart/**/*.spec.ts"`):
  5 files, 82 tests pass. `ng build core-ui`, `size:check` (`./chart` 6.87/6.87), `api:check`,
  eslint and prettier on the touched files pass. `dotnet build Tellma.slnx` and
  `dotnet format --verify-no-changes`: see the commit message.

### Secondary: the nightly dotnet job's "OS dependencies: unavailable"

Not the caller bug PC-F61 fixed: the dotnet job passes `pwsh .../playwright.ps1`, no package
manager, and apt ran. It is a slow mirror, not an unreachable one: at 13:38-13:44 the dotnet job's
apt fetched the 9 missing font packages (21.9 MB) from azure.archive.ubuntu.com at roughly
60-100 KB/s (fonts-ipafont-gothic 3.7 MB in ~59 s, fonts-wqy-zenhei 7.9 MB in ~83 s), so each
attempt hit the 150 s limit; attempt 2 resumed with 18.2 MB still to fetch and also ran out. The
client-e2e job fetched the same packages from the same mirror at 13:35 at several MB/s and
succeeded on attempt 1. Chromium launched without the fonts and the job passed, which is the
script's documented degraded outcome. No change made: the limit trades a longer step against a
fonts-only loss, and these jobs need none of the missing fonts (CJK, Thai, FreeFont, Unifont, X11).

### Open risks

- Real Safari (macOS, CoreText) is unverified. The mark is harmless where there is no bug: zero
  width, of the class of the letter before it, and pixel-identical in Chromium and Firefox.
- The Arabic words still differ from Chromium's at their tops in WebKit (dots, hamza: rows 43 vs
  46 for شركات, 41 vs 35 for ألف). That is inside the word, the same with or without the mark,
  and compares equal between together and apart.
- Hebrew and the other R-class scripts are not closed. A Hebrew word whose last glyph is a
  vertically positioned point could still shift what WebKit draws after it. No Hebrew face ships
  and no shipped language is R-class, so it is not measured; closing them with RLM (which, per the
  measurements above, stops the carry) did not fit the `./chart` ceiling and needs a budget raise
  if a Hebrew locale pack ever ships.

### What reviewers should examine hardest

- `AL_WORD_END`/`closeAlWords` in chart-text.ts: that the mark never lands inside a word
  (combining marks, ZWJ, tatweel), lands before a ZWNJ, and never changes bidi resolution (ALM only
  ever follows an AL letter).
- That `onTheLine` in the probe is a fair reference and not a copy of the product: it models "a run
  drawn on the line", and the negative check shows the spec still fails without the product's
  closing.
