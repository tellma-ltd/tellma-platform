// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Nodes;

namespace Tellma.Core.AspNetCore.IntegrationTests.Infrastructure
{
    /// <summary>The request bodies the suite sends and the parts of the answers it reads, in the web surface's JSON.</summary>
    internal static class Wire
    {
        /// <summary>A body naming ids: <c>{ "ids": [...] }</c>.</summary>
        /// <param name="ids">The ids.</param>
        /// <returns>The body.</returns>
        public static JsonObject Ids(params int[] ids)
        {
            return new JsonObject { ["ids"] = Array(ids) };
        }

        /// <summary>A save of entities, answering them read back.</summary>
        /// <param name="entities">The entities.</param>
        /// <returns>The body.</returns>
        public static JsonObject Save(params JsonNode[] entities)
        {
            return new JsonObject { ["entities"] = new JsonArray([.. entities.Select(static entity => entity.DeepClone())]) };
        }

        /// <summary>A query of one filter or none, its first page of rows with the count.</summary>
        /// <param name="select">The columns.</param>
        /// <param name="filter">The filter; null for none.</param>
        /// <param name="includeInactive">Whether inactive rows are included.</param>
        /// <returns>The body.</returns>
        public static JsonObject Query(string select, string? filter, bool includeInactive = false)
        {
            return new JsonObject
            {
                ["select"] = select,
                ["filters"] = filter is null ? [] : new JsonArray(filter),
                ["includeCount"] = true,
                ["includeInactive"] = includeInactive,
            };
        }

        /// <summary>A query answer's rows, each as its cells.</summary>
        /// <param name="answer">The answer.</param>
        /// <returns>The rows.</returns>
        public static List<JsonArray> Rows(JsonNode answer)
        {
            ArgumentNullException.ThrowIfNull(answer);
            return [.. answer["rows"]!["rows"]!.AsArray().Select(static row => row!.AsArray())];
        }

        /// <summary>The ids an entities answer lists, in its order.</summary>
        /// <param name="answer">The answer.</param>
        /// <returns>The ids.</returns>
        public static List<int> IdsOf(JsonNode answer)
        {
            ArgumentNullException.ThrowIfNull(answer);
            return [.. answer["ids"]!.AsArray().Select(static id => id!.GetValue<int>())];
        }

        /// <summary>An entities answer's entities, in its order.</summary>
        /// <param name="answer">The answer.</param>
        /// <returns>The entities.</returns>
        public static List<JsonObject> Entities(JsonNode answer)
        {
            ArgumentNullException.ThrowIfNull(answer);
            return [.. answer["entities"]!.AsArray().Select(static entity => entity!.AsObject())];
        }

        /// <summary>A JSON array of ids.</summary>
        /// <param name="ids">The ids.</param>
        /// <returns>The array.</returns>
        public static JsonArray Array(params int[] ids)
        {
            return new JsonArray([.. ids.Select(static id => JsonValue.Create(id))]);
        }
    }
}
