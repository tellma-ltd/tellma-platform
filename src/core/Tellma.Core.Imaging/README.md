# Tellma.Core.Imaging

The platform's image processor: [SkiaImageProcessor](SkiaImageProcessor.cs) implements
`IImageProcessor` on SkiaSharp, with PDF first-page thumbnails rendered by PDFium through
PDFtoImage. It is the only Tellma package that carries native binaries, and it depends on no Tellma
package but `Tellma.Core.Abstractions`. `Tellma.Core` binds to the `IImageProcessor` contract and
never references this package, so a host that adds no processor loads neither Skia nor PDFium, and a
distribution that prefers another imaging library registers its own processor and drops this
package.

```csharp
builder.Services.AddTellmaSkia();
```

`AddTellmaSkia` registers the processor (once, however often it is called) and the `core.imaging`
startup check, which loads both native libraries before the host serves traffic: a missing or
mismatched native asset fails startup with the fix, not the first upload with a server error.

## What it does

**Normalises uploads** (`ProcessAsync`). Re-encoding is a security control: polyglot files,
embedded metadata and decompression bombs never reach storage.

1. The bytes' signature must be JPEG, PNG, GIF or WebP, checked in managed code; anything else is
   refused before native code sees it, which keeps Skia's other decoders (BMP, ICO, WBMP, camera
   raw) out of reach. Skia's codec must then agree with the signature.
2. The header's width times height must not exceed the policy's pixel cap. A decoder allocates
   from the header, so the cap is checked on the header alone, before any pixel is decoded, and it
   is the bound on decode memory.
3. JPEG and WebP decode at the smallest scale that still covers the output; only the first frame of
   an animation is decoded; colour profiles are converted to sRGB.
4. The encoded orientation is applied, since Skia does not orient decoded images, and the image is
   fitted: `Contain` within the maximum dimension, `CoverSquare` to the centred square. Neither
   upscales.
5. The result is encoded in the policy's format (WebP and JPEG lossy, PNG lossless) from untagged
   pixels, so no metadata or profile is written. WebP and PNG keep transparency and an opaque input
   stays opaque; JPEG composites transparency over white.

Refusals are `ImageRejectedException` with code `Image.Rejected` and a `reason` of `format`,
`too-many-pixels` or `decode-failed`.

**Renders thumbnails of stored files** (`ThumbnailAsync`): an image under the default 50-megapixel
cap, fitted and oriented the same way, or a PDF's first page fitted to the thumbnail square on
white. Anything else, an encrypted or unparsable PDF, an image over the cap, or a render past its
time budget yields null, which is never a rejection of the file.

## Hostile input and native code

- **One PDF render at a time.** PDFium is not thread-safe, so renders queue behind one process-wide
  gate. The time budget covers the wait and the render.
- **The time budget cancels for real.** Native decoders read their input on demand through a gated
  view; when the budget runs out the call returns null at once and the view reads as end of input,
  so a streaming decode in flight stops at its next read instead of running on. A PDF render that is
  already inside PDFium cannot be interrupted: it keeps the gate until its native call returns.
  Once the call has returned, no new read of the caller's stream starts.
- **No managed exception reaches native code.** Every member of the gated view that a native reader
  calls returns end of input instead of throwing.
- **No native handle outlives a call.** Every Skia object is disposed in the method that creates
  it, on every path; the suite counts SkiaSharp's live wrappers to prove it.

## Telemetry

The processor records `tellma.blobs.image.duration` (by `kind` and `operation`) and
`tellma.blobs.image.failures` (by `kind`, `operation` and `reason`, the reasons above plus
`timeout`) on the `Tellma.Core` meter, because only it can tell a render that ran out of time from a
file it cannot render. The `BlobImageRender` debug event is written immediately before native code
first touches an input, so a process that dies inside a decoder is attributable to the last such
line; `BlobImageFailed` (debug) records each failure, and `BlobThumbnailUnexpectedFailure` (warning)
an exception the processor does not anticipate.

## Native assets

The package pins SkiaSharp and PDFtoImage centrally, with the Skia natives on the same milestone
(SkiaSharp refuses a mismatched one) and the PDFium builds PDFtoImage is compiled against. Linux
uses SkiaSharp's NoDependencies build, so no fontconfig is needed on a server or in a container.
A framework-dependent build carries the natives for win-x64, win-arm64, linux-x64, linux-arm64
(glibc and musl) and macOS; a runtime-specific publish carries its runtime's only. Licences: SkiaSharp
and PDFtoImage MIT, the PDFium builds Apache-2.0 (PDFium itself BSD-3-Clause).
