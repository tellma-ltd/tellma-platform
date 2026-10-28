# PDF Generation — Library and Approach Analysis for Tellma Documents

**Purpose:** Survey the 2026 landscape of PDF-generation libraries and approaches, and recommend one
for Tellma's generated documents — customer invoices, employee payslips and their kin. These are
branded and heavily customized per customer, filled from tenant data, downloaded interactively,
attached to outgoing email, and rendered in bulk by background jobs. The criteria are in
[Part 0](#part-0--the-requirement).

**Status:** Research/rationale document. It records the comparative analysis and a recommendation;
it is **not** the authoritative design. A later documents spec supersedes it where they differ.
Frozen once merged — not updated as libraries evolve.

**Field surveyed (research conducted 27 October 2026; versions and figures are point-in-time and
will drift):** about 30 options in seven families — browser engines, non-browser HTML engines,
typesetting languages, XSL-FO, report designers, office-document templating, and in-process .NET and
JavaScript PDF libraries — including the brief's own two proposals. Sources:
- **Licences** from NuGet, crates.io, npm and the repositories' LICENSE files.
- **Versions and dates** from the registries and release pages.
- **Platform, PDF/A and attachment support** from official documentation.
- **Open defects** from the issue trackers.
- **ZATCA and UAE requirements** from the regulators' own PDFs.

Vendor-published numbers are flagged as such, and anything not verified says so.

**Anchoring facts** that constrain the choice:

- **Distributions are closed-source and may be delivered on-premises.** Every component must be
  redistributable without copyleft or revenue conditions. The brief's gate is MIT or Apache-2.0;
  [Part 6](#part-6--misalignments-to-resolve) compares that with what the platform already ships.
- **Hosting:**
  - one Azure App Service per distribution, on a Windows or a Linux plan;
  - Azure Container Apps, already in use for the migrator job;
  - on-premises Windows Server and Linux hosts.
- **Arabic is first-class and Amharic is expected.** The server formats dates and numbers in the
  user's region and calendar: Gregorian, Umm al-Qura or Ethiopic.
- **SkiaSharp and PDFium are specified, not yet shipped.**
  [Spec 0017](../specs/0017-blob-storage.md) puts SkiaSharp 4.152.1 and PDFtoImage 5.4.0 in a
  `Tellma.Core.Imaging` package that does not exist in source yet.
- **Rendering already has a call site.** In [spec 0022](../specs/0022-email-outbox.md), the invoice
  `send` action renders the PDF to bytes in memory and attaches it to an outbox email. Bulk runs
  belong to the job runtime of [spec 0020](../specs/0020-background-jobs-and-scheduler.md).
- **Templates are pack content.** Modules ship baseline document templates, and compliance packs ship
  statutory formats (tax invoice, payslip). [Spec 0008](../specs/0008-queryex.md) anticipates
  templates that pull the data they print through Queryex.
- **Tellma 1.0 precedent: there was no server-side PDF.**
  - Tenants authored printing templates as HTML, with embedded Templex expressions that issued
    queries.
  - The server returned the rendered HTML, and the browser printed it. Save-as-PDF meant the user's
    own print dialog.
  - Email attachments were the raw `.html`.

---

## The question, answered in brief

- **Adopt Typst** (Apache-2.0) as the document engine, behind a Tellma-owned, engine-neutral renderer
  contract.
  - C# loads and shapes the data into a display-ready JSON view model.
  - The template is Typst source. Packs ship trusted templates. Templates stored per tenant are
    untrusted, so they compile in an isolated worker process with time and memory caps.
  - Start with the official Typst command-line tool (CLI) run as a subprocess. Add an in-process
    binding only if measured throughput requires it.
  - Typst is the only surveyed engine that clears the licence gate and also has all of the
    following:
    - real templates;
    - single-binary deployment with no system dependencies;
    - compiles measured in milliseconds;
    - Arabic shaping and bidirectional text;
    - native PDF/A-3 with embedded files — the exact output ZATCA mandates when an invoice is sent
      as PDF.
- **Headless Chromium (the brief's option 1) is the fallback, not the default.** It has the most
  familiar authoring (HTML/CSS through Fluid) and the strongest Arabic. Against it:
  - **It cannot run on App Service code plans.** The Windows sandbox blocks the Win32k calls Chrome
    needs, and Microsoft treats Chromium on Linux code plans as unsupported. Every distribution
    would gain a container sidecar.
  - **It is an order of magnitude slower:** about 0.2–0.3 s per invoice, independently measured.
  - **It cannot emit PDF/A or attachments without a post-processor.** Gotenberg's free path
    re-processes through LibreOffice, which rasterizes shaded table cells.
  - **It needs browser patching every week.**
  - **It is an SSRF surface for tenant-authored HTML.**
- **SkiaSharp procedural (option 2) is rejected as the engine.**
  - Skia's PDF backend is a canvas: no line breaking, no bidirectional text, no font fallback, no
    pagination, no tables.
  - From .NET it reaches PDF/A-2b only, with no attachments and no tagging.
  - Per-tenant templates would mean writing a layout engine — re-building QuestPDF, which itself
    went commercial.
  - Reusing an existing dependency counts for less than it seems. SkiaSharp is not yet shipped, and
    what it lacks is the expensive part.
- **Two licence changes in 2026 reshaped the .NET field.**
  - PDFsharp/MigraDoc moved from MIT to AGPL-3.0-or-commercial in 6.5.0 (14 October 2026).
  - QuestPDF moved to a revenue-conditional Community License 3.0 (July 2026).
  - No maintained pure-.NET HTML-to-PDF engine is MIT or Apache-2.0.
- **Typst's risks are real but bounded.**
  - It is pre-1.0, with a breaking release roughly every year. Stored templates need a
    compiler-version tag and a migration step.
  - It has no fuel or timeout for runaway templates; hence the worker process.
  - Its Arabic has open defects: no kashida justification, and text copied out of PDFs comes out
    mis-ordered in common readers.
  - A spike with pass/fail criteria ([5.5](#55-the-spike-that-confirms-it)) settles the open
    questions before a spec is written:
    - Arabic and Amharic fidelity;
    - running on an App Service Windows plan;
    - PDF/A-3b with embedded XML that passes veraPDF;
    - carried-forward subtotals;
    - batch throughput.

---

## Part 0 — The requirement

The brief listed five criteria and asked "anything else I forgot?". The additions are marked **(+)**;
[Part 4](#part-4--cross-cutting-what-the-brief-didnt-list) expands them.

1. **Free** — MIT or Apache-2.0, mandatory.
2. **Fast** — interactive downloads feel instant; a 5,000-payslip batch finishes in minutes.
3. **Cross-platform** — App Service (Windows and Linux plans), on-premises Windows and Linux.
4. **Template-driven** — per-customer layouts as data, not code.
5. **Mature and well supported.**
6. **(+) Complex scripts** — Arabic shaping and bidi, mixed-direction tables, Ethiopic (Amharic).
7. **(+) E-invoicing output** — PDF/A-3 with an embedded XML file (ZATCA; Factur-X in the EU), QR
   codes, Arabic text.
8. **(+) Template trust:**
   - who authors templates;
   - whether a hostile or broken template can hurt the host;
   - how stored templates survive engine upgrades.
9. **(+) Pagination semantics** — repeating table headers and footers, page *X* of *Y*, running
   headers, keep-together, per-page carried-forward subtotals.
10. **(+) Batch throughput and isolation** — thousands of documents per job; a bad template must not
    take the web process down.
11. **(+) Fonts and determinism** — bundled fonts and their licences; identical output on Windows and
    Linux; golden-file tests.
12. **(+) Accessibility** — tagged PDF and PDF/UA, and whether the European Accessibility Act reaches
    ERP documents.
13. **(+) Operations** — footprint, patch cadence, and what redistributing the bundle to on-premises
    customers obliges.
14. **(+) Authoring** — how well AI coding agents can write templates, compile diagnostics, live
    preview.

---

## Part 1 — At a glance: the whole field

"App Service" means a code (non-container) deployment. ✅ yes · ⚠️ partial or conditional · ❌ no ·
? unknown.

| Option (version, date) | Family | Licence | Speed | App Service Win / Linux | On-prem Win / Linux | Templates | Arabic · Ethiopic | PDF/A-3 + attachment | Verdict |
|---|---|---|---|---|---|---|---|---|---|
| **Typst** 0.15.1 (2026-07-17) | typesetting language | ✅ Apache-2.0 | ✅ milliseconds (vendor) | ⚠️ unverified / ✅ | ✅ / ✅ | ✅ markup + scripting over JSON | ⚠️ shaping and bidi, no kashida · untested | ✅ native | **Recommended** |
| **PuppeteerSharp** 25.15.0 (2026-10-21) + Chromium | browser | ✅ MIT wrapper; ⚠️ Chromium BSD + third-party | ⚠️ ~0.2–0.3 s/doc | ❌ / ❌ | ✅ / ✅ | ✅ HTML via Fluid | ✅ · ✅ | ❌ post-process | **Fallback** |
| **Gotenberg** 8.39.0 (2026-10-27) | browser as a service | ✅ MIT code; ⚠️ image bundles GPL/MPL tools | ⚠️ as above, plus HTTP | ❌ / ❌ | ❌ Linux containers only / ✅ | ✅ HTML | ✅ · font unverified | ⚠️ via LibreOffice, which rasterizes shaded cells | Fallback variant |
| Playwright .NET 1.64.1 (2026-10-22) | browser | ✅ MIT | ⚠️ as above | ❌ / ❌ | ✅ / ✅ | ✅ HTML | ✅ · ✅ | ❌ | Dominated by PuppeteerSharp |
| SkiaSharp `SKDocument` 4.152–4.154 | canvas | ✅ MIT (Skia BSD-3) | ✅ fastest raw | ✅ / ✅ | ✅ / ✅ | ❌ C# only | ❌ single-run shaping, no bidi | ❌ PDF/A-2b, no attachments | Rejected as engine |
| + Topten.RichTextKit 0.6.170 | text layout on Skia | ✅ Apache-2.0 | ✅ | ⚠️ version pin and native-asset conflicts | ⚠️ | ❌ | ⚠️ open RTL wrapping bug | ❌ | Rejected |
| PDFsharp / MigraDoc 6.5.0 (2026-10-14) | .NET document model | ❌ AGPL-3.0 or €750 (6.2.4 and earlier MIT) | ✅ | ✅ / ✅ | ✅ / ✅ | ⚠️ MigraDoc DDL | ❌ no RTL | ⚠️ "limited" | Disqualified |
| QuestPDF 2026.9.1 | .NET fluent layout | ❌ Community License 3.0 | ✅ | ✅ / ✅ | ✅ / ✅ | ❌ C# only | ✅ · ? | ✅ | Disqualified (reference design) |
| WeasyPrint 70.0 (2026-09-08) | HTML, non-browser | ❌ BSD-3 + LGPL Pango | ⚠️ 0.2–0.6 s (vendor) | ? / ⚠️ Python sidecar | ✅ executable / ✅ | ✅ HTML | ⚠️ docs list RTL as unsupported | ✅ incl. Factur-X | Fails gate |
| Apache FOP 2.12 (2026-10-20) | XSL-FO | ✅ Apache-2.0 | ? | ❌ JVM / ❌ JVM | ⚠️ JVM | ✅ XSLT | ✅ · ❌ Ethiopic unsupported | ✅ | Rejected |
| FastReport Open Source 2027.1.0 (2026-10-12) | report designer | ✅ MIT | ? | ❌ System.Drawing | ✅ / ❌ | ✅ `.frx` (embeds C#) | ❌ | ❌ each page is a bitmap | Rejected |
| DOCX templating (DocxTemplater, Clippit) + LibreOffice | office documents | ✅ MIT + ⚠️ MPL-2.0 | ❌ 1–3 s/doc | ❌ / ❌ | ⚠️ / ✅ | ✅ authored in Word | ? | ⚠️ via Gotenberg | Rejected |
| OfficeIMO.Word.Pdf 3.5.2 (2026-10-24) | office documents, pure .NET | ✅ MIT | ? | ✅ / ✅ | ✅ / ✅ | ✅ Word | ? | ✅ claimed | Watch (too new) |
| pdfmake 0.3.13, @react-pdf 4.9.0, PDFKit 0.20.2 | JavaScript libraries | ✅ MIT | ? | Node sidecar | Node | ⚠️ JSON / JSX | ❌ no real RTL | ⚠️ PDFKit only | Rejected |
| PeachPDF, HtmlRenderer.PdfSharp, Scryber.Core, wkhtmltopdf | .NET HTML engines | ❌ BSD-3 / LGPL | — | — | — | ✅ HTML | ? | — | Disqualified |
| LaTeX / Tectonic 0.17.0 | typesetting | ⚠️ mixed TeX licences | ❌ seconds | — | — | ✅ | ⚠️ | — | Rejected |
| iText, Aspose, Syncfusion, IronPDF, Prince, PDFreactor, Stimulsoft, Telerik, DevExpress, Oicana | commercial / AGPL / source-available | ❌ | — | — | — | — | — | — | Disqualified |

**What the table says:** after the licence gate and complex scripts, three candidates remain:
Typst, headless Chromium, and SkiaSharp as a foundation to build on.
- **Typst** is the only one that is template-driven, ships as a plain file on every target (App
  Service Windows pending the spike), and produces PDF/A-3 attachments natively.
- **Chromium** wins on authoring familiarity and Arabic, but loses on deployment, speed and
  e-invoicing output.
- **SkiaSharp** wins on deployment and raw speed, but has no templates and no layout.

---

## Part 2 — The shortlist, ranked

| Criterion | Typst | Headless Chromium | SkiaSharp procedural |
|---|---|---|---|
| 1 Licence | ✅ Apache-2.0 throughout | ✅ wrapper; ⚠️ browser bundle | ✅ |
| 2 Speed | ✅ millisecond compiles (vendor), plus 5–100 ms per CLI start | ⚠️ 0.2–0.3 s/doc warm; ~12 min per 5,000 per 4-vCPU node (extrapolated) | ✅ fastest at run time; slowest to build |
| 3 Cross-platform | ✅ static binaries, no system dependencies (Windows sandbox to confirm) | ❌ containers only on Azure | ✅ |
| 4 Templates | ✅ text templates with scripting | ✅✅ HTML/CSS + Fluid | ❌ unless we build an engine |
| 5 Maturity | ⚠️ 0.x, breaking changes yearly; 57k★, sustainable company | ✅✅ | ✅ library / ❌ layout layer |
| 6 Complex scripts | ⚠️ good shaping and bidi, open defects | ✅✅ | ❌ we build bidi, line breaking, fallback |
| 7 PDF/A-3 + XML | ✅ native | ❌ post-processor needed | ❌ post-processor needed |
| 8 Template trust | ⚠️ needs a worker process | ⚠️ SSRF lock-down, weekly patches | n/a (code) |
| 9 Pagination | ✅ repeating headers/footers, page X of Y; carried-forward unproven | ⚠️ `thead` repeats, margin boxes; no per-page state | ❌ we build it |
| 12 Accessibility | ✅ tagged by default, PDF/UA-1 | ⚠️ tagged, not PDF/UA | ❌ not exposed |
| 13 Operations | ✅ ~40 MB binary | ❌ 0.5–0.7 GB image, redeploys weekly to monthly | ✅ |

1. **Typst** *(recommended)*. The surveyed engine with the fewest gaps against the brief:
   - Apache-2.0 throughout;
   - single-file deployment;
   - real templates;
   - Arabic shaping;
   - native PDF/A-3 attachments;
   - tagged PDF by default.

   Its costs are 0.x churn and the worker-process isolation it needs for untrusted templates.
2. **Headless Chromium + Fluid, via PuppeteerSharp in a container** *(fallback)*. Choose it if the
   spike shows Typst's Arabic or Amharic is not acceptable, or that Typst does not run on the
   target hosts. Its costs are a container sidecar per distribution, a separate PDF/A-3
   post-processor, and a weekly patch cadence.
3. **SkiaSharp + a home-grown layout layer.** Only if both of the above fail. It is a multi-month
   engine project with an Arabic long tail.
4. **Apache FOP.** The closest runner-up on paper: Apache-2.0, mature, Arabic, PDF/A-3, PDF/UA. But
   it has no Ethiopic support and needs a Java runtime (JVM) beside the .NET host.

**Disqualified on licence:**
- PDFsharp/MigraDoc 6.5+ (AGPL or commercial).
- QuestPDF (revenue-conditional).
- iText (AGPL); iTextSharp.LGPLv2.Core (LGPL).
- WeasyPrint, PeachPDF, HtmlRenderer.PdfSharp (BSD-3).
- Scryber.Core, wkhtmltopdf (LGPL).
- Vivliostyle (AGPL).
- Oicana (PolyForm Noncommercial).
- Carbone (source-available).
- Every commercial engine and report designer.

---

## Part 3 — Contender by contender

### A. Typst — the recommendation

**What it is.** A markup-based typesetting system with a real scripting language — functions,
loops, conditionals, closures, dictionary and array operations — written in Rust. A template reads
its data from JSON, CSV, XML, YAML or TOML files that the host supplies, so an invoice is a template
module plus one JSON payload.

**Licence.**
- Apache-2.0 for the compiler and every crate needed to embed it (`typst`, `typst-pdf`,
  `typst-kit`, …).
- The dependencies are MIT, Apache-2.0 or both, with two exceptions: Unicode-3.0 line-breaking data,
  and the OFL fonts embedded in the CLI binary. Both are permissive notice licences; neither is
  copyleft.

**Maturity.**
- **Releases:** v0.15.1 (17 July 2026), after 0.12 (October 2024), 0.13 (February 2025), 0.14
  (October 2025) and 0.15 (June 2026).
- **Community:** about 56.9k GitHub stars and ~448 contributors (~110 credited on 0.15 alone); about
  3.6M crates.io downloads.
- **Backing:** Typst GmbH. In August 2025 it had five people and called itself financially
  sustainable, funded by web-app subscriptions, on-premises licences and open-source support.
- **Stability: it is pre-1.0, and every release breaks something.**
  - 0.15 removed `pdf.embed`, forbade backslashes in paths, and changed baseline layout.
  - In January 2026 the lead developer wrote that Typst is "ready for production use", but that
    releasing 1.x now "would be naive". The same post declines to promise migration tooling for
    every breaking change.

**PDF output.**
- All PDF/A levels since 0.14 (a-1b through a-4e), PDF/UA-1, tagged PDF by default, and PDF
  versions 1.4–2.0. Since 0.15 it can target several compatible standards at once.
- `pdf.attach(path, relationship: …, mime-type: …)` embeds files with a PDF/A-3 relationship
  (`source`, `data`, `alternative`, `supplement`). Its documentation cites ZUGFeRD/Factur-X invoices.
  A ZATCA-style PDF/A-3b invoice carrying the cleared XML is therefore native.
- Gaps:
  - attachments are refused under PDF/A-2;
  - no API was found for the custom XMP extension schema that Factur-X requires;
  - PDF/UA-2 is not supported.

**Pagination.**
- Table headers and footers repeat across pages by default, with multi-level subheaders, and cells
  break across pages.
- `numbering: "1 / 1"` prints page *X* of *Y*.
- Page headers and footers, grids and columns are built in, and layout runs in parallel at page
  breaks.
- Per-page carried-forward subtotals are not built in. `state` plus `context` should express them,
  but no published recipe exists, so the spike must prove it.

**Complex scripts.** Strengths:
- shaping through rustybuzz, a port of HarfBuzz;
- automatic bidi for runs;
- RTL grids and tables;
- Eastern-Arabic and Abjad numbering;
- font fallback per code point.

Open defects relevant to invoices:
- **No kashida justification** (#195, open since 2023, "not high on the priority list"). Use
  start-aligned Arabic.
- **Arabic text extracted from the PDF comes out mis-ordered** for ligature clusters in pdf.js,
  MuPDF and poppler, even when veraPDF passes (#4225, confirmed on 0.15.1 in October 2026). The page
  looks right, but copy-paste, search and screen readers suffer. ZATCA reads the embedded XML, not
  the text.
- **PDF/A-3a rejects a digit followed by an Arabic combining mark** (#8489). Target PDF/A-3b.
- **The compiler panics when an RTL span ends in a combining mark** (#8667). That is one more reason
  the renderer runs out of process.

There are no Ethiopic or Amharic issues or forum threads, good or bad. Ethiopic shapes without
reordering, so it should work with Noto Sans Ethiopic, but it is untested.

**Speed.** Typst's own figures:
- compiles "commonly complete in milliseconds";
- CLI start-up costs 5–100 ms depending on font loading; the library route avoids this.

No independent benchmark exists; the spike measures it.

**Platforms.**
- Official binaries exist for win-x64, win-arm64, macOS, and statically linked musl Linux (x64,
  arm64, armv7). They need no glibc and no fontconfig, and weigh about 40 MB.
- Fonts are read from files (`--font-path`, `--ignore-system-fonts`).
- No GDI or user32 path appears in its dependencies.
- The App Service sandbox documentation lists subprocess-based PDF tools (Rotativa/wkhtmltopdf) as
  working on Basic tier and above.
- So the CLI should run on Windows code plans, but this is unverified until the spike runs it.

**Embedding from .NET.** Three routes:
1. **CLI subprocess.** Run `typst compile` with the template, `data.json`, assets and fonts in a
   fresh temporary directory. JSON cannot go through `--input` because of Windows' command-line
   length limit. This route is the simplest, needs no Rust toolchain, and is process-isolated, which
   the untrusted tier needs anyway.
2. **In-process binding.** A thin Rust native library (`cdylib`) over `typst` + `typst-pdf`, called
   through P/Invoke. The compiler reads files through a host-implemented interface (`World`); an
   in-memory implementation decides every file, font and package the compiler sees, and no network
   exists unless the host adds one.
   - **Patterns to copy:** `typst-as-lib` (MIT, ~1.5M downloads), and typst-py (Apache-2.0, released
     the day after Typst 0.15).
   - **Existing .NET bindings:** several MIT ones appeared in 2026 — TypstInterop (embeds 0.15.1,
     with native builds per runtime identifier), Typst.Native, typstsharp and others. All are under
     ten months old, with single maintainers and a few thousand downloads. They are a starting point
     to fork, not a dependency.
   - **Disqualified:** Oicana, the most polished Typst-for-.NET product, is PolyForm Noncommercial.
3. **WASM.** Running Typst under a WebAssembly runtime gives fuel and memory limits for free. It is
   slower, and no one has hosted it from .NET before.

**Sandboxing.**
- **Built-in limits:**
  - `while` loops are capped at 10,000 iterations;
  - call, show-rule and layout depth are capped;
  - an in-memory `World` has no file or network reach.
- **Unbounded resources:** `for` loops, memory and total run time have no limit. A one-line
  template can allocate tens of gigabytes (#3150, open since January 2024), and there is no
  cancellation hook.
- **The CLI's project-root check can be escaped through symlinks** (#5454, open). This does not
  matter when the host writes a fresh directory of its own files.
- **The CLI downloads `@preview` packages on first import.** Vendor the allowed packages into the
  local package directory, and reject other imports when a template is saved.
- **Conclusion:** trusted templates can run anywhere. Tenant-authored ones need a process with a
  wall-clock timeout and a memory cap.

**Authoring and preview.**
- Compile diagnostics point to the exact source location (a span) and include hints.
- AI models trained on older Typst emit removed APIs (`locate`, `pdf.embed`, `pattern`). Template
  writing by agents therefore needs a compile-check loop and version-matched docs; each release
  ships its documentation as a PDF.
- typst.ts (Apache-2.0) compiles and renders Typst in the browser. That makes a live template
  preview in the SPA possible, provided its bundled compiler matches the server's.

**QR codes.** Generate the code in C# with Net.Codecrete.QrCodeGenerator (MIT, no dependencies, SVG
output), and hand the SVG to the template. That keeps ZATCA's TLV payload encoding in tested C#
rather than in template code.

### B. Headless Chromium (+ Fluid) — the fallback

**What it is.** Fluid (MIT, Liquid syntax) renders tenant data into HTML. Chromium prints it to PDF
through the Chrome DevTools Protocol, driven by one of:
- **PuppeteerSharp** (MIT, 25.15.0, 41.9M downloads), which releases every one to two weeks
  tracking Chrome;
- **Gotenberg** (MIT, 8.39.0, 13.5k stars), which wraps Chromium as an HTTP service.

Playwright for .NET (MIT) is not worth it here. It adds a bundled Node driver (~70 MB per app,
according to one secondary source) for no gain in a PDF-only service, and it defaults to untagged
PDFs.

**Strengths.**
- **Familiar authoring:** HTML and CSS are the most widely known layout language — for engineers,
  tenant implementers and agents alike.
- **Continuity with 1.0:** Tellma 1.0's tenant templates were HTML, so their bodies carry over;
  their Templex expressions do not.
- **Best text rendering:** Blink's text stack (HarfBuzz shaping, full bidi) gives the best Arabic
  and Ethiopic of any option.
- **Paged-media support:**
  - Chrome 131 added all sixteen `@page` margin boxes, with `counter(page)` and `counter(pages)`;
  - `thead` repeats on every page;
  - tagged PDF has been generated since Chrome 85, and PuppeteerSharp tags by default.

**Deployment — the deciding weakness.**
- **App Service Windows code plans:** the sandbox blocks "almost all of the Win32k.sys APIs".
  Community reports of Puppeteer there are mostly failures, and the PuppeteerSharp maintainer's
  advice is Docker.
- **App Service Linux code plans:** Chromium's shared libraries are missing and cannot be installed.
  A Microsoft Q&A moderator (February 2026) calls headless Chromium "not an officially supported
  workload" there, and points to a custom container or Container Apps.
- **Azure, therefore:** a container per distribution (Container Apps or Web App for Containers)
  beside the App Service. The alternative is one shared renderer for the fleet, which moves tenant
  data across distribution boundaries.
- **On-premises:** PuppeteerSharp runs in an ordinary Windows service or on a Linux host. Gotenberg
  needs Linux containers, which means WSL 2 or a Linux VM on Windows Server.

**Speed.**
- **Independent benchmark (October 2026):**
  - a four-page invoice took 0.32 s with `chrome-headless-shell`, versus 0.72–1.10 s with full
    Chrome;
  - 40 invoices took 5.8 s with four parallel jobs on a loaded 4-vCPU VM;
  - extrapolated, that is roughly 12 minutes per 5,000 payslips per node.
- **Vendor benchmark:** claims 3–58 ms warm.
- The independent figure is the safer planning number.

**E-invoicing.**
- `Page.printToPDF` has no PDF/A, PDF/UA or attachment option.
- Gotenberg post-processes PDF/A through LibreOffice, which rasterizes table cells that have a
  background colour — most invoice tables. It embeds attachments through QPDF.
- The alternatives are a frozen PDFsharp 6.2.4 (the last MIT release) or a home-grown PDF/A-3 writer
  step.
- Either way, it is a second component in the pipeline.

**Paged-media limits.**
- No running elements, `string-set`, cross-references or footnotes; Chrome lists them as future
  work.
- No per-page state. Carried-forward subtotals are therefore out of reach without JavaScript
  pagination, and Paged.js (MIT), the JavaScript library for that, has had no stable release since
  July 2023.

**Security and operations.**
- **Tenant-authored HTML makes the renderer an SSRF and local-file-read surface.** Gotenberg
  published ten advisories between April and October 2026, including DNS rebinding, an IPv6
  deny-list bypass, a WebSocket allow-list bypass, and SSRF through LibreOffice.
- **The lock-down:**
  - render from a string;
  - intercept every request and abort all but inlined assets;
  - disable JavaScript;
  - use a fresh browser context per document;
  - deny network egress.
- **Patch cadence:** Chrome ships security fixes every week, so the renderer image is rebuilt on
  that cadence.
- **Bundled licences:** the stock Gotenberg image (~706 MB) bundles PDFtk Java
  (GPL-2.0-or-later), LibreOffice (MPL-2.0) and ExifTool (Perl terms). Redistributing it to
  on-premises customers means shipping notices and source offers. A slim image built from
  Gotenberg's MIT code avoids this.

**Fluid**, for the record:
- Fluid.Core 2.40.0 (August 2026, MIT; 3.0 in beta) allow-lists member access, and offers step,
  recursion, output-size and collection limits plus cancellation.
- But the limits default to unlimited, and its README says it "is a template engine, not a security
  sandbox". Whether all the limits exist in 2.40, rather than only in 3.0, is unverified.
- Scriban, the other popular engine, is BSD-2-Clause, and published three High-severity advisories
  on 24 October 2026 alone.

### C. SkiaSharp procedural — the brief's option 2

**What SkiaSharp exposes.**
- `SKDocument.CreatePdf` plus `SKDocumentPdfMetadata`, which covers:
  - title, author, subject, keywords, creator, producer;
  - creation and modification dates;
  - raster DPI and JPEG quality;
  - a `PdfA` flag producing PDF/A-2b, which also makes output non-reproducible.
- `DrawUrlAnnotation` adds links.
- SkiaSharp's C layer does not expose upstream Skia's structure tree (tagged PDF), language, outline
  or compression options.
- Image filters are rasterized, and several blend modes are ignored.

**What it does not do.**
- `SKShaper` (SkiaSharp.HarfBuzz) shapes one run in one font. It has no bidi reordering, no line
  breaking and no font fallback, and an instance is not thread-safe.
- Everything a document needs above glyph placement would be Tellma's code:
  - bidirectional ordering (UAX #9) and line breaking (UAX #14);
  - splitting text by script and font;
  - justification;
  - tables and pagination;
  - repeating headers and page counters.
- PDF/A-3 and attachments would need a post-processor as well.

**The micro-templating extension.**
- Making it template-driven means designing a layout DSL and an engine to execute it — in effect,
  writing QuestPDF.
- QuestPDF is the measure of that cost. It is a full-time commercial product, and its 2026 releases
  alone add automatic table tagging, ligature `/ActualText`, missing-glyph handling and Factur-X
  attachment fixes. That is the long tail a home-grown engine inherits.
- Its last MIT release (2022.12.15) targets SkiaSharp 2.88 and would not load against SkiaSharp
  4.x, so it is useful only as reference source.

**RichTextKit** (Apache-2.0) supplies the missing text layer — bidi, line breaking, HarfBuzz
shaping, fallback — and revived in October 2026 (0.6.170). Against it:
- It requires SkiaSharp ≥ 4.153.1, which is a downgrade error against the 4.152.1 pin.
- It pulls in the fontconfig native package, which collides with the NoDependencies package that
  spec 0017 chose.
- It calls itself "still under development", and is tested on Windows only.
- It has an open bug that adds a leading space to wrapped right-to-left lines.

**Verdict.** SkiaSharp is fast and already planned, but it moves the whole problem into Tellma's
codebase. Even for single-customer distributions, where layouts coded in C# are acceptable, the
missing bidi and line-breaking layer makes Arabic documents a project of their own.

### D. The rest of the field

- **Apache FOP** (2.12, 20 October 2026, Apache-2.0). Mature XSL-FO with full Arabic, PDF/A-1/2/3,
  PDF/UA-1 and embedded files.
  - Rejected for three reasons:
    - Ethiopic is not among its supported scripts;
    - it needs a JVM beside the .NET host;
    - XSL-FO is verbose to author.
  - **FO.NET** is dead: its last release was in 2011, and a .NET Standard fork followed in 2020.
  - **FOP.Sharp**, an Apache-2.0 C# port first published in June 2026, has no track record.
- **WeasyPrint** (70.0, September 2026). Strengths: the best open-source CSS Paged Media support
  (running elements, footnotes, cross-references), PDF/A including Factur-X, and PDF/UA-1/2.
  Against it:
  - it fails the gate: BSD-3-Clause, with LGPL Pango at run time;
  - its documentation still lists right-to-left and bidirectional text as unsupported, despite a
    stream of RTL fixes;
  - it is a Python sidecar.
- **PDFsharp / MigraDoc.**
  - 6.5.0 (14 October 2026) is AGPL-3.0-only or a €750 commercial licence, with the choice
    enforced at run time.
  - The last MIT release is 6.2.4 (6 January 2026).
  - No version supports RTL or shaping.
  - 6.2.4 remains the only MIT .NET library that merges, embeds files, does PDF/A (described as
    limited) and signs. That makes it a candidate frozen post-processor, not an engine.
- **QuestPDF.** Community License 3.0 since July 2026:
  - free under USD 1M consolidated revenue, but never for public companies or the public sector;
  - Professional $1,999, Enterprise $4,999.

  Disqualified, but the best reference design for a Skia layout engine.
- **FastReport Open Source** (MIT):
  - its free PDF export draws each page into one bitmap, so there is no vector text;
  - it depends on System.Drawing, which is Windows-only on modern .NET;
  - its designer is Windows-only and not MIT;
  - its templates embed compiled C#.
- **DOCX templating.** MIT libraries abound (DocxTemplater, Clippit, OfficeIMO), and Word is the
  most tenant-friendly authoring tool. But conversion to PDF has only two options:
  - LibreOffice: MPL-2.0, 1–3 s per document, unreliable in parallel;
  - OfficeIMO.Word.Pdf: MIT, new, and it "does not guarantee exact Microsoft Word pagination".

  Carbone's licence is source-available, with a clause restricting use through a wrapper.
- **JavaScript libraries.** pdfmake, @react-pdf/renderer, PDFKit and jsPDF are MIT and active, but
  none does real RTL; pdfmake's RTL issue has been open since 2015. Running them in-process would
  need Jint, which is BSD-2-Clause.
- **Pure-.NET HTML engines.** None passes the gate:
  - PeachPDF: BSD-3; weekly releases, flexbox and margin boxes; RTL unknown.
  - HtmlRenderer.PdfSharp: BSD-3; CSS 2; System.Drawing.
  - Scryber.Core: LGPL-3.0.
  - wkhtmltopdf: LGPL-3.0, archived January 2023.
- **New Rust HTML engines** (ironpress, fulgur). Immature, with no .NET binding.
- **LaTeX / Tectonic.** Gigabyte distributions or on-demand bundles under mixed TeX licences, and
  seconds per document.
- **Commercial and copyleft:**
  - iText (AGPL);
  - Syncfusion (community licence capped by revenue, headcount and funding);
  - Aspose, IronPDF, Prince, PDFreactor, Stimulsoft, Telerik, DevExpress, ActiveReports, Oicana.

---

## Part 4 — Cross-cutting: what the brief didn't list

### 4.1 E-invoicing decides the output format

- **Saudi Arabia (ZATCA Phase 2).**
  - E-invoices "must be generated in XML format or PDF/A-3 format (with embedded XML)".
  - Submissions to ZATCA are XML only.
  - Standard (B2B) invoices go to buyers as XML or PDF/A-3. ZATCA's FAQ answers that if the invoice
    is sent as PDF, "PDF-A/3 (with embedded XML) is mandatory".
  - A QR code is mandatory. ZATCA produces it at clearance for B2B; the invoicing software produces
    it (nine TLV tags) for B2C.
  - The human-readable invoice must be in Arabic.
  - ZATCA's cryptographic stamp is on the XML, so no PDF signature is needed.
  - **Consequence:** for Saudi B2B invoices, the PDF is rendered after clearance returns the
    stamped XML and QR string, and it embeds that XML.
- **UAE (PINT AE over Peppol, live from 2027 for the largest taxpayers).**
  - The legal invoice is XML and "will not feature a QR code or barcode".
  - A PDF tax invoice is needed in addition only while the buyer is not on e-invoicing.
  - No PDF/A requirement.
- **EU (Factur-X 1.09.2 / ZUGFeRD 2.5.2, August 2026).** PDF/A-3 with embedded CII XML plus an XMP
  extension schema: the same capability as ZATCA, plus custom XMP.
- **Ethiopia (Directive 1142/2026, June 2026; secondary sources only).** Invoices carry a
  registration number and a QR code from the Ministry of Revenues' system. No file format is set.

The engine therefore needs PDF/A-3b plus an associated-file attachment. Typst does this natively;
Chromium and Skia need a post-processor. The document model also needs a slot for a QR string
supplied from outside.

### 4.2 Fonts, scripts and determinism

- **Ship the fonts; never rely on the host's.**
  - Noto Sans, Noto Naskh/Kufi Arabic and Noto Sans Ethiopic are OFL-1.1. Embedding them (full or
    subset) and bundling them with proprietary software are allowed, provided the copyright notice
    and licence text ship too.
  - Linux containers and NoDependencies Skia builds can lack fontconfig and system fonts. The
    SkiaSharp 4.156 preview notes say it "restores Linux system-font discovery for NoDependencies
    deployments".
- **Tenant brand fonts need a licence check per tenant.** Commercial fonts can forbid embedding on a
  server.
- **Pin the engine and fonts** so the same input produces the same bytes on Windows and Linux, and
  fix the creation timestamp for golden tests.

### 4.3 Template trust tiers

Tellma 1.0 let tenants author templates. If 2.0 keeps that, two tiers follow, whatever the engine:
- **Package templates** (module, compliance, distribution) are reviewed code and may render
  in-process.
- **Tenant templates** (stored as data) are untrusted. They render in a worker process with a
  wall-clock timeout and a memory cap, no network, and only files the host supplies.
  - For Typst, the threat is resource exhaustion and crashes.
  - For Chromium, it is SSRF and file reads.

Stored templates also need a compiler-version tag, and a validation pass whenever the engine is
upgraded ([4.5](#45-engine-upgrades-and-stored-templates)).

### 4.4 Shape data in C#, not in the template

- **Format on the server, not in the template.**
  - Typst has no locale data, no Umm al-Qura or Ethiopic calendar, and no amount-in-words.
  - Chromium has `Intl`, but it would format differently from the server.
  - So format in C#, with the same CLDR data, calendars and `IAmountToText` that the rest of the
    server uses. Hand the template a display-ready JSON view model, with formatted text beside the
    raw value where the template needs arithmetic.
  - The template's job is layout.
- **Queries move out of the template.** Spec 0008 envisages "templates that pull the data they print
  by issuing queries". A Typst template cannot call back into the host mid-render. So the template
  definition declares its queries, and the host runs them under the caller's permissions before
  rendering.

### 4.5 Engine upgrades and stored templates

Typst's yearly breaking releases make this the biggest ongoing cost of the recommendation. The
process:
- A platform release pins one compiler version.
- Package templates are migrated with the code.
- The migrator (or a job) compiles tenant templates against the new version and reports failures
  before the slot swap.
- With the CLI route, keeping the previous binary for a transition window is cheap.

### 4.6 Batch throughput and isolation

A 5,000-payslip run is a background job ([spec 0020](../specs/0020-background-jobs-and-scheduler.md)):
- it fans documents out across worker processes, one document per compile, giving a separate PDF
  for each email;
- it writes results to blob storage ([spec 0017](../specs/0017-blob-storage.md)) and the outbox
  ([spec 0022](../specs/0022-email-outbox.md)).

Cost per document:
- **Typst:** the compile plus 5–100 ms of process start-up (vendor figures, to be measured).
- **Chromium:** about 0.2–0.3 s, plus a sidecar to scale.

### 4.7 Accessibility

- The European Accessibility Act has applied to consumer-facing services since 28 June 2025. It
  requires "electronic information needed in the provision of the service" to be accessible.
- Scope:
  - invoices are not named;
  - B2C invoices of an EU e-commerce service are plausibly in scope;
  - payslips are not in scope;
  - microenterprises are exempt.
- Tellma's current markets are outside the EU, so this is a future concern.
- It still favours engines that tag:
  - Typst: tagged by default, PDF/UA-1;
  - Chromium: tagged, but not validated as PDF/UA;
  - Skia: tagging not reachable from SkiaSharp.

### 4.8 Testing

- Render golden documents and compare them as images through PDFtoImage, already specified in spec
  0017.
- Assert text and metadata with PdfPig (Apache-2.0; read side only).
- Validate PDF/A-3 output with veraPDF in CI.
- Include an Arabic invoice, a bilingual invoice and an Amharic payslip in the golden set.

### 4.9 Signatures

- No target jurisdiction requires a signed PDF.
- If tamper evidence is wanted later, PDFsharp 6.2.4 (MIT, frozen) signs with CMS and timestamps.
  Whether that meets the PAdES baseline is unverified.
- A signature is applied after attachments are embedded.

---

## Part 5 — Recommendation and architecture

### 5.1 Decision summary

| # | Decision | Recommendation |
|---|---|---|
| D1 | Engine | Typst, pinned per platform release |
| D2 | Integration | The official CLI as a subprocess first. An in-process binding only if measured throughput requires it, and only for package templates |
| D3 | Seam | An engine-neutral renderer contract in `Tellma.Core.Abstractions`, implemented in an optional Core-layer package |
| D4 | Data | Declared queries → C# shapes and formats a view model → JSON; templates do layout only |
| D5 | Trust | Package templates trusted; tenant templates rendered in a worker process with resource caps |
| D6 | E-invoicing | PDF/A-3b, with the XML attached through `pdf.attach`; QR generated in C# |
| D7 | Fonts | Bundled Noto families plus tenant brand fonts; system and embedded fonts ignored |
| D8 | Fallback | Fluid + PuppeteerSharp in a container, behind the same contract, if the spike fails |

### 5.2 The pipeline

```
template definition
  (Typst source, assets, declared queries, parameters,
   PDF standard, compiler version, trust tier)
        │
        ▼
load     run the declared queries through the read pipeline (caller's permissions)
shape    C# builds a display-ready view model (calendar, region, digits,
         amount-in-words, resolved labels) → JSON
render   Typst compiles template + data.json + assets + bundled fonts
         (PDF/A-3b and attachments where the document type requires them)
deliver  bytes → download response · blob · outbox attachment
```

### 5.3 The contract (sketch)

```csharp
public interface IDocumentRenderer
{
    Task<RenderedDocument> RenderAsync(DocumentRenderRequest request, CancellationToken cancellation);
}

public sealed record DocumentRenderRequest(
    DocumentTemplate Template,                      // source files, assets, compiler version, trust tier
    ReadOnlyMemory<byte> Model,                     // the JSON view model
    PdfConformance Conformance,                     // None, PdfA3b, …
    IReadOnlyList<DocumentAttachment> Attachments); // e.g. the cleared ZATCA XML

public sealed record DocumentAttachment(
    string FileName, string MediaType, AttachmentRelationship Relationship, ReadOnlyMemory<byte> Content);
```

Nothing in the contract names Typst. The Chromium fallback (HTML template, Fluid, PuppeteerSharp) is
therefore a second implementation, not a redesign. In spec 0022, `_renderer.RenderAsync(invoice)`
becomes a module service that shapes the invoice and calls this contract.

### 5.4 Packaging

- A new optional Core-layer package (working name `Tellma.Core.Documents`):
  - depends on `Tellma.Core.Abstractions` only;
  - carries the Typst binary for each runtime identifier: win-x64, win-arm64, and linux-x64 and
    linux-arm64 musl (the static musl build runs on glibc hosts too);
  - is composed explicitly by distributions, like the imaging package.
- Packs ship their templates and fonts as embedded resources.
- Third-party notices cover Apache-2.0, Unicode-3.0 and OFL.

### 5.5 The spike that confirms it

Pass/fail criteria, run before the documents spec:

1. **Arabic:** an Arabic reader reviews a bilingual Arabic/English tax invoice as correct. It
   includes an RTL table with LTR amounts and IDs, lam-alef ligatures, tanween, and Eastern-Arabic
   digits.
2. **Amharic:** a reader reviews a payslip in Amharic, set in Noto Sans Ethiopic, as correct.
3. **PDF/A-3b:** the invoice, with an embedded UBL XML, passes veraPDF.
4. **Hosting:** the CLI renders on an App Service Windows code plan, a Linux code plan, Windows
   Server and a Linux container.
5. **Throughput:**
   - 5,000 payslips on one 4-vCPU instance within an agreed budget (proposed: 5 minutes);
   - a single invoice within 300 ms end to end.
6. **Carried-forward subtotals:** a multi-page ledger table prints "carried forward" and "brought
   forward" on every page.
7. **Isolation:** a memory-bomb template and an unbounded `for` loop are killed at their caps
   without disturbing the host.
8. **Determinism:** Windows and Linux produce identical bytes for the same input and timestamp.

What a failure means:
- **Failing 1, 2 or 4 beyond repair** triggers the fallback (D8).
- **Failing 5** triggers the in-process binding.
- **Failing 6** means no surveyed free engine supports carried-forward subtotals; Chromium cannot
  either. It then becomes a scope decision ([Part 7](#part-7--open-questions), question 3).

---

## Part 6 — Misalignments to resolve

ARCHITECTURE.md is a living document, not an authority. Each of these should be brought into line
one way or the other:

1. **Native binaries.**
   - ARCHITECTURE.md describes `Tellma.Core.Imaging` as the only package carrying native binaries. A
     Typst-based renderer (or Chromium) would be a second.
   - Proposed rule: native binaries are confined to optional, explicitly composed Core-layer
     packages, never `Tellma.Core`.
2. **Document templates have no format.**
   - ARCHITECTURE.md assigns baseline document templates to modules, and statutory templates (tax
     invoice, payslip) to compliance packs. It names no template language, data contract or engine.
   - This analysis proposes Typst source over a JSON view model.
3. **Templates that issue queries.**
   - [Spec 0008](../specs/0008-queryex.md) describes document templates that "pull the data they
     print by issuing queries".
   - Under this recommendation the template does not issue queries. Its definition declares them,
     and the host runs them before rendering ([4.4](#44-shape-data-in-c-not-in-the-template)).
4. **The licence gate is stricter than current practice.**
   - The brief requires MIT or Apache-2.0. But spec 0017 already specifies Skia (BSD-3-Clause) and
     PDFium (BSD-style), and any engine brings data or font licences that only require notices
     (Unicode-3.0, OFL-1.1).
   - Practice would be matched by stating the policy as: "permissive (MIT, Apache-2.0, BSD, ISC,
     Unicode; OFL for fonts); no copyleft; no licences conditional on revenue or use".
   - That wording would readmit WeasyPrint and PeachPDF. Neither changes the recommendation.
5. **"SkiaSharp is already a dependency."**
   - It is specified in spec 0017 but not yet built.
   - Skia's PDF backend lacks the layout, shaping and PDF/A-3 features documents need.
   - So it should carry little weight in this choice.

---

## Part 7 — Open questions

1. **Who authors templates in 2.0?** Engineers and agents only, per distribution? Or tenant
   administrators too, as in 1.0? The answer decides two things:
   - whether the untrusted tier, a template editor and live preview (typst.ts) are needed in the
     first release;
   - how heavily HTML familiarity should weigh against Typst.
2. **Do Tellma 1.0's tenant printing templates migrate?** Their HTML bodies would carry over to the
   Chromium path, but not to Typst. Their Templex expressions carry over to neither. How many are in
   production?
3. **Are per-page carried-forward subtotals required** (payroll registers, statements, ledgers)? No
   surveyed free engine guarantees them; Typst may be able to express them (spike item 6).
4. **Is Factur-X/EU in scope?** Typst lacks an API for custom XMP, so Factur-X would need a
   post-processing step. ZATCA does not.
5. **How should the licence gate be worded?** See Part 6, item 4.
6. **What is the Typst version policy?**
   - How long does a superseded compiler stay available for tenant templates?
   - Who fixes a tenant template that fails to compile after an upgrade?
7. **Should documents also have an HTML rendition**, such as an invoice body inside the email, or an
   in-app preview without PDF? Typst's HTML export is experimental, so email bodies may stay a
   separate template.
8. **Is a Rust toolchain in platform CI acceptable**, if the in-process binding becomes necessary?
   It would need cross-compiling for each runtime identifier on Windows and Linux.

---

## Appendix — Sources

All accessed 27 October 2026.

**Typst**
- Releases and licence: <https://github.com/typst/typst/releases>,
  <https://github.com/typst/typst/blob/v0.15.1/Cargo.toml>, <https://crates.io/crates/typst>
- Changelogs: <https://typst.app/docs/changelog/0.12.0/>, <https://typst.app/docs/changelog/0.13.0/>,
  <https://typst.app/docs/changelog/0.14.0/>, <https://github.com/typst/typst/releases/tag/v0.15.0>
- PDF, attachments, tables: <https://typst.app/docs/reference/pdf/>,
  <https://typst.app/docs/reference/pdf/attach/>, <https://typst.app/docs/reference/model/table/>,
  <https://typst.app/docs/reference/text/text/>
- Stability and company: <https://laurmaedje.github.io/posts/evolving-typst>,
  <https://typst.app/blog/2025/future>, <https://typst.app/blog/2025/automated-generation>,
  <https://typst.app/docs/roadmap/>
- Issues: <https://github.com/typst/typst/issues/195>,
  <https://github.com/typst/typst/issues/3150>, <https://github.com/typst/typst/issues/4225>,
  <https://github.com/typst/typst/issues/5454>, <https://github.com/typst/typst/issues/8489>,
  <https://github.com/typst/typst/issues/8667>
- Sandbox limits: <https://github.com/typst/typst/blob/v0.15.1/crates/typst-eval/src/flow.rs>,
  <https://github.com/typst/typst/blob/v0.15.1/crates/typst-library/src/engine.rs>
- Embedding: <https://crates.io/crates/typst-as-lib>, <https://github.com/messense/typst-py>,
  <https://github.com/KanarekLife/TypstInterop>, <https://www.nuget.org/packages/typstsharp>,
  <https://github.com/Myriad-Dreamin/typst.ts>, <https://www.nuget.org/packages/Oicana/0.10.0/license>

**Headless Chromium and HTML templating**
- PuppeteerSharp: <https://www.nuget.org/packages/PuppeteerSharp>,
  <https://www.puppeteersharp.com/api/PuppeteerSharp/PdfOptions/>,
  <https://github.com/hardkoded/puppeteer-sharp/discussions/2423>
- Playwright: <https://playwright.dev/dotnet/docs/api/class-page#page-pdf>,
  <https://playwright.dev/dotnet/docs/browsers>
- Gotenberg: <https://github.com/gotenberg/gotenberg/releases>, <https://gotenberg.dev/docs/configuration>,
  <https://gotenberg.dev/docs/manipulate-pdfs/pdfa-pdfua>,
  <https://github.com/gotenberg/gotenberg/security/advisories>,
  <https://raw.githubusercontent.com/gotenberg/gotenberg/main/build/Dockerfile>
- Azure: <https://github.com/projectkudu/kudu/wiki/Azure-Web-App-sandbox>,
  <https://learn.microsoft.com/en-my/answers/questions/5781033/the-chromium-binary-fails-to-start-in-azure-app-se>
- Chrome printing: <https://developer.chrome.com/blog/print-margins>,
  <https://blog.chromium.org/2020/07/using-chrome-to-generate-more.html>,
  <https://raw.githubusercontent.com/ChromeDevTools/devtools-protocol/master/pdl/domains/Page.pdl>
- Benchmarks: <https://computingforgeeks.com/chrome-headless-print-to-pdf/> (independent),
  <https://pdf4.dev/blog/html-to-pdf-benchmark-2026> (vendor)
- Template engines: <https://github.com/sebastienros/fluid>, <https://www.nuget.org/packages/Fluid.Core>,
  <https://github.com/scriban/scriban/security/advisories>

**SkiaSharp and .NET PDF libraries**
- SkiaSharp: <https://www.nuget.org/packages/SkiaSharp>, <https://github.com/mono/SkiaSharp/releases>,
  <https://raw.githubusercontent.com/mono/SkiaSharp/main/binding/SkiaSharp/Definitions.cs>,
  <https://raw.githubusercontent.com/mono/SkiaSharp/main/source/SkiaSharp.HarfBuzz/SkiaSharp.HarfBuzz/SKShaper.cs>,
  <https://skia.org/docs/user/sample/pdf/>
- RichTextKit: <https://www.nuget.org/packages/Topten.RichTextKit>,
  <https://github.com/toptensoftware/RichTextKit>
- QuestPDF: <https://www.questpdf.com/license/community.html>, <https://www.questpdf.com/pricing.html>,
  <https://github.com/QuestPDF/QuestPDF/releases>, <https://www.nuget.org/packages/QuestPDF/2022.12.15>
- PDFsharp: <https://docs.pdfsharp.net/General/License/License.html>,
  <https://docs.pdfsharp.net/General/History.html>, <https://www.nuget.org/packages/PDFsharp/6.2.4>,
  <https://www.nuget.org/packages/PDFsharp/6.5.0>
- Utilities: <https://www.nuget.org/packages/PdfPig>,
  <https://www.nuget.org/packages/Net.Codecrete.QrCodeGenerator>

**Other engines**
- Apache FOP: <https://xmlgraphics.apache.org/fop/>,
  <https://xmlgraphics.apache.org/fop/2.10/complexscripts.html>
- WeasyPrint: <https://doc.courtbouillon.org/weasyprint/stable/api_reference.html>,
  <https://doc.courtbouillon.org/weasyprint/stable/changelog.html>
- FastReport: <https://github.com/FastReports/FastReport>,
  <https://github.com/FastReports/FastReport/issues/611>
- Office documents: <https://github.com/EvotecIT/OfficeIMO>, <https://github.com/Amberg/DocxTemplater>,
  <https://unpkg.com/carbone@3.8.2/LICENSE.md>
- JavaScript libraries: <https://github.com/bpampuch/pdfmake/issues/184>
- Pure-.NET HTML engines: <https://www.nuget.org/packages/PeachPDF>,
  <https://www.nuget.org/packages/Scryber.Core>

**Compliance and fonts**
- ZATCA:
  <https://zatca.gov.sa/en/E-Invoicing/Introduction/LawsAndRegulations/Documents/20230519_E-Invoicing%20Implementation%20Resolution%20English.pdf>,
  <https://zatca.gov.sa/en/E-Invoicing/Introduction/Guidelines/Documents/E-Invoicing_Detailed__Guideline.pdf>
- UAE: <https://mof.gov.ae/wp-content/uploads/2026/02/UAE-Electronic-Invoicing-Guidelines_V-1.0-23Feb2026.pdf>
- Factur-X: <https://fnfe-mpe.org/factur-x/>
- Ethiopia (secondary):
  <https://www.feyselandassociates.com/insights/articles-and-updates/ethiopia-introduces-electronic-invoicing-directive-no-11422026/>
- European Accessibility Act: <https://www.legislation.gov.uk/eudr/2019/882/annex/I>
- Fonts: <https://openfontlicense.org/ofl-faq/>, <https://github.com/notofonts/arabic>,
  <https://github.com/notofonts/ethiopic>
