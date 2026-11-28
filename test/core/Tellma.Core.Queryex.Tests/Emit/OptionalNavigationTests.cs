// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Tests.Emit
{
    /// <summary>
    ///     A navigation the host declares optional: a one-to-one step whose foreign key is the
    ///     declaring entity's own key, so the key is never absent but the target row may be.
    /// </summary>
    /// <remarks>
    ///     Such a step must behave as a nullable foreign key does: an outer join, a nullable path,
    ///     and a rebased criterion that tests for the target row before reading it. Declared
    ///     mandatory, the same step joins inner, which is what the contrast cases pin.
    /// </remarks>
    public sealed class OptionalNavigationTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>The optional step is joined outer and the path through it is nullable.</summary>
        [Fact]
        public void AnOptionalStep_IsJoinedOuter_AndItsPathIsNullable()
        {
            (QueryexSchema schema, EntityDescriptor widget) = Schema(isOptional: true);

            CompiledQuery compiled = Compile(schema, widget, "Name, Extra.Note, Extra.Id");

            Assert.Contains("LEFT JOIN [fx].[WidgetExtras] AS [P1] ON [P1].[Id] = [T].[Id]", compiled.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain("INNER JOIN", compiled.Sql, StringComparison.Ordinal);
            Assert.Equal(QueryexNullity.NotNull, compiled.Columns[0].Nullity);
            Assert.Equal(QueryexNullity.Nullable, compiled.Columns[1].Nullity);

            // The target's key is not null in its own table, yet absent where the row is missing.
            Assert.Equal(QueryexNullity.Nullable, compiled.Columns[2].Nullity);
        }

        /// <summary>The same step declared mandatory is joined inner: the flag is what changes the join.</summary>
        [Fact]
        public void TheSameStepDeclaredMandatory_IsJoinedInner()
        {
            (QueryexSchema schema, EntityDescriptor widget) = Schema(isOptional: false);

            CompiledQuery compiled = Compile(schema, widget, "Name, Extra.Id");

            Assert.Contains("INNER JOIN [fx].[WidgetExtras] AS [P1] ON [P1].[Id] = [T].[Id]", compiled.Sql, StringComparison.Ordinal);
            Assert.Equal(QueryexNullity.NotNull, compiled.Columns[1].Nullity);
        }

        /// <summary>A mandatory step behind the optional one is joined outer too, and its path is nullable.</summary>
        [Fact]
        public void AMandatoryStepBehindTheOptionalOne_IsJoinedOuter()
        {
            (QueryexSchema schema, EntityDescriptor widget) = Schema(isOptional: true);

            CompiledQuery compiled = Compile(schema, widget, "Extra.Kind.Name");

            Assert.Contains("LEFT JOIN [fx].[Kinds] AS [P2] ON [P2].[Id] = [P1].[KindId]", compiled.Sql, StringComparison.Ordinal);
            Assert.Equal(QueryexNullity.Nullable, compiled.Columns[0].Nullity);
        }

        /// <summary>
        ///     A criterion rebased onto the optional step tests for the target row first, so the
        ///     criterion stays two-valued under a negation.
        /// </summary>
        [Fact]
        public void ACriterionRebasedOntoTheOptionalStep_TestsForTheTargetRow()
        {
            (QueryexSchema schema, EntityDescriptor widget) = Schema(isOptional: true);

            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec { Root = widget, Select = "Name", Filter = FilterTree.Not(FilterTree.Via("Extra", FilterTree.Leaf("Note = 'x'"))) },
                new QueryCompilationOptions { Schema = schema, QueryexVersion = QueryexLanguage.Version });

            Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Select(static diagnostic => diagnostic.Code)));
            Assert.Contains("[P1].[Id] IS NOT NULL", result.Value.Sql, StringComparison.Ordinal);
        }

        /// <summary>A navigation is mandatory unless the host says otherwise.</summary>
        [Fact]
        public void ANavigation_IsMandatoryByDefault()
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity("fx.Kind", "[fx].[Kinds]").Key("Id", QueryexType.QxNumeric, "Id");
            _ = builder.Entity("fx.Widget", "[fx].[Widgets]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("KindId", QueryexType.QxNumeric, "KindId", isNotNull: true)
                .Navigation("Kind", "fx.Kind", "KindId");

            QueryexSchema schema = builder.Build();

            Assert.False(schema.FindEntity("fx.Widget")!.Navigations[0].IsOptional);
        }

        /// <summary>
        ///     A widget with an extension row keyed by the widget's own key, and a kind the extension
        ///     points at through a foreign key that is never absent.
        /// </summary>
        /// <param name="isOptional">Whether the extension step is declared optional.</param>
        /// <returns>The schema and the widget entity.</returns>
        private static (QueryexSchema Schema, EntityDescriptor Widget) Schema(bool isOptional)
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity("fx.Kind", "[fx].[Kinds]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Name", QueryexType.QxString, "Name", isNotNull: true);
            _ = builder.Entity("fx.WidgetExtra", "[fx].[WidgetExtras]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Note", QueryexType.QxString, "Note")
                .Property("KindId", QueryexType.QxNumeric, "KindId", isNotNull: true)
                .Navigation("Kind", "fx.Kind", "KindId");
            _ = builder.Entity("fx.Widget", "[fx].[Widgets]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Name", QueryexType.QxString, "Name", isNotNull: true)
                .Navigation("Extra", "fx.WidgetExtra", "Id", isOptional);

            QueryexSchema schema = builder.Build();
            return (schema, schema.FindEntity("fx.Widget")!);
        }

        /// <summary>Compiles a select over the widgets, failing the test on any diagnostic.</summary>
        /// <param name="schema">The schema.</param>
        /// <param name="widget">The widget entity.</param>
        /// <param name="select">The select list.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compile(QueryexSchema schema, EntityDescriptor widget, string select)
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec { Root = widget, Select = select },
                new QueryCompilationOptions { Schema = schema, QueryexVersion = QueryexLanguage.Version });

            Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Select(static diagnostic => diagnostic.Code)));
            return result.Value;
        }
    }
}
