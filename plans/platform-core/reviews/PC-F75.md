# PC-F75: Excel import hostile input: UCS-4 refusal and a vectorised section scan

## Implementer notes

### What was built

This fixes D-240's V-1 and V-2, found by PC-176's verification, in
`src/core/Tellma.Core/Excel/BoundedPartStream.cs`. V-3 (the header search over an all-blank sheet)
is out of scope. `reviews/PC-176.md` has no Verification section, so D-240's own statement of the
defects was taken as the brief.

- **V-1, UCS-4 refused.** A part holding a NUL byte in UTF-8, or a NUL unit in UTF-16, is refused
  as `MalformedWorkbook` with reason `xml`. That is the same `ImportFaults.Malformed(XmlReason)` the
  run bound raises.
  - U+0000 is never legal XML, and every UCS-4 layout of an ASCII character holds NUL bytes. That
    covers big-endian (1234), little-endian (4321) and the two unusual orders (2143, 3412), each
    marked or not.
  - Each layout reaches the scan either as UTF-8 (a NUL byte) or as UTF-16 (a NUL unit). For
    example, `FF FE 00 00` reads as a UTF-16 mark followed by a NUL unit.
  - So the part is refused before an XML reader can decode it in units the scan cannot see.
  - The check runs once over each read's whole units (`Contains`, vectorised). `Unit` checks the
    units that bypass it: the first two bytes and a UTF-16 unit split across reads.
- **V-2, the scan vectorised.**
  - Content is skipped to its next `<` (`IndexOf`).
  - A CDATA section, comment or processing instruction is skipped to the end of its whole
    terminator (`]]>`, `-->`, `?>`).
  - `FindTerminator` compares every unit of the terminator at every position of a `Vector<T>` at
    once, so its cost does not depend on what the section holds. The brief's example, an
    `IndexOfAny` over the marks, falls back to one unit at a time on text dense in marks (`]a]a`).
    .NET's sequence `IndexOf` confirms each place whose first and last units match, so it ran about
    10 times slower on `]a>` fillers.
  - A terminator split across reads is carried as the run of marks its read ends on
    (`LastIndexOfAnyExcept`), and the next read finishes it (`IndexOfAnyExcept`, then one unit).
  - UTF-16 parts are now scanned in place as `char` spans: natively, or against byte-swapped values
    for the other byte order. They are no longer scanned a byte at a time (deviation 2).
  - Only opener units (`<`, `!`, `-`, `[`, `CDATA[`, `?`) still go one unit at a time.
- The run rule itself is unchanged: from one `<` to the next, a section counted whole, bounded at
  4 MiB.
- `src/core/Tellma.Core/README.md` now names the NUL refusal and the vectorised scan.
  ARCHITECTURE.md does not describe the stream, so it is unchanged.

### Tests

- Unit (`test/core/Tellma.Core.Tests/Excel/`):
  - `BoundedPartStreamTests`:
    - UCS-4 in each byte order (1234 and 4321, each with and without its mark; 2143; 3412), read
      whole and a byte at a time, refused as `xml`.
    - A NUL in content, CDATA, a comment, a processing instruction and an attribute, in UTF-8 and in
      UTF-16 of both byte orders, marked or not, refused.
    - The same encodings without a NUL, including zero bytes inside UTF-16 units and U+3C00, read
      whole in 3-byte reads.
    - A flood of each section's terminator mark past the bound, refused.
    - Terminators after lone marks, short runs of marks and long runs of marks, split across reads
      of 1, 3 and 4,096 bytes in UTF-8 and UTF-16, each closing its section.
    - A sweep that puts each terminator at 96 offsets (every vector and tail position), in 7-byte and
      4,096-byte reads.
    - A cost theory. Eleven 64 MiB parts must each scan within 10 times the time of the same bytes of
      UTF-8 text, plus 25 ms. The parts are CDATA with fillers `a`, `]a`, `]]a`, `]a>` and `>`; a
      comment with `-a`; a processing instruction with `?a`; and UTF-16 text and CDATA in both byte
      orders. Each side takes the best of five interleaved drains. Measured locally (Debug): 10 to
      63 ms against a bound of about 125 ms.
  - `WorkbookReaderTests`: a UCS-4 shared-strings part, little-endian and big-endian with its mark,
    refused as `xml` at open while allocating under 16 MiB. Its flood characters (U+1003C
    little-endian, U+3C3C big-endian) hold a byte or a unit that reads as `<`, so the old scan saw
    the flood as short runs.
- Checked against the previous stream: all 26 new cases fail on it. The cost cases took 0.56 to
  1.4 s against the same bound, and the end-to-end case answered `cells` (the reader's text cap)
  instead of `xml`.
- Run locally:
  - Tellma.Core.Tests, Excel classes, unit filter.
  - Tellma.Core.IntegrationTests, `Tellma.Core.IntegrationTests.Excel` (ExcelExportTests,
    ExcelImportTests).
  - Solution build and `dotnet format --verify-no-changes`.
- No nightly-tier tests.

### Deviations

1. Any NUL byte (UTF-8) or unit (UTF-16) refuses the part, not only one that opens as UCS-4. Spec
   0019 §7.2 names no encoding rule. U+0000 is never legal XML, and OPC parts are UTF-8 or UTF-16,
   so this is the one check that covers every UCS-4 layout, marked or not, together with any other
   NUL a reader would refuse later (D-240).
2. The UTF-16 path is vectorised in content as well as in sections. Before, every UTF-16 part was
   scanned a byte at a time throughout, which is V-2's cost in a different shape.

### Open risks

- **The cost theory is timing-based.** It uses ratios to a baseline drained on the same host
  between the measured drains, best of five, with a 10x plus 25 ms bound: about 2 to 3 times
  headroom over the measured cost, and 11 times under the old one. A host slowed heavily during one
  side's drains only could still fail it.
- **Without vector hardware**, `FindTerminator` falls back to the span's sequence `IndexOf`. That is
  still linear, but slower on fillers dense in candidate places.
- **Tiny sections still cost a few units each.** A part of many tiny sections (`<![CDATA[]]>`
  repeated) scans each opener one unit at a time. The XML reader's own cost per node dominates
  there.

### What reviewers should examine hardest

- The carry of a terminator across reads: `_matched` set from the trailing marks when no terminator
  is found, the marks branch, and `Close` deciding on the unit after them.
- `FindTerminator`'s bounds: the loop condition `index + last + Count <= Length`, a two-unit
  terminator comparing its first unit twice, and the tail search covering every place the loop did
  not.
- The UTF-16 reinterpretation: `MemoryMarshal.Cast<byte, char>` over the even prefix, a split unit
  held in `_pending`, and the swapped tables on a little-endian host.

## Review 1

Reviewed c5f879c7 against 0019 §7.2, D-240 (V-1, V-2), PLAN §6-§7 and WATCHLIST c05. CHECKPOINTS.md
has no PC-F75 entry (a fix item), so D-240 and the LEDGER row were taken as the brief.

What I ran:
- Built `Tellma.Core.Tests`: "Build succeeded", 0 warnings.
- `dotnet format --verify-no-changes` over the three changed C# files: no diagnostics.
- `Tellma.Core.Tests`, filter `FullyQualifiedName~Tellma.Core.Tests.Excel`: 447 passed, 1 skipped
  (the Linux-only scratch-file case).
- The cost theory alone, three times, with 40 CPU-bound processes on this 20-thread host: 11/11
  passed each time (19-20 s per run). This host is not a 4-vCPU SMT runner (see R1-2).
- The pc/PC-F75 CI run (45129898858) was skipped, so it caught nothing. I did not run the Excel
  integration suites; the change is confined to the stream, which the unit suites drive directly.
- Scratchpad probes, loading the built `Tellma.Core.dll` by reflection:
  - **Differential.** A per-unit reference of the run rule (the pre-change `Detect`/`Advance`/`Close`
    state machine) was fed the same bytes as the stream. `_run`, `_state`, `_matched` and `_pending`
    were compared after every read. The inputs were random parts built from openers, terminators,
    partial openers, lone and repeated marks, `>`, `<`, U+3C00/U+3C3C/U+2D2D/U+5D5D/U+3E3E/U+3F3F,
    multi-byte UTF-8 and fillers of up to 300 repeats (`]`, `]a`, `]]a`, `]a>`, `-a`, `--a`, `?a`,
    `>`). They were encoded as UTF-8 (with or without a BOM) and UTF-16 LE/BE (with or without a
    mark). Reads were 1-200 bytes, or fixed at 1, 2, 3, 5, 7, 16, 17, 31-35, 63-67, 97, 128, 255 and
    4,096 bytes, constant or varying.
    - About 86 M read boundaries over 163,000 parts, with AVX2: 0 mismatches.
    - With `DOTNET_EnableHWIntrinsic=0` (the non-vector fallback), with `DOTNET_EnableAVX2=0`
      (16-byte vectors), and with 512-bit preferred: 0 mismatches each.
    - A deliberately wrong reference (CDATA needing three marks) was caught at once, so the probe
      can see a mismatch.
    - So the carry across reads, `FindTerminator`'s loop and tail bounds, odd UTF-16 offsets, the
      swapped tables and the exact run count all hold. Nothing below concerns them.
  - **Declared encoding** (R1-1): see the finding.
  - **SqlClient**: running `Microsoft.Data.SqlClient.TdsParser`'s static constructor (from the test
    bin's `runtimes/win/lib/net9.0` build) makes `Encoding.GetEncoding("iso-2022-jp")` succeed
    where it failed before. So any process that has opened a SQL connection has the code-page
    encodings registered.

UCS-4 and NUL coverage, checked by hand against `XmlTextReaderImpl.DetectEncoding`:
- 1234 and 2143 (marked or not) open `00 00`, so they are read as UTF-8 and the first `Unit(0)`
  refuses them.
- 4321 (`FF FE 00 00`, `3C 00 00 00`) and 3412 (`FE FF 00 00`, `00 3C 00 00`) are read as UTF-16,
  and the second unit is NUL.
- A UTF-32 switch made by a declaration after a UTF-8 or UTF-16 start is refused too. Every UTF-32
  unit has a zero high byte, and in UTF-16 every BMP character, which all markup is, has a zero unit.
- No legal UTF-8 or UTF-16 part holds U+0000. A zero byte inside a UTF-16 unit passes, at every
  split (the `_pending` path included).
- Both refusals go through `ImportFaults.Malformed(XmlReason)`.

Big-endian, by reasoning:
- `Unit` composes the pending and detected units from bytes, so the result does not depend on the
  host.
- `_bigEndian == BitConverter.IsLittleEndian` picks the swapped tables exactly when the data's order
  differs from the host's. `Vector.Equals` is lane-wise, and the swapped constants are what a native
  load of the foreign-order bytes yields.
- So the vector code is correct on a big-endian host.

Deviations:
- 1 (any NUL refuses) is justified. U+0000 is never legal, so no part the reader would accept is
  refused.
- 2 (UTF-16 content vectorised) is justified, and it is exact by the differential.

### Findings

**R1-1 (blocking): a declared non-Unicode encoding still makes the reader decode units the scan
cannot see, now reachable in every host because SqlClient registers the code-page encodings.**
- `XlsxXml.cs:91-101` hands the part to `XmlReader.Create(Stream, …)`. That reader switches its
  decoder to whatever the XML declaration's `encoding` names.
- `BoundedPartStream.cs:23-29` assumes the reader decodes in the encoding the first two bytes
  suggest ("a package's parts may be encoded in those alone"). The scan therefore keeps reading
  UTF-8.
- `Microsoft.Data.SqlClient`'s `TdsParser` static constructor registers
  `CodePagesEncodingProvider` for the whole process (probe above). From then on `iso-2022-jp`,
  `hz-gb-2312` and the other 7-bit stateful encodings are available, and in them a pair of ASCII
  bytes such as `<!` or `<a` is one CJK character.
- Probe, using `XlsxXml.Open` over `BoundedPartStream` with the provider registered:
  - The part: `<?xml version="1.0" encoding="iso-2022-jp"?><a b="` + `ESC $ B` + `<!` × 4,194,304
    + `ESC ( B` + `"/>`, 8 MiB uncompressed and a few KiB deflated.
  - The stream drains it without refusal, because it sees a `<` every two bytes.
  - The reader returns attribute `b` with 4,194,304 characters, twice the bound. It allocated
    24 MiB in about 350 ms for this one 8 MiB part.
  - `hz-gb-2312` (`~{` + `<a` × N + `~}`) gives the same result. Without the provider, both are
    refused ("System does not support … encoding").
- So V-1's cost is still reachable through a different door, and the attribute grows with the
  part's declared length, up to the package limits.
- Fix: make the reader decode in the scan's encoding, rather than trust the declaration.
  - Preferred: open each part as `XmlReader.Create(TextReader, …)` over a `StreamReader` built from
    the encoding the stream detected. A reader over a `TextReader` never switches encoding.
  - Alternative: have the stream refuse a leading XML declaration whose `encoding` is anything but
    UTF-8 or UTF-16, case-insensitively.
- Add a test that registers `CodePagesEncodingProvider` and refuses an `iso-2022-jp` part and an
  `hz-gb-2312` part as `xml`. Update the remark and the README line so they name the rule that is
  actually enforced.

**R1-2 (major): the cost theory is a wall-clock assertion in the PR tier, and a deterministic bound
is feasible.**
- `BoundedPartStreamTests.cs:212-231` asserts the best of five drains against 10× a UTF-8 baseline
  plus 25 ms. Its theory has 11 cases of 64 MiB.
- PLAN §6 requires timing-sensitive assertions to be `@perf` (nightly, `Category=Nightly`) with a CI
  multiplier. This one runs in every PR on Linux and Windows.
- The headroom is thin where it matters:
  - The implementer measured up to 63 ms against a bound of about 125 ms.
  - The slow side is compute-bound (three vector loads and compares a step), while the baseline is
    a memory-bound `IndexOf`.
  - On a 2-core/4-thread runner with xUnit running classes in parallel, a busy SMT sibling slows the
    compute-bound side more than the baseline, so the ratio itself moves toward the bound. Best of
    five does not cancel that.
  - My oversubscribed run passed, but this host has 20 threads, so it says little about a 4-vCPU
    runner. A failure there would have to be root-caused, never retried (D-011).
- Fix: count the work instead of timing it.
  - Expose an internal counter of the units the scan takes one at a time (the `Unit` calls from
    `Scan`).
  - Assert that for each hostile filler it stays within a small constant times the number of
    openers and read starts, independent of the section's length. The pre-change scan fails that
    by a factor of about a million.
  - If the no-confirm property of `FindTerminator` against the BCL sequence `IndexOf` should stay
    pinned, move the timing theory to `[Trait("Category", "Nightly")]` with a CI multiplier.

**R1-3 (major): no test fails if the vectorised scan closes a section early.**
- Every new section test asserts only that sections close:
  - `A_terminator_after_a_run_of_its_marks_closes_its_section_wherever_the_reads_split_it`
    (`BoundedPartStreamTests.cs:171`).
  - `A_terminator_is_found_at_every_place_a_vector_or_a_read_may_put_it` (`:188`).
  - The cost theory (`:212`).
- These fillers hold no `<`. A false match therefore leaves the following text in content, with no
  `<` to start a run, and the part still drains.
- The flood test (`:156`) has no `>`. The pre-change `A_section_is_one_run…` (`:80`) uses an
  `a…a<` filler that has no marks, and runs only in UTF-8 with 4,096-byte reads.
- So any of these mutants would pass the whole suite, while letting a `<` inside a CDATA section
  start runs the reader never sees, which is the V-1 class again:
  - `FindTerminator` comparing only the first and last units (`]a>` matches).
  - `Close` accepting `>` after a carried `_matched` of 1 for CDATA.
  - The vector loop returning the vector's start instead of the confirmed place.
- The code is correct today (by the differential). The rule is not pinned.
- Fix: add a refusal theory.
  - Each section's mark-dense fillers (`]a>`, `]]a`, `]>`, `-a>`, `->`, `--a`, `?a`), interleaved
    with `<`, flood one section past the bound.
  - Run it in UTF-8 and in UTF-16 of both orders, with reads of 1, 7 and 4,096 bytes, plus one read
    size that splits each terminator.
  - Assert that each part is refused as `xml`.
- Optionally extend `:188` so its sweep also crosses read boundaries while vectors are engaged. With
  4,096-byte reads only about four terminators fall near a read end. With 7-byte reads the vector
  loop never runs.

### Responses

**R1-1 (fixed, per D-245).**
- `XlsxXml.Open` now takes a `BoundedPartStream`, and so do `SharedStringStore.Load` and
  `SheetReader.Open`. It parses `XmlReader.Create(TextReader, …)` over a new `PartTextReader`.
  - At its first read, `PartTextReader` asks the stream for `DetectEncoding()`. The stream reads its
    first two bytes early, scans them, and returns them first to the next reads.
  - It then decodes through a `StreamReader` in exactly that encoding: UTF-8, or UTF-16 of the byte
    order found. Byte-order-mark detection is off. Each encoding skips its own mark and throws on
    invalid bytes.
  - A reader over a `TextReader` never switches encoding, so the stream and the reader cannot
    disagree.
- A leading declaration (`<?xml` and white space) is read ahead up to `?>`, then replayed to the
  reader. Its `encoding`, when present, must be `UTF-8` or `UTF-16`, case-insensitively. Anything
  else is refused as an `XmlException`, which every caller already maps to
  `ImportFaults.Malformed(xml[, sheet])`. That includes `UTF-16LE`, `utf-32` and every code page.
- Settings are unchanged: the same `XmlReaderSettings` object, with DTD prohibited, no resolver, a
  `BoundedNameTable`, comments and PIs ignored, and `CloseInput`. Disposing the reader disposes the
  text reader, which disposes the part.
- The stream's remark, `XlsxXml`'s remark and the README now name the rule as enforced.
- Tests:
  - `PartTextReaderTests` registers `CodePagesEncodingProvider` and first asserts that each encoding
    resolves.
    - These declarations are refused: `iso-2022-jp`, `hz-gb-2312`, `windows-1252` (a single-byte
      code page), `iso-8859-1`, `us-ascii`, `utf-32` and `UTF-16LE`.
    - These pass: legal `UTF-8`/`utf-8` declarations (marked and unmarked, double and single
      quotes), `UTF-16`/`utf-16` declarations (LE marked, BE marked, LE unmarked), a declaration
      without `encoding`, and no declaration. Each carries non-ASCII text and a supplementary
      character, all decoded exactly.
    - Also covered: an unclosed declaration over the bound is refused, as is an invalid UTF-8
      sequence. The settings and the part's disposal are pinned, and a DTD is still refused.
  - `WorkbookReaderTests.A_part_declaring_a_code_page_encoding_is_refused_at_open_holding_little_memory`
    uses the reviewer's iso-2022-jp and hz-gb-2312 floods, 8 MiB each, in an `sst` attribute. Each
    is refused as `xml` at open while allocating under 16 MiB.
  - With `Open` put back on `XmlReader.Create(Stream, …)`, 8 of these cases fail. They are both
    floods, 5 of the 7 declarations, and the unclosed one. On the old path, `utf-32` and `UTF-16LE`
    already fail as an encoding switch.
- New deviation 3: a declaration must close within `PartTextReader.MaxDeclarationLength` (1,024
  characters), or the part is refused.
  - XML allows unbounded white space inside a declaration. Without a bound, the read-ahead would
    hold up to the 4 MiB run bound before the encoding could be checked.
  - Real producers write about 55 characters.
- New deviation 4: a declaration of UTF-8 in a UTF-16 part, or the reverse, is accepted and decoded
  as scanned. The reader ignores the name, so the mismatch is harmless.

**R1-2 (fixed).**
- `BoundedPartStream.UnitsTakenSingly` counts every unit `Unit` takes, which is every unit not
  skipped a vector at a time.
- The new theory `A_part_takes_a_few_units_one_at_a_time_per_opener_and_per_read_however_long_its_nodes`
  covers 14 cases: CDATA with fillers `a`, `]a`, `]]a`, `]a>` and `>`; comments with `-a` and `-a>`;
  a PI with `?a`; and UTF-16 content and sections in both byte orders.
  - Each case is 16 nodes of 1 Mi units, in 64 KiB reads.
  - It asserts `UnitsTakenSingly <= 12 * (openers + reads)`, where openers are the `<` count.
  - That bound is 3,700 to 6,800 units, against the previous scan's 16 million (it took every unit
    singly).
- The wall-clock theory stays only as a pin on `FindTerminator`'s no-confirm property, which counting
  cannot see. It is now `[Trait("Category", "Nightly")]`, with its 10x baseline ratio as the CI
  multiplier, so nothing time-based runs in the PR tier.

**R1-3 (fixed).**
- `A_flood_of_marks_and_less_than_signs_in_one_section_is_one_run_refused_past_the_bound` has
  108 cases.
  - Fillers: CDATA `]a>`, `]]a`, `]>` and `]`; comments `-a>`, `->` and `--a`; PI `?a` and `?`.
  - Each filler is interleaved with `<` and floods one section past the bound.
  - It runs in UTF-8, UTF-16 LE and marked UTF-16 BE, with reads of 1, 7, 4,096 and 4,097 bytes.
    The 1-byte reads split every terminator and unit; 4,097 splits them at moving offsets with
    vectors engaged.
- `A_section_is_counted_whole_to_its_terminator_however_many_less_than_signs_precede_it` covers each
  section kind in all three encodings, with vector-engaging reads (4,096, 4,097 and 65,536).
  - Each part is half the bound of section ending on 64 `<`, then half the bound of text. It must
    be refused.
  - Four paddings move the terminator within the vector.
  - This targets the vector-start mutant, which a flood cannot see: the only full match is the real
    terminator, reached after the refusal.
- Mutants, each applied alone, built and run against the two theories, then reverted (`cmp`
  confirmed):

  | Mutant | Failing cases | Failing theory |
  | --- | --- | --- |
  | `FindTerminator` comparing first and last units only, taking the first matching lane unconfirmed | 12 | the flood |
  | The vector loop returning the vector's start | 9 of 9 | the `<`-block theory |
  | CDATA's `Close` needing one mark (the carried-`_matched` case) | 12 | the flood |

- The optional extension of the offset sweep was not taken. The 4,097-byte reads in both new
  theories cross read boundaries with vectors engaged.
