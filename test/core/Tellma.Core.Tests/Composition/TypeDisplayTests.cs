// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>C# renderings of types and interface members in problem texts and fixes.</summary>
    public class TypeDisplayTests
    {
        [Theory]
        [InlineData(typeof(int), "int")]
        [InlineData(typeof(int?), "int?")]
        [InlineData(typeof(string[]), "string[]")]
        [InlineData(typeof(int[,]), "int[,]")]
        [InlineData(typeof(List<Guid?>), "List<Guid?>")]
        [InlineData(typeof(Dictionary<string, List<int>>), "Dictionary<string, List<int>>")]
        [InlineData(typeof(IRepository<>), "IRepository<T>")]
        public void Types_render_as_CSharp(Type type, string expected)
        {
            Assert.Equal(expected, TypeDisplay.Short(type));
        }

        [Fact]
        public void Qualified_renderings_carry_namespaces()
        {
            Assert.Equal("System.Collections.Generic.List<System.Guid>", TypeDisplay.Qualified(typeof(List<Guid>)));
        }

        [Fact]
        public void The_members_to_implement_skip_default_implementations_and_include_base_interfaces()
        {
            string[] expected =
            [
                "string Code { get; }",
                "void Submit(int attempt, string? reason)",
                "event EventHandler? Submitted",
                "string? EInvoiceStatus { get; set; }",
                "Guid? EInvoiceUuid { get; init; }",
            ];

            Assert.Equal(expected, TypeDisplay.RequiredMembers(typeof(IZatcaTracked)));
        }

        /// <summary>A shape that extends another and adds a property, a method and an event.</summary>
        private interface IZatcaTracked : IEInvoiceTracked
        {
            string Code { get; }

            event EventHandler? Submitted;

            void Submit(int attempt, string? reason);
        }
    }
}
