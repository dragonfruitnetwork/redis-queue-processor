// Copyright (c) DragonFruit Network <inbox@dragonfruit.network>
// Licensed under MIT. Refer to the LICENSE file for more info

using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DragonFruit.Data.Queues.Jobs;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace DragonFruit.Data.Queues.Tests.Jobs
{
    [JobTypeId("counter")]
    public class IncrementCounterJob : Job
    {
        internal const string RedisKey = "test-counter";

        public override bool AllowDuplicates => true;

        public IncrementCounterJob(int value = 1)
        {
            Value = value;
        }

        [JsonPropertyName("value")]
        public int Value { get; set; }

        public override Task Perform(IServiceProvider scope)
        {
            var redis = scope.GetRequiredService<IConnectionMultiplexer>();
            return redis.GetDatabase().StringIncrementAsync(RedisKey, Value);
        }
    }
}
