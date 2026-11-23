// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Sharing;

namespace Tellma.Core.Composition.Realizers
{
    // Spec 0033 §5.1: one Shareable<TEntity, TShare>() declaration registers everything the sharing
    // model needs for the pair: the share table's model, the access criteria (§5.3), the commands
    // (§5.4), the shares extra (§5.2), the shared-with-me filter (§5.5) and the notification type a
    // new user grant raises (§7). The pair's model-level rules (one reference to the row, a share
    // entity that is no stack and no child collection, a service derived from ShareableService) are
    // the startup check's, against the built model.
    /// <summary>
    ///     Realises a <see cref="ShareableContributionItem" />: judges the item's shape, records the
    ///     pair for the startup check, and registers the pair's model configuration, components,
    ///     services and notification type.
    /// </summary>
    internal sealed class ShareableContributionRealizer : IContributionRealizer<ShareableContributionItem>
    {
        // Spec 0021 §2.3 catalogues the share notice under the reports group.
        /// <summary>The notification settings group every share notice belongs to.</summary>
        private const string NoticeCategory = "reports";

        /// <inheritdoc />
        public void Realize(ShareableContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // --- The item's shape: a shareable entity, a share entity, a notification type ---
            if (!ShareableShape.IsShareable(item.EntityType))
            {
                Report(
                    context,
                    $"Feature '{owner.Name}' makes {Described(item.EntityType)} shareable, which is not a concrete top-level entity implementing IShareable.",
                    "Make a concrete class deriving from TopLevelEntity and implementing IShareable shareable.");
                return;
            }

            Type entity = item.EntityType;
            if (!ShareableShape.IsShareRow(item.ShareType))
            {
                Report(
                    context,
                    $"Feature '{owner.Name}' shares {TypeDisplay.Qualified(entity)} through {Described(item.ShareType)}, which is not a concrete Entity<int> implementing IShareRow.",
                    "Share the entity through a concrete class deriving from Entity<int> and implementing IShareRow.");
                return;
            }

            Type share = item.ShareType;
            if (string.IsNullOrWhiteSpace(item.NotificationType))
            {
                Report(
                    context,
                    $"Feature '{owner.Name}' makes {TypeDisplay.Qualified(entity)} shareable with no notification type.",
                    "Name the notification type a new user grant raises: contribution.Shareable<TEntity, TShare>(\"pack.entity.shared\").");
                return;
            }

            if (ShareableShape.RowForeignKey(share, entity) is null)
            {
                Report(
                    context,
                    $"Feature '{owner.Name}' shares {TypeDisplay.Qualified(entity)} through {TypeDisplay.Qualified(share)}, which has no single property marked [References(typeof({TypeDisplay.Short(entity)}))].",
                    $"Give {TypeDisplay.Short(share)} exactly one int property marked [References(typeof({TypeDisplay.Short(entity)}))], its grant's row.");
                return;
            }

            // --- One declaration per entity ---
            if (context.Record.Shareables.Find(existing => existing.EntityType == entity) is ContributedShareable earlier)
            {
                Report(
                    context,
                    $"Feature '{owner.Name}' makes {TypeDisplay.Qualified(entity)} shareable, which feature '{earlier.Owner.Name}' already made shareable.",
                    "Declare an entity shareable once, through one share entity.");
                return;
            }

            context.Record.Shareables.Add(new ContributedShareable(entity, share, item.NotificationType, owner));

            // --- The share table's model, the extra and the named filter ---
            context.Record.ModelConfigurations.Add(new ContributedType(typeof(ShareRowConfiguration<,>).MakeGenericType(share, entity), owner));
            context.Record.Components.Add(new ContributedComponent(entity, typeof(SharesContributor<,>).MakeGenericType(entity, share), ComponentKind.DetailsContributor, owner));
            context.Record.Components.Add(new ContributedComponent(entity, typeof(SharedWithMeFilters<,>).MakeGenericType(entity, share), ComponentKind.NamedFilterContributor, owner));

            // --- The criteria, one more provider among the composition's, and the scope's commands ---
            Type criteria = typeof(ShareableAccessCriteria<,>).MakeGenericType(entity, share);
            context.Services.AddSingleton(typeof(IAccessCriteriaProvider), criteria);
            context.Record.Services.Add(new ContributedService(typeof(IAccessCriteriaProvider), criteria, owner));

            Type commandsService = typeof(IShareCommands<>).MakeGenericType(entity);
            Type commands = typeof(ShareCommands<,>).MakeGenericType(entity, share);
            ShareableNotice notice = new(item.NotificationType);
            context.Services.Add(ServiceDescriptor.Scoped(commandsService, provider => ActivatorUtilities.CreateInstance(provider, commands, notice)));
            context.Record.Services.Add(new ContributedService(commandsService, commands, owner));

            // --- The notice of a new user grant, which opens the shared row ---
            // The row's resource is the entity's name: its table's schema and the declared class's name.
            NotificationTypeDescriptor descriptor = new(item.NotificationType, NoticeCategory, Mutable: true, TargetResource: Resource(entity));
            context.Record.NotificationTypes.Add(new ContributedNotificationType(descriptor, owner));
            _ = context.Services.AddSingleton(descriptor);
        }

        /// <summary>The resource of an entity class: its table's schema and its name, as the model names it.</summary>
        /// <param name="entity">The entity class.</param>
        /// <returns>The resource; null for a class that names no table schema, which the notification check then reports.</returns>
        private static string? Resource(Type entity)
        {
            return entity.GetCustomAttribute<TableAttribute>()?.Schema is string schema ? $"{schema}.{entity.Name}" : null;
        }

        /// <summary>A type as a problem names it.</summary>
        /// <param name="type">The type; null for none.</param>
        /// <returns>The description.</returns>
        private static string Described(Type? type)
        {
            return type is null ? "no type" : TypeDisplay.Qualified(type);
        }

        /// <summary>Reports a problem of a shareable item.</summary>
        /// <param name="context">Where the problem is reported.</param>
        /// <param name="problem">What is wrong.</param>
        /// <param name="fix">The remedy.</param>
        private static void Report(RealizationContext context, string problem, string fix)
        {
            context.Report(new CompositionProblem(CompositionNames.Source, problem, fix));
        }
    }
}
