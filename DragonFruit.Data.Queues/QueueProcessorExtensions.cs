// Copyright (c) DragonFruit Network <inbox@dragonfruit.network>
// Licensed under MIT. Refer to the LICENSE file for more info

using System.Reflection;
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
        /// <param name="batchSize">The size of batches to be retrieved from the queue. Defaults to 1</param>
        /// <param name="databaseId">The id of the database to listen for changes on. Defaults to database 0</param>
        public static void AddQueueProcessor(this IServiceCollection services, string queueKey, int batchSize = 1, int databaseId = 0)
        {
            AddQueueProcessor<Job>(services, queueKey, batchSize, databaseId);
        }

        /// <summary>
        /// Registers a dedicated type queue processor
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <param name="queueKey">The key of the redis object</param>
        /// <param name="batchSize">The size of batches to be retrieved from the queue. Defaults to 1</param>
        /// <param name="databaseId">The id of the database to listen for changes on. Defaults to database 0</param>
        public static void AddQueueProcessor<T>(this IServiceCollection services, string queueKey, int batchSize = 1, int databaseId = 0) where T : Job
        {
            services.AddSingleton(s =>
            {
                var redis = s.GetRequiredService<IConnectionMultiplexer>();
                var scope = s.GetRequiredService<IServiceScopeFactory>();
                var logger = s.GetService<ILogger<QueueProcessor<T>>>();

                var queue = new QueueProcessor<T>(logger, scope, redis, queueKey, databaseId) { MaxConcurrency = batchSize };

                if (typeof(T) == typeof(Job))
                {
                    queue.RegisterJobs(Assembly.GetEntryAssembly());
                }
                else
                {
                    queue.RegisterJob(typeof(T));
                }

                return queue;
            });

            services.AddHostedService(s => s.GetRequiredService<QueueProcessor<T>>());
        }
    }
}
