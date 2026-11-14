// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     What a realizer works against: the services it registers into and the problems the
    ///     realisation has found so far.
    /// </summary>
    public sealed class RealizationContext
    {
        private readonly List<CompositionProblem> _problems = [];

        /// <summary>Creates the context for one realisation.</summary>
        /// <param name="services">The host's services.</param>
        /// <param name="record">The composition being realised.</param>
        internal RealizationContext(IServiceCollection services, TellmaCompositionRecord record)
        {
            Services = services;
            Record = record;
        }

        /// <summary>The host's services, which realizers register into.</summary>
        public IServiceCollection Services { get; }

        /// <summary>Every problem reported so far, in the order reported.</summary>
        public IReadOnlyList<CompositionProblem> Problems => _problems;

        /// <summary>The composition being realised, which realizers record what they realise into.</summary>
        internal TellmaCompositionRecord Record { get; }

        /// <summary>Reports a problem. The composition root throws every reported problem at once after the last item.</summary>
        /// <param name="problem">The problem.</param>
        /// <exception cref="ArgumentNullException"><paramref name="problem" /> is null.</exception>
        public void Report(CompositionProblem problem)
        {
            ArgumentNullException.ThrowIfNull(problem);
            _problems.Add(problem);
        }
    }
}
