// Copyright (c) DragonFruit Network <inbox@dragonfruit.network>
// Licensed under MIT. Refer to the LICENSE file for more info

using System;
using System.Threading.Tasks;
using DragonFruit.Data.Queues.Jobs;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace DragonFruit.Data.Queues.Tests.Jobs
{
    public class SetSwitchJob : Job
    {
        internal const string RedisKey = "test-switch";

        public override Task Perform(IServiceProvider scope)
        {
            var redis = scope.GetRequiredService<IConnectionMultiplexer>();
            return redis.GetDatabase().StringSetAsync(RedisKey, "1");
        }
    }
}
