// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Crud.Validation
{
    // Spec 0015 §7.5: one IConfirmations per request scope. The entry point seeds the confirmed keys
    // once (spec 0016's Tellma-Confirm header, MCP's token, spec 0020's job row); under a system
    // scope every key counts as confirmed; a key nobody declared is an authoring bug. Warnings are
    // raised through a write frame's view, so the frame's validation outcome raises them together.
    /// <summary>
    ///     The confirmed keys of one request scope, and the write frames' views that record the
    ///     warnings raised under them.
    /// </summary>
    /// <param name="declared">The declared keys.</param>
    /// <param name="contexts">The scope's request context, which says whether the scope is the system's and who confirms.</param>
    /// <param name="metrics">The instruments.</param>
    /// <param name="logger">The logger of the consent records.</param>
    internal sealed class Confirmations(
        DeclaredConfirmations declared, IRequestContextAccessor contexts, CrudMetrics metrics, ILogger<Confirmations> logger) : IConfirmations
    {
        /// <summary>Guards the seed.</summary>
        private readonly Lock _gate = new();

        /// <summary>The declared keys.</summary>
        private readonly DeclaredConfirmations _declared = declared ?? throw new ArgumentNullException(nameof(declared));

        /// <summary>The scope's request context.</summary>
        private readonly IRequestContextAccessor _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));

        /// <summary>The instruments.</summary>
        private readonly CrudMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

        /// <summary>The logger of the consent records.</summary>
        private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>The seeded keys, in ordinal order; null until the entry point seeds them.</summary>
        private string[]? _seeded;

        /// <summary>The seeded keys, for lookups.</summary>
        private FrozenSet<string> _confirmed = [];

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

        /// <summary>Refuses a warning raised outside every write frame, where no outcome would ever raise it.</summary>
        /// <param name="key">The warning's key.</param>
        /// <param name="rows">The ids of the rows it concerns.</param>
        /// <param name="arguments">Its arguments.</param>
        /// <exception cref="InvalidOperationException">Always: a warning is raised through a frame's confirmations.</exception>
        public void Require(string key, IReadOnlyList<long>? rows = null, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            _declared.EnsureDeclared(key, nameof(key));
            throw new InvalidOperationException(
                $"The warning '{key}' was raised on the scope's confirmations, outside every write frame, where no validation outcome would raise it. Raise it through the frame it belongs to: a context's Host.Confirmations.");
        }

        /// <summary>Opens the view a write frame raises its warnings through.</summary>
        /// <param name="resource">The resource the frame's operation runs on, for the consent record.</param>
        /// <param name="operation">The frame's operation, such as <c>gl.Invoice:post</c>.</param>
        /// <returns>The view.</returns>
        public ConfirmationFrame Frame(string resource, string operation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            return new ConfirmationFrame(this, resource, operation);
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
