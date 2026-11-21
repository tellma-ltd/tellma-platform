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

- Spec 0034 §7.4, §14 and decision 26 ("the marks are LRM and RLM only"; the unit test pins "only
  LRM and RLM"; "per-run LRM and RLM marks") and spec 0024 §7.9 ("with LRM and RLM"): the adapter also writes ALM (U+061C), closing Arabic
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

- A label the engine truncates (`overflow: 'truncate'` on the side legend and on the horizontal
  bar chart's category axis) loses its closing ALM with the cut text, so in WebKit a cut Arabic word
  and the `...` after it can still sit off the line. Not a regression (every label had it before),
  and no story label is truncated, so the e2e does not see it. Tried and rejected: `ellipsis:
  '\u061C...'` on both text styles. It fits the chunk budget (200.12/200.15) and closes a cut
  Arabic word in WebKit (`النقدية الأخ...` bottom row 120 -> 118 at 48 px, Chromium's 118) with 0
  differing pixels in Chromium and Firefox on every Arabic-final or wrapped case, but on a plain
  Latin label (`abcdef...` ltr) it changes 25/119 pixels at 12/48 px in Chromium, 19/87 in Firefox
  and 20/83 in WebKit: the ALM splits the label and its ellipsis into separate bidi runs, which
  lose their kerning. Recorded in the header comment.

### What reviewers should examine hardest

- `AL_WORD_END`/`closeAlWords` in chart-text.ts: that the mark never lands inside a word
  (combining marks, ZWJ, tatweel), lands before a ZWNJ, and never changes bidi resolution (ALM only
  ever follows an AL letter).
- That `onTheLine` in the probe is a fair reference and not a copy of the product: it models "a run
  drawn on the line", and the negative check shows the spec still fails without the product's
  closing.

## Review 1

### Findings

Scope: commit b4b8f762 only. No blocking or major findings. What I ran myself, against the
final code, with logs prefixed PC-F63-r1- in the orchestrator's scratch folder:

- core-ui chart unit tests (`ng test core-ui --include "projects/core/tellma-core-ui/chart/**/*.spec.ts"`):
  5 files, 82 tests pass.
- `chart-bidi.spec.ts` against the dev server, Windows: WebKit 4/4, Chromium 4/4, Firefox 4/4.
- Negative check on the final code: a local proxy in front of the dev server rewrote the served
  chunk so that `closeAlWords` returns its input (the product's closing off, the probe and story
  untouched, nothing in the worktree edited). WebKit then fails 3 of 4: `:96` (`code-arabic ltr`)
  and `:139` ltr and rtl (`facade-column-ar "-35 ألف"`). That is the set the implementer reported
  from the earlier version, so the negative check still holds and the spec still catches a product
  that stops closing its words.
- ALM is pixel-neutral where there is no bug: 18 strings (harakat, tatweel, ZWNJ, Arabic-Indic and
  European digits, `%`, ranges, commas, pre-marked runs), each drawn with and without
  `closeAlWords`, at 16 and 48 px, ltr and rtl, with the showcase's font stack: 0 differing
  pixels in Chromium and Firefox (72 of 72 pairs each). WebKit differs on every string with a
  cursive final, which is the fix working. `مُحَمَّدٌ 12`, `عـ 5` and `می‌خواهم 3.5` show no
  difference in WebKit, so a word ending in a haraka or a tatweel does not trigger the bug.
- `size:check` on the built FESM, which holds the final `AL_WORD_END` and the compacted
  `RTL_LETTER`: `./chart` 6.87/6.87 ok, chart-engine 200.13/200.15 ok.

Root cause, checked against WebKit's source (main): `GraphicsContext::drawBidiText` draws the
visually ordered subruns and moves `currentPoint` by the `FloatSize` that `FontCascade::drawText`
returns. `drawText` builds `startPoint = point + glyphBuffer.initialAdvance()` and returns
`startPoint - point` after `drawGlyphBuffer`, which writes back only the pen's x. So the height
each run hands to the next is exactly its glyph buffer's initial-advance height, the y-offset of
its first drawn (leftmost) glyph. This confirms the carry the implementer describes. The source
does not show the second effect, the Arabic word itself drawn off the line. That rests on
measurement: the implementer's ink rows, and my identity run, where only WebKit changes when the
mark is added. It also reproduces with the same three failures on Windows WebKit, so the runner's
fonts are not the cause. I agree with the diagnosis.

Exposure to text that leaves the canvas: `ɵtmCanvasText` has one product caller,
`chart-option.ts:277`, and its output stays in the ECharts option. Tooltips are rich text drawn
on the canvas, `aria.label` is off, and events match by `dataIndex`. The figure's name, its
description and the table view are built from the spec. `tm-chart.spec.ts:777`
("keeps every bidi mark out of the markup, in Arabic too") already checks every attribute and
all `textContent`, table view included, against a set that contains U+061C, and its spec has
Arabic labels, so a leaked ALM would fail it. Word-boundary cases I ran through `AL_WORD_END`:
the empty string, Latin only, Arabic-Indic digits only, a lone tatweel, a tatweel between letters
(backtracking cannot close before it), ZWJ runs, superscript alef, U+0600 before digits, the
ligature U+FDF2, Syriac, Thaana, Hanifi Rohingya, a word at the end of the string, Arabic beside
Latin on either side. All land after the word's last mark or joiner, never inside a word. An
incoming ALM is stripped per piece before marking, so no word is closed twice.

The reference is independent of the product. The story's apart mode and the probe's `drawApart`
use their own regex (`onTheLine`, Arabic script only) and their own `runDirection`. The spec file
is unchanged, and its assertions are still `toBe(0)` on full RGBA. The negative check above shows
that closing the reference did not hide the product.

1. **R1-1 (minor) — the `./chart` budget note no longer states the measure.**
   `client/projects/core/tellma-core-ui/package.json:133` still reads "Set to 6.87, the linked
   measure 6.83 plus 0.04 KB". `./chart` now measures 6.868 and sits at its ceiling. The note does
   not mention the ALM closing that took the 0.04 KB, or the `ɵtmRunDirection` and `RTL_LETTER`
   compaction that paid for part of it. The ceilings are documented as ratchets set from
   measured reality. The next checkpoint to touch `./chart` will read 0.04 KB of headroom that
   is not there, and will not know why. Fix: in the `./chart` note, give the new measure (6.868),
   name the closing and the compaction, and say the headroom is about 0.002 KB, so any addition
   needs a D-031 raise.

2. **R1-2 (minor) — a truncated label loses its closing mark.** Two places set
   `overflow: 'truncate'`: the side legend (`chart-option.ts:506`) and the bar chart's category
   axis (`chart-option.ts:668`). zrender's `truncateSingleLine`
   (`zrender/lib/graphic/helper/parseText.js:54`) cuts the string with `substr` and appends the
   ellipsis. The ALM after the last word goes first, and the cut word ends on a letter that is
   now a final form. Take an LTR page with a long Arabic member name in a side legend: the `...`
   resolves L and is drawn after the Arabic run, so WebKit's carry and the off-line word come back
   for exactly those labels. This is not a regression (before the fix every label had the bug),
   but the header comment (`chart-text.ts:19`, "Every word ... is also closed") and the Open risks
   do not mention it. The e2e cannot catch it, because no story label is truncated. Fix: at least
   add it to the header comment and the Open risks. If it is cheap within the budget, measure an
   ellipsis of ALM followed by `...` on those two text styles in WebKit, Chromium and Firefox. By
   UAX #9 an AL mark there should not move the dots or the digits before them, but adopt it only
   if the pixels agree.

3. **R1-3 (minor) — the deviation leaves out one passage of spec 0034.** The commit's
   Deviations and the implementer's notes cite spec 0034 §7.4 and §14 and spec 0024 §7.9.
   Decision 26 of spec 0034 (`docs/specs/0034-report-renderers-and-studio.md:1674`, "takes the
   page's direction by CSS and per-run LRM and RLM marks") says the same and is not cited. Fix:
   cite decision 26 in the flag for Ahmad (wording below), so all four passages get amended
   together.

Rulings on the implementer's points:

- (1) ALM against "LRM and RLM only". This is the right fix. Of the zero-width marks, only ALM
  is both drawn by the Arabic face (its unicode-range U+0600-06FF covers U+061C, and the Latin
  face's range does not) and of the letters' own class AL, so it moves no bidi resolution. RLM
  fixes only the carry and reorders a number that follows. It also stays inside spec 0024 §7.9's
  stated control set, so every existing strip and leak check covers it. Proposed wording for
  Ahmad: "Spec 0034 §7.4, §14 and decision 26, and spec 0024 §7.9 limit canvas text to LRM and
  RLM. The chart adapter also closes every Arabic-script word with a zero-width ARABIC LETTER MARK
  (U+061C). Without it, WebKit's canvas (`GraphicsContext::drawBidiText`) draws an Arabic word
  whose last letter is a cursive final (ة, ف, ت) off the baseline and moves every run drawn after
  it. RLM cannot replace it: it leaves the word off the line and changes how a following number
  reads. ALM is of the letters' own bidi class, draws nothing in Chromium and Firefox (pixel
  identical) and stays inside the option strings like the other marks. Proposed amendment: 'The
  marks are LRM and RLM, plus an ALM closing each word of an AL-class script.'"
- (2) Hebrew and the other R-class scripts. No real bug for shipped content. No shipped locale
  is R-class, and Hebrew base letters do not attach cursively, so they carry no GPOS y-offset;
  marks do not trigger the bug either (the haraka-final word above is unchanged in WebKit).
  Tenant data in Hebrew would draw from a system face with the same property. The scripts that
  could trigger it are the cursive R-class ones, N'Ko and Adlam. No face or locale ships for
  them, so keeping that in the Open risks is enough.
- (3) The `./chart` budget. No ceiling raise is needed now: `size:check` passes at 6.87/6.87.
  The note must be brought up to date (R1-1), and the next addition to `./chart` needs a D-031
  raise.
- (4) The nightly dotnet job's "OS dependencies: unavailable". Confirmed from the job's log
  (job 136319890814). Attempt 1 ran 13:38:53-13:41:19 and attempt 2 ran 13:41:40-13:44:09, each
  about the 150 s limit. Both were mid-download from azure.archive.ubuntu.com:
  fonts-ipafont-gothic (3.7 MB) took about 59 s, and fonts-wqy-zenhei (7.9 MB) took about 83 s.
  Attempt 2 restarted with "Need to get 18.2 MB/21.9 MB". The client-e2e job fetched 140 MB from
  the same mirror at 13:35-13:36 in 21 s (6.6 MB/s). The cause is a slow mirror, not a missed
  caller, and Chromium launched in the documented degraded mode. No change is needed.

Not verified here: Linux runner WebKit (the next nightly) and real Safari (CoreText).

### Responses

- R1-1: fixed in 3f9e52c2. The `./chart` note now gives the 6.868 measure, names the ALM closing
  and the run-direction / RTL letter-class compaction that paid for part of it, and states the
  ~0.002 KB headroom (any addition needs a D-031 raise).
- R1-2: fixed in 3f9e52c2 (documented, not the real fix). The real fix, an ALM-prefixed
  `ellipsis` on both truncating text styles, is cheap: it fits the chart-engine chunk (measured
  200.12/200.15). It also fixes a cut Arabic word in WebKit with 0 differing pixels in Chromium and
  Firefox on every Arabic or wrapped case tried. It fails the pixels-agree condition, though. On a
  plain Latin label (`abcdef...` ltr) it changes 25/119 pixels at 12/48 px in Chromium, 19/87 in
  Firefox and 20/83 in WebKit, because the ALM splits the label and its ellipsis into separate
  bidi runs that lose their kerning. Latin labels are the common truncated case, so the engine's
  plain ellipsis is kept, and the limitation is in the chart-text.ts header comment and the Open
  risks.
- R1-3: fixed in 3f9e52c2. The Deviations entry cites spec 0034 decision 26 alongside §7.4, §14
  and spec 0024 §7.9. The orchestrator flag should use the reviewer's wording.
