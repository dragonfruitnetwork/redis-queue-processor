// Copyright (c) DragonFruit Network <inbox@dragonfruit.network>
// Licensed under MIT. Refer to the LICENSE file for more info

using DragonFruit.Data.Queues.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DragonFruit.Data.Queues
{
    public static class QueueProcessorExtensions
    {
        /// <summary>
        /// Registers a generic queue processor
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <param name="queueKey">The key of the redis object</param>
        /// <param name="databaseId">The id of the database to listen for changes on. Defaults to database 0</param>
        public static void AddQueueProcessor(this IServiceCollection services, string queueKey, int databaseId = 0)
        {
            AddQueueProcessor<Job>(services, queueKey, databaseId);
        }

        /// <summary>
        /// Registers a dedicated type queue processor
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <param name="queueKey">The key of the redis object</param>
        /// <param name="databaseId">The id of the database to listen for changes on. Defaults to database 0</param>
        public static void AddQueueProcessor<T>(this IServiceCollection services, string queueKey, int databaseId = 0) where T : Job
        {
            services.AddSingleton(s =>
            {
                var logger = s.GetRequiredService<ILogger<QueueProcessor<T>>>();
                var redis = s.GetRequiredService<IConnectionMultiplexer>();
                var scope = s.GetRequiredService<IServiceScopeFactory>();

                return new QueueProcessor<T>(logger, scope, redis, queueKey, databaseId);
            });

            services.AddHostedService(s => s.GetRequiredService<QueueProcessor<T>>());
        }
    }
}
