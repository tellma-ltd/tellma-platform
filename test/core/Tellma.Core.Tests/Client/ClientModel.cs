// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Client
{
    // The client entity model suite's composition: an Acme-like model of two features. acme holds a
    // customer stack with every member kind (a multilingual name, an enum with a wire name, a
    // decimal, a reference, a value set, a long, a JSON column, a blob, a defaulted column, a child
    // collection and a sibling) and actions of both kinds, and a cacheable currency without
    // details; gl holds a tree of centers whose manager is a core user and that
    // carries acme's customer type.

    /// <summary>The names and the composition of the client entity model's test model.</summary>
    internal static class ClientModel
    {
        /// <summary>The customer stack.</summary>
        public const string Customers = "acme.Customer";

        /// <summary>The customer's child entity.</summary>
        public const string Contacts = "acme.CustomerContact";

        /// <summary>The customer's sibling.</summary>
        public const string Extras = "acme.CustomerExtra";

        /// <summary>The cacheable currency stack.</summary>
        public const string Currencies = "acme.Currency";

        /// <summary>The tree of centers.</summary>
        public const string Centers = "gl.Center";

        /// <summary>The customer tiers: one keyed value whose key is declared, one whose key is not, and a literal.</summary>
        public const string Tiers = "acme.Tier";

        /// <summary>The languages the composition offers.</summary>
        public static readonly string[] Languages = ["en", "ar"];

        /// <summary>Composes the model with the platform.</summary>
        /// <param name="configuration">The configuration; the test composition's sound one when null.</param>
        /// <param name="glFirst">Whether the gl feature is added before the acme feature, rather than after it.</param>
        /// <returns>The services.</returns>
        public static IServiceCollection Compose(IReadOnlyDictionary<string, string?>? configuration = null, bool glFirst = false)
        {
            TestFeature<Tag.A> acme = new("acme", contribute: static contribution => contribution
                .Entity<Customer, CustomerService<Customer>>()
                .Entity<CustomerExtra>()
                .Entity<Currency>()
                .ValueSet(new ValueSetDescriptor(
                    Tiers,
                    [new ValueSetEntry("gold", "Acme_Tier_Gold", null), new ValueSetEntry("silver", "Acme_Tier_Silver", null), new ValueSetEntry("VIP", null, "VIP")])));
            TestFeature<Tag.B> gl = new("gl", contribute: static contribution => contribution.Entity<Center>());

            return TestComposition.Compose(
                tellma => _ = glFirst
                    ? tellma.Languages(Languages).AddFeature(gl).AddFeature(acme)
                    : tellma.Languages(Languages).AddFeature(acme).AddFeature(gl),
                configuration: TestComposition.Configuration(configuration));
        }
    }

    /// <summary>A customer's kind; the wholesale member travels by a name of its own.</summary>
    public enum CustomerType
    {
        /// <summary>A retail customer.</summary>
        Retail,

        /// <summary>A wholesale customer.</summary>
        [JsonStringEnumMemberName("wholesale-trade")]
        Wholesale,
    }

    /// <summary>The urgency of a credit-limit change, which only an action's arguments carry.</summary>
    public enum Priority
    {
        /// <summary>Applied in the next review.</summary>
        Low,

        /// <summary>Applied now.</summary>
        High,
    }

    /// <summary>A center's kind.</summary>
    public enum CenterType
    {
        /// <summary>A grouping of other centers.</summary>
        Abstract,

        /// <summary>A service center.</summary>
        Service,

        /// <summary>A sales center.</summary>
        Sale,
    }

    /// <summary>A customer: every member kind the client entity model describes.</summary>
    [Table("Customers", Schema = "acme")]
    [TableType]
    [DisplayNameSource(nameof(Name), Prefix = nameof(Code))]
    public class Customer : TopLevelEntity, IActivatable
    {
        /// <summary>The customer's code.</summary>
        [Column(TypeName = "varchar(50)")]
        [Unique]
        public string Code { get; set; } = null!;

        /// <summary>The name in the first content language.</summary>
        [MaxLength(255)]
        [Multilingual]
        [Searchable]
        public string Name { get; set; } = null!;

        /// <summary>The name in the second content language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the third content language.</summary>
        public string? Name3 { get; set; }

        /// <summary>The customer's kind.</summary>
        [MaxLength(16)]
        public CustomerType CustomerType { get; set; }

        /// <summary>The credit limit.</summary>
        [Column(TypeName = "decimal(19,4)")]
        public decimal CreditLimit { get; set; }

        /// <summary>The currency the limit is in.</summary>
        [References(typeof(Currency))]
        public int? CurrencyId { get; set; }

        /// <summary>The grammatical gender of the customer's contact: an enum of the platform's own assembly.</summary>
        public Gender? ContactGender { get; set; }

        /// <summary>The customer's time zone.</summary>
        [MaxLength(64)]
        [ValueSet(CoreValueSets.TimeZone)]
        public string? TimeZone { get; set; }

        /// <summary>The customer's tier.</summary>
        [MaxLength(16)]
        [ValueSet(ClientModel.Tiers)]
        public string? Tier { get; set; }

        /// <summary>The customer's rank, which the service fills when a save sends none.</summary>
        [MaxLength(16)]
        [Defaulted]
        public string Rank { get; set; } = null!;

        /// <summary>A reference in another system, too wide for a JavaScript number.</summary>
        [ExcludeFromExcel]
        public long? ExternalReference { get; set; }

        /// <summary>Free-form notes, stored as JSON.</summary>
        [JsonColumn]
        public string? Notes { get; set; }

        /// <summary>The customer's picture.</summary>
        [BlobReference("customer-image", BlobPreset.Avatar)]
        public int? ImageId { get; set; }

        /// <inheritdoc />
        public bool IsActive { get; set; } = true;

        /// <summary>The customer's contacts.</summary>
        [NotMapped]
        public List<CustomerContact>? Contacts { get; set; }
    }

    /// <summary>A contact of a customer.</summary>
    [Table("CustomerContacts", Schema = "acme")]
    [TableType]
    public class CustomerContact : ChildEntity
    {
        /// <summary>The customer.</summary>
        [ParentKey]
        public int CustomerId { get; set; }

        /// <summary>The contact's name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>The contact's email address.</summary>
        [MaxLength(255)]
        public string? Email { get; set; }
    }

    /// <summary>A customer's one-to-one extra row.</summary>
    [Table("CustomerExtras", Schema = "acme")]
    [Sibling(typeof(Customer), "Extra")]
    public class CustomerExtra : Entity<int>
    {
        /// <summary>A note.</summary>
        [MaxLength(255)]
        public string? Note { get; set; }
    }

    /// <summary>A currency: cacheable, queried and exported, with no details read.</summary>
    [Table("Currencies", Schema = "acme")]
    [TableType]
    [Cacheable(MaxRows = 200)]
    [Stack(Operations = StackOperations.Query | StackOperations.Export)]
    public class Currency : TopLevelEntity, IActivatable
    {
        /// <summary>The currency's ISO code, which search text matches from its start.</summary>
        [Column(TypeName = "varchar(3)")]
        [Unique]
        [Searchable(SearchKind.Prefix)]
        public string Code { get; set; } = null!;

        /// <summary>The currency's name.</summary>
        [MaxLength(255)]
        [Searchable]
        public string Name { get; set; } = null!;

        /// <summary>The digits after the point.</summary>
        public short DecimalPlaces { get; set; }

        /// <inheritdoc />
        public bool IsActive { get; set; } = true;
    }

    /// <summary>A center: a tree whose manager is a user.</summary>
    [Table("Centers", Schema = "gl")]
    [Tree(MaxDepth = 8)]
    [TableType]
    public class Center : ActivatableTreeEntity
    {
        /// <summary>The center's code.</summary>
        [Column(TypeName = "varchar(50)")]
        [Unique]
        public string Code { get; set; } = null!;

        /// <summary>The center's name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>The center's kind.</summary>
        [MaxLength(16)]
        public CenterType CenterType { get; set; }

        /// <summary>The kind of customer the center serves: an enum another schema names.</summary>
        [MaxLength(16)]
        public CustomerType? ServedCustomerType { get; set; }

        /// <summary>The user who manages the center.</summary>
        [References(typeof(User))]
        public int? ManagerId { get; set; }
    }

    /// <summary>The arguments of the set-credit-limit action.</summary>
    /// <param name="CreditLimit">The new limit.</param>
    /// <param name="AssigneeId">The user who reviews the change.</param>
    /// <param name="Priority">How soon the change applies.</param>
    /// <param name="Comment">Why the limit changes.</param>
    /// <param name="Reference">A reference in another system.</param>
    /// <param name="EffectiveAt">When the change applies.</param>
    /// <param name="Reviewers">The users who are told.</param>
    /// <param name="Tags">Free-form tags.</param>
    public sealed record SetCreditLimitArguments(
        decimal CreditLimit,
        [property: EntityRef("core.User")] int? AssigneeId,
        Priority Priority,
        [property: MaxLength(200)] string? Comment,
        long? Reference,
        DateTimeOffset? EffectiveAt,
        [property: EntityRef("core.User")] IReadOnlyList<int>? Reviewers,
        IReadOnlyList<string>? Tags);

    /// <summary>The body of the statement action: a nullable member of each kind a body must carry.</summary>
    /// <param name="Language">The statement's language, required by its annotation.</param>
    public sealed record StatementRequest([property: Required] string? Language)
    {
        /// <summary>The statement's layout, which a body must carry because the member is required.</summary>
        public required string? Layout { get; init; }

        /// <summary>The statement's channel, which a body must carry because the JSON rules require it.</summary>
        [JsonRequired]
        public string? Channel { get; init; }
    }

    /// <summary>The customer stack's service: one entity action with arguments, one API action with a body, one without a result.</summary>
    /// <typeparam name="TCustomer">The customer leaf.</typeparam>
    /// <param name="factory">The pipeline factory.</param>
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The signature is what the fixture declares; the platform reads it, never runs it.")]
    public class CustomerService<TCustomer>(IEntityPipelineFactory factory) : EntityService<TCustomer>(factory)
        where TCustomer : Customer
    {
        /// <summary>Sets the customers' credit limit.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task.</returns>
        [EntityAction("set-credit-limit", AllowWhenReadOnly = true)]
        public Task SetCreditLimitAsync(ActionContext<TCustomer, int> context, SetCreditLimitArguments arguments)
        {
            return Task.CompletedTask;
        }

        /// <summary>Renders a statement.</summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The statement.</returns>
        [ApiAction("statement", Mutation = false, Idempotent = true, Action = "Read")]
        public Task<string> StatementAsync(StatementRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(request.Language ?? string.Empty);
        }

        /// <summary>Answers whether the service is up; any member may call it.</summary>
        /// <returns>The answer.</returns>
        [ApiAction("ping", Mutation = false, MemberOnly = true)]
        public Task<bool> PingAsync()
        {
            return Task.FromResult(true);
        }

        /// <summary>Recomputes every customer's rank.</summary>
        /// <returns>A task.</returns>
        [ApiAction("ranks/recompute", Destructive = true, Action = "Update")]
        public Task RecomputeRanksAsync()
        {
            return Task.CompletedTask;
        }
    }
}
