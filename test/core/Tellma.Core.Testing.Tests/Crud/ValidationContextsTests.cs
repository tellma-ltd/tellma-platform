// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Testing.Crud;

namespace Tellma.Core.Testing.Tests.Crud
{
    /// <summary>
    ///     The validation contexts a rule's unit test builds: what the pipeline's first round would
    ///     have set is readable, and the write host refuses what a rule under test never reaches.
    /// </summary>
    public class ValidationContextsTests
    {
        private static readonly RequestContext Caller = RequestContext.Tenantless(Proxy.Of<ICalendarSystem>(), DateTimeOffset.UnixEpoch);

        [Fact]
        public void A_save_context_carries_the_payload_which_rows_are_new_and_the_before_images()
        {
            Row inserted = new("new");
            Row updated = new("updated");
            Row stored = new("stored");
            IContextLoader loader = Proxy.Of<IContextLoader>();

            SaveContext<Row> context = ValidationContexts.Save(loader, Caller, [(inserted, null), (updated, stored)]);

            Assert.Equal([inserted, updated], context.Entities);
            Assert.Equal((true, false), (context.IsNew(0), context.IsNew(1)));
            Assert.Equal((null, stored), (context.Before(0), context.Before(1)));
            Assert.True(context.Changed(1, static row => row.Name));
            Assert.Same(loader, context.Loader);
            Assert.False(context.Errors.HasErrors);
            Assert.Equal((WriteHostKind.Pipeline, "save"), (context.Host.Kind, context.Host.Operation));
            _ = Assert.Throws<NotSupportedException>(() => context.Host.Confirmations);
        }

        [Fact]
        public void A_delete_context_carries_the_ids_the_targets_and_its_shape()
        {
            Row target = new("target");

            DeleteContext<Row, int> byIds = ValidationContexts.Delete(Proxy.Of<IContextLoader>(), Caller, [7, 7], [target]);
            DeleteContext<Row, int> withDescendants = ValidationContexts.Delete(Proxy.Of<IContextLoader>(), Caller, [7], [target], withDescendants: true);

            Assert.Equal([7, 7], byIds.Ids);
            Assert.Equal([target], byIds.Entities);
            Assert.Equal((false, true), (byIds.WithDescendants, withDescendants.WithDescendants));
            Assert.Equal("delete", byIds.Host.Operation);
        }

        /// <summary>A payload row.</summary>
        /// <param name="Name">Its name.</param>
        private sealed record Row(string Name);

        /// <summary>A stand-in for an interface no test call reaches: every member refuses.</summary>
        public class Proxy : DispatchProxy
        {
            /// <summary>A stand-in for an interface.</summary>
            /// <typeparam name="T">The interface.</typeparam>
            /// <returns>The stand-in.</returns>
            public static T Of<T>()
                where T : class
            {
                return Create<T, Proxy>();
            }

            /// <inheritdoc />
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                throw new NotSupportedException($"The test reaches {targetMethod?.Name}, which no stand-in answers.");
            }
        }
    }
}
