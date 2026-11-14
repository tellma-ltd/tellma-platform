// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     The job contributions' problems no single item shows, reported into the composition gate
    ///     once every item is realised: a handler key registered twice, a built-in id or key declared
    ///     twice, a retired key some handler still registers, and a built-in naming a retired key.
    /// </summary>
    /// <remarks>
    ///     A built-in naming a key no handler registers is legal (another host may run the handler),
    ///     so it is the <c>jobs.handlers</c> startup check's warning, not a problem here.
    /// </remarks>
    internal static class JobContributionChecks
    {
        /// <summary>Reports every cross-item problem of the recorded job contributions.</summary>
        /// <param name="context">The realisation, whose record holds what the job realizers recorded.</param>
        public static void Check(RealizationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            TellmaCompositionRecord record = context.Record;

            // --- A key registered more than once: the registry could not tell the handlers apart ---
            foreach (IGrouping<string, ContributedJobHandler> clash in record.JobHandlers
                .GroupBy(static handler => handler.Attribute.Key, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1))
            {
                string handlers = string.Join(" and ", clash.Select(static handler => $"{TypeDisplay.Qualified(handler.HandlerType)} of '{handler.Owner.Name}'"));
                Report(
                    context,
                    $"The job handler key '{clash.Key}' is registered more than once, by {handlers}.",
                    "Register each key once; a handler whose arguments changed incompatibly takes a new key.");
            }

            // --- A built-in id declared more than once: the seed rows would collide ---
            foreach (IGrouping<int, ContributedBuiltInSchedule> clash in record.BuiltInSchedules
                .GroupBy(static schedule => schedule.Seed.Id)
                .Where(static group => group.Count() > 1))
            {
                Report(
                    context,
                    string.Create(CultureInfo.InvariantCulture, $"The built-in schedule id {clash.Key} is declared more than once, by {Owners(clash)}."),
                    "Give every built-in schedule an id of its own in the feature's band.");
            }

            // --- A built-in key declared more than once: the seed rows' unique code would collide ---
            foreach (IGrouping<string, ContributedBuiltInSchedule> clash in record.BuiltInSchedules
                .GroupBy(static schedule => schedule.Seed.HandlerKey, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1))
            {
                Report(
                    context,
                    $"The handler key '{clash.Key}' has more than one built-in schedule, declared by {Owners(clash)}.",
                    "Declare one built-in schedule per handler key; a second cadence is a user schedule.");
            }

            // --- A retired key is retired for good: no handler registers it and no built-in names it ---
            HashSet<string> retired = new(record.RetiredJobKeys.Select(static key => key.Key), StringComparer.Ordinal);
            foreach (ContributedJobHandler handler in record.JobHandlers.Where(handler => retired.Contains(handler.Attribute.Key)))
            {
                Report(
                    context,
                    $"The job handler {TypeDisplay.Qualified(handler.HandlerType)} of the feature '{handler.Owner.Name}' registers the key '{handler.Attribute.Key}', which a feature retired.",
                    "Give the handler a new key, or stop retiring the key; a retired key's queued jobs are cancelled.");
            }

            foreach (ContributedBuiltInSchedule schedule in record.BuiltInSchedules.Where(schedule => retired.Contains(schedule.Seed.HandlerKey)))
            {
                Report(
                    context,
                    string.Create(CultureInfo.InvariantCulture, $"The built-in schedule {schedule.Seed.Id} of the feature '{schedule.Owner.Name}' names the key '{schedule.Seed.HandlerKey}', which a feature retired."),
                    "Remove the built-in schedule along with the handler it ran, or stop retiring the key.");
            }
        }

        /// <summary>Lists the features that declared a group of built-in schedules, once each.</summary>
        /// <param name="schedules">The schedules.</param>
        /// <returns>The quoted feature names, joined.</returns>
        private static string Owners(IEnumerable<ContributedBuiltInSchedule> schedules)
        {
            return string.Join(" and ", schedules.Select(static schedule => schedule.Owner.Name).Distinct(StringComparer.Ordinal).Select(static name => $"'{name}'"));
        }

        /// <summary>Reports one problem.</summary>
        /// <param name="context">The realisation.</param>
        /// <param name="problem">What is wrong.</param>
        /// <param name="fix">How to fix it.</param>
        private static void Report(RealizationContext context, string problem, string fix)
        {
            context.Report(new CompositionProblem(CompositionNames.Source, problem, fix));
        }
    }
}
