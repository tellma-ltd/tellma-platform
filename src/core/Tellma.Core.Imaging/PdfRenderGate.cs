// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     The process-wide gate PDF work queues behind, and what it knows about the render holding
    ///     it. PDFium keeps global state and is not thread-safe, so one PDFium call sequence runs at a
    ///     time in the process, whichever processor started it.
    /// </summary>
    /// <remarks>
    ///     A render inside PDFium cannot be interrupted, so it keeps the gate until its native call
    ///     returns, even after its caller gave up. While such an abandoned render holds the gate, a new
    ///     thumbnail fails fast instead of waiting out its whole budget behind it.
    /// </remarks>
    internal static class PdfRenderGate
    {
        // Guards the current render and its flags.
        private static readonly Lock Sync = new();

        // The render holding the gate, or null.
        private static Render? s_current;

        /// <summary>The semaphore itself: one holder at a time.</summary>
        public static SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>Whether the gate is held by a render whose caller has already given up on it.</summary>
        public static bool IsHeldByAbandonedRender
        {
            get
            {
                lock (Sync)
                {
                    return s_current is { Abandoned: true };
                }
            }
        }

        /// <summary>Records that the caller holding the gate starts a render; call right after entering.</summary>
        /// <returns>The render, to abandon or end.</returns>
        public static Render Begin()
        {
            var render = new Render();
            lock (Sync)
            {
                s_current = render;
            }

            return render;
        }

        /// <summary>Records that a render's caller gave up while the render is still running.</summary>
        /// <param name="render">The render.</param>
        public static void Abandon(Render render)
        {
            lock (Sync)
            {
                if (!render.Ended)
                {
                    render.Abandoned = true;
                }
            }
        }

        /// <summary>Records that a render's native work ended, and releases the gate.</summary>
        /// <param name="render">The render.</param>
        public static void End(Render render)
        {
            lock (Sync)
            {
                render.Ended = true;
                if (ReferenceEquals(s_current, render))
                {
                    s_current = null;
                }
            }

            Semaphore.Release();
        }

        /// <summary>One render's state, changed only under the gate's lock.</summary>
        internal sealed class Render
        {
            /// <summary>Whether its caller gave up while it ran.</summary>
            public bool Abandoned { get; set; }

            /// <summary>Whether its native work ended.</summary>
            public bool Ended { get; set; }
        }
    }
}
