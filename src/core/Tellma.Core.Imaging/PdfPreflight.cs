// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     Walks page 1 of a PDF through PDFium's own object API before it is rendered, and refuses a
    ///     page whose render would cost more than a thumbnail is worth: an image declaring more pixels
    ///     than the cap, a JPEG 2000 or JBIG2 image (decoded whole into memory) over a smaller cap,
    ///     more objects than the cap counted through every form, or a page side beyond the PDF limit.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         PDFium decodes an embedded image at its own resolution whatever the output size, and
    ///         one render call cannot be interrupted, so these caps are checked on the declarations
    ///         alone, before <c>FPDF_RenderPageBitmap</c> runs: decoding images and drawing are what
    ///         the walk keeps from happening.
    ///     </para>
    ///     <para>
    ///         Loading page 1 is not bounded the same way. <c>FPDF_LoadPage</c> parses the page's
    ///         content and builds a separate instance of every form for every time it is drawn, so a
    ///         page that nests forms costs objects, memory and time exponential in the nesting depth,
    ///         whatever the file's size: the input-length cap does not bound it, and the object cap
    ///         applies only after the load has returned. That load cannot be interrupted either.
    ///     </para>
    ///     <para>
    ///         So these stay residual risks, which only rendering out of process (in a memory-limited,
    ///         killable worker) removes: that expansion of nested forms inside the load, memory spent on
    ///         non-image content, images inside patterns, soft masks and Type 3 glyphs, and PDFium's own
    ///         bugs.
    ///     </para>
    ///     <para>
    ///         Not thread-safe, like PDFium: the caller holds the process-wide render gate. The
    ///         bindings target the same PDFium library PDFtoImage loads.
    ///     </para>
    /// </remarks>
    internal static partial class PdfPreflight
    {
        /// <summary>The largest PDF the thumbnail renderer reads: 32 MiB, which bounds parsing and a damaged-file rebuild scan.</summary>
        public const long MaxInputBytes = 32L * 1024 * 1024;

        /// <summary>The largest page side, in points: the PDF format's own limit of 200 inches.</summary>
        public const float MaxPageSide = 14_400f;

        /// <summary>The largest image, in pixels, that any filter may declare.</summary>
        public const long MaxImagePixels = Abstractions.Blobs.ImagePolicy.DefaultMaxInputPixels;

        /// <summary>
        ///     The largest JPEG 2000 or JBIG2 image, in pixels: their decoders hold the whole image in
        ///     memory rather than streaming scanlines.
        /// </summary>
        public const long MaxWholeImagePixels = 16_000_000;

        /// <summary>The most page objects page 1 may hold, counted through every nested form.</summary>
        public const int MaxObjects = 100_000;

        // PDFium's page-object types and error codes (fpdf_edit.h, fpdfview.h).
        private const int ImageObject = 3;
        private const int FormObject = 5;
        private const uint PasswordError = 4;
        private const uint SecurityError = 5;

        // The library name PDFtoImage binds to: pdfium.dll on Windows, libpdfium.so or .dylib elsewhere.
        private const string Library = "pdfium";

        // PDFium keeps global state that is initialised once per process; the call is idempotent
        // in PDFium, and this flag only saves the native call on every walk.
        private static bool s_initialised;

        /// <summary>Checks page 1 of <paramref name="pdf" /> against the caps, where PDFium exists.</summary>
        /// <param name="pdf">The whole document, at most <see cref="MaxInputBytes" />.</param>
        /// <param name="cancellationToken">Stops the walk between objects.</param>
        /// <exception cref="ImageFailure">The document cannot be opened, needs a password, or page 1 exceeds a cap.</exception>
        public static void Run(byte[] pdf, CancellationToken cancellationToken)
        {
            // PDFium ships for exactly these operating systems; the platform analyzer recognises the
            // guard only in statement form.
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                Check(pdf, cancellationToken);
                return;
            }

            throw ImageFailure.DecodeFailed("PDF rendering is not available on this operating system.");
        }

        /// <summary>Checks page 1 of <paramref name="pdf" /> against the caps.</summary>
        /// <param name="pdf">The whole document, at most <see cref="MaxInputBytes" />.</param>
        /// <param name="cancellationToken">Stops the walk between objects.</param>
        /// <exception cref="ImageFailure">The document cannot be opened, needs a password, or page 1 exceeds a cap.</exception>
        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static unsafe void Check(byte[] pdf, CancellationToken cancellationToken)
        {
            if (!s_initialised)
            {
                InitLibrary();
                s_initialised = true;
            }

            // Section: open the document from the pinned buffer and load page 1 only.
            fixed (byte* data = pdf)
            {
                IntPtr document = LoadMemDocument64(data, (nuint)pdf.Length, null);
                if (document == IntPtr.Zero)
                {
                    uint error = (uint)GetLastError().Value;
                    throw error is PasswordError or SecurityError
                        ? ImageFailure.DecodeFailed("The PDF requires a password or an unsupported security handler.")
                        : ImageFailure.DecodeFailed($"The PDF could not be opened (PDFium error {error}).");
                }

                try
                {
                    if (GetPageCount(document) < 1)
                    {
                        throw ImageFailure.DecodeFailed("The PDF has no pages.");
                    }

                    IntPtr page = LoadPage(document, 0);
                    if (page == IntPtr.Zero)
                    {
                        throw ImageFailure.DecodeFailed("Page 1 could not be loaded.");
                    }

                    try
                    {
                        CheckPage(page, cancellationToken);
                    }
                    finally
                    {
                        ClosePage(page);
                    }
                }
                finally
                {
                    CloseDocument(document);
                }
            }
        }

        /// <summary>Checks the page size, then every object, forms included, without recursion.</summary>
        /// <param name="page">The loaded page.</param>
        /// <param name="cancellationToken">Stops the walk between objects.</param>
        private static void CheckPage(IntPtr page, CancellationToken cancellationToken)
        {
            float width = GetPageWidthF(page);
            float height = GetPageHeightF(page);
            if (!(width <= MaxPageSide && height <= MaxPageSide))
            {
                throw ImageFailure.DecodeFailed($"Page 1 measures {width} x {height} points, more than {MaxPageSide} on a side.");
            }

            // Section: a breadth-first walk over the page's objects and every form's, counting all
            // of them; the count stops the walk the moment it passes the cap, so a fan-out of forms
            // costs the walk no more than the cap.
            var pending = new Queue<IntPtr>();
            int pageObjects = CountObjects(page);
            int total = 0;
            for (int i = 0; i < pageObjects; i++)
            {
                Visit(GetObject(page, i), pending, ref total);
            }

            while (pending.TryDequeue(out IntPtr form))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = FormCountObjects(form);
                for (int i = 0; i < count; i++)
                {
                    Visit(FormGetObject(form, new CULong((uint)i)), pending, ref total);
                }
            }
        }

        /// <summary>Counts one object, checks an image's declared size, and queues a form.</summary>
        /// <param name="pageObject">The object.</param>
        /// <param name="pending">The forms still to walk.</param>
        /// <param name="total">The running object count.</param>
        private static void Visit(IntPtr pageObject, Queue<IntPtr> pending, ref int total)
        {
            if (++total > MaxObjects)
            {
                throw ImageFailure.DecodeFailed($"Page 1 holds more than {MaxObjects} objects.");
            }

            switch (GetObjectType(pageObject))
            {
                case ImageObject:
                    CheckImage(pageObject);
                    break;

                case FormObject:
                    pending.Enqueue(pageObject);
                    break;

                default:
                    break;
            }
        }

        /// <summary>Refuses an image whose declared size exceeds its filter's cap, or cannot be read.</summary>
        /// <param name="image">The image object.</param>
        private static void CheckImage(IntPtr image)
        {
            if (!GetImagePixelSize(image, out uint width, out uint height))
            {
                throw ImageFailure.DecodeFailed("An image on page 1 declares no readable size.");
            }

            long pixels = (long)width * height;
            long cap = DecodesWhole(image) ? MaxWholeImagePixels : MaxImagePixels;
            if (pixels > cap)
            {
                throw ImageFailure.DecodeFailed($"An image on page 1 declares {width} x {height} pixels, more than {cap}.");
            }
        }

        /// <summary>Whether any of an image's filters is one whose decoder holds the whole image in memory.</summary>
        /// <param name="image">The image object.</param>
        /// <returns>True for JPXDecode or JBIG2Decode.</returns>
        private static unsafe bool DecodesWhole(IntPtr image)
        {
            int filters = GetImageFilterCount(image);
            Span<byte> name = stackalloc byte[32];
            for (int i = 0; i < filters; i++)
            {
                fixed (byte* buffer = name)
                {
                    // The length includes the terminating NUL; a longer name is no filter of interest.
                    nuint length = GetImageFilter(image, i, buffer, new CULong((uint)name.Length)).Value;
                    if (length is > 1 and <= 32)
                    {
                        string filter = Encoding.ASCII.GetString(name[..((int)length - 1)]);
                        if (filter is "JPXDecode" or "JBIG2Decode")
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        // Section: the PDFium exports the walk uses (fpdfview.h, fpdf_edit.h). PDFium's int is 32-bit
        // everywhere and size_t is nuint; its unsigned long is 32-bit on Windows and 64-bit on Unix,
        // so it binds as CULong, which has C's width on each platform.
        [LibraryImport(Library, EntryPoint = "FPDF_InitLibrary")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial void InitLibrary();

        [LibraryImport(Library, EntryPoint = "FPDF_LoadMemDocument64")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static unsafe partial IntPtr LoadMemDocument64(byte* data, nuint size, byte* password);

        [LibraryImport(Library, EntryPoint = "FPDF_GetLastError")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial CULong GetLastError();

        [LibraryImport(Library, EntryPoint = "FPDF_CloseDocument")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial void CloseDocument(IntPtr document);

        [LibraryImport(Library, EntryPoint = "FPDF_GetPageCount")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int GetPageCount(IntPtr document);

        [LibraryImport(Library, EntryPoint = "FPDF_LoadPage")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial IntPtr LoadPage(IntPtr document, int index);

        [LibraryImport(Library, EntryPoint = "FPDF_ClosePage")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial void ClosePage(IntPtr page);

        [LibraryImport(Library, EntryPoint = "FPDF_GetPageWidthF")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial float GetPageWidthF(IntPtr page);

        [LibraryImport(Library, EntryPoint = "FPDF_GetPageHeightF")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial float GetPageHeightF(IntPtr page);

        [LibraryImport(Library, EntryPoint = "FPDFPage_CountObjects")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int CountObjects(IntPtr page);

        [LibraryImport(Library, EntryPoint = "FPDFPage_GetObject")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial IntPtr GetObject(IntPtr page, int index);

        [LibraryImport(Library, EntryPoint = "FPDFPageObj_GetType")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int GetObjectType(IntPtr pageObject);

        [LibraryImport(Library, EntryPoint = "FPDFImageObj_GetImagePixelSize")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetImagePixelSize(IntPtr image, out uint width, out uint height);

        [LibraryImport(Library, EntryPoint = "FPDFImageObj_GetImageFilterCount")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int GetImageFilterCount(IntPtr image);

        [LibraryImport(Library, EntryPoint = "FPDFImageObj_GetImageFilter")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static unsafe partial CULong GetImageFilter(IntPtr image, int index, byte* buffer, CULong length);

        [LibraryImport(Library, EntryPoint = "FPDFFormObj_CountObjects")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int FormCountObjects(IntPtr form);

        [LibraryImport(Library, EntryPoint = "FPDFFormObj_GetObject")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial IntPtr FormGetObject(IntPtr form, CULong index);
    }
}
