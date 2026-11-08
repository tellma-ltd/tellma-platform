// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tests.Composition
{
    /// <summary>The contribution sugar records the item records the platform realises.</summary>
    public class FeatureContributionTests
    {
        [Fact]
        public void The_service_sugar_records_one_item_per_lifetime_in_order()
        {
            FeatureContribution contribution = new FeatureContribution()
                .Singleton<IService, Service>()
                .Scoped<IService, Service>()
                .Transient<IService, Service>();

            FeatureContributionItem[] expected =
            [
                new ServiceContributionItem(typeof(IService), typeof(Service), null, ServiceLifetimeKind.Singleton),
                new ServiceContributionItem(typeof(IService), typeof(Service), null, ServiceLifetimeKind.Scoped),
                new ServiceContributionItem(typeof(IService), typeof(Service), null, ServiceLifetimeKind.Transient),
            ];
            Assert.Equal(expected, contribution.Items);
        }

        [Fact]
        public void The_table_type_and_shape_sugar_record_their_items()
        {
            FeatureContribution contribution = new FeatureContribution()
                .TableType<Service>()
                .RequiresShape<Service, IService>();

            FeatureContributionItem[] expected =
            [
                new TableTypeContributionItem(typeof(Service)),
                new ShapeRequirementContributionItem(typeof(Service), typeof(IService)),
            ];
            Assert.Equal(expected, contribution.Items);
        }

        [Fact]
        public void The_calendar_sugar_records_its_item()
        {
            FeatureContribution contribution = new FeatureContribution().Calendar<CalendarSystem>();

            Assert.Equal(new CalendarContributionItem(typeof(CalendarSystem)), Assert.Single(contribution.Items));
        }

        [Fact]
        public void Add_and_Feature_record_items_and_nested_features_and_refuse_null()
        {
            NestedFeature nested = new();
            TableTypeContributionItem item = new(typeof(Service));

            FeatureContribution contribution = new FeatureContribution().Add(item).Feature(nested);

            Assert.Same(item, Assert.Single(contribution.Items));
            Assert.Same(nested, Assert.Single(contribution.Features));
            _ = Assert.Throws<ArgumentNullException>(() => contribution.Add(null!));
            _ = Assert.Throws<ArgumentNullException>(() => contribution.Feature(null!));
        }

        /// <summary>A service.</summary>
        private interface IService;

        /// <summary>Its implementation.</summary>
        private sealed class Service : IService;

        /// <summary>A calendar system; only its type is recorded.</summary>
        private sealed class CalendarSystem : ICalendarSystem
        {
            /// <inheritdoc />
            public string Code => "xx";

            /// <inheritdoc />
            public System.Globalization.Calendar Calendar { get; } = new System.Globalization.GregorianCalendar();

            /// <inheritdoc />
            public int MonthCount => 12;

            /// <inheritdoc />
            public DateOnly MinSupported => DateOnly.MinValue;

            /// <inheritdoc />
            public DateOnly MaxSupported => DateOnly.MaxValue;

            /// <inheritdoc />
            public (int Year, int Month, int Day) Decompose(DateOnly date)
            {
                return (date.Year, date.Month, date.Day);
            }

            /// <inheritdoc />
            public string Format(DateOnly date, System.Globalization.CultureInfo culture, DateStyle style)
            {
                return string.Empty;
            }

            /// <inheritdoc />
            public string Format(DateTimeOffset instant, TimeZoneInfo zone, System.Globalization.CultureInfo culture, DateStyle style, bool includeTime = true)
            {
                return string.Empty;
            }

            /// <inheritdoc />
            public string MonthName(int month, System.Globalization.CultureInfo culture, bool abbreviated)
            {
                return string.Empty;
            }

            /// <inheritdoc />
            public DateOnly? TryParse(string text, System.Globalization.CultureInfo culture)
            {
                return null;
            }
        }

        /// <summary>A nested feature.</summary>
        private sealed class NestedFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "nested";

            /// <inheritdoc />
            public void Declare(FeatureDeclaration declaration)
            {
            }

            /// <inheritdoc />
            public void Contribute(FeatureContribution contribution)
            {
            }
        }
    }
}
