// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Tests.Crud
{
    /// <summary>
    ///     The descriptor, option and name contracts of the CRUD pipeline: their defaults, their
    ///     validation, and the kinds a module cannot add to.
    /// </summary>
    public class CrudContractTests
    {
        [Fact]
        public void The_default_limits_are_the_ceilings_an_undeclared_stack_runs_under()
        {
            StackAttribute stack = new();
            StackLimits limits = StackLimits.Default;

            Assert.Equal(
                (stack.MaxTake, stack.CountCap, stack.MaxSkipWindow, stack.MaxSaveCount, stack.MaxRowsPerSave, stack.MaxIds,
                    stack.MaxDeleteByQueryRows, stack.MaxExpandDepth, stack.MaxValidationRounds, stack.MaxSearchLength, stack.MaxExtras),
                (limits.MaxTake, limits.CountCap, limits.MaxSkipWindow, limits.MaxSaveCount, limits.MaxRowsPerSave, limits.MaxIds,
                    limits.MaxDeleteByQueryRows, limits.MaxExpandDepth, limits.MaxValidationRounds, limits.MaxSearchLength, limits.MaxExtras));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void A_ceiling_is_positive_whether_constructed_or_copied(int ceiling)
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new StackLimits(ceiling, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new StackLimits(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, ceiling));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => StackLimits.Default with { MaxExpandDepth = ceiling });

            Assert.Equal(5, (StackLimits.Default with { MaxExpandDepth = 5 }).MaxExpandDepth);
        }

        [Theory]
        [InlineData(typeof(ActionDescriptor))]
        [InlineData(typeof(ExportSource))]
        [InlineData(typeof(PersistContext<Widget>))]
        [InlineData(typeof(PersistOutcome<Widget>))]
        [InlineData(typeof(DeletePersistOutcome<Widget>))]
        [InlineData(typeof(ActionPersistOutcome<Widget>))]
        [InlineData(typeof(DetailsPlan<Widget>))]
        [InlineData(typeof(DeleteContext<Widget>))]
        [InlineData(typeof(ActionContext<Widget>))]
        public void A_closed_kind_offers_another_assembly_no_constructor_but_a_record_copy(Type type)
        {
            // A record's copy constructor must stay protected; it needs an existing instance, so it
            // adds no new way to make one.
            IEnumerable<ConstructorInfo> reachable = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(static constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly)
                .Where(constructor => !(constructor.GetParameters() is [{ } only] && only.ParameterType == type));

            Assert.Empty(reachable);
        }

        [Fact]
        public void An_action_descriptor_is_named_and_has_a_result_type()
        {
            _ = Assert.Throws<ArgumentException>(() => EntityAction(" ", typeof(void)));
            _ = Assert.Throws<ArgumentNullException>(() => EntityAction("archive", null!));

            EntityActionDescriptor action = EntityAction("archive", typeof(void));
            Assert.Equal(("archive", typeof(void), "archive"), (action.Name, action.ResultType, action.Action));
            Assert.IsAssignableFrom<ActionDescriptor>(action);
        }

        [Fact]
        public void A_details_request_copies_what_it_includes_and_the_shared_none_includes_nothing()
        {
            List<string> include = ["Lines"];
            DetailsRequest request = new() { Include = include };
            include.Add("Attachments");

            Assert.Equal(["Lines"], request.Include);
            Assert.Empty(DetailsRequest.None.Include);
            Assert.IsNotType<List<string>>(request.Include);
            _ = Assert.Throws<ArgumentNullException>(() => new DetailsRequest { Include = null! });
        }

        [Fact]
        public void Save_options_default_to_a_checked_web_save_that_returns_its_rows()
        {
            SaveOptions options = new();

            Assert.True(options.ReturnEntities);
            Assert.Same(DetailsRequest.None, options.Details);
            Assert.Equal((ConcurrencyMode.Check, SaveSource.Web), (options.Concurrency, options.Source));
            Assert.Null(options.OnPersist);
            _ = Assert.Throws<ArgumentNullException>(() => options.Details = null!);
            Assert.Equal([0, 1, 2, 3, 4], new[] { SaveSource.Web, SaveSource.PublicApi, SaveSource.Agent, SaveSource.Import, SaveSource.System }.Select(static source => (int)source));
        }

        [Fact]
        public void Action_options_return_nothing_by_default()
        {
            ActionOptions options = new();

            Assert.False(options.ReturnEntities);
            Assert.Null(options.Details);
        }

        [Fact]
        public void An_enlisted_save_runs_after_the_host_and_an_enlisted_delete_before_it()
        {
            EnlistSaveOptions save = new();
            EnlistDeleteOptions delete = new();

            Assert.Equal((EnlistOrder.AfterHost, ConcurrencyMode.Override), (save.Order, save.Concurrency));
            Assert.Empty(save.ServerOwned);
            Assert.Null(save.MapPath);
            Assert.Null(save.Source);
            _ = Assert.Throws<ArgumentNullException>(() => save.ServerOwned = null!);
            Assert.Equal(EnlistOrder.BeforeHost, delete.Order);
            Assert.Null(delete.MapPath);
            Assert.Null(delete.Source);
        }

        [Fact]
        public void The_telemetry_names_are_unique_and_scoped_to_the_pipeline()
        {
            string[] names = [.. typeof(CrudTelemetryNames).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(static field => field.IsLiteral)
                .Select(static field => (string)field.GetRawConstantValue()!)];

            Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal("Tellma.Core", CrudTelemetryNames.MeterName);
            Assert.All(
                names.Where(static name => name != CrudTelemetryNames.MeterName),
                static name => Assert.True(
                    name.StartsWith("tellma.crud.", StringComparison.Ordinal) || name.StartsWith("crud.", StringComparison.Ordinal),
                    name));
        }

        [Fact]
        public void A_record_permission_names_its_row_and_what_it_refuses()
        {
            RecordPermissions<int> permissions = new(7, new Dictionary<string, string> { ["Update"] = "Posted" });

            Assert.Equal(7, permissions.Id);
            Assert.Equal("Posted", permissions.Refused["Update"]);
            _ = Assert.Throws<ArgumentNullException>(() => new RecordPermissions<int>(7, null!));
        }

        /// <summary>An entity action descriptor with every flag off.</summary>
        /// <param name="name">The action's name.</param>
        /// <param name="resultType">The result type.</param>
        /// <returns>The descriptor.</returns>
        private static EntityActionDescriptor EntityAction(string name, Type resultType)
        {
            return new EntityActionDescriptor(
                name, null, Mutation: true, AllowWhenReadOnly: false, Idempotent: false, Long: false, Destructive: false, Mcp: default,
                ContractRevision: 1, resultType, "archive", SupportsFilter: false, LoadChildren: false, PostCheck: false,
                SingleTarget: false, IsBuiltIn: false, ArgumentsType: null);
        }
    }
}
