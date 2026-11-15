// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     What a feature adds in <see cref="ITellmaFeature.Contribute" />: data-only
    ///     <see cref="FeatureContributionItem" /> records, and nested features. Each method records an
    ///     item; nothing is registered until the platform realises the composition.
    /// </summary>
    /// <remarks>
    ///     The typed methods are sugar over <see cref="Add" />. Each spec that introduces an item kind
    ///     adds its sugar here or as an extension method; <see cref="Add" /> is the escape hatch for an
    ///     item kind that has none.
    /// </remarks>
    public sealed class FeatureContribution
    {
        private readonly List<FeatureContributionItem> _items = [];
        private readonly List<ITellmaFeature> _features = [];

        /// <summary>The items recorded so far, in the order they were added.</summary>
        public IReadOnlyList<FeatureContributionItem> Items => _items;

        /// <summary>The nested features recorded by <see cref="Feature" />, in the order they were added.</summary>
        public IReadOnlyList<ITellmaFeature> Features => _features;

        /// <summary>
        ///     Adds an item. An item whose exact runtime type no realizer handles is a composition
        ///     problem naming the item type.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="item" /> is null.</exception>
        public FeatureContribution Add(FeatureContributionItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _items.Add(item);
            return this;
        }

        /// <summary>
        ///     Adds a nested feature. It is declared, validated and ordered with every other feature,
        ///     and it is realised before the feature that nests it, as if that feature required it.
        ///     Nesting an instance of a feature type that was already added is a no-op for the same
        ///     instance and a composition problem for a different one.
        /// </summary>
        /// <param name="feature">The nested feature.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="feature" /> is null.</exception>
        public FeatureContribution Feature(ITellmaFeature feature)
        {
            ArgumentNullException.ThrowIfNull(feature);
            _features.Add(feature);
            return this;
        }

        /// <summary>Registers <typeparamref name="TImplementation" /> as a singleton <typeparamref name="TService" />.</summary>
        /// <typeparam name="TService">The service type.</typeparam>
        /// <typeparam name="TImplementation">The concrete implementation type.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Singleton<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            return Add(new ServiceContributionItem(typeof(TService), typeof(TImplementation), null, ServiceLifetimeKind.Singleton));
        }

        /// <summary>Registers <typeparamref name="TImplementation" /> as a scoped <typeparamref name="TService" />.</summary>
        /// <typeparam name="TService">The service type.</typeparam>
        /// <typeparam name="TImplementation">The concrete implementation type.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Scoped<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            return Add(new ServiceContributionItem(typeof(TService), typeof(TImplementation), null, ServiceLifetimeKind.Scoped));
        }

        /// <summary>Registers <typeparamref name="TImplementation" /> as a transient <typeparamref name="TService" />.</summary>
        /// <typeparam name="TService">The service type.</typeparam>
        /// <typeparam name="TImplementation">The concrete implementation type.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Transient<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            return Add(new ServiceContributionItem(typeof(TService), typeof(TImplementation), null, ServiceLifetimeKind.Transient));
        }

        /// <summary>
        ///     Contributes a standalone table-type class: the row type of a table-valued parameter
        ///     that is not an entity.
        /// </summary>
        /// <typeparam name="TRow">The row class.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution TableType<TRow>()
            where TRow : class
        {
            return Add(new TableTypeContributionItem(typeof(TRow)));
        }

        /// <summary>
        ///     Contributes a calendar system. The platform creates one instance of it, a singleton,
        ///     and composes it into the calendar registry by its code.
        /// </summary>
        /// <typeparam name="TCalendar">The calendar system class, with a public parameterless constructor.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Calendar<TCalendar>()
            where TCalendar : class, ICalendarSystem
        {
            return Add(new CalendarContributionItem(typeof(TCalendar)));
        }

        /// <summary>
        ///     Contributes a value set: a named enumeration over string properties. The platform
        ///     composes it into the value-set registry after its own sets; a property draws from it
        ///     through <see cref="ValueSetAttribute" />.
        /// </summary>
        /// <param name="descriptor">The set: its name, its values with their labels, and any aliases.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="descriptor" /> is null.</exception>
        public FeatureContribution ValueSet(ValueSetDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return Add(new ValueSetContributionItem(descriptor));
        }

        /// <summary>
        ///     Registers a notification type, which the feature's code raises. The platform composes it
        ///     into the notification type registry, after the types of the features realised before
        ///     this one; the startup gate refuses a key registered twice or a type that breaks the
        ///     descriptor's rules.
        /// </summary>
        /// <param name="descriptor">The type.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="descriptor" /> is null.</exception>
        public FeatureContribution NotificationType(NotificationTypeDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return Add(new NotificationTypeContributionItem(descriptor));
        }

        /// <summary>
        ///     Registers a notification channel. A channel other than <c>inbox</c> delivers through the
        ///     <see cref="INotificationChannel" /> the feature also registers as a scoped service for it;
        ///     the startup gate refuses a key registered twice or one that breaks the grammar.
        /// </summary>
        /// <param name="descriptor">The channel.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="descriptor" /> is null.</exception>
        public FeatureContribution NotificationChannel(NotificationChannelDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return Add(new NotificationChannelContributionItem(descriptor));
        }

        /// <summary>
        ///     Registers a client event, which the feature's code publishes. Publishing a name no
        ///     feature registered is refused; the startup gate refuses a name registered twice or one
        ///     that breaks the grammar.
        /// </summary>
        /// <param name="name">The event's name, such as <c>inbox.changed</c>.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name" /> is null.</exception>
        public FeatureContribution ClientEvent(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return Add(new ClientEventContributionItem(name));
        }

        /// <summary>
        ///     Registers securables no stack declares: settings categories, the pairs of hand-mapped
        ///     endpoints, aliases of renamed resources and changes to the sensitive set. The platform
        ///     runs <paramref name="configure" /> once, when it composes the securable registry, and
        ///     the securables it adds belong to this feature unless they name another.
        /// </summary>
        /// <param name="configure">Registers the securables.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is null.</exception>
        public FeatureContribution Securables(Action<SecurableRegistryBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            return Add(new SecurablesContributionItem(configure));
        }

        /// <summary>
        ///     Contributes an entity with the platform's default service. The entity joins the tenant
        ///     model; a distribution's leaf replaces it when one is registered for it.
        /// </summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Entity<TEntity>()
            where TEntity : class
        {
            return Add(new StackContributionItem(typeof(TEntity), null, null));
        }

        /// <summary>
        ///     Contributes an entity whose stack runs through <typeparamref name="TService" />. The entity
        ///     joins the tenant model; a distribution's leaf replaces it when one is registered for it.
        /// </summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <typeparam name="TService">The service class of the entity's stack.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Entity<TEntity, TService>()
            where TEntity : class
            where TService : class
        {
            return Add(new StackContributionItem(typeof(TEntity), typeof(TService), null));
        }

        /// <summary>
        ///     Contributes a routed service: a class marked <see cref="ApiRouteAttribute" /> whose
        ///     <see cref="ApiActionAttribute" /> methods are projected under its route segment, each
        ///     reached through the platform's action invoker.
        /// </summary>
        /// <typeparam name="TService">The service class.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution ApiService<TService>()
            where TService : class
        {
            return Add(new ApiServiceContributionItem(typeof(TService)));
        }

        /// <summary>
        ///     Attaches a validator to the stack of <typeparamref name="TEntity" />. A validator generic
        ///     over the entity, registered closed over it, runs closed over the stack's leaf.
        /// </summary>
        /// <typeparam name="TEntity">The entity the validator is written against.</typeparam>
        /// <typeparam name="TValidator">The validator class.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Validator<TEntity, TValidator>()
            where TEntity : class
            where TValidator : class, IEntityValidator<TEntity>
        {
            return Add(new ValidatorContributionItem(typeof(TEntity), typeof(TValidator), ComponentKind.Validator));
        }

        /// <summary>
        ///     Attaches a validator to the stack of <typeparamref name="TEntity" /> by its open generic
        ///     definition of one type parameter, for a validator whose constraints only the stack's leaf
        ///     meets.
        /// </summary>
        /// <typeparam name="TEntity">The entity whose stack the validator joins.</typeparam>
        /// <param name="definition">The open definition, such as <c>typeof(EInvoiceRules&lt;&gt;)</c>.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definition" /> is null.</exception>
        public FeatureContribution Validator<TEntity>(Type definition)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(definition);
            return Add(new ValidatorContributionItem(typeof(TEntity), definition, ComponentKind.Validator));
        }

        /// <summary>
        ///     Attaches a persist effect to the stack of <typeparamref name="TEntity" />. An effect
        ///     generic over the entity, registered closed over it, runs closed over the stack's leaf.
        /// </summary>
        /// <typeparam name="TEntity">The entity the effect is written against.</typeparam>
        /// <typeparam name="TEffect">The effect class.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution PersistEffect<TEntity, TEffect>()
            where TEntity : class
            where TEffect : class, IPersistEffect<TEntity>
        {
            return Add(new ValidatorContributionItem(typeof(TEntity), typeof(TEffect), ComponentKind.PersistEffect));
        }

        /// <summary>Attaches a persist effect to the stack of <typeparamref name="TEntity" /> by its open generic definition of one type parameter.</summary>
        /// <typeparam name="TEntity">The entity whose stack the effect joins.</typeparam>
        /// <param name="definition">The open definition.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definition" /> is null.</exception>
        public FeatureContribution PersistEffect<TEntity>(Type definition)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(definition);
            return Add(new ValidatorContributionItem(typeof(TEntity), definition, ComponentKind.PersistEffect));
        }

        /// <summary>
        ///     Attaches a details contributor to the stack of <typeparamref name="TEntity" />: the class
        ///     that produces the details extras its <see cref="DetailsExtrasAttribute" /> names.
        /// </summary>
        /// <typeparam name="TEntity">The entity the contributor is written against.</typeparam>
        /// <typeparam name="TContributor">The contributor class.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution DetailsContributor<TEntity, TContributor>()
            where TEntity : class
            where TContributor : class, IDetailsContributor<TEntity>
        {
            return Add(new ValidatorContributionItem(typeof(TEntity), typeof(TContributor), ComponentKind.DetailsContributor));
        }

        /// <summary>Attaches a details contributor to the stack of <typeparamref name="TEntity" /> by its open generic definition of one type parameter.</summary>
        /// <typeparam name="TEntity">The entity whose stack the contributor joins.</typeparam>
        /// <param name="definition">The open definition.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definition" /> is null.</exception>
        public FeatureContribution DetailsContributor<TEntity>(Type definition)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(definition);
            return Add(new ValidatorContributionItem(typeof(TEntity), definition, ComponentKind.DetailsContributor));
        }

        /// <summary>
        ///     Attaches a named filter contributor to the stack of <typeparamref name="TEntity" />. The
        ///     platform creates it once, at startup, from the root service provider, so its constructor
        ///     takes singletons only.
        /// </summary>
        /// <typeparam name="TEntity">The entity the contributor is written against.</typeparam>
        /// <typeparam name="TContributor">The contributor class.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution NamedFilters<TEntity, TContributor>()
            where TEntity : class
            where TContributor : class, INamedFilterContributor<TEntity>
        {
            return Add(new ValidatorContributionItem(typeof(TEntity), typeof(TContributor), ComponentKind.NamedFilterContributor));
        }

        /// <summary>Attaches a named filter contributor to the stack of <typeparamref name="TEntity" /> by its open generic definition of one type parameter.</summary>
        /// <typeparam name="TEntity">The entity whose stack the contributor joins.</typeparam>
        /// <param name="definition">The open definition.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definition" /> is null.</exception>
        public FeatureContribution NamedFilters<TEntity>(Type definition)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(definition);
            return Add(new ValidatorContributionItem(typeof(TEntity), definition, ComponentKind.NamedFilterContributor));
        }

        /// <summary>
        ///     Attaches a companion to the stack of <typeparamref name="TEntity" />: a class generic over
        ///     the entity whose <see cref="ApiActionAttribute" /> methods are projected under the stack's
        ///     segment and resource. The platform closes it over the stack's leaf.
        /// </summary>
        /// <typeparam name="TEntity">The entity whose stack the companion joins.</typeparam>
        /// <typeparam name="TCompanion">The companion class, closed over <typeparamref name="TEntity" />.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution EntityCompanion<TEntity, TCompanion>()
            where TEntity : class
            where TCompanion : class
        {
            return Add(new StackCompanionContributionItem(typeof(TEntity), typeof(TCompanion)));
        }

        /// <summary>
        ///     Declares a confirmation key: the key of a warning a caller must confirm before an
        ///     operation proceeds. Each key is declared once in the composition, and a neutral string
        ///     resource of a composed feature must render it.
        /// </summary>
        /// <param name="key">The key, dotted PascalCase, such as <c>Inventory.BackdatedRecost</c>.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key" /> is null.</exception>
        public FeatureContribution Confirmation(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return Add(new ConfirmationContributionItem(key));
        }

        /// <summary>
        ///     Contributes a tenant provisioning step. The migrator runs it, in its order, on every
        ///     tenant database whose recorded version of it is behind. Contributing the same class twice,
        ///     from this feature or another, is a composition problem, and a second step with the same
        ///     name anywhere in the composition is a startup problem.
        /// </summary>
        /// <typeparam name="TStep">The step class, created from the tenant scope it runs in.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution ProvisioningStep<TStep>()
            where TStep : class, ITenantProvisioningStep
        {
            return Add(new ProvisioningStepContributionItem(typeof(TStep)));
        }

        /// <summary>
        ///     Requires the leaf registered for <typeparamref name="TEntity" /> to implement the
        ///     interface <typeparamref name="TShape" />. A leaf without it fails startup with the
        ///     interface's members listed as the remedy.
        /// </summary>
        /// <typeparam name="TEntity">The entity another feature owns.</typeparam>
        /// <typeparam name="TShape">The interface its leaf must implement.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution RequiresShape<TEntity, TShape>()
            where TEntity : class
            where TShape : class
        {
            return Add(new ShapeRequirementContributionItem(typeof(TEntity), typeof(TShape)));
        }

        /// <summary>
        ///     Registers a background job handler: a class carrying <see cref="Jobs.JobHandlerAttribute" />
        ///     that implements exactly one of <see cref="Jobs.IJobHandler" /> and
        ///     <see cref="Jobs.IEntityJobHandler{TEntity}" />. The platform resolves it from each job's
        ///     tenant scope; the composition gate refuses a malformed or duplicated key, an option out of
        ///     range, and a key under the platform's <c>core.</c> prefix from any feature but the platform's.
        /// </summary>
        /// <typeparam name="THandler">The handler class.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution JobHandler<THandler>()
            where THandler : class
        {
            return Add(new JobHandlerContributionItem(typeof(THandler)));
        }

        /// <summary>
        ///     Declares a built-in schedule: a row of <c>core.Schedules</c> in the reserved id band that
        ///     the migrations seed, running as the system user, with its code and name equal to the
        ///     handler key. The id and the key are stable for the life of the distribution; a changed
        ///     default is a new key.
        /// </summary>
        /// <param name="id">
        ///     The row's id, from 1 to <see cref="WellKnownIds.ReservedIdBandEnd" />: 1–99 are the
        ///     platform's, 100–499 the module packs', 500–999 the distribution's.
        /// </param>
        /// <param name="handlerKey">The key of the handler whose jobs the schedule enqueues.</param>
        /// <param name="cron">The five-field Unix cron expression.</param>
        /// <param name="arguments">The arguments each job receives, serialised as JSON; null for none.</param>
        /// <param name="timeZoneId">A zone of the platform's time-zone set; null for the tenant's zone at fire time.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handlerKey" /> or <paramref name="cron" /> is null.</exception>
        public FeatureContribution BuiltInSchedule(int id, string handlerKey, string cron, object? arguments = null, string? timeZoneId = null)
        {
            ArgumentNullException.ThrowIfNull(handlerKey);
            ArgumentNullException.ThrowIfNull(cron);
            return Add(new BuiltInScheduleContributionItem(id, handlerKey, cron, arguments, timeZoneId));
        }

        /// <summary>
        ///     Retires a job handler key the feature removed or replaced. Jobs of the key are cancelled
        ///     and schedules naming it pause; the composition gate refuses a key some handler still
        ///     registers or a built-in schedule still names.
        /// </summary>
        /// <param name="key">The retired handler key.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key" /> is null.</exception>
        public FeatureContribution RetiredJobKey(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return Add(new RetiredJobKeyContributionItem(key));
        }
    }
}
