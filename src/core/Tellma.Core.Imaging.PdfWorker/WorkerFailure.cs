// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Imaging.PdfWorker
{
    /// <summary>
    ///     Why the worker refuses to render a PDF: it is encrypted, malformed, has no page, or
    ///     page 1 exceeds a cap. The worker answers it as a refusal, which the processor counts as a
    ///     file it cannot render.
    /// </summary>
    /// <param name="message">What was refused, for the processor's debug log.</param>
    /// <param name="innerException">The library's exception, when one caused the refusal.</param>
    internal sealed class WorkerFailure(string message, Exception? innerException = null) : Exception(message, innerException);
}
