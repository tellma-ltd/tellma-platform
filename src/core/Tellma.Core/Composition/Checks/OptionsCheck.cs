// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.options</c>: every options type the composition bound passes its validation. One
    ///     problem per failure, so a section with three bad values reports three.
    /// </summary>
    /// <param name="record">The realised composition.</param>
    internal sealed class OptionsCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.Options;

        /// <inheritdoc />
        public bool RequiresInfrastructure => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            List<CompositionProblem> problems = [];
            foreach (BoundOptions options in record.BoundOptions)
            {
                foreach (string failure in options.Validate(services))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The options {TypeDisplay.Qualified(options.OptionsType)} bound from '{options.ConfigurationPath}' by '{options.Owner}' are invalid: {failure}",
                        $"Set valid values under the configuration section '{options.ConfigurationPath}'."));
                }
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
