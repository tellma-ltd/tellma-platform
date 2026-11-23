// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Xml;

namespace Tellma.Core.Excel
{
    // An XML reader atomizes every distinct element name, attribute name, prefix and namespace it
    // meets into its name table and keeps them until it is disposed. The token bound caps one name
    // and the part's length caps the part, but neither caps how many distinct names a part holds, so
    // a part of distinct empty elements would hold several times its own size. Spec 0019 §7.2 sets
    // no bound for names; a real part uses well under a hundred, so the cap below leaves ample room.
    /// <summary>
    ///     A name table that charges each name it adds its UTF-16 bytes plus a fixed overhead, and
    ///     refuses the part once the charge passes <see cref="MaxBytes" />. One table serves one
    ///     part's reader.
    /// </summary>
    internal sealed class BoundedNameTable : NameTable
    {
        /// <summary>The most bytes one part's distinct names may be charged.</summary>
        public const long MaxBytes = 1L << 20;

        /// <summary>The fixed charge of one name beyond its text: the table entry and the object header it costs.</summary>
        public const int EntryOverhead = 16;

        /// <summary>The bytes the names were charged so far, text and overhead together.</summary>
        public long ChargedBytes { get; private set; }

        /// <inheritdoc />
        /// <exception cref="Abstractions.Excel.ImportException">A new name takes the charge past <see cref="MaxBytes" />.</exception>
        public override string Add(char[] key, int start, int len)
        {
            // Only a name the table lacks costs anything; one already atomized is returned as is.
            if (len > 0 && Get(key, start, len) is null)
            {
                Charge(len);
            }

            return base.Add(key, start, len);
        }

        /// <inheritdoc />
        /// <exception cref="Abstractions.Excel.ImportException">A new name takes the charge past <see cref="MaxBytes" />.</exception>
        public override string Add(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (key.Length > 0 && Get(key) is null)
            {
                Charge(key.Length);
            }

            return base.Add(key);
        }

        /// <summary>Charges a new name, refusing the part past the bound.</summary>
        /// <param name="length">The name's length, in characters.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The charge passes <see cref="MaxBytes" />.</exception>
        private void Charge(int length)
        {
            ChargedBytes += ((long)length * sizeof(char)) + EntryOverhead;
            if (ChargedBytes > MaxBytes)
            {
                throw ImportFaults.TooLarge(ChargedBytes, MaxBytes);
            }
        }
    }
}
