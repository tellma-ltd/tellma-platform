// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Notifications;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.notification-types</c>: every type's key and category match their grammars, keys
    ///     are unique, every severity is defined, a channel-only type is neither mutable nor a toast,
    ///     and every target names a top-level entity of the model whose key a 64-bit target id holds.
    /// </summary>
    /// <remarks>
    ///     Reads the composition alone. The registry keeps the first type of a key, so that this check,
    ///     and not a failing constructor, is where a mistake surfaces. A model that does not build is
    ///     the entity-metadata check's to report; the targets are then not judged.
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed class NotificationTypesCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.NotificationTypes;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];

            // --- Every type on its own ---
            foreach (ContributedNotificationType contributed in record.NotificationTypes)
            {
                CheckType(contributed.Descriptor, contributed.Owner.Name, problems);
            }

            // --- Keys are unique, compared ordinally as the registry finds them ---
            foreach (IGrouping<string, ContributedNotificationType> clash in record.NotificationTypes
                .Where(static contributed => contributed.Descriptor.Key is not null)
                .GroupBy(static contributed => contributed.Descriptor.Key, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1))
            {
                string owners = string.Join(" and ", clash.Select(static contributed => $"'{contributed.Owner.Name}'"));
                problems.Add(new CompositionProblem(
                    Name,
                    $"The notification type '{clash.Key}' is registered more than once, by the features {owners}.",
                    "Register each notification type once, in the feature whose code raises it; a changed meaning is a new key."));
            }

            // --- The targets, against the model ---
            if (services.GetService<EntityMetadataSource>()?.Build.Provider is IEntityMetadataProvider metadata)
            {
                CheckTargets(metadata, problems);
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>Checks one type on its own: its key, its category, its severity and the channel-only rule.</summary>
        /// <param name="type">The type.</param>
        /// <param name="owner">The name of the feature that registered it.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckType(NotificationTypeDescriptor type, string owner, List<CompositionProblem> problems)
        {
            string described = $"The notification type '{type.Key}' of the feature '{owner}'";

            // --- The key and the category ---
            if (!NotificationNames.IsTypeKey(type.Key))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} does not have a well-formed key.",
                    $"Key the type with two or more dot-separated segments of lowercase letters, digits and hyphens, each starting with a letter, at most {NotificationNames.MaxTypeKeyLength} characters, led by the feature's slug, such as 'acme.document.approved'."));
            }

            if (!NotificationNames.IsCategory(type.Category))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} has the category '{type.Category}', which is not well formed.",
                    $"Name the category with lowercase letters, digits and hyphens, starting with a letter, at most {NotificationNames.MaxCategoryLength} characters, such as 'jobs'."));
            }

            // --- The severity: rows store it by name, which an undefined value has none of ---
            if (!Enum.IsDefined(type.Severity))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} has the undefined severity {(int)type.Severity}.",
                    "Use Info, Success, Warning or Error."));
            }

            // --- A channel-only type writes no row, so the inbox can neither be muted nor toast ---
            if (!type.Inbox && (type.Mutable || type.Toast))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} is channel-only, yet {(type.Mutable ? "mutable" : "a toast")}{(type.Mutable && type.Toast ? " and a toast" : string.Empty)}.",
                    "Declare a channel-only type (Inbox = false) with Mutable = false and Toast = false: it writes no inbox row to mute or to toast."));
            }
        }

        /// <summary>Checks that every target names a top-level entity of the model with an integer key.</summary>
        /// <param name="metadata">The model's entity metadata.</param>
        /// <param name="problems">Receives the problems.</param>
        internal void CheckTargets(IEntityMetadataProvider metadata, List<CompositionProblem> problems)
        {
            // A target is the resource the client routes to. The stack resources are the top-level
            // entities' names; when the securable registry lands (spec 0021 §11 also accepts a
            // registered securable resource), its resources join these.
            Dictionary<string, EntityMetadata> resources = new(StringComparer.Ordinal);
            foreach (EntityMetadata entity in metadata.All.Where(static entity => entity.IsTopLevel))
            {
                _ = resources.TryAdd(entity.Name, entity);
            }

            foreach (ContributedNotificationType contributed in record.NotificationTypes)
            {
                NotificationTypeDescriptor type = contributed.Descriptor;
                if (type.TargetResource is not string target)
                {
                    continue;
                }

                string described = $"The notification type '{type.Key}' of the feature '{contributed.Owner.Name}'";
                if (!resources.TryGetValue(target, out EntityMetadata? entity))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"{described} targets '{target}', which is not a top-level entity of the model.",
                        "Target a top-level entity by its name, such as 'core.User', so the client can route to it; set the target to null for a type that points at nothing."));
                    continue;
                }

                // The rows' target id is a 64-bit integer, which holds an int or a long key and
                // nothing else.
                Type key = Nullable.GetUnderlyingType(entity.Key.ClrType) ?? entity.Key.ClrType;
                if (key != typeof(int) && key != typeof(long))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"{described} targets '{target}', whose key is a {key.Name}, which a notification's 64-bit target id cannot hold.",
                        "Target an entity keyed by an int or a long, or set the target to null and carry the reference in the arguments."));
                }
            }
        }
    }
}
