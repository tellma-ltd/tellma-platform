// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Crud.Validation
{
    // Spec 0015 §7.5: one IConfirmations per request scope, shared by every frame and enlisted group
    // in it. The entry point seeds the confirmed keys once (spec 0016's Tellma-Confirm header, MCP's
    // token, spec 0020's job row); under a system scope every key counts as confirmed; a key nobody
    // declared is an authoring bug. Require records a warning and carries on: into the innermost open
    // write frame, whose validation outcome raises it, or, outside every frame, onto the scope, where
    // code running outside validation tests IsConfirmed and throws itself.
    /// <summary>
    ///     The confirmed keys of one request scope, the write frames open in it, and the warnings
    ///     raised under them. A warning raised on the scope lands in the innermost open frame, so an
    ///     enlisted group raising through its host's scope is raised by the host's outcome.
    /// </summary>
    /// <param name="declared">The declared keys.</param>
    /// <param name="contexts">The scope's request context, which says whether the scope is the system's and who confirms.</param>
    /// <param name="metrics">The instruments.</param>
    /// <param name="logger">The logger of the consent records.</param>
    internal sealed class Confirmations(
        DeclaredConfirmations declared, IRequestContextAccessor contexts, CrudMetrics metrics, ILogger<Confirmations> logger) : IConfirmations
    {
        /// <summary>Guards the seed, the open frames and the warnings raised outside them.</summary>
        private readonly Lock _gate = new();

        /// <summary>The declared keys.</summary>
        private readonly DeclaredConfirmations _declared = declared ?? throw new ArgumentNullException(nameof(declared));

        /// <summary>The scope's request context.</summary>
        private readonly IRequestContextAccessor _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));

        /// <summary>The instruments.</summary>
        private readonly CrudMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

        /// <summary>The logger of the consent records.</summary>
        private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>The open write frames, outermost first.</summary>
        private readonly List<ConfirmationFrame> _frames = [];

        /// <summary>The unconfirmed warnings raised outside every frame, in the order they were raised.</summary>
        private readonly List<ConfirmationRequest> _unframed = [];

        /// <summary>The seeded keys, in ordinal order; null until the entry point seeds them.</summary>
        private string[]? _seeded;

        /// <summary>The seeded keys, for lookups.</summary>
        private FrozenSet<string> _confirmed = [];

        /// <summary>
        ///     The unconfirmed warnings raised while no write frame was open, in the order they were
        ///     raised. No validation outcome raises them: code that runs outside validation tests
        ///     <see cref="IsConfirmed" /> and throws itself.
        /// </summary>
        public IReadOnlyList<ConfirmationRequest> Unframed
        {
            get
            {
                lock (_gate)
                {
                    return [.. _unframed];
                }
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> Confirmed
        {
            get
            {
                lock (_gate)
                {
                    return _seeded ?? [];
                }
            }
        }

        /// <summary>
        ///     Seeds the keys the caller confirmed, once per scope. A key no feature declares is dropped:
        ///     a client may confirm a warning the server no longer raises.
        /// </summary>
        /// <param name="keys">The keys the caller confirmed.</param>
        /// <exception cref="InvalidOperationException">The scope was seeded already.</exception>
        public void Seed(IEnumerable<string> keys)
        {
            ArgumentNullException.ThrowIfNull(keys);
            lock (_gate)
            {
                if (_seeded is not null)
                {
                    throw new InvalidOperationException("A scope's confirmed keys are seeded once, by its entry point.");
                }

                _seeded = [.. keys.Where(key => key is not null && _declared.Keys.Contains(key)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
                _confirmed = _seeded.ToFrozenSet(StringComparer.Ordinal);
            }
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException"><paramref name="key" /> was not declared.</exception>
        public bool IsConfirmed(string key)
        {
            _declared.EnsureDeclared(key, nameof(key));
            if (_contexts.Current.IsSystem)
            {
                return true;
            }

            lock (_gate)
            {
                return _confirmed.Contains(key);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        ///     The warning lands in the innermost open write frame, whose validation outcome raises it
        ///     with the frame's own. Outside every frame it is kept in <see cref="Unframed" />.
        /// </remarks>
        /// <exception cref="InvalidOperationException"><paramref name="key" /> was not declared.</exception>
        public void Require(string key, IReadOnlyList<long>? rows = null, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            _declared.EnsureDeclared(key, nameof(key));
            ConfirmationFrame? frame;
            lock (_gate)
            {
                frame = _frames.Count > 0 ? _frames[^1] : null;
            }

            // Inside a frame: the frame counts, logs and records it under its resource and operation.
            if (frame is not null)
            {
                frame.Require(key, rows, arguments);
                return;
            }

            // Outside every frame: counted and logged here, and kept on the scope when unconfirmed.
            IReadOnlyList<long> ids = rows ?? [];
            bool confirmed = IsConfirmed(key);
            _metrics.CountConfirmation(key, confirmed);
            RequestContext context = _contexts.Current;
            if (confirmed)
            {
                if (!context.IsSystem)
                {
                    CrudLog.ConfirmationAcceptedOutsideFrame(_logger, context.UserId, key, ids);
                }

                return;
            }

            ConfirmationRequest request = new(key, ids, arguments ?? new Dictionary<string, object?>());
            lock (_gate)
            {
                _unframed.Add(request);
            }
        }

        /// <summary>
        ///     Opens a write frame: until it is disposed, it is the innermost frame, and warnings raised
        ///     on the scope land in it.
        /// </summary>
        /// <param name="resource">The resource the frame's operation runs on, for the consent record.</param>
        /// <param name="operation">The frame's operation, such as <c>gl.Invoice:post</c>.</param>
        /// <returns>The frame, whose validation outcome raises its warnings.</returns>
        public ConfirmationFrame Frame(string resource, string operation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            ConfirmationFrame frame = new(this, resource, operation);
            lock (_gate)
            {
                _frames.Add(frame);
            }

            return frame;
        }

        /// <summary>Closes a frame, wherever it sits among the open ones; closing it twice is a no-op.</summary>
        /// <param name="frame">The frame.</param>
        internal void Close(ConfirmationFrame frame)
        {
            lock (_gate)
            {
                _ = _frames.Remove(frame);
            }
        }

        /// <summary>
        ///     Counts a warning raised under a frame and, when the caller confirmed it, records the
        ///     consent; the system scope's warnings count as confirmed and record none.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="key">The warning's key.</param>
        /// <param name="rows">The ids of the rows it concerns.</param>
        /// <returns>True when the warning counts as confirmed.</returns>
        internal bool Raise(ConfirmationFrame frame, string key, IReadOnlyList<long> rows)
        {
            bool confirmed = IsConfirmed(key);
            _metrics.CountConfirmation(key, confirmed);
            RequestContext context = _contexts.Current;
            if (confirmed && !context.IsSystem)
            {
                CrudLog.ConfirmationAccepted(_logger, context.UserId, frame.Resource, frame.Operation, key, rows);
            }

            return confirmed;
        }
    }
}
