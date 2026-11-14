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
        /// <summary>Two entities and the service of the second, which participants elsewhere call.</summary>
        private const string Model = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Tellma.Core.Abstractions.Crud;
            using Tellma.Core.Abstractions.Entities;

            public class Center : TopLevelEntity;

            public class Unit : TopLevelEntity;

            public class UnitService : EntityService<Unit>;

            """;

        /// <summary>Each front-door write called from a hook override is refused, lambdas included.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Front_door_writes_from_a_hook_override_are_refused()
        {
            return AnalyzerHarness.VerifyAsync<NestedPipelineAnalyzer>(Model + """
                public class CenterService(UnitService units) : EntityService<Center>
                {
                    protected override async Task ValidateAsync(IReadOnlyList<Center> entities)
                    {
                        await {|TELLMA0005:units.SaveAsync([])|};
                        await {|TELLMA0005:units.DeleteByIdsAsync([1])|};
                        await {|TELLMA0005:units.DeleteByQueryAsync("Code eq 'x'")|};
                        await {|TELLMA0005:units.ExecuteActionAsync("close", [1])|};
                        _ = await {|TELLMA0005:units.ExecuteActionAsync<int>("count", [1])|};
                        await {|TELLMA0005:units.ActivateAsync([1])|};
                        await {|TELLMA0005:units.DeactivateAsync([1])|};
                        await {|TELLMA0005:units.DeleteWithDescendantsAsync([1])|};
                        System.Func<Task> later = () => {|TELLMA0005:units.SaveAsync([])|};
                        _ = await units.GetByIdsAsync([1]);
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
                public class CenterService(UnitService units) : EntityService<Center>
                {
                    [EntityAction("close")]
                    public Task CloseAsync(int[] ids) => {|TELLMA0005:units.SaveAsync([])|};
                }

                public class CenterRules(UnitService units) : IEntityValidator<Center>
                {
                    public Task ValidateAsync(IReadOnlyList<Center> entities) => {|TELLMA0005:units.SaveAsync([])|};

                    private Task Helper() => {|TELLMA0005:units.DeleteByIdsAsync([1])|};
                }

                public class CenterEffect(UnitService units) : IPersistEffect<Center>
                {
                    public Task ContributeAsync(IReadOnlyList<Center> entities) => {|TELLMA0005:units.ActivateAsync([1])|};
                }
                """);
        }

        /// <summary>Code outside any pipeline participant runs the front door freely.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Front_door_writes_outside_a_participant_pass()
        {
            return AnalyzerHarness.VerifyAsync<NestedPipelineAnalyzer>(Model + """
                public class CenterService(UnitService units) : EntityService<Center>
                {
                    // A method of the service that is neither a hook nor an action is the front door's caller.
                    public Task ImportAsync() => units.SaveAsync([]);
                }

                public class ImportJob(UnitService units)
                {
                    public Task RunAsync() => units.SaveAsync([]);
                }
                """);
        }
    }
}
