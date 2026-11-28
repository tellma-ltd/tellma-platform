# PC-F55: chart-bidi e2e never draws on Linux (from PC-194)

## Implementer notes

The implementer has not written notes yet. The diagnosis is in the commit message of 4c7e1259.

## Review 1

Reviewed commit 4c7e1259 only.

What I ran:
- The tm-chart unit suite: `ng test core-ui --include chart/tm-chart.spec.ts`. 29 of 29 passed.
- `CI=1 node scripts/e2e.mjs --project=chromium --grep-invert "@nightly|@perf|@quarantine" e2e/specs/chart-bidi.spec.ts` on Windows. 4 of 4 passed.
- Two Chromium experiments in the session scratchpad:
  1. A bare page with a slow woff2 face and a missing `local()` face in the same family list.
  2. The built showcase story with Linux's missing faces simulated. An init script added `local()` faces that cannot load for 'Noto Sans Fallback' and 'Noto Sans Arabic Fallback'.

The diagnosis is right. A missing `local()` face makes `document.fonts.load()` reject with NetworkError. With the faces simulated as missing, the old await would stall the story, and the fixed story reaches "drawn". All 14 together/apart comparisons give 0 differing pixels. The four rtl plain/apart cases that should differ (name-code, negative-arabic, percent-arabic, tooltip-line) do differ.

The other font waits do not hang:
- chart-engine.ts:260 settles on both outcomes.
- The probe does not wait on fonts.
- chart.spec and the facade test of chart-bidi.spec.ts do not use the story's path. Both were green on Linux in run 44086742988.

The findings below are about how early the wait now ends.

### Findings

**R1-1 (major): the story draws before the self-hosted faces have loaded.**
`client/projects/internal/showcase/src/app/chart/chart-bidi-story.ts:358-364`

Chromium rejects `FontFaceSet.load()` at the first face that fails. It does not wait for the other faces in the same load. A missing `local()` face fails almost at once.

In the simulated-Linux run of the built story:
- The story's load rejected after 9 ms.
- At that moment 'Noto Sans Arabic' was still `loading`.
- The canvases drew at once, so Arabic was drawn in a fallback face. The painted-pixel counts show it: code-arabic rtl 506 against 525, and negative-arabic ltr 273 against 292, compared with a run where the Arabic face had loaded.

On a Linux runner the Arabic in the battery is therefore drawn either in Noto Sans Arabic or in whatever fontconfig picks. Which one depends on whether a facade chart's engine had already started the Arabic load.

This is not a false pass: together and apart draw in one synchronous pass with the same faces. But:
- The new comment "draws with the faces that did load, as CSS does" is wrong. It draws before they load.
- The template comment at line 300, "Set once every canvas has drawn with the fonts loaded", no longer holds.
- The battery now depends on system fonts and on timing. `eng/playwright-browsers/install-browsers.sh` lets a run continue when apt could not add fonts.

Suggested fix: `.catch(() => document.fonts.ready)`, and correct both comments. `ready` settles only once the faces still loading have finished. In the bare-page experiment it waited the full 1.5 s of a slow woff2 after the load had already rejected. The battery would then always draw in the self-hosted Noto faces, whatever fonts the system has.

**R1-2 (major): tm-chart has the same early settle, and the new unit test pins it.**
`client/projects/core/tellma-core-ui/chart/internal/chart-engine.ts:244-261` and `client/projects/core/tellma-core-ui/chart/tm-chart.spec.ts:432-448`

The engine waits with `document.fonts.load(font, sample).then(done, done)`. On a system without Arial or Tahoma, the load rejects at the failed metric-fallback face while the Noto woff2 faces are still loading. That covers every Linux, ChromeOS and Android client. The bare-page experiment showed the same 3 ms rejection with the woff2 face still loading.

`done` then:
- marks the key settled;
- measures and lays out with fallback widths.

Nothing redraws on `loadingdone`, unlike tm-pivot.ts:1011-1017. This is exactly the hazard the comment at lines 245-246 says the wait prevents: "The renderer caches text widths per font".

It does not hang, so CI stays green. In production, legends, axis labels and data labels can be placed with widths that do not match the glyphs drawn once Noto arrives.

The new test asserts this behaviour. Its comment, "the chart draws with the faces it has", repeats the wrong model from R1-1.

Suggested fix:
- In the engine, wait the same way: `document.fonts.load(font, sample).catch(() => document.fonts.ready).then(done, done)`.
- Make the unit test check that a rejected load still waits for `ready` before the first measure, rather than only that a canvas appears.

The code is outside this commit's diff, but it is the same root cause. It may be routed to a follow-up fix item instead of being fixed here.

**R1-3 (minor): no test runs the changed path outside Linux CI.**
`client/projects/core/tellma-core-ui/chart/tm-chart.spec.ts:432` and `client/e2e/specs/chart-bidi.spec.ts:72-75`

The new unit test exercises chart-engine.ts:260, which this commit does not change. It passes with or without the fix, and it does not touch the story. On Windows and on developer machines Arial and Tahoma exist, so the story's `.catch` never runs. Only the Linux CI run would catch a regression.

Suggested fix: make chart-bidi.spec.ts reproduce Linux in every environment:
- Before `open()`, add an init script that adds faces which cannot load, for example `document.fonts.add(new FontFace('Noto Sans Fallback', "local('<a name no system has>')", { unicodeRange: 'U+0000-00FF' }))`, and the same for 'Noto Sans Arabic Fallback'.
- Keep the existing assertions.

I verified that this injection reproduces the Linux rejection on Windows Chromium. Also reword the unit test's title and comment so they do not suggest the test covers the story.

**R1-4 (minor): the commit message does not list the suites run.**
Commit 4c7e1259 message

PLAN.md §7.1 item 4 requires that "The commit message lists the suites run", and reviewers check that list. The message names neither the client gates nor the e2e run. On Windows those runs do not reach the changed path either (R1-3).

Suggested fix: list the gates and e2e invocations that were run in the fix commit's message.

### Responses

- R1-1: fixed in 0818687f.
  - The story now waits with `.catch(() => document.fonts.ready)`, so its canvases draw once every
    face that is loading has settled. That means they always draw in the self-hosted Noto faces.
  - Both comments are corrected: the load comment, and the template comment, which now reads
    "with every face it needs settled".
- R1-2: fixed in 0818687f.
  - chart-engine.ts waits the same way: `document.fonts.load(font, sample).catch(() => document.fonts.ready).then(done, done)`.
    A comment explains why.
  - The unit test is rewritten as "waits, after a font load fails, for the faces still loading
    before it measures". It mocks a rejected `load` and holds `ready` pending, then asserts no
    canvas until `ready` settles, and a canvas after.
  - Mutant: I put back `.then(done, done)` without the `ready` wait, and the test fails.
  - Size: the engine chunk goes from 200.11 to 200.13 KB (+0.02), within its 200.15 ceiling.
    `./chart` is unchanged at 6.83. No budget change.
- R1-3: fixed in 0818687f.
  - In chart-bidi.spec.ts, `withMissingFallbackFaces(page)` adds an init script before both
    `open()` and the facade test. It registers `local('Tellma No Such Font')` faces for
    'Noto Sans Fallback' (U+0000-00FF) and 'Noto Sans Arabic Fallback' (U+0600-06FF), so the
    rejection path runs on every platform.
  - Mutant: without the story's `.catch`, both canvas-battery tests fail on Windows with the Linux
    CI error (`bidi-drawn` stays "drawing").
  - The unit test's title and comment no longer suggest it covers the story.
- R1-4: fixed in 0818687f. The commit message lists the gates and the e2e invocation it ran.
