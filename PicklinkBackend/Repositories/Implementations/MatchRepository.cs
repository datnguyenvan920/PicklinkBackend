using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.Data;
using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories.Implementations;

public class MatchRepository : IMatchRepository
{
    private readonly ApplicationDbContext _dbContext;

    public MatchRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IQueryable<Match> Matches => _dbContext.Matches;
    public IQueryable<MatchParticipant> MatchParticipants => _dbContext.MatchParticipants;
    public IQueryable<MatchCheckIn> MatchCheckIns => _dbContext.MatchCheckIns;
    public IQueryable<MatchSlotAbsence> MatchSlotAbsences => _dbContext.MatchSlotAbsences;
    public IQueryable<MatchSlotReplacementRequest> MatchSlotReplacementRequests => _dbContext.MatchSlotReplacementRequests;
    public IQueryable<Scorecard> Scorecards => _dbContext.Scorecards;
    public IQueryable<RatingHistory> RatingHistories => _dbContext.RatingHistories;
    public IQueryable<Booking> Bookings => _dbContext.Bookings;
    public IQueryable<BookingSlot> BookingSlots => _dbContext.BookingSlots;
    public IQueryable<BookingOperation> BookingOperations => _dbContext.BookingOperations;
    public IQueryable<BookingCheckInGroup> BookingCheckInGroups => _dbContext.BookingCheckInGroups;
    public IQueryable<Payment> Payments => _dbContext.Payments;
    public IQueryable<Player> Players => _dbContext.Players;
    public IQueryable<User> Users => _dbContext.Users;
    public IQueryable<Venue> Venues => _dbContext.Venues;
    public IQueryable<Court> Courts => _dbContext.Courts;
    public IQueryable<Conversation> Conversations => _dbContext.Conversations;
    public IQueryable<ConversationParticipant> ConversationParticipants => _dbContext.ConversationParticipants;
    public IQueryable<Message> Messages => _dbContext.Messages;
    public IQueryable<VenueAuditLog> VenueAuditLogs => _dbContext.VenueAuditLogs;
    public IQueryable<OwnerBankAccount> OwnerBankAccounts => _dbContext.OwnerBankAccounts;
    public IQueryable<FavoriteVenue> FavoriteVenues => _dbContext.FavoriteVenues;
    public IQueryable<MatchmakingQueue> MatchmakingQueues => _dbContext.MatchmakingQueues;
    public IQueryable<MatchmakingQueueSlot> MatchmakingQueueSlots => _dbContext.MatchmakingQueueSlots;
    public IQueryable<MatchmakingQueuePlayer> MatchmakingQueuePlayers => _dbContext.MatchmakingQueuePlayers;
    public IQueryable<MatchAvailabilitySlot> MatchAvailabilitySlots => _dbContext.MatchAvailabilitySlots;

    public Task<Match?> GetByIdAsync(int matchId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Matches
            .Include(m => m.MatchParticipants)
            .SingleOrDefaultAsync(m => m.MatchId == matchId, cancellationToken);
    }

    public async Task AddMatchAsync(Match match, CancellationToken cancellationToken = default)
    {
        await _dbContext.Matches.AddAsync(match, cancellationToken);
    }

    public async Task AddBookingAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        await _dbContext.Bookings.AddAsync(booking, cancellationToken);
    }

    public async Task AddParticipantAsync(MatchParticipant participant, CancellationToken cancellationToken = default)
    {
        await _dbContext.MatchParticipants.AddAsync(participant, cancellationToken);
    }

    public async Task AddCheckInAsync(MatchCheckIn checkIn, CancellationToken cancellationToken = default)
    {
        await _dbContext.MatchCheckIns.AddAsync(checkIn, cancellationToken);
    }

    public async Task AddAbsenceAsync(MatchSlotAbsence absence, CancellationToken cancellationToken = default)
    {
        await _dbContext.MatchSlotAbsences.AddAsync(absence, cancellationToken);
    }

    public async Task AddReplacementRequestAsync(MatchSlotReplacementRequest request, CancellationToken cancellationToken = default)
    {
        await _dbContext.MatchSlotReplacementRequests.AddAsync(request, cancellationToken);
    }

    public async Task AddScorecardAsync(Scorecard scorecard, CancellationToken cancellationToken = default)
    {
        await _dbContext.Scorecards.AddAsync(scorecard, cancellationToken);
    }

    public async Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        await _dbContext.Conversations.AddAsync(conversation, cancellationToken);
    }

    public async Task AddMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        await _dbContext.Messages.AddAsync(message, cancellationToken);
    }

    public async Task AddAuditLogAsync(VenueAuditLog log, CancellationToken cancellationToken = default)
    {
        await _dbContext.VenueAuditLogs.AddAsync(log, cancellationToken);
    }

    public async Task AddQueueAsync(MatchmakingQueue queue, CancellationToken cancellationToken = default)
    {
        await _dbContext.MatchmakingQueues.AddAsync(queue, cancellationToken);
    }

    public async Task AddQueuePlayerAsync(MatchmakingQueuePlayer queuePlayer, CancellationToken cancellationToken = default)
    {
        await _dbContext.MatchmakingQueuePlayers.AddAsync(queuePlayer, cancellationToken);
    }

    public Task RemoveQueuePlayerAsync(MatchmakingQueuePlayer queuePlayer, CancellationToken cancellationToken = default)
    {
        _dbContext.MatchmakingQueuePlayers.Remove(queuePlayer);
        return Task.CompletedTask;
    }

    public Task RemoveConversationParticipantAsync(ConversationParticipant participant, CancellationToken cancellationToken = default)
    {
        _dbContext.ConversationParticipants.Remove(participant);
        return Task.CompletedTask;
    }

    public Task RemoveRangeConversationParticipantsAsync(IEnumerable<ConversationParticipant> participants, CancellationToken cancellationToken = default)
    {
        _dbContext.ConversationParticipants.RemoveRange(participants);
        return Task.CompletedTask;
    }

    public Task RemoveRangeMessagesAsync(IEnumerable<Message> messages, CancellationToken cancellationToken = default)
    {
        _dbContext.Messages.RemoveRange(messages);
        return Task.CompletedTask;
    }

    public Task RemoveRangeConversationsAsync(IEnumerable<Conversation> conversations, CancellationToken cancellationToken = default)
    {
        _dbContext.Conversations.RemoveRange(conversations);
        return Task.CompletedTask;
    }

    public Task AddConversationParticipantAsync(ConversationParticipant participant, CancellationToken cancellationToken = default)
    {
        _dbContext.ConversationParticipants.Add(participant);
        return Task.CompletedTask;
    }

    public Task RemoveRangeQueuesAsync(IEnumerable<MatchmakingQueue> queues, CancellationToken cancellationToken = default)
    {
        _dbContext.MatchmakingQueues.RemoveRange(queues);
        return Task.CompletedTask;
    }

    public Task RemoveRangeQueueSlotsAsync(IEnumerable<MatchmakingQueueSlot> queueSlots, CancellationToken cancellationToken = default)
    {
        _dbContext.MatchmakingQueueSlots.RemoveRange(queueSlots);
        return Task.CompletedTask;
    }

    public Task RemoveRangeMatchAvailabilitySlotsAsync(IEnumerable<MatchAvailabilitySlot> availabilitySlots, CancellationToken cancellationToken = default)
    {
        _dbContext.MatchAvailabilitySlots.RemoveRange(availabilitySlots);
        return Task.CompletedTask;
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
