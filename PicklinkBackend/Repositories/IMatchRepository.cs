using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories;

public interface IMatchRepository
{
    IQueryable<Match> Matches { get; }
    IQueryable<MatchParticipant> MatchParticipants { get; }
    IQueryable<MatchCheckIn> MatchCheckIns { get; }
    IQueryable<MatchSlotAbsence> MatchSlotAbsences { get; }
    IQueryable<MatchSlotReplacementRequest> MatchSlotReplacementRequests { get; }
    IQueryable<Scorecard> Scorecards { get; }
    IQueryable<RatingHistory> RatingHistories { get; }
    IQueryable<Booking> Bookings { get; }
    IQueryable<BookingSlot> BookingSlots { get; }
    IQueryable<BookingOperation> BookingOperations { get; }
    IQueryable<BookingCheckInGroup> BookingCheckInGroups { get; }
    IQueryable<Payment> Payments { get; }
    IQueryable<Player> Players { get; }
    IQueryable<User> Users { get; }
    IQueryable<Venue> Venues { get; }
    IQueryable<Court> Courts { get; }
    IQueryable<Conversation> Conversations { get; }
    IQueryable<ConversationParticipant> ConversationParticipants { get; }
    IQueryable<Message> Messages { get; }
    IQueryable<VenueAuditLog> VenueAuditLogs { get; }
    IQueryable<OwnerBankAccount> OwnerBankAccounts { get; }
    IQueryable<FavoriteVenue> FavoriteVenues { get; }
    IQueryable<MatchmakingQueue> MatchmakingQueues { get; }
    IQueryable<MatchmakingQueueSlot> MatchmakingQueueSlots { get; }
    IQueryable<MatchmakingQueuePlayer> MatchmakingQueuePlayers { get; }
    IQueryable<MatchAvailabilitySlot> MatchAvailabilitySlots { get; }

    Task<Match?> GetByIdAsync(int matchId, CancellationToken cancellationToken = default);
    Task AddMatchAsync(Match match, CancellationToken cancellationToken = default);
    Task AddBookingAsync(Booking booking, CancellationToken cancellationToken = default);
    Task AddParticipantAsync(MatchParticipant participant, CancellationToken cancellationToken = default);
    Task AddCheckInAsync(MatchCheckIn checkIn, CancellationToken cancellationToken = default);
    Task AddAbsenceAsync(MatchSlotAbsence absence, CancellationToken cancellationToken = default);
    Task AddReplacementRequestAsync(MatchSlotReplacementRequest request, CancellationToken cancellationToken = default);
    Task AddScorecardAsync(Scorecard scorecard, CancellationToken cancellationToken = default);
    Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken = default);
    Task AddMessageAsync(Message message, CancellationToken cancellationToken = default);
    Task AddAuditLogAsync(VenueAuditLog log, CancellationToken cancellationToken = default);
    Task AddQueueAsync(MatchmakingQueue queue, CancellationToken cancellationToken = default);
    Task AddQueuePlayerAsync(MatchmakingQueuePlayer queuePlayer, CancellationToken cancellationToken = default);
    Task RemoveQueuePlayerAsync(MatchmakingQueuePlayer queuePlayer, CancellationToken cancellationToken = default);
    Task AddConversationParticipantAsync(ConversationParticipant participant, CancellationToken cancellationToken = default);
    Task RemoveConversationParticipantAsync(ConversationParticipant participant, CancellationToken cancellationToken = default);
    Task RemoveRangeConversationParticipantsAsync(IEnumerable<ConversationParticipant> participants, CancellationToken cancellationToken = default);
    Task RemoveRangeMessagesAsync(IEnumerable<Message> messages, CancellationToken cancellationToken = default);
    Task RemoveRangeConversationsAsync(IEnumerable<Conversation> conversations, CancellationToken cancellationToken = default);
    Task RemoveRangeQueuesAsync(IEnumerable<MatchmakingQueue> queues, CancellationToken cancellationToken = default);
    Task RemoveRangeQueueSlotsAsync(IEnumerable<MatchmakingQueueSlot> queueSlots, CancellationToken cancellationToken = default);
    Task RemoveRangeMatchAvailabilitySlotsAsync(IEnumerable<MatchAvailabilitySlot> availabilitySlots, CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
