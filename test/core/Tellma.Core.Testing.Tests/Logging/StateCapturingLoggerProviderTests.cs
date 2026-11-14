// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections;
using Tellma.Testing.Support.Logging;

namespace Tellma.Core.Testing.Tests.Logging
{
    /// <summary>
    ///     The capturing provider finds a value wherever a structured sink would find it: in each
    ///     shape a log state takes, nested, and behind a cycle; and never where it is not.
    /// </summary>
    /// <remarks>
    ///     The suites that assert a secret reaches no log are only as good as this walk: a shape it
    ///     skipped would let a leaking change pass them.
    /// </remarks>
    public sealed class StateCapturingLoggerProviderTests
    {
        /// <summary>The value every state hides somewhere; no rendering of a type name contains it.</summary>
        private const string Secret = "walker-secret-4f1a9c";

        [Fact]
        public void Finds_a_value_in_a_header_list_of_a_pair_sequence_whose_message_redacts_it()
        {
            // The HTTP client factory's header event: a list of pairs whose values are sequences.
            List<KeyValuePair<string, object>> state = [new("Authorization", new[] { $"Bearer {Secret}" })];

            Assert.Single(Capture(state, message: "Authorization: *").Mentioning(Secret));
        }

        [Fact]
        public void Finds_a_value_in_a_non_generic_dictionary()
        {
            Hashtable state = new() { ["key"] = new Hashtable { ["nested"] = Secret } };

            Assert.Single(Capture(state).Mentioning(Secret));
        }

        [Fact]
        public void Finds_a_value_in_a_dictionary_entry_key()
        {
            Hashtable state = new() { [Secret] = "value" };

            Assert.Single(Capture(state).Mentioning(Secret));
        }

        [Fact]
        public void Finds_a_value_in_a_destructured_object_nested_in_a_member()
        {
            // An object logged for destructuring: its rendering is the type name alone.
            List<KeyValuePair<string, object?>> state = [new("Options", new Outer { Inner = new Inner { ApiKey = Secret } })];

            Assert.Single(Capture(state).Mentioning(Secret));
        }

        [Fact]
        public void Finds_a_value_in_an_exceptions_data()
        {
            InvalidOperationException exception = new("failed");
            exception.Data["credential"] = Secret;
            List<KeyValuePair<string, object?>> state = [new("Failure", exception)];

            Assert.Single(Capture(state).Mentioning(Secret));
        }

        [Fact]
        public void Finds_a_value_in_a_scope()
        {
            StateCapturingLoggerProvider provider = new();
            using IDisposable? scope = provider.CreateLogger("Tests").BeginScope(new Outer { Inner = new Inner { ApiKey = Secret } });

            CapturedLogEntry entry = Assert.Single(provider.Mentioning(Secret));
            Assert.Null(entry.Level);
        }

        [Fact]
        public void Ends_a_cyclic_state_and_still_finds_a_value_behind_the_cycle()
        {
            Node first = new() { Label = "first" };
            Node second = new() { Label = Secret, Next = first };
            first.Next = second;

            Assert.Single(Capture(first).Mentioning(Secret));
        }

        [Fact]
        public void Skips_a_property_whose_getter_throws_and_walks_the_rest()
        {
            Assert.Single(Capture(new Throwing { Label = Secret }).Mentioning(Secret));
        }

        [Fact]
        public void Finds_nothing_where_the_value_is_absent()
        {
            List<KeyValuePair<string, object?>> state = [new("Options", new Outer { Inner = new Inner { ApiKey = "something else" } })];

            Assert.Empty(Capture(state).Mentioning(Secret));
        }

        /// <summary>Logs one event with a state and returns the provider that captured it.</summary>
        /// <typeparam name="TState">The state's type.</typeparam>
        /// <param name="state">The state.</param>
        /// <param name="message">The rendered message; a text without the value when null.</param>
        /// <returns>The provider.</returns>
        private static StateCapturingLoggerProvider Capture<TState>(TState state, string message = "an event")
        {
            StateCapturingLoggerProvider provider = new();
            provider.CreateLogger("Tests").Log(LogLevel.Trace, default, state, null, (_, _) => message);
            return provider;
        }

        /// <summary>An options-like object, rendered as its type name.</summary>
        private sealed class Outer
        {
            /// <summary>A nested object.</summary>
            public Inner? Inner { get; init; }
        }

        /// <summary>The nested object that holds the value.</summary>
        private sealed class Inner
        {
            /// <summary>The value.</summary>
            public string? ApiKey { get; init; }
        }

        /// <summary>A node of a cycle.</summary>
        private sealed class Node
        {
            /// <summary>The node's label.</summary>
            public string? Label { get; init; }

            /// <summary>The next node.</summary>
            public Node? Next { get; set; }
        }

        /// <summary>An object one of whose getters throws.</summary>
        private sealed class Throwing
        {
            /// <summary>A getter that throws.</summary>
            public string Broken => Refuse(Label);

            /// <summary>The value.</summary>
            public string? Label { get; init; }

            /// <summary>Throws, as a getter that cannot be read does.</summary>
            /// <param name="label">The label the getter would have described.</param>
            /// <returns>Nothing; it always throws.</returns>
            private static string Refuse(string? label)
            {
                throw new InvalidOperationException($"Not readable after {label?.Length ?? 0} characters.");
            }
        }
    }
}
