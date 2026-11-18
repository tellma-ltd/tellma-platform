// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Crud
{
    // Spec 0015 §5.1 judges a persist nested in a frame by the frame: an [ApiAction] frame by its
    // descriptor, a job scope by its handler's RunsWhenReadOnly, a provisioning frame as admitted
    // (§13.3; spec 0011 §6.2; spec 0020 §3.1). The hosts that open those frames enter them here; the
    // pipeline's own operation is no frame of its own, its descriptor judging it when no frame does.
    /// <summary>
    ///     The write frames open in a scope, outermost first, which the tenant-state verdict of every
    ///     persist reads. Scoped.
    /// </summary>
    internal sealed class WriteFrames
    {
        /// <summary>Guards the open frames.</summary>
        private readonly Lock _gate = new();

        /// <summary>The open frames, outermost first.</summary>
        private readonly List<WriteFrame> _open = [];

        /// <summary>The open frames, outermost first.</summary>
        public IReadOnlyList<WriteFrame> Open
        {
            get
            {
                lock (_gate)
                {
                    return [.. _open];
                }
            }
        }

        /// <summary>Enters a frame: until the returned scope is disposed, the scope's persists are nested in it.</summary>
        /// <param name="frame">The frame.</param>
        /// <returns>The scope, whose disposal leaves the frame.</returns>
        /// <exception cref="ArgumentException">The frame is the pipeline's own, which is no frame of its own.</exception>
        public IDisposable Enter(WriteFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (frame.Kind == WriteHostKind.Pipeline)
            {
                throw new ArgumentException("A pipeline operation is judged by its own descriptor; only an API action, a job or a provisioning step opens a write frame.", nameof(frame));
            }

            lock (_gate)
            {
                _open.Add(frame);
            }

            return new Leaving(this, frame);
        }

        /// <summary>Leaves a frame.</summary>
        /// <param name="frame">The frame.</param>
        private void Leave(WriteFrame frame)
        {
            lock (_gate)
            {
                _ = _open.Remove(frame);
            }
        }

        /// <summary>Leaves a frame when disposed.</summary>
        /// <param name="frames">The scope's frames.</param>
        /// <param name="frame">The frame.</param>
        private sealed class Leaving(WriteFrames frames, WriteFrame frame) : IDisposable
        {
            /// <inheritdoc />
            public void Dispose()
            {
                frames.Leave(frame);
            }
        }
    }

    /// <summary>A write frame a persist may be nested in.</summary>
    /// <param name="Kind">The frame's host: an API action, a job or a provisioning step.</param>
    /// <param name="Operation">The frame's operation, for diagnostics, such as <c>gl.Invoice:post</c>.</param>
    /// <param name="AdmitsReadOnly">
    ///     Whether the frame admits a persist on a read-only tenant: an API action that is no mutation or
    ///     allows the read-only state, a job whose handler runs on a read-only tenant, and every
    ///     provisioning step.
    /// </param>
    internal sealed record WriteFrame(WriteHostKind Kind, string Operation, bool AdmitsReadOnly);
}
