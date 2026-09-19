# Research: Excel codec — export and import (theme `excel`, future spec 0018)

All findings verified on **2026-09-01** unless stated otherwise. Each finding says what was
verified, lists sources (primary first), and ends with a one-line design implication. Anything
not confirmed against a primary source is marked **Inference** or **Unverified**.

Inputs read for context: `.tmp/crud-stack.md` ("ExportByQuery", "ExportByQueryForImport",
"ExportByIds", "ExportByIdsForImport", "Import", the Queryex TVP restriction note, "Background
Tasks", "Inbox"), `.tmp/crud-stack-specs.md` (entry 0018 and the natural-key seam owned by 0011),
`ARCHITECTURE.md` (Data Layer: leaf-only mapping, no parent→child EF navigations, app-assigned
ids, `HasData` reserved band; Frontend i18n: `TmDateAdapter`/Hijri via locale packs; Reports
tier 3 "within-stack exports"). What the designers need from this file: a library choice that
exports up to the Excel row limit with bounded memory and re-reads the same file; the exact Excel
facts that constrain the sheet layout (limits, number formats, calendars, date serials, string
storage, metadata carriers, validation lists, protection); and the header, natural-key, child-row
and error-reporting conventions of comparable systems.

---

## 1. .NET Excel libraries as of 2026-09

### 1.1 Versions, dates, licenses, target frameworks (all verified against the NuGet v3 registration and nuspec APIs on 2026-09-01)

| Package | Latest stable | Published | License | Target frameworks in nuspec | Notable dependencies |
|---|---|---|---|---|---|
| ClosedXML | 0.105.1 | 2026-07-25 | MIT | netstandard2.0, netstandard2.1 | DocumentFormat.OpenXml [3.1.1,4.0.0), ClosedXML.Parser 2.0.0 (MIT), ExcelNumberFormat 1.1.0 (MIT), RBush.Signed 4.0.0, **SixLabors.Fonts [1.0.0,3.0.0)**, Microsoft.Bcl.HashCode, System.Buffers, System.Memory |
| EPPlus | 8.7.0 | 2026-08-20 | Polyform Noncommercial 1.0.0 / commercial (custom `license.md`) | net35, net462, netstandard2.0, netstandard2.1, net8.0, net9.0, **net10.0** | EPPlus.Interfaces, Microsoft.Extensions.Configuration.Json, Microsoft.IO.RecyclableMemoryStream, System.Security.Cryptography.Pkcs/Xml |
| MiniExcel | 1.46.0 (2.0.0-preview.4 on 2026-06-13) | 2026-08-22 | Apache-2.0 | net45, net461, netstandard2.0, net8.0, net9.0, **net10.0** (2.0 preview: netstandard2.0, net8/9/10) | Microsoft.Bcl.AsyncInterfaces only (2.0 splits into MiniExcel.Core / MiniExcel.OpenXml / MiniExcel.Csv) |
| DocumentFormat.OpenXml (Open XML SDK) | 3.5.1 | 2026-03-18 | MIT | net35, net40, net46, netstandard2.0, net8.0, **net10.0** | DocumentFormat.OpenXml.Framework 3.5.1 |
| NPOI | 2.8.0 | 2026-04-06 (GitHub tag `2.8.0-rc3` 2026-04-03) | Apache-2.0 **plus** an "Open Source Maintenance Fee" EULA on binaries (`OSMFEULA.txt`) | net472, netstandard2.0, netstandard2.1, net8.0, **net10.0** | SkiaSharp 3.119.2 (+ native Linux assets), BouncyCastle, SharpZipLib, MathNet.Numerics, ExtendedNumerics.BigDecimal, ZString, Enums.NET, RecyclableMemoryStream |
| LargeXlsx (write-only streaming) | 2.0.2 | 2026-07-26 | 2-clause BSD (README) | netstandard2.0, netcoreapp3.1 | SharpCompress 0.50.1, System.Memory |
| Sylvan.Data.Excel (streaming reader + flat writer) | 0.5.8 | 2026-08-10 | MIT | netstandard2.0, netstandard2.1, net6.0, net8.0 | none |
| ExcelDataReader (read-only) | 3.9.0 | 2026-06-16 | MIT | net462, netstandard2.0, netstandard2.1, net8.0 | System.ValueTuple |

Sources: NuGet registration index `https://api.nuget.org/v3/registration5-gz-semver2/<id>/index.json`
and nuspecs `https://api.nuget.org/v3-flatcontainer/<id>/<version>/<id>.nuspec` (queried by
script); package pages https://www.nuget.org/packages/ClosedXML, https://www.nuget.org/packages/EPPlus,
https://www.nuget.org/packages/MiniExcel, https://www.nuget.org/packages/DocumentFormat.OpenXml,
https://www.nuget.org/packages/NPOI; GitHub releases https://github.com/ClosedXML/ClosedXML/releases
(0.105.1, 2026-07-25), https://github.com/dotnet/Open-XML-SDK/releases (v3.5.1, 2026-03-18),
https://github.com/nissl-lab/npoi/releases (2.8.0-rc3, 2026-04-03).

Note on dates: a web summary of the ClosedXML/Open-XML-SDK/NPOI release pages reported 2024/2025
years; the GitHub API `published_at` values and NuGet `published` values above are authoritative.

**Implication:** every candidate runs on .NET 10 (netstandard2.0 or an explicit net10.0 target); .NET 10 support does not discriminate between them — licensing and memory model do.

### 1.2 Licensing details that matter for an Apache-2.0 platform redistributed to distributions

- **EPPlus 8**: "EPPlus 8 has a dual license model with a community license for noncommercial use: Polyform Noncommercial 1.0.0 … will require a commercial license to be used in a commercial business." The license must be declared in code (`ExcelPackage.License.SetCommercial(key)` / `SetNonCommercialPersonal` / `SetNonCommercialOrganization`), in `appsettings.json` (`"EPPlus": { "ExcelPackage": { "License": "Commercial:<key>" } }`) or the `EPPlusLicense` environment variable; otherwise `LicenseNotSetException` is thrown. Pricing on 2026-09-01: per-developer subscription US$329–569/year, perpetual US$809–899 per developer, organization packages US$6,395 (≤10 devs) / US$10,395 (≤25) / US$16,195 (≤50); the EULA prohibits offering the software "as a service other than as a minor feature of your own service". Sources: https://github.com/EPPlusSoftware/EPPlus (README), https://epplussoftware.com/en/LicenseOverview, https://epplussoftware.com/developers/licensenotsetexception.
  **Implication:** every distribution author (a separate legal entity) would need a commercial EPPlus license and a license key in configuration; incompatible with "add a package reference and go".
- **NPOI 2.8.0**: source stays Apache-2.0, but "an EULA on binary releases is added to the repo and nuget packages that requires payment of the maintenance fee"; the EULA applies to "Users that use the Software as part of revenue-generating activities and have an annual gross revenue greater than or equal to US$10,000", exempts users below that or who pay separate support, and states "To the extent any term of this Agreement conflicts with User's rights under the OSI License … the OSI License shall govern". Fees are owed only for direct dependencies. Sources: https://github.com/nissl-lab/npoi/releases/tag/2.8.0-rc3, https://www.nuget.org/packages/NPOI/2.8.0/License, https://opensourcemaintenancefee.org/consumers/which/.
  **Implication:** a NuGet dependency on NPOI puts a fee obligation on Tellma and on every distribution that references it directly; plus it drags SkiaSharp native binaries into every web host.
- **ClosedXML**: MIT, but depends on SixLabors.Fonts [1.0.0,3.0.0). SixLabors.Fonts 2.x is under the **Six Labors Split License 1.0**: Apache-2.0 applies when "You are consuming the Work as a Transitive Package Dependency" or in open-source software or for companies under US$1M revenue; otherwise a commercial license. Source: https://raw.githubusercontent.com/SixLabors/Fonts/main/LICENSE.
  **Implication:** safe as a transitive dependency of ClosedXML, but a distribution must never add SixLabors.Fonts as a direct reference; a subtle trap worth a comment if ClosedXML were chosen.
- **MiniExcel** Apache-2.0; **Open XML SDK** MIT; **LargeXlsx** BSD-2; **Sylvan.Data.Excel** MIT; **ExcelDataReader** MIT — no obligations beyond attribution.

### 1.3 Memory model and streaming support per library

- **ClosedXML** holds the whole workbook in memory. Since 0.100.0 (2023-01-09) saving streams cell values instead of building a second DOM ("sheet data (=cell values) are now directly streamed to the output file"; a 30,000 × 45 report went from 2.08 GiB to 0.8 GiB); 0.104.1 added "streaming cell loading" for a 15–20% load speed-up, but cells are still materialized. Issue #2734 (opened 2025-07-01 against 0.105.0, no maintainer reply as of 2026-09-01): `OutOfMemoryException` after ~350,000 of 500,000 rows × 180 formatted columns. There is no forward-only writer or reader API. Sources: https://github.com/ClosedXML/ClosedXML/releases/tag/0.100.0, https://github.com/ClosedXML/ClosedXML/releases, https://github.com/ClosedXML/ClosedXML/issues/2734.
  **Implication:** cannot meet "bounded memory at 1M rows"; also the slowest reader in every benchmark below.
- **EPPlus** keeps the workbook in memory until `Save`/`SaveAs`; requests for a worksheet "flush" to stream large exports were raised as issues and never became an API (**Inference** from https://github.com/JanKallman/EPPlus/issues/366 and the allocation profile below; I found no forward-only writer in the EPPlus 8 API).
- **MiniExcel** streams row by row in both directions: "the data is processed row by row in a streaming manner"; export from `IDataReader` is "Recommended, it can avoid to load all data into memory"; 2.0 makes `QueryAsync` return `IAsyncEnumerable<T>`. String cells are written as **inline strings** (`t="inlineStr"`; constants `InlineString = "inlineStr"`, `SharedString = "s"` in `src/MiniExcel.OpenXml/Constants/ExcelDataTypes.cs`, writer in `Helpers/XmlCellWriter.cs`). On read, "If the SharedStrings size exceeds 5 MB, MiniExcel default will use local disk cache" (`SharedStringCacheSize`, `EnableSharedStringCache = false` to disable); the README's 1,000,000-row unique-string example drops from 195 MB to 65 MB peak at the cost of 7.4 s → 27.2 s. Feature surface (README at tag 1.46.0): `ExcelFormat`/`ExcelColumn(Format, Width, Hidden, Index)`/`DynamicExcelColumn`, `AutoFilter` toggle, `FreezeRowCount`/`FreezeColumnCount`, hidden sheets, templates (`SaveAsByTemplate`). **Not documented** (grep of the 1.46.0 and 2.0 READMEs): data validation, sheet protection, defined names, custom XML parts — assume unsupported. Sources: https://github.com/mini-software/MiniExcel (README.md at tag 1.46.0, README_V2.md, V2-Upgrade-Notes.md, source files named above).
- **Open XML SDK 3.5.1** offers DOM and SAX: "the DOM approach requires loading entire Open XML parts into memory, which can cause an Out of Memory exception … Using the SAX approach, you can employ an OpenXMLReader to read the XML in the file one element at a time, without having to load the entire file into memory"; `OpenXmlPartReader`/`OpenXmlPartWriter` "access a part's stream with forward-only access". `OpenXmlWriter.Create(OpenXmlPart|Stream)` with `WriteStartElement`/`WriteElement`/`WriteEndElement`/`WriteString`. Everything else (styles, `dataValidation`, `sheetProtection`, `definedName`, `CustomXmlPart`) is available but must be assembled by hand. Sources: https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/how-to-parse-and-read-a-large-spreadsheet, https://learn.microsoft.com/en-us/office/open-xml/word/how-to-replace-text-in-a-word-document-with-sax, https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.openxmlwriter.
- **NPOI** has a port of POI's streaming writer (`ooxml/XSSF/Streaming/SXSSFWorkbook.cs` exists in the repo; 2.8.0 notes "larger I/O buffers optimization for SXSSF" and lazy loading of the shared-string table and styles), but reading is the in-memory XSSF model and it is the heaviest allocator in every benchmark below. Sources: https://github.com/nissl-lab/npoi (code search), release notes above.
- **LargeXlsx**: "potentially huge files can be created while consuming a low, constant amount of memory"; writes top-to-bottom only; supports inline strings and optional shared strings (which "accumulate in RAM"); styles, number formats, data validation drop-down lists, sheet password protection, merged cells, auto filters, RTL sheets. Write-only. Source: https://github.com/salvois/LargeXlsx.
- **Sylvan.Data.Excel**: "readonly, row by row, forward-only access" through `DbDataReader` (`GetString`, `GetInt32`, `GetDateTime`…), reads .xlsx/.xlsb/.xls; the writer "does not support custom formatting, charts, or other common features". Source: https://github.com/MarkPflug/Sylvan.Data.Excel.
- **ExcelDataReader**: forward-only `IDataReader` over .xlsx/.xlsb/.xls/.csv; no writer. Source: https://github.com/ExcelDataReader/ExcelDataReader.

### 1.4 Benchmarks

**(a) MiniExcel repository benchmark, 2026 run (BenchmarkDotNet 0.15.8, .NET 10.0.9, Linux, 2 physical cores).** Row count is **100,000 × 10** (`BenchmarkBase.cs`: `RowCount = 100_000`, file `data/Test100,000x10.xlsx`), not 1M as the README implies. "Allocated" is total GC allocation, not peak working set (the repo's *Benchmark Guide* says peak must be measured separately with VS Diagnostic Tools).

| Create (write) 100k×10 | Mean | Allocated |
|---|---|---|
| MiniExcel | 595 ms | 426 MB |
| ClosedXML | 1,278 ms | 401 MB |
| Open XML SDK (DOM) | 1,695 ms | 763 MB |
| EPPlus | 1,801 ms | 288 MB |
| NPOI | 3,006 ms | 1,742 MB |

| Query (read all) 100k×10 | Mean | Allocated |
|---|---|---|
| MiniExcel Query (with mapping) | 777 ms | 718 MB |
| ExcelDataReader Query | 940 ms | 308 MB |
| EPPlus Query | 1,691 ms | 955 MB |
| MiniExcel Query (dynamic) | 2,028 ms | 685 MB |
| Open XML SDK Query (DOM) | 2,642 ms | 583 MB |
| ClosedXML Query | 2,882 ms | 1,120 MB |
| NPOI Query | 3,862 ms | 1,258 MB |

Source: https://github.com/mini-software/MiniExcel/tree/master/benchmarks/results (`create-benchmark.md`, `query-benchmark.md`, `Benchmark Guide.md`), `benchmarks/MiniExcel.Benchmarks/BenchmarkBase.cs`.

**(b) MiniExcel README at tag 1.31.0 (older run, .NET Core 3.1, Windows 10), file `Test1,000,000x10.xlsx` (1,000,000 × 10 "HelloWorld" cells, 23 MB), reporting *max memory usage*:** read — MiniExcel 17.3 MB / 14.2 s, ExcelDataReader 17.3 MB / 22.6 s, EPPlus 1,451 MB / 23.6 s, Open XML SDK (DOM) 1,412 MB / 52.0 s, ClosedXML 2,184 MB / 191.4 s; write — MiniExcel 15 MB / 11.5 s, EPPlus 1,204 MB / 22.5 s, Open XML SDK (DOM) 2,621 MB / 42.5 s, ClosedXML 7,141 MB / 140.9 s. Source: https://github.com/mini-software/MiniExcel/blob/1.31.0/README.md. (Older library versions; ClosedXML has since improved save memory per 1.3, but these remain the only published *peak* numbers at 1M rows.)

**(c) Sylvan benchmarks (65,535-row file, allocations).** Read .xlsx: baseline unzip+XmlReader 112 ms / 247 KB; Sylvan 162 ms / 666 KB; ExcelDataReader 383 ms / 191 MB; MiniExcel 414 ms / 648 MB; EPPlus 699 ms / 570 MB; ClosedXML 1,072 ms / 728 MB; NPOI 1,571 ms / 1,004 MB. Write .xlsx: Sylvan 69 ms / 166 KB; LargeXlsx 92 ms / 136 KB; MiniExcel 187 ms / 334 MB; Open XML SDK 991 ms / 542 MB; EPPlus 1,412–1,522 ms / 401–643 MB; NPOI 2,099 ms / 1,135 MB. Source: https://github.com/MarkPflug/Benchmarks/blob/main/docs/ExcelReaderBenchmarks.md and `ExcelWriterBenchmarks.md` (author is the Sylvan maintainer; treat as indicative).

**Implication:** only the forward-only designs (Open XML SDK SAX, MiniExcel, LargeXlsx, Sylvan) keep memory flat with row count; DOM libraries (ClosedXML, EPPlus, Open XML DOM, NPOI-XSSF) allocate hundreds of MB per 100k rows and gigabytes at 1M.

### 1.5 Recommendation (my synthesis — the facts above are verified, the choice is a judgement)

Requirements from the brain dump: export up to the row limit with bounded memory; the same file re-imported; per-column Excel number formats that carry the tenant's localization; hidden metadata so the importer can recover the column→property map, natural-key choice and languages; children in the same workbook; optional dropdowns and protection; Apache-2.0 platform shipped to third-party distributions; .NET 10; Linux hosts.

1. **Build the codec on `DocumentFormat.OpenXml` 3.5.1 directly**, writing sheets with `OpenXmlWriter` (forward-only, inline strings, a pre-built `styles.xml` with one `cellXfs` entry per distinct number format) and reading with `OpenXmlReader` over the sheet part, the styles part loaded as a small DOM, and the shared-string part streamed into an indexed store. It is the only MIT, Microsoft-maintained option that covers every feature the theme needs (custom XML part, hidden defined names, `dataValidation`, `sheetProtection`, freeze panes, autofilter, hidden sheets/columns) in one pass with bounded memory and no transitive license risk. Cost: roughly a thousand lines of platform code that must be written once and tested against Excel, LibreOffice and Google Sheets round trips.
2. **Alternative with less code**: MiniExcel 1.46.0 (Apache-2.0, streaming both ways, disk-cached SST, `IDataReader` export, column formats/widths/hidden, freeze panes, autofilter) — but it cannot emit validation lists, protection, defined names or custom XML parts, so the manifest would have to live in a hidden sheet, and a post-processing pass with the Open XML SDK would be needed for anything else. MiniExcel 2.0 is still preview (2.0.0-preview.4, 2026-06-13) with a breaking API split; pin 1.x if chosen.
3. **Reading accelerator**: Sylvan.Data.Excel (MIT, zero dependencies, `DbDataReader` shape) is the fastest reader by a wide margin and maps naturally onto a TVP bulk-load pipeline; worth adopting for import if the hand-written SAX reader proves slow, at the price of a second parsing code path.
4. **Reject**: ClosedXML (whole workbook in memory, OOM reports at ~350k rows, slowest reader), EPPlus (per-developer commercial license for every distribution author, license key in configuration), NPOI (binary maintenance-fee EULA, SkiaSharp native dependencies, in-memory reads).

Two facts constrain whichever library is picked: Excel always rewrites strings into a shared-string table on save (see 2.4), so *user-edited* files force the importer to hold or disk-cache an SST whose size is unbounded by the platform; and the 1,048,576-row limit means "export everything" needs either a hard cap or multi-sheet chunking (see 2.1).

---

## 2. Excel facts

### 2.1 Limits (verified)

- Worksheet size **1,048,576 rows × 16,384 columns**; 32,767 characters per cell; 15 significant digits of numeric precision; largest positive number 9.99999999999999E+307; earliest date 1900-01-01 (1904-01-01 in the 1904 system), latest 9999-12-31; formula text ≤ 8,192 characters; unique cell formats/styles 65,490; number formats "between 200 and 250, depending on the language version"; 10,000 items in filter drop-down lists. Source: https://support.microsoft.com/en-us/office/excel-specifications-and-limits-1672b34d-7043-467e-8e27-269d656771c3.
- Worksheet names: at most 31 characters, cannot be blank, cannot contain `/ \ ? * : [ ]`, cannot begin or end with an apostrophe, and "History" is reserved. Source: https://support.microsoft.com/en-us/office/rename-a-worksheet-3f1f7148-ee83-404d-8ef0-9ff99fbad1f9.
- Numbers beyond 15 digits: "any numbers past the 15th digit are rounded down to zero"; leading zeros are dropped unless the cell is Text (`@`) formatted or the value is prefixed with an apostrophe. Sources: https://support.microsoft.com/topic/1bf7b935-36e1-4985-842f-5dfa51f85fe7, https://support.microsoft.com/en-us/office/format-numbers-as-text-583160db-936b-4e52-bdff-6f1863518ba4.
- A number format string must be shorter than 255 characters (Office implementer note). Source: https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/17d11129-219b-4e2c-88db-45844d21e528.

**Implication:** one sheet holds at most 1,048,575 data rows under a header, so the export limit is either a hard cap below that or a second sheet; `long` ids, codes, phone numbers and anything with leading zeros must be written as text cells with the `@` format on the column so user-typed values stay text; sheet names derived from entity/collection names need truncation to 31 characters and character scrubbing; `decimal(19,4)` money beyond 15 significant digits loses precision in Excel — Excel is not a lossless carrier for such values.

### 2.2 Number-format codes, locale tags and calendars

- Format codes have up to four `;`-separated sections (positive;negative;zero;text); placeholders `0 # ? , %`; date/time tokens `yy yyyy m mm mmm mmmm d dd ddd dddd h hh m mm s ss AM/PM [h] [mm] [ss]`; conditions `[<=100]`; eight named colors; literal text in quotes or after `\`; `_` for a character-width space; `@` for the text section. Sources: https://support.microsoft.com/en-us/office/number-format-codes-5026bbd6-04bc-48cd-bf33-80f18b4eae68, the [MS-OI29500] ABNF above (`NFPartYear = 2 or 4 y`, `NFPartMonth = 1..5 m`, `NFPartDay = 1..4 d`, colors, conditions).
- Locale tag grammar (Office implementer note, verified): `NFPartLocaleID = "[" "$" 1*UTF16-ANY [ "-" 3*8 HEXDIG ] "]"`, at most one per section — i.e. the part after `-` is **3 to 8 hex digits**, which is what accommodates the extended form below. Source: https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/17d11129-219b-4e2c-88db-45844d21e528 (identical text in [MS-OE376] §3.8.30). **Inference:** Excel itself emits tags with an empty symbol part (`[$-409]`, `[$-F800]dddd, mmmm dd, yyyy`) that the published ABNF does not literally admit; treat the symbol as optional.
- Extended tag semantics `[$-NNCCLLLL]`: `NN` = native-numeral (digit substitution) code, `CC` = calendar code, `LLLL` = LCID. Calendar codes follow the Windows CALID values in hex: 01 Gregorian, 03 Japanese (Gengou), 05 Korean, 06 Hijri, 07 Thai Buddhist, 08 Hebrew, **17h (= 23) Um Al Qura**; LibreOffice's help tabulates the same scheme (00/01 Gregorian, 03 Gengou, 06/17 Hijri, 07 Buddhist, 08 Jewish) and gives `[$-0D0741E]` = Thai numerals + Buddhist calendar + Thai LCID; a Microsoft Q&A answer uses `[$-D07041E]` for the same effect. Sources: Windows CALID table https://learn.microsoft.com/en-us/windows/win32/intl/calendar-identifiers (values 1–12 and 23, "The designator for CAL_UMALQURA is 23, not 13"); https://help.libreoffice.org/latest/en-US/text/shared/01/05020301.html; https://learn.microsoft.com/en-us/answers/questions/4805997/thai-localization-for-dates-incorrect. **Status:** the encoding is verified by triangulation (Microsoft's spec gives the grammar, Microsoft's NLS docs give the calendar numbers, LibreOffice documents the layout for Excel interoperability, a Microsoft-hosted answer uses it); no single Microsoft page documents `NNCCLLLL` for Excel.
- `B1`/`B2` prefixes: Excel's own help (Office 2003 era) says that with Arabic editing enabled, typing `B2` before the date code (e.g. `B2dd/mm/yy`) renders the Hijri calendar and `B1` forces Gregorian; community posts add `[$-,117]B2dd/mm/yyyy` for Um Al Qura and note that "B2 gives you the Saudi Arabia Hijri calendar, and Hijri dates are not the same as Umm-Al-Qura dates". Sources: https://documentation.help/ms-office-excel-2003/xlhowbidiSwitchGregorianHijriCalendars.htm (mirror of Microsoft help; returned HTTP 403 on 2026-09-01, text taken from search-engine snippets — **Unverified**), https://techcommunity.microsoft.com/t5/excel/extracting-the-name-of-the-hijri-month-from-the-hijri-date/m-p/4017658. The support page "Display an alternate calendar" (https://support.microsoft.com/en-us/office/display-an-alternate-calendar-8cb201d4-c175-4d9e-9d8f-b4df00ca8f82) covers Outlook only.
- Hijri vs Um Al Qura in Windows NLS: CAL_HIJRI (6) is the arithmetic (Kuwaiti) algorithm; CAL_UMALQURA (23, Vista+) is the Saudi table-based calendar. Source: calendar-identifiers page above; the UWP `CalendarIdentifiers.UmAlQura` page (https://learn.microsoft.com/en-us/uwp/api/windows.globalization.calendaridentifiers.umalqura) states the supported range 1318–1500 AH (~1900-04-30 to 2077-11-16) — taken from a search snippet, **not fetched directly**.
- **Ethiopian calendar: no Windows CALID exists** (the complete list is 1–12 and 23). **Inference:** since Excel renders alternate calendars through the NLS calendar set, Excel cannot display Ethiopian dates via a number format; the same holds for Persian (CALID list has none).

**Implication:** localized display of dates in an exported sheet can be expressed purely as a per-column number format string — `[$-060401]dd/mm/yyyy` (Hijri) or `[$-170401]` (Um Al Qura) for Arabic-calendar tenants — while the underlying value stays a Gregorian serial; Ethiopian-calendar tenants cannot get calendar-aware rendering from Excel, so the design must either export Gregorian serials (round-trippable, mis-rendered for the user) or add a text display column that import ignores; the number-format string is metadata only and must be stripped/ignored on import exactly as the brain dump assumes.

### 2.3 Date serial semantics

- Two date systems: 1900 (serial 1 = 1900-01-01) and 1904 (serial 0 = 1904-01-01); the same date's serial is 1,462 larger in the 1900 system. Excel deliberately treats 1900 as a leap year for Lotus 1-2-3 compatibility (a fictitious 1900-02-29 exists), so `WEEKDAY` "returns incorrect values for dates before March 1, 1900"; all other leap years, including 2100, are handled correctly. Sources: https://learn.microsoft.com/en-us/troubleshoot/microsoft-365-apps/excel/wrongly-assumes-1900-is-leap-year; the 1900/1904 difference article is retired on learn.microsoft.com (content-retirement page), so the 1,462-day figure is confirmed only by secondary sources (https://bettersolutions.com/excel/dates-times/1904-date-system.htm) — **partially unverified**.
- In the file, `workbookPr/@date1904` selects the base ("The default value for this attribute is false"); `dateCompatibility` (Office 2010+, default true) decides whether the compatibility bases or the full ISO 8601 range apply. Cell type `t="d"` (ISO 8601 text date) exists "only … in Office 2010 and later"; the other cell types are `b`, `n`, `e`, `s`, `str`, `inlineStr`. Sources: https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.workbookproperties, https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.cellvalues. **Inference:** Excel writes dates as numeric serials with a date number format (not `t="d"`) in the default Transitional format, so an importer must decide "date vs number" from the cell's style (`cellXfs` → `numFmtId`, built-in ids 14–22/45–47 or a custom code containing date tokens).
- Time-of-day is the fractional part of the serial; `DateOnly` vs `DateTime` cannot be distinguished from the value, only from the format or from metadata.

**Implication:** export with `date1904="0"`, serials computed from 1899-12-30 (the standard offset that absorbs the leap-year bug for dates ≥ 1900-03-01); import must honor `date1904` if a user saved the file on a workbook with that setting, must treat serials < 61 as suspect, and must use the column's declared property type (from the manifest, not the cell format) to decide date vs datetime vs number.

### 2.4 Shared strings vs inline strings

- "Excel always creates a shared string table when it saves a file. However, using the shared string table is not required to create a valid SpreadsheetML file"; inline strings (`t="inlineStr"` with `<is><t>`) are valid and cost file size and load time when strings repeat; exactly one SST part per package. Source: https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/working-with-the-shared-string-table.
- Streaming writers use inline strings (MiniExcel: `t="inlineStr"`; LargeXlsx: inline by default, optional SST that "accumulates in RAM"). A streaming reader must resolve `t="s"` indexes against the whole SST, hence MiniExcel's disk cache above 5 MB.

**Implication:** the platform's own export can be single-pass with inline strings (bounded memory, larger file); a file that a user opened and saved in Excel comes back with an SST, so import memory is bounded only by the SST size unless the reader spills it to disk — the design must cap import file size/row count or use a disk-backed SST index (a good reason to hand off large imports to the background machinery of T10).

### 2.5 Carrying metadata in the workbook

- **Custom XML parts**: `CustomXmlPart` (relationship type customXml) can be added to a package (`AddNewPart<CustomXmlPart>()`) and holds arbitrary XML with an optional `CustomXmlPropertiesPart`. Source: https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.customxmlpart. **Unverified here:** that Excel preserves a custom XML part it does not understand across open/save (Office add-ins rely on this; not tested in this research).
- **Defined names**: workbook-level `definedName` with attributes `name`, `localSheetId`, `hidden`, `comment`, `description`; "Names can also be used to represent formulas or values that do not change (constants)"; names in the cell-reference range A1–XFD1048576 are errors. Source: https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.definedname.
- **Hidden sheets** (`sheet/@state="hidden"`) are supported by MiniExcel (`SheetState.Hidden`) and the SDK; `veryHidden` exists in the schema (**not verified here**).
- Data validation and sheet protection (2.6, 2.7) are additional, user-visible carriers.

**Implication:** put the export manifest (entity, platform schema fingerprint, column→property map with the language each `Name` column was exported in, natural-key choices, calendar, `date1904` assumption, row-limit truncation flag) in a hidden defined name holding a JSON constant and/or a custom XML part; a hidden sheet is the fallback readable by every library; the header row alone is fragile because users rename columns.

### 2.6 Data validation lists

- `dataValidation` element: `type` (list, whole, decimal, date, time, textLength, custom), `sqref`, `allowBlank`, `showDropDown`, `showInputMessage`/`prompt`/`promptTitle`, `showErrorMessage`/`errorStyle` (stop, warning, information)/`error`/`errorTitle`, children `formula1`/`formula2`; Office 2013 adds an `x12ac:list` child. Source: https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.datavalidation.
- A list source is either typed values separated by commas (`Fruit,Vegetables,…`), a range, or an Excel table (table-backed lists update automatically; Microsoft recommends a separate, hidden and protected worksheet); "Ignore blank" and "In-cell dropdown" toggles; input message and error alert texts are limited to 225 characters. Source: https://support.microsoft.com/en-us/office/create-a-drop-down-list-7693307a-59ef-400a-b769-c5402dce407b, https://support.microsoft.com/en-us/office/apply-data-validation-to-cells-29fecbcc-d1b9-42c1-9d76-eff3ce5f7249.
- A typed (comma-delimited) list is limited to 255 characters including commas; range-based lists are not so limited (community sources report up to 32,767 items). Sources: https://learn.microsoft.com/en-us/answers/questions/4928298/data-validation-is-there-a-way-to-surpass-the-255 (Microsoft-hosted Q&A), https://www.excelforum.com/excel-formulas-and-functions/1062666-data-validation-list-source-has-too-many-characters.html — **community-verified only**. Validation is not applied to pasted values (common knowledge, **unverified** on a Microsoft page).

**Implication:** boolean/enum columns can carry typed lists (short enough); FK columns can carry a dropdown only when the reference set is small enough to embed on a hidden lookup sheet (thousands, not millions), and only as UX sugar — the server re-validates every value because validation does not survive paste or other tools.

### 2.7 Sheet protection

- `sheetProtection` attributes: legacy `password` (16-bit hash) or `algorithmName` + `hashValue` + `saltValue` + `spinCount`, plus per-action locks (`sheet`, `objects`, `scenarios`, `formatCells`, `formatColumns`, `formatRows`, `insertColumns`, `insertRows`, `insertHyperlinks`, `deleteColumns`, `deleteRows`, `selectLockedCells`, `sort`, `autoFilter`, `pivotTables`, `selectUnlockedCells`). Source: https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.sheetprotection.
- "Worksheet level protection isn't intended as a security feature. It simply prevents users from modifying locked cells"; all cells are locked by default, so editable cells must be unlocked before protecting; allowed actions (select locked/unlocked cells, format, insert/delete rows and columns, sort, AutoFilter, …) are chosen per sheet. Source: https://support.microsoft.com/en-us/office/protect-a-worksheet-3179efdb-1285-4d49-a9c3-f4ca36276de6.

**Implication:** protection can guard the header row, hidden metadata columns and the manifest sheet against accidental edits (with data cells unlocked and sort/filter allowed), but the importer must verify the manifest itself because protection is trivially removed.

---

## 3. Round-trippable import/export patterns in other systems

### 3.1 Odoo 18 (verified from the documentation source, 2026-09-01)

- **Export**: list view → Export; "With the *I want to update data (import-compatible export)* option ticked, the system only shows the fields that can be imported" and includes the External ID; formats CSV and XLSX; export templates can be saved. Field labels map to technical names (e.g. *Related Company* = `parent_id`).
- **Headers**: columns map to fields by label; Odoo "heuristically tries to find the type of field for each column … based on the first ten lines"; unmatched columns are mapped manually; "Show fields of relation fields (advanced)" exposes sub-fields; a downloadable import template exists per model; "It is strongly advised to **not** remove the External ID (ID) column".
- **Natural keys for lookups**: three column conventions per relation — `Country` (name or code), `Country/Database ID` (PostgreSQL id, "should rarely be used"), `Country/External ID` (`base.be`, for third-party data); "The ID is expected when two records have the same name. In such a case add `/ ID` at the end of the column title"; on duplicate names "the validation is halted, but the data may still be imported" (linked to the first match) and External ID is recommended; many2many values are comma-separated without spaces.
- **Child rows (one2many)**: "a specific row **must** be reserved in the CSV file for each order line. The first order line is imported on the same row as the information relative to order. Any additional lines need an additional row that does not have any information in the fields relative to the order."
- **Errors**: a *Test* button validates the whole file before import; errors are reported per file column/row in the mapping UI (exact per-row format not verified).
- **Update semantics**: re-importing with the same External ID updates instead of duplicating; conflicts occur if two records share an External ID.

Sources: https://www.odoo.com/documentation/18.0/applications/essentials/export_import_data.html (rendered) and its source https://raw.githubusercontent.com/odoo/documentation/18.0/content/applications/essentials/export_import_data.rst.

**Implication:** Odoo's `Field/External ID` naming is a proven header convention for "this column is a natural key of a related entity"; its one2many layout (children as extra rows with blank parent cells) keeps a single sheet but breaks sorting/filtering and collapses under Excel's row limit; its "update by External ID" is the Merge mode of the brain dump.

### 3.2 Dataverse / Dynamics 365 Customer Engagement (verified on learn.microsoft.com, 2026-09-01)

- **Export**: static worksheet "up to 100,000 rows at a time", same columns/order/sort/widths as the view; "Open in Excel Online" edits are saved back as an import job; dynamic worksheets refresh from the service. Exports (except PivotTables) "include the required hidden columns"; "Hidden column values can't be modified. If you modify the hidden fields after Export to Excel, the files won't Import back in to Dataverse"; "If the original record has been modified between the last time when the Excel spreadsheet was exported … and the time when the file is imported, an error is generated and the record will be identified as such in the import log". Community documentation names the three hidden columns A–C as "(Do not modify)" record GUID, row checksum and Modified On (**secondary sources**: https://community.dynamics.com/blogs/post/?postid=dcaa1d92-72d6-411a-9fb2-bc0e69bf9401, https://crmbook.powerobjects.com/crm-book-basics/data-management-in-microsoft-dynamics-crm/importing-data-in-microsoft-dynamics-crm/using-excel-reimport-in-dynamics-crm/).
- **Import**: .xlsx/.csv/.xml, 8 MB per file (32 MB zip); "download an Excel template … Don't add or modify columns"; automatic mapping when "column headings match the column display names", manual mapping otherwise; required ("Primary") fields must map; option sets map value-by-value; lookups are resolved by choosing, per related table, "the columns to search during import"; alternate keys (CSV/XML) identify rows for update; duplicate detection rules; "Excel import updates fields from the primary table but ignores fields from related tables"; self-referencing lookups are resolved "in two phases"; files > 1 MB are processed sequentially.
- **Errors**: an import job with statuses Submitted → Parsing → Transforming → Importing → Completed; counts of Success / Failures / Partial Failures; a Failures tab with "Export Error Rows" producing a file to correct and re-import; the whole import (or just the imported records) can be deleted afterwards.

Sources: https://learn.microsoft.com/en-us/power-apps/user/export-data-excel, https://learn.microsoft.com/en-us/power-apps/user/export-excel-static-worksheet, https://learn.microsoft.com/en-us/power-apps/user/export-to-excel-online, https://learn.microsoft.com/en-us/power-apps/user/import-data, https://learn.microsoft.com/en-us/training/modules/export-dataverse-excel/edit-update.

**Implication:** Dataverse shows the surrogate-plus-checksum pattern (hidden id + row checksum + modified-on) for same-tenant round trips with optimistic-concurrency detection per row, an "error rows" file as the per-row error channel, and hard size/row thresholds beyond which work becomes a tracked job — directly applicable to the concurrency stamp of T2 and the background hand-off of T10.

### 3.3 Dynamics 365 Finance & Operations data management (verified, 2026-09-01)

- Data entities are denormalized views over tables; import goes source → staging table → target, with field mapping regenerated per entity and required fields marked; entities are sequenced within a data package ("Sales tax codes" 1.1.1 before "Sales tax groups" 1.1.2).
- **Natural keys**: staging tables of a header/lines composite are "linked by SalesID, DefinitionGroup, and ExecutionId" — i.e. lines carry the parent's natural key (SalesID), not a RecId; composite (header + lines) entities are "only supported for a data management platform that's part of XML file-based imports/exports" — **Excel/CSV cannot carry a composite entity**; per-entity Excel/CSV files are sequenced in a package instead.
- **Errors**: "select View staging data on each tile … Sort and scroll through the records with Transfer status = Error to display the errors in the Message section … Fix a record (or all records) directly in staging by selecting Edit, Validate all, and Copy data to target, or fix the import file … and reimport"; "Staging log details displays Error column (field) details"; the framework can "skip selected records and choose to proceed with the import by using only the good data".
- **Excel add-in**: an entity binds to an Excel table; header and lines are separate data sources on one workbook ("If you add data sources as related data sources, the header publishes before the lines"); publish batch size default and maximum 100 rows; lookups and validation happen in the add-in; a "Copy Environment Data" mode treats existing rows as new when publishing to another environment.

Sources: https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/data-entities-data-packages, https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/develop-composite-data-entities, https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/data-entities, https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/fin-ops/mobile-apps/use-excel-add-in.

**Implication:** the most industrial of the three systems keeps children on their own sheet/file keyed by the parent's natural key and reports errors per staging row with the offending field named — the shape to copy for a parent sheet plus one child sheet per collection, with an error column and cell coordinates on the returned workbook.

### 3.4 Salesforce Data Loader v67.0.0 (2026-06-25; verified from the vendor's source repository because the help pages are script-rendered)

- CSV only; operations insert/update/upsert/delete/export; **upsert** matches on a field marked External ID (or the record Id).
- **Parent lookups by natural key**: field mapping uses `<relationship name>:<idLookup field of parent>` (legacy format, e.g. `Account:External_Id__c`) and a newer `relationship:ParentObject-field` form for polymorphic lookups (`ParentIdLookupFieldFormatter.java`: `OLD_FORMAT_PARENT_IDLOOKUP_FIELD_SEPARATOR_CHAR = ":"`, `NEW_FORMAT_PARENT_IDLOOKUP_FIELD_SEPARATOR_CHAR = "-"`).
- **Per-row results**: every run writes a `success` CSV (input columns plus `ID` and `STATUS`; Bulk v2: `sf__id`, `sf__Created`) and an `error` CSV (input columns plus `ERROR`) (`AppConfig.java`: `ID_COLUMN_NAME = "ID"`, `STATUS_COLUMN_NAME = "STATUS"`, `ERROR_COLUMN_NAME = "ERROR"`, `process.outputSuccess`/`process.outputError`).
- **Batching**: SOAP/REST import batches max 200 rows (`MAX_NUM_ROWS_SOAP_API_IMPORT_BATCH = 200`), Bulk API 10,000 rows per batch, Bulk v2 jobs up to 150,000,000 bytes; export batches 200–2,000 (default 500).

Sources: https://github.com/forcedotcom/dataloader (files `src/main/java/com/salesforce/dataloader/config/AppConfig.java`, `src/main/java/com/salesforce/dataloader/dyna/ParentIdLookupFieldFormatter.java`, releases page); https://developer.salesforce.com/tools/data-loader ("Detailed success and error log files in CSV format"); Salesforce help article on relating records by External ID (https://help.salesforce.com/s/articleView?id=000320964) could not be rendered.

**Implication:** the `Relationship:ExternalIdField` header convention and the "echo the input rows plus ID/STATUS or ERROR columns" result files are the CSV-world equivalents of Odoo's `Field/External ID` and Dataverse's error-rows export; all three converge on (a) a natural key named in the header, (b) per-row status/error in a returned file, (c) partial success being the norm for bulk tools — the brain dump's all-or-nothing transactional import is the stricter choice and should be a deliberate decision.

---

## 4. Cross-cutting implications the designers should weigh

1. **Row cap**: 1,048,575 data rows per sheet is a hard ceiling; exports near it should split sheets or refuse with a clear limit, and the display-shape export ("same rows as the grid, no paging up to a limit") needs that limit stated in rows and bytes.
2. **Text-typed columns**: ids, codes and anything with leading zeros must be written as text with `@` format; import must accept both text and numeric cells for the same column (Excel users retype values).
3. **Localization metadata** fits entirely in per-column number-format strings (`[$-NNCCLLLL]` + tokens), including Hijri/Um Al Qura; Ethiopian has no Excel rendering — needs a documented fallback.
4. **Manifest carrier**: hidden defined name and/or custom XML part (plus hidden sheet fallback); do not depend on the header row alone; verify a schema fingerprint on import.
5. **Strings**: write inline strings; expect an SST on the way back; decide the import memory bound (cap or disk-backed SST index) and the threshold for background processing (Dataverse: 1 MB sequential, 100k rows per static export; F&O: batch jobs; Data Loader: 200-row API batches).
6. **Children**: prefer one sheet per collection keyed by the parent natural key (F&O composite staging, Excel add-in "related data sources") over Odoo's blank-parent-cell rows; child sheets need their own natural key only if updates must be matched row-to-row.
7. **Errors**: return the uploaded workbook (or a CSV) with a status/error column and row/column coordinates, in the tradition of Data Loader `error.csv` and Dataverse "Export Error Rows"; the brain dump's validation-error format (T6) should carry sheet name, row and column.
8. **Library**: Open XML SDK 3.5.1 SAX (recommended) or MiniExcel 1.46.0; not ClosedXML/EPPlus/NPOI, for the memory and license reasons in §1.
