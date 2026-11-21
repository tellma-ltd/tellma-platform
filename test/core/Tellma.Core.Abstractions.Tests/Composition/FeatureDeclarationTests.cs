// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tests.Composition
{
    /// <summary>What a feature's declaration records.</summary>
    public class FeatureDeclarationTests
    {
        [Fact]
        public void Requirements_are_recorded_in_order_without_repeats()
        {
            FeatureDeclaration declaration = new FeatureDeclaration().Requires("gl").Requires("sales").Requires("gl");

            string[] expected = ["gl", "sales"];
            Assert.Equal(expected, declaration.RequiredFeatures);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void A_requirement_must_name_a_feature(string? name)
        {
            _ = Assert.ThrowsAny<ArgumentException>(() => new FeatureDeclaration().Requires(name!));
        }

        [Fact]
        public void Options_bindings_carry_their_type_and_path_and_hand_the_type_back_to_a_visitor()
        {
            FeatureDeclaration declaration = new FeatureDeclaration().Options<SampleOptions>("Tellma:Gl");

            FeatureOptionsBinding binding = Assert.Single(declaration.OptionsBindings);
            Assert.Equal(typeof(SampleOptions), binding.OptionsType);
            Assert.Equal("Tellma:Gl", binding.ConfigurationPath);

            RecordingVisitor visitor = new();
            binding.Accept(visitor);
            Assert.Equal(typeof(SampleOptions), visitor.Visited);
        }

        [Fact]
        public void An_options_binding_must_name_a_path()
        {
            _ = Assert.ThrowsAny<ArgumentException>(() => new FeatureDeclaration().Options<SampleOptions>(" "));
        }

        /// <summary>Options to bind.</summary>
        private sealed class SampleOptions;

        /// <summary>Records the type argument it was visited with.</summary>
        private sealed class RecordingVisitor : IFeatureOptionsBindingVisitor
        {
            /// <summary>The visited options type.</summary>
            public Type? Visited { get; private set; }

            /// <inheritdoc />
            public void Visit<TOptions>(FeatureOptionsBinding binding)
                where TOptions : class
            {
                Visited = typeof(TOptions);
            }
        }
    }
}
