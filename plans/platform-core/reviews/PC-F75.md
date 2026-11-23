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
