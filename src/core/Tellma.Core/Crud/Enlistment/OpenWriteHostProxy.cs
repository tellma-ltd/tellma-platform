// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §11.3 and §13.3: an API action's method, a job's handler and a provisioning step
    // inject IOpenWriteHost, a scoped service created before their frame opens. It stands for the
    // open frame the calling flow runs in, found on the flow: a method that nests an operation of
    // its own finds its own frame, never the nested one. It enlists only from the frame's own work:
    // code running inside a pipeline's or a group's frame nested in the open frame (a hook of a
    // front-door save the method called, or an enlisted write's participant) enlists with its
    // context's Host, so its write nests where it belongs, takes its frame's path map and is
    // matched on a re-run; through the injected host it would land at the open frame's top level.
    /// <summary>The injected open frame: forwards to the innermost open frame of the calling flow. Scoped.</summary>
    internal sealed class OpenWriteHostProxy : IOpenWriteHost, IEnlistmentHost
    {
        /// <inheritdoc />
        public WriteHostKind Kind => Frame.Kind;

        /// <inheritdoc />
        public string Operation => Frame.Operation;

        /// <inheritdoc />
        public IConfirmations Confirmations => Frame.Confirmations;

        /// <summary>The innermost open frame of the calling flow.</summary>
        /// <exception cref="InvalidOperationException">The flow runs in no open frame.</exception>
        private static OpenWriteFrame Frame => FrameFlow.InnermostOpen ?? throw new InvalidOperationException(
            "No open frame runs in this flow. An injected IOpenWriteHost serves an [ApiAction] method invoked through IApiActionInvoker, a job's handler and a provisioning step; a pipeline's hooks enlist with their context's Host.");

        /// <summary>The open frame an enlistment through the injected host joins: the calling flow's innermost frame, which must be the open frame itself.</summary>
        /// <exception cref="InvalidOperationException">The flow runs in no open frame, or inside a pipeline's or a group's frame nested in it.</exception>
        private static OpenWriteFrame EnlistingFrame
        {
            get
            {
                OpenWriteFrame open = Frame;
                EnlistmentFrame? innermost = FrameFlow.Innermost;
                return ReferenceEquals(innermost, open)
                    ? open
                    : throw new InvalidOperationException(
                        $"A write was enlisted through the injected IOpenWriteHost from inside the frame of {innermost?.Operation}, which runs within {open.Operation}. The hooks of an operation and the participants of an enlisted write enlist with their context's Host; the injected host serves the method, the handler or the step itself.");
            }
        }

        /// <inheritdoc />
        public Task<EnlistmentOutcome> PersistAsync(CancellationToken cancellationToken = default)
        {
            return Frame.PersistAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<EnlistedSave<TTarget>> EnlistSaveAsync<TTarget>(IEnlists<TTarget> writer, IReadOnlyList<TTarget> rows, EnlistSaveOptions options)
        {
            return EnlistingFrame.EnlistSaveAsync(writer, rows, options);
        }

        /// <inheritdoc />
        public EnlistedDelete<TKey> EnlistDelete<TTarget, TKey>(IEnlists<TTarget> writer, IReadOnlyList<TKey> ids, EnlistDeleteOptions options)
            where TTarget : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            return EnlistingFrame.EnlistDelete(writer, ids, options);
        }
    }
}
