// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Crud.Validation
{
    /// <summary>
    ///     One write frame's view of its scope's confirmations: the scope's confirmed keys, and the
    ///     warnings raised under the frame and not confirmed, which the frame's validation outcome
    ///     raises together. A group enlisted into the frame raises through the same view.
    /// </summary>
    internal sealed class ConfirmationFrame : IConfirmations
    {
        /// <summary>Guards the recorded warnings.</summary>
        private readonly Lock _gate = new();

        /// <summary>The scope's confirmations.</summary>
        private readonly Confirmations _scope;

        /// <summary>The unconfirmed warnings, in the order they were raised.</summary>
        private readonly List<ConfirmationRequest> _unconfirmed = [];

        /// <summary>Creates a view.</summary>
        /// <param name="scope">The scope's confirmations.</param>
        /// <param name="resource">The resource the frame's operation runs on.</param>
        /// <param name="operation">The frame's operation.</param>
        internal ConfirmationFrame(Confirmations scope, string resource, string operation)
        {
            _scope = scope;
            Resource = resource;
            Operation = operation;
        }

        /// <summary>The resource the frame's operation runs on.</summary>
        public string Resource { get; }

        /// <summary>The frame's operation.</summary>
        public string Operation { get; }

        /// <inheritdoc />
        public IReadOnlyCollection<string> Confirmed => _scope.Confirmed;

        /// <summary>
        ///     The warnings raised under the frame and not confirmed, ordered by key and then in the
        ///     order they were raised, so concurrent validators produce one answer.
        /// </summary>
        public IReadOnlyList<ConfirmationRequest> Unconfirmed
        {
            get
            {
                lock (_gate)
                {
                    // A stable sort: warnings of one key keep the order they were raised in.
                    return [.. _unconfirmed.OrderBy(static request => request.Key, StringComparer.Ordinal)];
                }
            }
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException"><paramref name="key" /> was not declared.</exception>
        public bool IsConfirmed(string key)
        {
            return _scope.IsConfirmed(key);
        }

        /// <inheritdoc />
        public void Require(string key, IReadOnlyList<long>? rows = null, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            IReadOnlyList<long> ids = rows ?? [];
            if (_scope.Raise(this, key, ids))
            {
                return;
            }

            ConfirmationRequest request = new(key, ids, arguments ?? new Dictionary<string, object?>());
            lock (_gate)
            {
                _unconfirmed.Add(request);
            }
        }
    }
}
