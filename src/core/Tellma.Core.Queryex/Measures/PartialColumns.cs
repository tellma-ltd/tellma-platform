// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;

namespace Tellma.Core.Queryex.Measures
{
    /// <summary>One partial's values across the cells of a table: one typed slot per cell.</summary>
    internal abstract class PartialColumn
    {
        /// <summary>Makes room for at least a number of cells, keeping the ones there are.</summary>
        /// <param name="capacity">The cells to hold.</param>
        internal abstract void Grow(int capacity);

        /// <summary>Combines one row's value into a cell.</summary>
        /// <param name="cell">The cell.</param>
        /// <param name="buffer">The reader's typed array for the partial's column.</param>
        /// <param name="row">The row's position in it.</param>
        /// <exception cref="ArgumentException">
        ///     The array is not of the type the partial is read as, or the value does not fit the
        ///     partial's type.
        /// </exception>
        internal abstract void Add(int cell, Array buffer, int row);

        /// <summary>Combines one cell into another.</summary>
        /// <param name="target">The cell combined into.</param>
        /// <param name="source">The cell combined from, unchanged.</param>
        internal abstract void Merge(int target, int source);

        /// <summary>The partial's value for a cell.</summary>
        /// <param name="cell">The cell.</param>
        /// <returns>The value, at the partial's type.</returns>
        internal abstract MeasureValue Read(int cell);

        /// <summary>The error for a reader array of the wrong type.</summary>
        /// <param name="buffer">The array.</param>
        /// <param name="expected">The element type the partial is read as.</param>
        /// <returns>The exception.</returns>
        internal static ArgumentException WrongBuffer(Array buffer, Type expected)
        {
            return new ArgumentException(
                "The partial is read from an array of " + expected.Name + ", not of "
                    + (buffer.GetType().GetElementType()?.Name ?? buffer.GetType().Name) + ".",
                nameof(buffer));
        }

        /// <summary>The error for a value that does not fit the partial's type.</summary>
        /// <returns>The exception.</returns>
        internal static ArgumentException Unfit()
        {
            return new ArgumentException(
                "The value does not fit the partial's type as the reader reported it.",
                "buffer");
        }
    }

    /// <summary>A total, held as a 128-bit mantissa at the column's scale.</summary>
    internal sealed class SumColumn : PartialColumn
    {
        /// <summary>The smallest magnitude past 38 digits.</summary>
        private static readonly Int128 Limit = MeasureArithmetic.PowerOfTen(NumericShape.MaxPrecision);

        /// <summary>The column's precision, as the reader reported it.</summary>
        private readonly byte _precision;

        /// <summary>The column's scale, as the reader reported it.</summary>
        private readonly byte _scale;

        /// <summary>The totals' mantissas.</summary>
        private Int128[] _values;

        /// <summary>Whether any row with a value has been combined into each cell.</summary>
        private bool[] _present;

        /// <summary>Initializes the column.</summary>
        /// <param name="type">The column's precision and scale.</param>
        /// <param name="capacity">The cells to hold at first.</param>
        internal SumColumn(QueryexNumericType type, int capacity)
        {
            _precision = type.Precision;
            _scale = type.Scale;
            _values = new Int128[capacity];
            _present = new bool[capacity];
        }

        /// <inheritdoc />
        internal override void Grow(int capacity)
        {
            Array.Resize(ref _values, capacity);
            Array.Resize(ref _present, capacity);
        }

        /// <inheritdoc />
        internal override void Add(int cell, Array buffer, int row)
        {
            if (buffer is not decimal[] values)
            {
                throw WrongBuffer(buffer, typeof(decimal));
            }

            if (!MeasureArithmetic.TryRescale(values[row], NumericShape.MaxPrecision, _scale, out Int128 mantissa))
            {
                throw Unfit();
            }

            Combine(cell, mantissa);
        }

        /// <inheritdoc />
        internal override void Merge(int target, int source)
        {
            if (_present[source])
            {
                Combine(target, _values[source]);
            }
        }

        /// <inheritdoc />
        internal override MeasureValue Read(int cell)
        {
            return _present[cell]
                ? new MeasureValue(
                    QueryexType.QxNumeric,
                    false,
                    MeasureArithmetic.FromMantissa(_values[cell], _precision, _scale))
                : new MeasureValue(QueryexType.QxNumeric, true);
        }

        /// <summary>Adds a mantissa into a cell's total.</summary>
        /// <param name="cell">The cell.</param>
        /// <param name="mantissa">The mantissa, at the column's scale.</param>
        /// <exception cref="QueryexEvaluationException">The total passes 38 digits.</exception>
        private void Combine(int cell, Int128 mantissa)
        {
            if (!_present[cell])
            {
                _values[cell] = mantissa;
                _present[cell] = true;
                return;
            }

            Int128 total;
            try
            {
                total = checked(_values[cell] + mantissa);
            }
            catch (OverflowException)
            {
                throw QueryexEvaluationException.TotalOverflowed();
            }

            // Where the backend's own total would have raised its overflow error.
            if (Int128.Abs(total) >= Limit)
            {
                throw QueryexEvaluationException.TotalOverflowed();
            }

            _values[cell] = total;
        }
    }

    /// <summary>A count, held as a 64-bit whole number.</summary>
    internal sealed class CountColumn : PartialColumn
    {
        /// <summary>The counts.</summary>
        private long[] _values;

        /// <summary>Initializes the column.</summary>
        /// <param name="capacity">The cells to hold at first.</param>
        internal CountColumn(int capacity)
        {
            _values = new long[capacity];
        }

        /// <inheritdoc />
        internal override void Grow(int capacity)
        {
            Array.Resize(ref _values, capacity);
        }

        /// <inheritdoc />
        internal override void Add(int cell, Array buffer, int row)
        {
            if (buffer is not long[] values)
            {
                throw WrongBuffer(buffer, typeof(long));
            }

            long count = values[row];
            if (count < 0)
            {
                throw Unfit();
            }

            Combine(cell, count);
        }

        /// <inheritdoc />
        internal override void Merge(int target, int source)
        {
            Combine(target, _values[source]);
        }

        /// <inheritdoc />
        internal override MeasureValue Read(int cell)
        {
            return new MeasureValue(
                QueryexType.QxNumeric,
                false,
                MeasureArithmetic.At(new SqlDecimal(_values[cell]), NumericShape.Whole(WholeWidth.BigInt)));
        }

        /// <summary>Adds into a cell's count, as the backend's 64-bit count does.</summary>
        /// <param name="cell">The cell.</param>
        /// <param name="count">The count to add.</param>
        private void Combine(int cell, long count)
        {
            try
            {
                _values[cell] = checked(_values[cell] + count);
            }
            catch (OverflowException)
            {
                throw QueryexEvaluationException.TotalOverflowed();
            }
        }
    }

    /// <summary>The least or the greatest of the values combined into each cell.</summary>
    /// <typeparam name="T">How a value is held.</typeparam>
    internal sealed class ExtremeColumn<T> : PartialColumn
    {
        /// <summary>Whether the greatest value is kept rather than the least.</summary>
        private readonly bool _maximum;

        /// <summary>The order the extreme is taken in.</summary>
        private readonly IComparer<T> _order;

        /// <summary>
        ///     The order that settles a tie, keeping the smaller of two values the first order calls
        ///     equal; null where the first order has no ties between different values.
        /// </summary>
        private readonly IComparer<T>? _tie;

        /// <summary>Reads one row's value from a reader array, or throws when the array is not one of its type.</summary>
        private readonly Func<Array, int, T> _reader;

        /// <summary>Gives a held value as the partial's value.</summary>
        private readonly Func<T, MeasureValue> _writer;

        /// <summary>The partial's type, for an empty cell.</summary>
        private readonly QueryexType _type;

        /// <summary>The extremes.</summary>
        private T[] _values;

        /// <summary>Whether any row with a value has been combined into each cell.</summary>
        private bool[] _present;

        /// <summary>Initializes the column.</summary>
        /// <param name="maximum">Whether the greatest value is kept rather than the least.</param>
        /// <param name="order">The order the extreme is taken in.</param>
        /// <param name="tie">The order that settles a tie, or null.</param>
        /// <param name="type">The partial's type.</param>
        /// <param name="reader">Reads one row's value from a reader array.</param>
        /// <param name="writer">Gives a held value as the partial's value.</param>
        /// <param name="capacity">The cells to hold at first.</param>
        internal ExtremeColumn(
            bool maximum,
            IComparer<T> order,
            IComparer<T>? tie,
            QueryexType type,
            Func<Array, int, T> reader,
            Func<T, MeasureValue> writer,
            int capacity)
        {
            _maximum = maximum;
            _order = order;
            _tie = tie;
            _type = type;
            _reader = reader;
            _writer = writer;
            _values = new T[capacity];
            _present = new bool[capacity];
        }

        /// <inheritdoc />
        internal override void Grow(int capacity)
        {
            Array.Resize(ref _values, capacity);
            Array.Resize(ref _present, capacity);
        }

        /// <inheritdoc />
        internal override void Add(int cell, Array buffer, int row)
        {
            Combine(cell, _reader(buffer, row));
        }

        /// <inheritdoc />
        internal override void Merge(int target, int source)
        {
            if (_present[source])
            {
                Combine(target, _values[source]);
            }
        }

        /// <inheritdoc />
        internal override MeasureValue Read(int cell)
        {
            return _present[cell] ? _writer(_values[cell]) : new MeasureValue(_type, true);
        }

        /// <summary>Keeps a value in a cell where it is more extreme than the one there.</summary>
        /// <param name="cell">The cell.</param>
        /// <param name="value">The value.</param>
        private void Combine(int cell, T value)
        {
            if (!_present[cell])
            {
                _values[cell] = value;
                _present[cell] = true;
                return;
            }

            int comparison = _order.Compare(value, _values[cell]);
            bool replaces = comparison == 0
                ? _tie is not null && _tie.Compare(value, _values[cell]) < 0
                : _maximum ? comparison > 0 : comparison < 0;

            if (replaces)
            {
                _values[cell] = value;
            }
        }
    }
}
