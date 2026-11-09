# Tellma.Core.Imaging.Tests

The image processor's unit suite, over real SkiaSharp and PDFium on Windows and Linux. Every
fixture is generated in code, honest or hostile, so no binary file is committed: Skia encodes the
honest images, and the hostile ones are honest files with their headers patched or segments spliced
in ([ImageFixtures](Fixtures/ImageFixtures.cs)); the PDFs are written with correct cross-reference
tables, including one encrypted with RC4 under an empty user password, computed here
([DocumentFixtures](Fixtures/DocumentFixtures.cs)).

| Folder | Covers |
|---|---|
| `Processing/` | Each accepted format; `Contain` and `CoverSquare` dimensions and crops, never upscaled; thumbnail sizes from the normalised image; output content type per format; alpha kept in WebP and PNG, opaque inputs opaque, JPEG over white; an animated GIF and an animated WebP (at a reduced decode scale) reduced to their first frame; all eight Exif orientations applied; no Exif, comment, text or colour-profile container in any output; a JPEG/HTML polyglot re-encoded with no `<` in its first 8 KiB; headers declaring 60 megapixels refused before decode in all four formats; other formats refused with no native call; malformed and truncated files refused as undecodable. |
| `Thumbnails/` | Image thumbnails fitted and oriented; PDF page 1 at the thumbnail size, from a multi-page PDF too; password-protected, unparsable, empty and truncated PDFs, OOXML, zip and text yielding null, while a PDF encrypted under an empty user password renders; the PDF preflight (an image declaring too many pixels, fewer for JPEG 2000 and JBIG2, a fan-out of forms, an oversized page and an oversized file each yield null and are never rendered); the budgets and gates on a fake clock (work waiting for a gate or slot never starts; a render past its budget returns null at once and keeps the gate until its native call returns, and later PDFs fail fast meanwhile; normalisation past its budget is a timeout rejection that keeps its slot until the decode ends; a full set of slots makes an upload busy; a read in flight holds the call, and the caller's stream is never touched after it returns). |
| `Observability/` | Both instruments with their closed tag sets, and the render, failure and unexpected-failure events. |
| `Composition/` | `AddTellmaSkia` registers one processor and one check however often it is called; the concurrency binds from `Tellma:Blobs`, defaults to half the processors and refuses zero; the `core.imaging` check passes. |
| `Packaging/` | The package's references (the abstractions, SkiaSharp and PDFtoImage only), both native libraries present for win-x64, linux-x64 and linux-arm64, and no native symbol file in the output or the deps.json. |
| `Internals/` | The fit and orientation arithmetic, the gated input view, and no native Skia object left alive after any path, an abandoned decode included. |

Suites that hold the process-wide PDF render gate or count live native objects run in one
non-parallel collection.

## Running

```bash
dotnet test test/core/Tellma.Core.Imaging.Tests
```
