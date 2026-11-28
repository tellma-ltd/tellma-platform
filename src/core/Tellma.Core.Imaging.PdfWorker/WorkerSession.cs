// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Core.Imaging.PdfWorker
{
    /// <summary>
    ///     One worker's life: enter the sandbox, report ready, read one request, render it, answer,
    ///     exit. A worker renders exactly one document, so no upload ever shares a process with
    ///     another's.
    /// </summary>
    internal static class WorkerSession
    {
        /// <summary>Runs the worker.</summary>
        /// <param name="arguments">The command line.</param>
        /// <param name="rasterizer">Renders the page; PDFium in the worker.</param>
        /// <returns>The exit code: 0 once a result is written, otherwise one of <see cref="WorkerExitCodes" />.</returns>
        public static int Run(string[] arguments, IPdfRasterizer rasterizer)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            ArgumentNullException.ThrowIfNull(rasterizer);

            // Section: the sandbox, before anything of the upload is read. A worker that cannot
            // enter it says why on standard error, which the host keeps for its log, and stops.
            List<string> remaining = [.. arguments];
            WorkerCapabilities capabilities;
            Stream output;
            try
            {
                capabilities = WorkerSandbox.Enter(remaining, out output);
            }
            catch (ArgumentException exception)
            {
                return Report(WorkerExitCodes.BadArguments, exception);
            }
#pragma warning disable CA1031 // Whatever stops the sandbox stops the worker; the reason goes to the host.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                return Report(WorkerExitCodes.SandboxFailed, exception);
            }

            if (remaining.Count > 0)
            {
                return Report(WorkerExitCodes.BadArguments, new ArgumentException($"Unknown argument '{remaining[0]}'."));
            }

            using (output)
            {
                return Serve(capabilities, Console.OpenStandardInput(), output, rasterizer);
            }
        }

        /// <summary>Reports ready, reads the request, renders it and writes the result.</summary>
        /// <param name="capabilities">What the worker enforces on itself.</param>
        /// <param name="input">The worker's input.</param>
        /// <param name="output">The worker's protocol output.</param>
        /// <param name="rasterizer">Renders the page.</param>
        /// <returns>The exit code.</returns>
        internal static int Serve(WorkerCapabilities capabilities, Stream input, Stream output, IPdfRasterizer rasterizer)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(rasterizer);

            // Section: ready, then the request. The host applies its limits between the two.
            WorkerRequest request;
            try
            {
                WorkerProtocol.WriteReady(output, capabilities);
                request = WorkerProtocol.ReadRequest(input);
            }
            catch (EndOfStreamException)
            {
                return WorkerExitCodes.HostGone;
            }
            catch (IOException)
            {
                return WorkerExitCodes.HostGone;
            }
            catch (InvalidDataException exception)
            {
                return Report(WorkerExitCodes.BadRequest, exception);
            }

            // Section: from here the host writes nothing more, so input ending means it is gone.
            WorkerSandbox.ExitWhenInputEnds(input);

            // Section: the render. A refusal is the document's fault; any other failure (out of
            // memory under the host's cap, for one) is the worker's, and the host tells them apart.
            WorkerStatus status;
            byte[] payload;
            try
            {
                payload = PdfRenderer.Render(request.Pdf, request.Size, rasterizer);
                status = WorkerStatus.Rendered;
            }
            catch (WorkerFailure failure)
            {
                (status, payload) = (WorkerStatus.Refused, Detail(failure.Message));
            }
#pragma warning disable CA1031 // The host classifies the failure; the worker only reports it.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                (status, payload) = (WorkerStatus.Failed, Detail($"{exception.GetType().Name}: {exception.Message}"));
            }

            try
            {
                WorkerProtocol.WriteResult(output, status, payload);
                return 0;
            }
            catch (IOException)
            {
                return WorkerExitCodes.HostGone;
            }
        }

        /// <summary>Encodes a detail for a result frame, cut to the protocol's cap on a character boundary.</summary>
        /// <param name="text">The detail.</param>
        /// <returns>At most <see cref="WorkerProtocol.MaxDetailBytes" /> bytes of UTF-8.</returns>
        internal static byte[] Detail(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length <= WorkerProtocol.MaxDetailBytes)
            {
                return bytes;
            }

            int length = WorkerProtocol.MaxDetailBytes;
            while (length > 0 && (bytes[length] & 0xC0) == 0x80)
            {
                length--;
            }

            return bytes[..length];
        }

        /// <summary>Writes why the worker stops to standard error, for the host's log.</summary>
        /// <param name="exitCode">The exit code to return.</param>
        /// <param name="exception">What stopped it.</param>
        /// <returns><paramref name="exitCode" />.</returns>
        private static int Report(int exitCode, Exception exception)
        {
            try
            {
                Console.Error.WriteLine($"Tellma PDF worker: {exception.GetType().Name}: {exception.Message}");
            }
            catch (IOException)
            {
                // The host is gone; there is no one to tell.
            }

            return exitCode;
        }
    }
}
