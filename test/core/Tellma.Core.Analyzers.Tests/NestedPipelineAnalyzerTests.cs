// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Analyzers.Tests.Infrastructure;

namespace Tellma.Core.Analyzers.Tests
{
    /// <summary>TELLMA0005: a pipeline participant never runs a nested pipeline.</summary>
    public class NestedPipelineAnalyzerTests
    {
        /// <summary>
        ///     Two entities and the service of the second, which participants elsewhere call. The second
        ///     is a tree that can be deactivated, so its service takes every front-door write.
        /// </summary>
        private const string Model = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Tellma.Core.Abstractions.Api;
            using Tellma.Core.Abstractions.Crud;
            using Tellma.Core.Abstractions.Entities;
            using Tellma.Core.Abstractions.Validation;

            public class Center : TopLevelEntity;

            public class Unit : TreeEntity, IActivatable
            {
                public bool IsActive { get; set; }
            }

            public class UnitService(IEntityPipelineFactory factory) : EntityService<Unit>(factory);

            """;

        /// <summary>Each front-door write called from a hook override is refused, lambdas included.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Front_door_writes_from_a_hook_override_are_refused()
        {
            return AnalyzerHarness.VerifyAsync<NestedPipelineAnalyzer>(Model + """
                public class CenterService(IEntityPipelineFactory factory, UnitService units) : EntityService<Center>(factory)
                {
                    protected override async Task ValidateAsync(SaveContext<Center> context)
                    {
                        _ = await {|TELLMA0005:units.SaveAsync([], new SaveOptions())|};
                        _ = await {|TELLMA0005:units.DeleteByIdsAsync([1])|};
                        _ = await {|TELLMA0005:units.DeleteByQueryAsync(new DeleteByQueryRequest("Code eq 'x'", null, 1))|};
                        _ = await {|TELLMA0005:units.ExecuteActionAsync("close", [1], null, new ActionOptions())|};
                        _ = await {|TELLMA0005:units.ExecuteActionAsync<int>("count", [1], null, new ActionOptions())|};
                        _ = await {|TELLMA0005:units.ActivateAsync([1])|};
                        _ = await {|TELLMA0005:units.DeactivateAsync([1])|};
                        _ = await {|TELLMA0005:units.DeleteWithDescendantsAsync([1])|};
                        System.Func<Task> later = () => {|TELLMA0005:units.SaveAsync([], new SaveOptions())|};
                        _ = await units.GetByIdsAsync([1], DetailsRequest.None);
                    }
                }
                """);
        }

        /// <summary>An entity action, a validator and a persist effect are participants too.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Actions_validators_and_effects_are_participants()
        {
            return AnalyzerHarness.VerifyAsync<NestedPipelineAnalyzer>(Model + """
                public class CenterService(IEntityPipelineFactory factory, UnitService units) : EntityService<Center>(factory)
                {
                    [EntityAction("close")]
                    public Task CloseAsync(int[] ids) => {|TELLMA0005:units.SaveAsync([], new SaveOptions())|};
                }

                public class CenterRules(UnitService units) : IEntityValidator<Center>
                {
                    public Task ValidateAsync(SaveContext<Center> context) => {|TELLMA0005:units.SaveAsync([], new SaveOptions())|};

                    private Task Helper() => {|TELLMA0005:units.DeleteByIdsAsync([1])|};
                }

                public class CenterEffect(UnitService units) : IPersistEffect<Center>
                {
                    public Task ContributeAsync(PersistContext<Center> context) => {|TELLMA0005:units.ActivateAsync([1])|};
                }
                """);
        }

        /// <summary>Code outside any pipeline participant runs the front door freely.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Front_door_writes_outside_a_participant_pass()
        {
            return AnalyzerHarness.VerifyAsync<NestedPipelineAnalyzer>(Model + """
                public class CenterService(IEntityPipelineFactory factory, UnitService units) : EntityService<Center>(factory)
                {
                    // A method of the service that is neither a hook nor an action is the front door's caller.
                    public Task ImportAsync() => units.SaveAsync([], new SaveOptions());
                }

                public class ImportJob(UnitService units)
                {
                    public Task RunAsync() => units.SaveAsync([], new SaveOptions());
                }
                """);
        }
    }
}
