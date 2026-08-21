using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Bookings;

/// <summary>
/// Six-character check-in code.
///
/// Staff read these off a player's phone and type them at the court, so the alphabet drops the
/// characters people mix up out loud — 0/O and 1/I — rather than chasing maximum entropy. Thirty-two
/// symbols keep each draw an unbiased five bits, giving 32^6 (about 1.07 billion) codes.
///
/// That space is small enough to matter: checkInCode is unique-indexed, and by the birthday bound a
/// clash becomes likely somewhere in the tens of thousands of codes — well inside this product's
/// life. So codes are checked against the table before use rather than trusted blind.
/// </summary>
public static class CheckInCode
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    internal const int Length = 6;
    private const int MaximumAttempts = 5;

    public static string Next()
    {
        var source = RandomNumberGenerator.GetBytes(Length);

        return string.Create(Length, source, (code, bytes) =>
        {
            for (var index = 0; index < code.Length; index++) code[index] = Alphabet[bytes[index] & 31];
        });
    }

    public static string? Compact(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Length <= Length ? code : code[^Length..];

    /// <summary>
    /// A single code confirmed free against <paramref name="existingCodes"/>. Used where one code is
    /// issued at a time, such as buying a session ticket.
    /// </summary>
    public static async Task<string> NextUniqueAsync(
        IQueryable<string> existingCodes,
        CancellationToken cancellationToken = default,
        ISet<string>? reservedCodes = null)
    {
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var code = Next();
            if (!await existingCodes.AnyAsync(existing => existing == code, cancellationToken) && (reservedCodes is null || reservedCodes.Add(code))) return code;
        }

        // ponytail: the unique index is the backstop if five draws somehow all clash.
        return Next();
    }

    /// <summary>
    /// Re-rolls any code in <paramref name="groups"/> that is already taken, or repeated within the
    /// batch, before the caller saves. Leaves the codes untouched when nothing clashes, which is the
    /// overwhelmingly common case — one query, no writes.
    /// </summary>
    public static async Task EnsureUniqueAsync(
        IReadOnlyCollection<BookingCheckInGroup> groups,
        IQueryable<BookingCheckInGroup> existingGroups,
        CancellationToken cancellationToken = default)
    {
        if (groups.Count == 0) return;

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var codes = groups.Select(group => group.CheckInCode).ToList();
            var clashing = new HashSet<string>(await existingGroups
                .Where(group => codes.Contains(group.CheckInCode))
                .Select(group => group.CheckInCode)
                .ToListAsync(cancellationToken));

            foreach (var duplicate in codes.GroupBy(code => code).Where(entry => entry.Count() > 1))
                clashing.Add(duplicate.Key);

            if (clashing.Count == 0) return;

            var reissued = false;
            foreach (var group in groups)
            {
                // Re-roll one member of each clashing pair; the other keeps the code it already has.
                if (!clashing.Remove(group.CheckInCode)) continue;
                group.CheckInCode = Next();
                reissued = true;
            }

            if (!reissued) return;
        }

        // ponytail: five rounds is already absurd at this density; the unique index is the backstop.
    }
}
