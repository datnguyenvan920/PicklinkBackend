using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using PicklinkBackend.Data;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Notifications;
using Worker = PicklinkBackend.Services.Matches.MatchmakingWorker;

namespace Picklink.Test.Services.Matches
{
    // Exercises the real MatchmakingWorker.ApplyMatchAsync (the code that runs after two
    // independent solo auto-match queues are found compatible) end-to-end against an
    // in-memory ApplicationDbContext, to check whether BOTH matched players actually get a
    // NotificationLog row and a realtime "Created" push - not just one of them.
    [TestFixture]
    public class MatchmakingWorkerApplyMatchTests
    {
        private ApplicationDbContext _db = null!;
        private Worker _worker = null!;
        private NotificationRealtimeNotifier _notificationRealtime = null!;

        [SetUp]
        public void Setup()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            _db = new ApplicationDbContext(options);

            _notificationRealtime = new NotificationRealtimeNotifier();
            var matchRealtime = new MatchRealtimeNotifier();
            var configuration = new ConfigurationBuilder().Build();
            var scopeFactory = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>().Object;

            _worker = new Worker(
                scopeFactory,
                configuration,
                NullLogger<Worker>.Instance,
                matchRealtime,
                _notificationRealtime);
        }

        [TearDown]
        public void TearDown()
        {
            _worker.Dispose();
            _db.Dispose();
        }

        [Test]
        public async Task ApplyMatchAsync_NotifiesEveryMatchedPlayer_NotJustOne()
        {
            // Two independent players, each with their own solo auto-match queue (IsPublic=false),
            // exactly like /api/matchmaking/join-solo would create.
            var userA = new User { Username = "playerA", Email = "a@test.com", PasswordHash = "x", UserType = "Player" };
            var userB = new User { Username = "playerB", Email = "b@test.com", PasswordHash = "x", UserType = "Player" };
            _db.Users.AddRange(userA, userB);
            await _db.SaveChangesAsync();

            var playerA = new Player { UserId = userA.UserId, SkillLevel = 3 };
            var playerB = new Player { UserId = userB.UserId, SkillLevel = 3 };
            _db.Players.AddRange(playerA, playerB);
            await _db.SaveChangesAsync();

            var queueA = new MatchmakingQueue
            {
                Title = "Queue A",
                MatchType = "1vs1",
                PlayerCount = 2,
                SkillLevel = 3,
                MinSkillLevel = 1,
                MaxSkillLevel = 5,
                IsActive = true,
                IsPublic = false,
            };
            queueA.QueuePlayers.Add(new MatchmakingQueuePlayer { PlayerId = playerA.PlayerId, IsHost = true });

            var queueB = new MatchmakingQueue
            {
                Title = "Queue B",
                MatchType = "1vs1",
                PlayerCount = 2,
                SkillLevel = 3,
                MinSkillLevel = 1,
                MaxSkillLevel = 5,
                IsActive = true,
                IsPublic = false,
            };
            queueB.QueuePlayers.Add(new MatchmakingQueuePlayer { PlayerId = playerB.PlayerId, IsHost = true });

            _db.MatchmakingQueues.AddRange(queueA, queueB);
            await _db.SaveChangesAsync();

            // Subscribe both users to the realtime notifier BEFORE the match is applied,
            // exactly like each player's browser tab holding open its own SSE connection.
            using var subA = _notificationRealtime.Subscribe(userA.UserId);
            using var subB = _notificationRealtime.Subscribe(userB.UserId);

            // Reload the way MatchmakingWorker.RunMatchmakingScanCoreAsync does, with the same Includes.
            var queues = await _db.MatchmakingQueues
                .Include(q => q.QueueSlots)
                .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
                .OrderBy(q => q.MatchmakingQueueId)
                .ToListAsync();

            var date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            var start = new TimeOnly(18, 0);
            var end = new TimeOnly(19, 0);

            var method = typeof(Worker).GetMethod("ApplyMatchAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var task = (Task<bool>)method.Invoke(_worker, new object[] { _db, queues, date, start, end, CancellationToken.None })!;
            var success = await task;

            Assert.That(success, Is.True, "ApplyMatchAsync should have succeeded.");

            var notifications = await _db.NotificationLogs.ToListAsync();
            Console.WriteLine($"NotificationLog rows created: {notifications.Count}");
            foreach (var n in notifications)
                Console.WriteLine($"  UserId={n.UserId} Title=\"{n.Title}\" LinkTo={n.LinkTo}");

            Assert.That(notifications.Select(n => n.UserId).Distinct().OrderBy(x => x),
                Is.EqualTo(new[] { userA.UserId, userB.UserId }.OrderBy(x => x)),
                "Both matched players should get a NotificationLog row - not just one of them.");

            var gotA = subA.Reader.TryRead(out var eventA);
            var gotB = subB.Reader.TryRead(out var eventB);
            Console.WriteLine($"Realtime push reached A: {gotA} ({eventA})");
            Console.WriteLine($"Realtime push reached B: {gotB} ({eventB})");

            Assert.That(gotA, Is.True, "Player A should receive a realtime 'match found' push.");
            Assert.That(gotB, Is.True, "Player B should receive a realtime 'match found' push.");
        }
    }
}
