using System.ComponentModel.DataAnnotations;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using Worker = PicklinkBackend.MatchmakingWorker.MatchmakingWorker;

namespace PicklinkBackend.Tests.Services;

public class MatchmakingWorkerTests
{
    [Fact]
    public void FourSoloQueuesCanFormA2Vs2Match()
    {
        var now = DateTime.Now;
        var date = DateOnly.FromDateTime(now).AddDays(1);
        var queues = Enumerable.Range(1, 4)
            .Select(id => CreateQueue(id, date, new TimeOnly(18, 0), new TimeOnly(20, 0), id))
            .ToList();

        var found = Worker.TryFindCompatibleGroup(
            queues,
            2,
            now,
            out var matchedQueues,
            out var matchedDate,
            out var start,
            out var end);

        Assert.True(found);
        Assert.Equal(4, matchedQueues.Count);
        Assert.Equal(date, matchedDate);
        Assert.Equal(new TimeOnly(18, 0), start);
        Assert.Equal(new TimeOnly(20, 0), end);
    }

    [Fact]
    public void EightSoloQueuesCanFormAnEightPlayerMatch()
    {
        var now = DateTime.Now;
        var date = DateOnly.FromDateTime(now).AddDays(1);
        var queues = Enumerable.Range(1, 8)
            .Select(id => CreateQueue(id, date, new TimeOnly(18, 0), new TimeOnly(20, 0), id))
            .ToList();
        queues.ForEach(queue => queue.PlayerCount = 8);

        var found = Worker.TryFindCompatibleGroup(
            queues,
            2,
            now,
            out var matchedQueues,
            out _,
            out _,
            out _);

        Assert.True(found);
        Assert.Equal(8, matchedQueues.Count);
    }

    [Fact]
    public void QueueSkillRangesMustAcceptEveryOtherQueueOwner()
    {
        var now = DateTime.Now;
        var date = DateOnly.FromDateTime(now).AddDays(1);
        var queues = Enumerable.Range(1, 4)
            .Select(id => CreateQueue(id, date, new TimeOnly(18, 0), new TimeOnly(20, 0), id))
            .ToList();
        queues[0].MinSkillLevel = 4;
        queues[0].MaxSkillLevel = 5;

        var found = Worker.TryFindCompatibleGroup(
            queues,
            2,
            now,
            out _,
            out _,
            out _,
            out _);

        Assert.False(found);
    }

    [Fact]
    public void FullPublicQueueCanFormAMatchByItself()
    {
        var now = DateTime.Now;
        var queue = CreateQueue(
            1,
            DateOnly.FromDateTime(now).AddDays(1),
            new TimeOnly(18, 0),
            new TimeOnly(20, 0),
            1, 2, 3, 4);
        queue.IsPublic = true;

        var found = Worker.TryFindCompatibleGroup(
            new[] { queue },
            3,
            now,
            out var matchedQueues,
            out _,
            out _,
            out _);

        Assert.True(found);
        Assert.Single(matchedQueues);
        Assert.Same(queue, matchedQueues[0]);
    }

    [Fact]
    public void ScheduleIntersectionRequiresAtLeast90Minutes()
    {
        var now = DateTime.Now;
        var date = DateOnly.FromDateTime(now).AddDays(1);
        var first = CreateQueue(1, date, new TimeOnly(18, 0), new TimeOnly(20, 0), 1);
        var second = CreateQueue(2, date, new TimeOnly(18, 31), new TimeOnly(20, 0), 2);

        Assert.False(Worker.TryFindScheduleIntersection(
            new[] { first, second }, now, out _, out _, out _));

        second.QueueSlots.Single().TimeStart = new TimeOnly(18, 30);
        Assert.True(Worker.TryFindScheduleIntersection(
            new[] { first, second }, now, out _, out var start, out var end));
        Assert.Equal(TimeSpan.FromMinutes(90), end - start);
    }

    [Fact]
    public void RequestValidationRejectsInvalidReplayShapeAndPastTodaySlot()
    {
        var request = new JoinSoloQueueRequest
        {
            Title = "Test queue",
            PlayerCount = 4,
            MatchType = "2vs2",
            ReplayType = "Weekly",
            SearchLatitude = 21.0285,
            SearchLongitude = 105.8542,
            QueueSlots = new List<QueueSlotRequest>
            {
                new()
                {
                    SpecificDate = DateOnly.FromDateTime(DateTime.Now),
                    TimeStart = "00:00",
                    TimeEnd = "01:30"
                }
            }
        };

        var results = Validate(request);
        Assert.Contains(results, result => result.ErrorMessage!.Contains("does not match ReplayType"));

        request.ReplayType = "None";
        results = Validate(request);
        Assert.Contains(results, result => result.ErrorMessage!.Contains("starts in the past"));
    }

    [Fact]
    public void RequestValidationRejectsWideDateRangesAndOverlappingSlots()
    {
        var firstDate = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        var request = new JoinSoloQueueRequest
        {
            Title = "Test queue",
            PlayerCount = 4,
            MatchType = "2vs2",
            ReplayType = "None",
            QueueSlots =
            [
                new()
                {
                    SpecificDate = firstDate,
                    TimeStart = "18:00",
                    TimeEnd = "20:00"
                },
                new()
                {
                    SpecificDate = firstDate.AddDays(31),
                    TimeStart = "18:00",
                    TimeEnd = "20:00"
                }
            ]
        };

        var results = Validate(request);
        Assert.Contains(results, result => result.ErrorMessage!.Contains("31 consecutive dates"));

        request.QueueSlots[1].SpecificDate = firstDate;
        request.QueueSlots[1].TimeStart = "19:00";
        request.QueueSlots[1].TimeEnd = "21:00";
        results = Validate(request);
        Assert.Contains(results, result => result.ErrorMessage!.Contains("cannot overlap"));
    }

    private static MatchmakingQueue CreateQueue(
        int queueId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        params int[] playerIds)
    {
        var queue = new MatchmakingQueue
        {
            MatchmakingQueueId = queueId,
            Title = "Test queue",
            PlayerCount = 4,
            MatchType = "2vs2",
            SkillLevel = 3,
            Province = "Hà Nội",
            MinSkillLevel = 1,
            MaxSkillLevel = 5,
            Ward = "Ba Đình",
            UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
        };
        queue.QueueSlots.Add(new MatchmakingQueueSlot
        {
            SpecificDate = date,
            TimeStart = start,
            TimeEnd = end
        });

        foreach (var playerId in playerIds)
        {
            queue.QueuePlayers.Add(new MatchmakingQueuePlayer
            {
                PlayerId = playerId,
                IsHost = playerId == playerIds[0]
            });
        }

        return queue;
    }

    private static List<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return results;
    }
}
