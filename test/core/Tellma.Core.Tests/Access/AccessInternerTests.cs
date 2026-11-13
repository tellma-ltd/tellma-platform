// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Access;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The interner shares one instance of each string and stays bounded: past its entries or its
    ///     characters it starts over, and an equal string still comes back.
    /// </summary>
    public sealed class AccessInternerTests
    {
        [Fact]
        public void An_equal_string_comes_back_as_the_first_instance()
        {
            AccessInterner interner = new();
            string first = Text("Amount > 1");

            Assert.Same(first, interner.Intern(first));
            Assert.Same(first, interner.Intern(Text("Amount > 1")));
            Assert.Equal(1, interner.Count);
            _ = Assert.Throws<ArgumentNullException>(() => interner.Intern(null!));
        }

        [Fact]
        public void Past_its_entries_the_interner_starts_over_and_an_equal_string_still_comes_back()
        {
            AccessInterner interner = new();
            string first = interner.Intern(Text("0"));
            for (int index = 1; index < AccessInterner.MaxEntries; index++)
            {
                _ = interner.Intern(index.ToString(CultureInfo.InvariantCulture));
            }

            // Full, it still shares what it holds: a string it knows never starts it over.
            Assert.Equal(AccessInterner.MaxEntries, interner.Count);
            Assert.Same(first, interner.Intern(Text("0")));
            Assert.Equal(AccessInterner.MaxEntries, interner.Count);

            // A new string starts it over and is held.
            string beyond = Text("beyond");
            Assert.Same(beyond, interner.Intern(beyond));
            Assert.Equal(1, interner.Count);

            // An equal string still comes back: the first instance is let go, and the next is shared.
            string again = Text("0");
            Assert.Same(again, interner.Intern(again));
            Assert.Equal(first, again);
            Assert.Same(again, interner.Intern(Text("0")));
            Assert.Equal(2, interner.Count);
        }

        [Fact]
        public void Past_its_characters_the_interner_starts_over()
        {
            AccessInterner interner = new();
            string small = interner.Intern(Text("Amount > 1"));

            // Exactly at the bound, both are held.
            string large = new('x', checked((int)AccessInterner.MaxCharacters) - small.Length);
            Assert.Same(large, interner.Intern(large));
            Assert.Equal(2, interner.Count);

            // One character past it starts over, and an equal string still comes back.
            string next = Text("y");
            Assert.Same(next, interner.Intern(next));
            Assert.Equal(1, interner.Count);
            Assert.Equal(small, interner.Intern(Text("Amount > 1")));
            Assert.Equal(2, interner.Count);
        }

        /// <summary>A fresh instance of a text, never one the runtime interned.</summary>
        /// <param name="value">The text.</param>
        /// <returns>A new string equal to it.</returns>
        private static string Text(string value)
        {
            return new string(value.AsSpan());
        }
    }
}
