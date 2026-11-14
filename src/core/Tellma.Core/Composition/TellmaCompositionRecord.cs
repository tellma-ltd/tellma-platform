// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Jobs;
using Tellma.Core.Localization;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     The realised composition: the slug, the features in realisation order, the leaf
    ///     substitutions and what the realizers recorded. Registered as a singleton, so the startup
    ///     checks and the platform's own services (the data context's model, the client metadata
    ///     emit) read the composition instead of re-running it.
    /// </summary>
    /// <param name="slug">The distribution slug.</param>
    /// <param name="configuration">The configuration the composition was built from.</param>
    /// <param name="environment">The host environment the composition was built for.</param>
    internal sealed class TellmaCompositionRecord(string slug, IConfiguration configuration, IHostEnvironment environment)
    {
        private Dictionary<Type, Type> _leaves = [];

        /// <summary>The distribution slug.</summary>
        public string Slug { get; } = slug;

        /// <summary>The configuration the composition was built from.</summary>
        public IConfiguration Configuration { get; } = configuration;

        /// <summary>The host environment the composition was built for.</summary>
        public IHostEnvironment Environment { get; } = environment;

        /// <summary>Every feature, in realisation order: <c>core</c> first, each feature after those it requires.</summary>
        public IReadOnlyList<ITellmaFeature> Features { get; private set; } = [];

        /// <summary>The leaf substitutions: each replaced default entity type and the leaf that replaces it.</summary>
        public IReadOnlyDictionary<Type, Type> Leaves => _leaves;

        /// <summary>The entities the features contributed, in realisation order, before leaf substitution.</summary>
        public List<ContributedEntity> Entities { get; } = [];

        /// <summary>The EF model configurations Core's features contributed, in realisation order.</summary>
        public List<ContributedType> ModelConfigurations { get; } = [];

        /// <summary>The standalone table-type row classes the features contributed, in realisation order.</summary>
        public List<ContributedType> TableTypes { get; } = [];

        /// <summary>The calendar system classes the features contributed, in realisation order: the platform's three first.</summary>
        public List<ContributedType> Calendars { get; } = [];

        /// <summary>The value sets the features contributed, in realisation order; the platform's own four are not among them.</summary>
        public List<ContributedValueSet> ValueSets { get; } = [];

        /// <summary>
        ///     The tenant provisioning steps the composition runs, in realisation order: each class a
        ///     feature contributed, once, and every instance a realiser adds, so one class may run as
        ///     many steps (one per report seed, say). The runner keys each by the name its instance
        ///     reports.
        /// </summary>
        public List<ProvisioningStepRegistration> ProvisioningSteps { get; } = [];

        /// <summary>The notification types the features contributed, in realisation order.</summary>
        public List<ContributedNotificationType> NotificationTypes { get; } = [];

        /// <summary>The notification channels the features contributed, in realisation order: the platform's two first.</summary>
        public List<ContributedNotificationChannel> NotificationChannels { get; } = [];

        /// <summary>The client event names the features contributed, in realisation order.</summary>
        public List<ContributedClientEvent> ClientEvents { get; } = [];

        /// <summary>The background job handlers the features registered, in realisation order, each read from its attribute.</summary>
        public List<ContributedJobHandler> JobHandlers { get; } = [];

        /// <summary>The built-in schedules the features declared, in realisation order.</summary>
        public List<ContributedBuiltInSchedule> BuiltInSchedules { get; } = [];

        /// <summary>The job handler keys the features retired, in realisation order.</summary>
        public List<ContributedRetiredJobKey> RetiredJobKeys { get; } = [];

        /// <summary>
        ///     The services the features registered, in realisation order, including those a dependent
        ///     feature's registration later replaced; the startup gate names a service's feature from it.
        /// </summary>
        public List<ContributedService> Services { get; } = [];

        /// <summary>What the distribution declared about languages: the offered codes and the added languages.</summary>
        public LanguageDeclaration Languages { get; private set; } = LanguageDeclaration.None;

        /// <summary>The entity shapes features require of other features' entities, in realisation order.</summary>
        public List<ShapeRequirement> ShapeRequirements { get; } = [];

        /// <summary>
        ///     The platform's built-in startup checks, also registered as services. The startup gate
        ///     runs these alone when a contributed check cannot be created.
        /// </summary>
        public List<IStartupCheck> BuiltInChecks { get; } = [];

        /// <summary>Every options type bound by the composition, with the validation the startup gate runs.</summary>
        public List<BoundOptions> BoundOptions { get; } = [];

        /// <summary>The leaf registered for <paramref name="entityType" />: its substitute, else the type itself.</summary>
        /// <param name="entityType">The entity type.</param>
        /// <returns>The leaf.</returns>
        public Type LeafOf(Type entityType)
        {
            return _leaves.TryGetValue(entityType, out Type? leaf) ? leaf : entityType;
        }

        /// <summary>Fixes the feature order, the leaf substitutions and the language declaration once the graph gate passes.</summary>
        /// <param name="features">The features in realisation order.</param>
        /// <param name="leaves">The leaf substitutions.</param>
        /// <param name="languages">What the distribution declared about languages.</param>
        public void Settle(IReadOnlyList<ITellmaFeature> features, IReadOnlyDictionary<Type, Type> leaves, LanguageDeclaration languages)
        {
            Features = [.. features];
            _leaves = new Dictionary<Type, Type>(leaves);
            Languages = languages;
        }
    }

    /// <summary>One provisioning step the composition runs.</summary>
    /// <param name="StepType">The step's class, closed over the registered leaves.</param>
    /// <param name="Owner">The feature the step belongs to.</param>
    /// <param name="Create">
    ///     Creates the step from a scope's services: the tenant scope it runs in, or a scope outside any
    ///     tenant where only its name, order and version are read.
    /// </param>
    internal sealed record ProvisioningStepRegistration(Type StepType, ITellmaFeature Owner, Func<IServiceProvider, ITenantProvisioningStep> Create);

    /// <summary>A type a feature contributed.</summary>
    /// <param name="Type">The contributed type.</param>
    /// <param name="Owner">The feature that contributed it.</param>
    internal sealed record ContributedType(Type Type, ITellmaFeature Owner);

    /// <summary>A value set a feature contributed.</summary>
    /// <param name="Descriptor">The set as contributed.</param>
    /// <param name="Owner">The feature that contributed it.</param>
    internal sealed record ContributedValueSet(ValueSetDescriptor Descriptor, ITellmaFeature Owner);

    /// <summary>A notification type a feature contributed.</summary>
    /// <param name="Descriptor">The type as contributed.</param>
    /// <param name="Owner">The feature that contributed it.</param>
    internal sealed record ContributedNotificationType(NotificationTypeDescriptor Descriptor, ITellmaFeature Owner);

    /// <summary>A notification channel a feature contributed.</summary>
    /// <param name="Descriptor">The channel as contributed.</param>
    /// <param name="Owner">The feature that contributed it.</param>
    internal sealed record ContributedNotificationChannel(NotificationChannelDescriptor Descriptor, ITellmaFeature Owner);

    /// <summary>A client event a feature contributed.</summary>
    /// <param name="Name">The event's name as contributed.</param>
    /// <param name="Owner">The feature that contributed it.</param>
    internal sealed record ContributedClientEvent(string Name, ITellmaFeature Owner);

    /// <summary>A background job handler a feature registered, as its attribute and interface declare it.</summary>
    /// <param name="HandlerType">The handler class.</param>
    /// <param name="Attribute">The class's handler attribute: the key and the options.</param>
    /// <param name="EntityType">
    ///     The entity an entity-backed handler processes, as its interface names it (before leaf
    ///     substitution); null for an arguments-only handler.
    /// </param>
    /// <param name="Owner">The feature that registered it.</param>
    internal sealed record ContributedJobHandler(Type HandlerType, JobHandlerAttribute Attribute, Type? EntityType, ITellmaFeature Owner);

    /// <summary>A built-in schedule a feature declared.</summary>
    /// <param name="Seed">The schedule, its arguments serialised, as the migrations seed it.</param>
    /// <param name="Owner">The feature that declared it.</param>
    internal sealed record ContributedBuiltInSchedule(BuiltInScheduleSeed Seed, ITellmaFeature Owner);

    /// <summary>A job handler key a feature retired.</summary>
    /// <param name="Key">The key.</param>
    /// <param name="Owner">The feature that retired it.</param>
    internal sealed record ContributedRetiredJobKey(string Key, ITellmaFeature Owner);

    /// <summary>A service a feature registered.</summary>
    /// <param name="ServiceType">The service type.</param>
    /// <param name="ImplementationType">The implementation type, or <see langword="null" /> for a factory registration.</param>
    /// <param name="Owner">The feature that registered it.</param>
    internal sealed record ContributedService(Type ServiceType, Type? ImplementationType, ITellmaFeature Owner);

    /// <summary>An entity a feature contributed, with its stack's service and operations.</summary>
    /// <param name="EntityType">The entity, before leaf substitution.</param>
    /// <param name="ServiceType">The service class of its stack, or <see langword="null" /> for the default.</param>
    /// <param name="Operations">The operations overriding the entity's declaration, or <see langword="null" />.</param>
    /// <param name="Owner">The feature that contributed it.</param>
    internal sealed record ContributedEntity(Type EntityType, Type? ServiceType, StackOperations? Operations, ITellmaFeature Owner);

    /// <summary>A shape a feature requires of another feature's entity.</summary>
    /// <param name="EntityType">The entity whose leaf must carry the shape.</param>
    /// <param name="ShapeType">The interface the leaf must implement.</param>
    /// <param name="Owner">The feature that requires it.</param>
    internal sealed record ShapeRequirement(Type EntityType, Type ShapeType, ITellmaFeature Owner);

    /// <summary>One bound options type and how to validate it.</summary>
    /// <param name="OptionsType">The options type.</param>
    /// <param name="ConfigurationPath">The configuration path it binds from.</param>
    /// <param name="Owner">The name of the feature that bound it, or <c>core</c> for the platform's own.</param>
    /// <param name="Validate">Resolves the options from a provider and returns each validation failure; empty when valid.</param>
    internal sealed record BoundOptions(
        Type OptionsType,
        string ConfigurationPath,
        string Owner,
        Func<IServiceProvider, IReadOnlyList<string>> Validate);
}
