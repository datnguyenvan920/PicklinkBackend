using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Owner;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Owner.Implementations;

public sealed class OwnerOperationQueryService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IUserRepository _userRepository;
    private readonly IPaymentRepository _paymentRepository;

    public OwnerOperationQueryService(
        IBookingRepository bookingRepository,
        IUserRepository userRepository,
        IPaymentRepository paymentRepository)
    {
        _bookingRepository = bookingRepository;
        _userRepository = userRepository;
        _paymentRepository = paymentRepository;
    }

    public async Task<OwnerOperationResult<PaginatedResponse<OwnerBookingResponse>>> ListBookingsAsync(
        DateOnly? from,
        DateOnly? to,
        string? status,
        string? search,
        string? bookingType,
        int page,
        int pageSize,
        int? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (ownerUserId is null) return OwnerOperationResult<PaginatedResponse<OwnerBookingResponse>>.Unauthorized();

        var query = _bookingRepository.Bookings
            .AsNoTracking()
            .Where(item => item.PlayerId != null && item.Court.Venue.Owner.UserId == ownerUserId.Value);
        if (bookingType?.Equals("regular", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(item => item.MatchId == null &&
                item.Payments.Any(payment => payment.SubmittedAt != null
                    || payment.PaidAt != null
                    || payment.Status == "Paid"
                    || payment.Status == "WaitingForConfirmation"
                    || payment.Status == "RefundPending"));
        else if (bookingType?.Equals("match", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(item => item.MatchId != null &&
                item.Payments.Any(payment => payment.SubmittedAt != null
                    || payment.PaidAt != null
                    || payment.Status == "Paid"
                    || payment.Status == "WaitingForConfirmation"
                    || payment.Status == "RefundPending"));
        if (from.HasValue)
        {
            var start = VietnamTime.ToUtc(from.Value.ToDateTime(TimeOnly.MinValue));
            query = query.Where(item => item.CreatedAt >= start);
        }
        if (to.HasValue)
        {
            var end = VietnamTime.ToUtc(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
            query = query.Where(item => item.CreatedAt < end);
        }
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(item => item.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(item =>
                (item.BookingCode != null && item.BookingCode.Contains(keyword)) ||
                item.Player!.User.Username.Contains(keyword) ||
                item.Court.Venue.VenueName.Contains(keyword));
        }

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var localNow = VietnamTime.Now;
        var orderedQuery = query.OrderByDescending(item => item.CreatedAt);
        var bookings = await orderedQuery
            .ThenByDescending(item => item.BookingId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new OwnerBookingResponse
            {
                BookingId = item.BookingId,
                MatchId = item.MatchId,
                MatchType = item.Match == null ? null : item.Match.MatchType,
                RequiredPlayerCount = item.Match == null ? null : item.Match.RequiredPlayerCount,
                AcceptedPlayerCount = item.Match == null
                    ? null
                    : item.Match.MatchParticipants.Count(participant => participant.Status == "Approved" || participant.Status == "Accepted"),
                MatchPlayers = item.Match == null
                    ? new List<OwnerMatchPlayerResponse>()
                    : item.Match.MatchParticipants
                        .Where(participant => participant.Status == "Approved" || participant.Status == "Accepted")
                        .OrderByDescending(participant => participant.IsHost)
                        .ThenBy(participant => participant.RequestedAt)
                        .Select(participant => new OwnerMatchPlayerResponse
                        {
                            PlayerId = participant.PlayerId,
                            UserId = participant.Player.UserId,
                            PlayerName = participant.Player.User.Username,
                            IsHost = participant.IsHost,
                            PaymentStatus = item.Payments
                                .Where(payment => payment.PayerId == participant.PlayerId)
                                .OrderByDescending(payment => payment.PaymentId)
                                .Select(payment => payment.Status)
                                .FirstOrDefault() ?? "Pending"
                        })
                        .ToList(),
                BookingCode = item.BookingCode ?? string.Empty,
                BookingStatus = item.Status,
                CheckInStatus = item.Status == "Cancelled" || item.Status == "Expired"
                    ? "Cancelled"
                    : item.Operation != null
                        ? item.Operation.CheckInStatus
                        : item.Status == "Confirmed" && localNow >= item.StartTime.AddMinutes(-30)
                            ? "Ready"
                            : "NotOpen",
                PaymentStatus = item.Payments
                    .OrderByDescending(payment => payment.Status == "WaitingForConfirmation")
                    .ThenByDescending(payment => payment.Status == "Pending")
                    .ThenByDescending(payment => payment.Status == "Paid")
                    .ThenByDescending(payment => payment.SubmittedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.Status).FirstOrDefault() ?? "Pending",
                PaymentMethod = item.Payments
                    .OrderByDescending(payment => payment.Status == "WaitingForConfirmation")
                    .ThenByDescending(payment => payment.SubmittedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.PaymentMethod).FirstOrDefault(),
                PaymentId = item.Payments
                    .OrderByDescending(payment => payment.Status == "WaitingForConfirmation")
                    .ThenByDescending(payment => payment.SubmittedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => (int?)payment.PaymentId).FirstOrDefault(),
                TotalAmount = item.TotalAmount,
                RefundAmount = item.Payments.Where(payment => payment.Status == "RefundPending" || payment.Status == "Refunded").Sum(payment => payment.Amount),
                CourtAmount = item.CourtAmount,
                HourlyPrice = item.HourlyPriceSnapshot,
                VenueId = item.Court.VenueId,
                VenueName = item.Court.Venue.VenueName,
                VenuePhone = item.Court.Venue.PhoneNumber,
                Address = item.Court.Venue.Address,
                CourtId = item.CourtId,
                CourtNumber = item.Court.CourtNumber,
                PlayerName = item.Player!.User.Username,
                PlayerEmail = item.Player.User.Email,
                PlayerCity = item.Player.User.City,
                PlayerCommune = item.Player.User.Commune,
                StartTime = item.StartTime,
                EndTime = item.EndTime,
                CreatedAt = item.CreatedAt,
                HoldExpiresAt = item.HoldExpiresAt,
                CodeVerifiedAt = item.Operation == null ? null : item.Operation.CodeVerifiedAt,
                PaymentConfirmedAt = item.Operation == null ? null : item.Operation.PaymentConfirmedAt,
                CheckedInAt = item.Operation == null ? null : item.Operation.CheckedInAt,
                NoShowAt = item.Operation == null ? null : item.Operation.NoShowAt,
                PaymentPaidAt = item.Payments.OrderByDescending(payment => payment.PaidAt)
                    .Select(payment => payment.PaidAt).FirstOrDefault(),
                PaymentVerifiedAt = item.Payments.OrderByDescending(payment => payment.VerifiedAt)
                    .Select(payment => payment.VerifiedAt).FirstOrDefault(),
                TransferCode = item.Payments
                    .OrderByDescending(payment => payment.Status == "WaitingForConfirmation")
                    .ThenByDescending(payment => payment.SubmittedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.TransferCode).FirstOrDefault(),
                ReceiptImageUrl = item.Payments
                    .Where(payment => payment.ReceiptImageUrl != null)
                    .OrderByDescending(payment => payment.SubmittedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.ReceiptImageUrl).FirstOrDefault(),
                RejectionReason = item.Payments
                    .Where(payment => payment.RejectionReason != null)
                    .OrderByDescending(payment => payment.VerifiedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.RejectionReason).FirstOrDefault(),
                Slots = item.Slots.OrderBy(slot => slot.StartTime).ThenBy(slot => slot.CourtId).Select(slot => new OwnerBookingSlotResponse
                {
                    BookingSlotId = slot.BookingSlotId,
                    CourtId = slot.CourtId,
                    CourtNumber = slot.Court.CourtNumber,
                    StartTime = slot.StartTime,
                    EndTime = slot.EndTime,
                    CourtAmount = slot.CourtAmount
                }).ToList()
            })
            .ToListAsync(cancellationToken);
        var bookingIds = bookings.Select(item => item.BookingId).ToArray();
        var checkInGroups = (await _bookingRepository.BookingCheckInGroups.AsNoTracking()
            .Where(group => bookingIds.Contains(group.BookingId))
            .Select(group => new { group.BookingId, group.StartTime, group.EndTime, group.CheckInStatus })
            .ToListAsync(cancellationToken))
            .ToLookup(group => group.BookingId);

        foreach (var booking in bookings)
        {
            if (string.IsNullOrWhiteSpace(booking.BookingCode)) booking.BookingCode = $"PL-{booking.BookingId}";
            booking.CheckInStatus = BookingOccurrencePolicy.GetCheckInStatus(
                booking.BookingStatus,
                booking.CheckInStatus,
                checkInGroups[booking.BookingId].Select(group => new BookingOccurrence(group.StartTime, group.EndTime, group.CheckInStatus)),
                localNow,
                booking.StartTime,
                booking.EndTime);
            NormalizeBookingDates(booking);
        }

        return OwnerOperationResult<PaginatedResponse<OwnerBookingResponse>>.Success(
            Pagination.Create(bookings, totalCount, page, pageSize));
    }

    public async Task<OwnerOperationResult<OwnerBookingResponse>> GetBookingAsync(
        int bookingId,
        int? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (ownerUserId is null) return OwnerOperationResult<OwnerBookingResponse>.Unauthorized();

        var booking = await BookingQuery(ownerUserId.Value)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return OwnerOperationResult<OwnerBookingResponse>.NotFound("Không tìm thấy booking thuộc cụm sân của Owner.");

        var actorIds = booking.StatusHistories.Select(item => item.ActorUserId)
            .Concat(booking.Payments.SelectMany(item => item.StatusHistories).Select(item => item.ActorUserId))
            .Concat(new[]
            {
                booking.Operation?.CodeVerifiedByUserId,
                booking.Operation?.PaymentConfirmedByUserId,
                booking.Operation?.CheckedInByUserId,
                booking.Operation?.NoShowByUserId
            })
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .Distinct()
            .ToList();
        var actors = await _userRepository.Users.AsNoTracking()
            .Where(item => actorIds.Contains(item.UserId))
            .ToDictionaryAsync(item => item.UserId, item => item.Username, cancellationToken);

        return OwnerOperationResult<OwnerBookingResponse>.Success(MapBooking(booking, actors));
    }

    public async Task<OwnerOperationResult<OwnerRevenueReportResponse>> GetRevenueReportAsync(
        DateOnly from,
        DateOnly to,
        string? source,
        int? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
            return OwnerOperationResult<OwnerRevenueReportResponse>.BadRequest("Khoảng báo cáo phải từ 1 đến 367 ngày.");
        if (ownerUserId is null) return OwnerOperationResult<OwnerRevenueReportResponse>.Unauthorized();

        var wantsCourt = string.IsNullOrWhiteSpace(source) || source.Equals("Court", StringComparison.OrdinalIgnoreCase);
        var wantsMatch = string.IsNullOrWhiteSpace(source) || source.Equals("Match", StringComparison.OrdinalIgnoreCase);
        var wantsTicket = string.IsNullOrWhiteSpace(source) || source.Equals("Ticket", StringComparison.OrdinalIgnoreCase);

        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        // "Doanh thu" is money received, not court usage — a booking can be paid well before or
        // (for slow bank-transfer review) somewhat after its play date, so records are attributed to
        // PaidAt (falling back to CreatedAt while still unpaid). PaidAt isn't indexed/filterable at
        // the DB level here, so the fetch first grabs a generously padded window by play date (the
        // one bounded column available) and then the precise payment-date filter runs in memory.
        var fetchStart = start.AddDays(-90);
        var fetchEnd = end.AddDays(90);

        var records = wantsCourt || wantsMatch
            ? (await BookingQuery(ownerUserId.Value, includeHistory: false)
                .Include(item => item.Payments).ThenInclude(item => item.StatusHistories)
                .Where(item => item.StartTime >= fetchStart && item.StartTime < fetchEnd)
                .OrderBy(item => item.StartTime)
                .ToListAsync(cancellationToken))
                .Where(item => (wantsCourt && item.MatchId is null) || (wantsMatch && item.MatchId is not null))
                .Where(item => RefundAwareRevenueDate(SelectRepresentativePayment(item), item.CreatedAt) is var date && date >= start && date < end)
                .Select(item => MapBooking(item))
                .ToList()
            : new List<OwnerBookingResponse>();
        var paid = records.Where(item => item.PaymentStatus == "Paid" && item.BookingStatus != "Cancelled").ToList();

        // Ticket-session sales aren't regular bookings (one booking can have many buyers, each with
        // their own payment), so they're aggregated separately here and folded into the same totals.
        var tickets = wantsTicket
            ? (await _paymentRepository.SessionTickets.AsNoTracking()
                .Include(item => item.Payment).ThenInclude(item => item.StatusHistories)
                .Include(item => item.Player).ThenInclude(item => item.User)
                .Include(item => item.TicketSession).ThenInclude(item => item.Booking).ThenInclude(item => item.Court)
                    .ThenInclude(item => item.Venue).ThenInclude(item => item.Owner)
                .Where(item => item.TicketSession.Booking.Court.Venue.Owner.UserId == ownerUserId.Value
                    && item.TicketSession.Booking.StartTime >= fetchStart
                    && item.TicketSession.Booking.StartTime < fetchEnd)
                .ToListAsync(cancellationToken))
                .Where(item => RefundAwareRevenueDate(item.Payment, item.CreatedAt) is var date && date >= start && date < end)
                .ToList()
            : new List<SessionTicket>();
        // Cancelled-after-payment tickets stay "Paid" (tickets are non-refundable), so they still count.
        var paidTickets = tickets.Where(item => item.Payment.Status == "Paid").ToList();
        var pendingTicketAmount = tickets
            .Where(item => item.Payment.Status is "Pending" or "WaitingForConfirmation")
            .Sum(item => item.Payment.Amount);
        var refundedTicketAmount = tickets
            .Where(item => item.Payment.Status is "RefundPending" or "Refunded")
            .Sum(item => item.Payment.Amount);
        var cancelledTicketCount = tickets.Count(item => item.Status is "Cancelled" or "Expired");

        var gross = paid.Sum(item => item.TotalAmount) + paidTickets.Sum(item => item.Payment.Amount);
        var paidCount = paid.Count + paidTickets.Count;

        var dailyTotals = paid
            .GroupBy(item => DateOnly.FromDateTime(RevenueDate(item.PaymentPaidAt, item.CreatedAt)))
            .ToDictionary(group => group.Key, group => (Revenue: group.Sum(item => item.TotalAmount), Count: group.Count()));
        foreach (var ticketGroup in paidTickets.GroupBy(item => DateOnly.FromDateTime(RevenueDate(item.Payment.PaidAt, item.CreatedAt))))
        {
            var ticketRevenue = ticketGroup.Sum(item => item.Payment.Amount);
            dailyTotals[ticketGroup.Key] = dailyTotals.TryGetValue(ticketGroup.Key, out var existing)
                ? (existing.Revenue + ticketRevenue, existing.Count + ticketGroup.Count())
                : (ticketRevenue, ticketGroup.Count());
        }

        return OwnerOperationResult<OwnerRevenueReportResponse>.Success(new OwnerRevenueReportResponse
        {
            From = from,
            To = to,
            GrossRevenue = gross,
            PaidBookings = paidCount,
            PendingAmount = records.Where(item => item.PaymentStatus is "Pending" or "WaitingForConfirmation").Sum(item => item.TotalAmount)
                + pendingTicketAmount,
            RefundedAmount = records.Sum(item => item.RefundAmount) + refundedTicketAmount,
            CancelledBookings = records.Count(item => item.BookingStatus is "Cancelled" or "Expired") + cancelledTicketCount,
            AverageBookingValue = paidCount == 0 ? 0 : gross / paidCount,
            Daily = dailyTotals.Select(entry => new OwnerDailyRevenueResponse
            {
                Date = entry.Key,
                Revenue = entry.Value.Revenue,
                BookingCount = entry.Value.Count
            }).OrderBy(item => item.Date).ToList(),
            Bookings = records,
            Tickets = tickets.Select(MapTicketRevenue).ToList()
        });
    }

    // Money in hand (PaidAt) is the revenue date; before that happens, the record is attributed to
    // when it was opened (CreatedAt) so still-pending amounts land somewhere sensible.
    private static DateTime RevenueDate(DateTime? paidAt, DateTime createdAt) => paidAt ?? createdAt;

    // A payment that has since moved to RefundPending/Refunded must stay attributed to the period the
    // refund obligation actually landed in, not the (possibly much earlier) original PaidAt — otherwise
    // a refund that arises this period for a booking paid last period silently disappears from both
    // periods' reports. Falls back to the plain PaidAt/CreatedAt rule when there's no refund in play.
    private static DateTime RefundAwareRevenueDate(Payment? payment, DateTime createdAt)
    {
        if (payment is null) return createdAt;
        if (payment.Status is "RefundPending" or "Refunded")
        {
            var refundAt = payment.StatusHistories
                .Where(history => history.ToStatus is "RefundPending" or "Refunded")
                .OrderByDescending(history => history.CreatedAt)
                .Select(history => (DateTime?)history.CreatedAt)
                .FirstOrDefault();
            if (refundAt is not null) return refundAt.Value;
        }
        return RevenueDate(payment.PaidAt, createdAt);
    }

    private static OwnerTicketRevenueResponse MapTicketRevenue(SessionTicket ticket) => new()
    {
        SessionTicketId = ticket.SessionTicketId,
        TicketSessionId = ticket.TicketSessionId,
        TicketCode = ticket.TicketCode,
        Status = ticket.Status,
        PaymentStatus = ticket.Payment.Status,
        PaymentMethod = ticket.Payment.PaymentMethod,
        Amount = ticket.Payment.Amount,
        RefundAmount = ticket.Payment.Status is "RefundPending" or "Refunded" ? ticket.Payment.Amount : 0,
        SessionTitle = ticket.TicketSession.Title,
        PlayerName = ticket.Player.User.Username,
        PlayerEmail = ticket.Player.User.Email,
        VenueId = ticket.TicketSession.Booking.Court.VenueId,
        VenueName = ticket.TicketSession.Booking.Court.Venue.VenueName,
        VenueAddress = ticket.TicketSession.Booking.Court.Venue.Address,
        CourtId = ticket.TicketSession.Booking.CourtId,
        CourtNumber = ticket.TicketSession.Booking.Court.CourtNumber,
        StartTime = ticket.TicketSession.Booking.StartTime,
        EndTime = ticket.TicketSession.Booking.EndTime,
        CreatedAt = ticket.CreatedAt,
        PaymentPaidAt = ticket.Payment.PaidAt
    };

    private IQueryable<Booking> BookingQuery(int userId, bool includeHistory = true)
    {
        // Walk-ins taken at the counter are real bookings even when the customer has no account,
        // so they belong in the owner's booking lists and revenue alongside online ones.
        IQueryable<Booking> query = _bookingRepository.Bookings.AsNoTracking()
            .AsSplitQuery()
            .Where(item => (item.PlayerId != null
                    || item.OwnerEntryType == OwnerScheduleEntry.WalkInPaid
                    || item.OwnerEntryType == OwnerScheduleEntry.WalkInUnpaid)
                && item.Court.Venue.Owner.UserId == userId)
            .Include(item => item.Operation)
            .Include(item => item.Slots).ThenInclude(slot => slot.Court)
            .Include(item => item.CheckInGroups).ThenInclude(group => group.Court)
            .Include(item => item.Payments)
            .Include(item => item.Player).ThenInclude(item => item!.User)
            .Include(item => item.Match).ThenInclude(item => item!.MatchParticipants)
                .ThenInclude(item => item.Player).ThenInclude(item => item.User)
            .Include(item => item.Court).ThenInclude(item => item.Venue);

        if (includeHistory)
        {
            query = query
                .Include(item => item.StatusHistories)
                .Include(item => item.Payments).ThenInclude(item => item.StatusHistories);
        }

        return query;
    }

    // Priority order for picking the one payment that represents the whole booking (a match booking
    // can have one Payment row per participant). RefundPending/Refunded must outrank dead-end statuses
    // like Expired/Cancelled/Failed — otherwise a booking where the payer who actually paid ended up
    // refunded, while another payer's payment simply expired unpaid, gets tie-broken by PaymentId onto
    // the expired row and the refund vanishes from the owner's view entirely (paymentStatus="Expired"
    // instead of "Refunded").
    private static Payment? SelectRepresentativePayment(Booking booking) =>
        booking.Payments
            .OrderByDescending(item => item.Status == "RefundPending")
            .ThenByDescending(item => item.Status == "WaitingForConfirmation")
            .ThenByDescending(item => item.Status == "Pending")
            .ThenByDescending(item => item.Status == "Paid")
            .ThenByDescending(item => item.Status == "Refunded")
            .ThenByDescending(item => item.SubmittedAt)
            .ThenByDescending(item => item.PaymentId)
            .FirstOrDefault();

    private static OwnerBookingResponse MapBooking(Booking booking, IReadOnlyDictionary<int, string>? actors = null)
    {
        var payment = SelectRepresentativePayment(booking);
        var localNow = VietnamTime.Now;
        var checkInStatus = BookingOccurrencePolicy.GetCheckInStatus(
            booking.Status,
            booking.Operation?.CheckInStatus,
            booking.CheckInGroups.Select(group => new BookingOccurrence(group.StartTime, group.EndTime, group.CheckInStatus)),
            localNow,
            booking.StartTime,
            booking.EndTime);
        return new OwnerBookingResponse
        {
            BookingId = booking.BookingId,
            MatchId = booking.MatchId,
            MatchType = booking.Match?.MatchType,
            RequiredPlayerCount = booking.Match?.RequiredPlayerCount,
            AcceptedPlayerCount = booking.Match?.MatchParticipants.Count(item => item.Status == "Approved" || item.Status == "Accepted"),
            MatchPlayers = booking.Match?.MatchParticipants
                .Where(item => item.Status == "Approved" || item.Status == "Accepted")
                .OrderByDescending(item => item.IsHost)
                .ThenBy(item => item.RequestedAt)
                .Select(item => new OwnerMatchPlayerResponse
                {
                    PlayerId = item.PlayerId,
                    PlayerName = item.Player.User.Username,
                    IsHost = item.IsHost,
                    PaymentStatus = booking.Payments
                        .Where(paymentItem => paymentItem.PayerId == item.PlayerId)
                        .OrderByDescending(paymentItem => paymentItem.PaymentId)
                        .Select(paymentItem => paymentItem.Status)
                        .FirstOrDefault() ?? "Pending"
                })
                .ToList() ?? new List<OwnerMatchPlayerResponse>(),
            BookingCode = booking.BookingCode ?? $"PL-{booking.BookingId}",
            BookingStatus = booking.Status,
            CheckInStatus = checkInStatus,
            PaymentStatus = payment?.Status
                ?? OwnerScheduleEntry.ImpliedPaymentStatus(booking.OwnerEntryType)
                ?? "Pending",
            PaymentMethod = payment?.PaymentMethod,
            PaymentId = payment?.PaymentId,
            TotalAmount = booking.TotalAmount,
            RefundAmount = booking.Payments.Where(item => item.Status is "RefundPending" or "Refunded").Sum(item => item.Amount),
            CourtAmount = booking.CourtAmount,
            HourlyPrice = booking.HourlyPriceSnapshot,
            VenueId = booking.Court.VenueId,
            VenueName = booking.Court.Venue.VenueName,
            VenuePhone = booking.Court.Venue.PhoneNumber,
            Address = booking.Court.Venue.Address,
            CourtId = booking.CourtId,
            CourtNumber = booking.Court.CourtNumber,
            PlayerName = booking.Player?.User.Username ?? booking.Title ?? "Khach",
            PlayerEmail = booking.Player?.User.Email,
            PlayerCity = booking.Player?.User.City,
            PlayerCommune = booking.Player?.User.Commune,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            CreatedAt = AsUtc(booking.CreatedAt),
            HoldExpiresAt = AsUtc(booking.HoldExpiresAt),
            CodeVerifiedAt = AsUtc(booking.Operation?.CodeVerifiedAt),
            PaymentConfirmedAt = AsUtc(booking.Operation?.PaymentConfirmedAt),
            CheckedInAt = AsUtc(booking.Operation?.CheckedInAt),
            NoShowAt = AsUtc(booking.Operation?.NoShowAt),
            CodeVerifiedBy = ActorName(booking.Operation?.CodeVerifiedByUserId, actors),
            PaymentConfirmedBy = ActorName(booking.Operation?.PaymentConfirmedByUserId, actors),
            CheckedInBy = ActorName(booking.Operation?.CheckedInByUserId, actors),
            NoShowBy = ActorName(booking.Operation?.NoShowByUserId, actors),
            PaymentPaidAt = AsUtc(payment?.PaidAt),
            PaymentVerifiedAt = AsUtc(payment?.VerifiedAt),
            TransferCode = payment?.TransferCode,
            ReceiptImageUrl = payment?.ReceiptImageUrl,
            RefundProofImageUrl = payment?.RefundProofImageUrl,
            RefundReference = payment?.RefundReference,
            RefundProofSubmittedAt = AsUtc(payment?.RefundProofSubmittedAt),
            RefundDisputeStatus = payment?.RefundDisputeStatus,
            RefundDisputeReason = payment?.RefundDisputeReason,
            RefundDisputedAt = AsUtc(payment?.RefundDisputedAt),
            RefundDisputeResolution = payment?.RefundDisputeResolution,
            RefundDisputeResolvedAt = AsUtc(payment?.RefundDisputeResolvedAt),
            RejectionReason = payment?.RejectionReason,
            BookingHistory = booking.StatusHistories.OrderBy(item => item.ChangedAt).Select(item => new OwnerBookingHistoryResponse
            {
                FromStatus = item.FromStatus,
                ToStatus = item.ToStatus,
                Reason = item.Reason,
                ActorName = ActorName(item.ActorUserId, actors),
                ChangedAt = AsUtc(item.ChangedAt)
            }).ToList(),
            Slots = booking.Slots.OrderBy(slot => slot.StartTime).ThenBy(slot => slot.CourtId).Select(slot => new OwnerBookingSlotResponse
            {
                BookingSlotId = slot.BookingSlotId,
                CourtId = slot.CourtId,
                CourtNumber = slot.Court.CourtNumber,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                CourtAmount = slot.CourtAmount
            }).ToList(),
            CheckInGroups = booking.CheckInGroups.OrderBy(group => group.StartTime).Select(group => new OwnerBookingCheckInGroupResponse
            {
                BookingCheckInGroupId = group.BookingCheckInGroupId,
                CourtId = group.CourtId,
                CourtNumber = group.Court.CourtNumber,
                StartTime = group.StartTime,
                EndTime = group.EndTime,
                CheckInStatus = group.CheckInStatus
            }).ToList(),
            PaymentHistory = payment?.StatusHistories.OrderBy(item => item.CreatedAt).Select(item => new OwnerPaymentHistoryResponse
            {
                FromStatus = item.FromStatus,
                ToStatus = item.ToStatus,
                Action = item.Action,
                Reason = item.Reason,
                ActorName = ActorName(item.ActorUserId, actors),
                CreatedAt = AsUtc(item.CreatedAt)
            }).ToList() ?? new List<OwnerPaymentHistoryResponse>()
        };
    }

    private static string? ActorName(int? actorId, IReadOnlyDictionary<int, string>? actors) =>
        actorId.HasValue && actors is not null && actors.TryGetValue(actorId.Value, out var name) ? name : null;

    private static void NormalizeBookingDates(OwnerBookingResponse booking)
    {
        booking.CreatedAt = AsUtc(booking.CreatedAt);
        booking.HoldExpiresAt = AsUtc(booking.HoldExpiresAt);
        booking.CodeVerifiedAt = AsUtc(booking.CodeVerifiedAt);
        booking.PaymentConfirmedAt = AsUtc(booking.PaymentConfirmedAt);
        booking.CheckedInAt = AsUtc(booking.CheckedInAt);
        booking.NoShowAt = AsUtc(booking.NoShowAt);
        booking.PaymentPaidAt = AsUtc(booking.PaymentPaidAt);
        booking.PaymentVerifiedAt = AsUtc(booking.PaymentVerifiedAt);
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
