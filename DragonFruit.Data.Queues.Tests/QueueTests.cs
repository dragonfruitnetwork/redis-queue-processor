// Copyright (c) DragonFruit Network <inbox@dragonfruit.network>
// Licensed under MIT. Refer to the LICENSE file for more info

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DragonFruit.Data.Queues.Jobs;
using DragonFruit.Data.Queues.Tests.Jobs;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using StackExchange.Redis;

namespace DragonFruit.Data.Queues.Tests
{
    [TestFixture]
    public class QueueTests
    {
        private ServiceProvider _services;

        [OneTimeSetUp]
        public async Task InitiailiseServices()
        {
            var builder = new ServiceCollection();

            var redisConnectionInfo = new ConfigurationOptions
            {
                Ssl = false,
                EndPoints = new EndPointCollection
                {
                    new IPEndPoint(IPAddress.Loopback, 6379)
                }
            };

            // queue processors must be registered after redis
            builder.AddSingleton<IConnectionMultiplexer>(await ConnectionMultiplexer.ConnectAsync(redisConnectionInfo).ConfigureAwait(false));
            builder.AddQueueProcessor("queues:generic-queue", 5);

            _services = builder.BuildServiceProvider();

            // because we're not actually a hosted service runner, we need to manually start the processor...
            var queue = _services.GetRequiredService<QueueProcessor<Job>>();

            // and as this is a test jobs need to be manually registered
            queue.RegisterJobs(GetType().Assembly);

            await queue.Clear();
            await queue.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task TestGenericQueue()
        {
            var tasks = Enumerable.Range(0, 1000).Select(_ => new IncrementCounterJob(Random.Shared.Next(1, 15))).ToArray();

            // send all tasks
            await _services.GetRequiredService<QueueProcessor<Job>>().EnqueueAsync(tasks).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            var redis = _services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
            var value = await redis.StringGetAsync(IncrementCounterJob.RedisKey).ContinueWith(t => int.Parse(t.Result)).ConfigureAwait(false);

            Assert.That(value, Is.EqualTo(tasks.Sum(x => x.Value)));
        }

        [OneTimeTearDown]
        public async Task Cleanup()
        {
            await _services.GetRequiredService<QueueProcessor<Job>>().StopAsync(CancellationToken.None).ConfigureAwait(false);
            await _services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().KeyDeleteAsync(IncrementCounterJob.RedisKey).ConfigureAwait(false);

            await _services.DisposeAsync().ConfigureAwait(false);
        }
    }
}
