// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Composition;

namespace Tellma.Core.Jobs
{
    /// <summary>One job handler as this instance registers it.</summary>
    /// <param name="Key">The handler key.</param>
    /// <param name="HandlerType">The handler class, resolved from the job's scope per invocation.</param>
    /// <param name="Options">The handler's declared options.</param>
    /// <param name="EntityType">
    ///     The leaf entity type an entity-backed handler processes, as the model maps it; null for an
    ///     arguments-only handler.
    /// </param>
    /// <param name="Owner">The feature that registered the handler.</param>
    internal sealed record JobHandlerRegistration(string Key, Type HandlerType, JobHandlerAttribute Options, Type? EntityType, ITellmaFeature Owner);

    /// <summary>
    ///     The job handlers this instance registers, the keys the features retired and the built-in
    ///     schedules they declared, as the composition realised them. A singleton built once from the
    ///     composition record; immutable.
    /// </summary>
    internal sealed class JobHandlerRegistry
    {
        /// <summary>The registered handlers by key.</summary>
        private readonly Dictionary<string, JobHandlerRegistration> _handlers;

        /// <summary>The retired keys.</summary>
        private readonly HashSet<string> _retired;

        /// <summary>Builds the registry from the composition record.</summary>
        /// <param name="record">The realised composition, whose job contributions passed the gate.</param>
        public JobHandlerRegistry(TellmaCompositionRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            // --- The handlers, keyed; an entity handler's entity is resolved to the leaf the model maps ---
            _handlers = new(StringComparer.Ordinal);
            foreach (ContributedJobHandler handler in record.JobHandlers)
            {
                Type? entityType = handler.EntityType is null ? null : record.LeafOf(handler.EntityType);
                _handlers[handler.Attribute.Key] = new JobHandlerRegistration(handler.Attribute.Key, handler.HandlerType, handler.Attribute, entityType, handler.Owner);
            }

            _retired = new(record.RetiredJobKeys.Select(static retired => retired.Key), StringComparer.Ordinal);
            Handlers = [.. _handlers.Values.OrderBy(static handler => handler.Key, StringComparer.Ordinal)];
            RetiredKeys = [.. _retired.Order(StringComparer.Ordinal)];
            BuiltInSchedules = [.. record.BuiltInSchedules.Select(static schedule => schedule.Seed)];
        }

        /// <summary>The registered handlers, ordered by key.</summary>
        public IReadOnlyList<JobHandlerRegistration> Handlers { get; }

        /// <summary>The keys the features retired, ordered.</summary>
        public IReadOnlyList<string> RetiredKeys { get; }

        /// <summary>The built-in schedules the features declared, in declaration order.</summary>
        public IReadOnlyList<BuiltInScheduleSeed> BuiltInSchedules { get; }

        /// <summary>Finds the handler registered under a key.</summary>
        /// <param name="key">The handler key.</param>
        /// <param name="registration">The handler; null when no handler registers the key.</param>
        /// <returns>True when a handler registers the key.</returns>
        public bool TryFind(string key, [NotNullWhen(true)] out JobHandlerRegistration? registration)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _handlers.TryGetValue(key, out registration);
        }

        /// <summary>Whether a feature retired the key.</summary>
        /// <param name="key">The handler key.</param>
        /// <returns>True when the key is retired.</returns>
        public bool IsRetired(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _retired.Contains(key);
        }
    }
}
