using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class ReservationService(IDbContextFactory<AppDbContext> dbFactory, IBackgroundSync? sync = null)
{
    private const string KitchenCardName = "Cartão de Acesso – Cozinha";
    private const string CinemaCardName  = "Cartão de Acesso – Cinema";

    // Returns a count of non-cancelled reservations per day for the given month.
    public async Task<Dictionary<int, int>> GetCountsByMonthAsync(int year, int month)
    {
        var utcStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var utcEnd   = utcStart.AddMonths(1);

        await using var db = await dbFactory.CreateDbContextAsync();
        var reservations = await db.Reservations
            .Where(r => !r.IsCancelled && r.StartTime < utcEnd && r.EndTime > utcStart)
            .Select(r => new { r.StartTime, r.EndTime })
            .ToListAsync();

        var counts = new Dictionary<int, int>();
        int daysInMonth = DateTime.DaysInMonth(year, month);
        for (int day = 1; day <= daysInMonth; day++)
        {
            var dayStart = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
            var dayEnd   = dayStart.AddDays(1);
            counts[day]  = reservations.Count(r => r.StartTime < dayEnd && r.EndTime > dayStart);
        }
        return counts;
    }

    // Returns all non-cancelled reservations that overlap with the given local date.
    public async Task<List<Reservation>> GetByDateAsync(DateTime localDate)
    {
        var utcStart = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Local).ToUniversalTime();
        var utcEnd   = utcStart.AddDays(1);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Reservations
            .Include(r => r.AccessCardLoan)
            .Where(r => !r.IsCancelled && r.StartTime < utcEnd && r.EndTime > utcStart)
            .OrderBy(r => r.StartTime)
            .ToListAsync();
    }

    // Looks up a non-cancelled reservation linked to a specific loan (used before the loan is returned).
    public async Task<Reservation?> GetActiveByLoanIdAsync(int loanId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Reservations
            .FirstOrDefaultAsync(r => r.AccessCardLoanId == loanId && !r.IsCancelled);
    }

    // Creates the reservation without lending the card.
    // Cozinha: validates 08:00–22:00. Cinema: allows multi-day (end date ≥ start date).
    public async Task<(bool Success, string? Error, Reservation? Result)> CreateAsync(
        ReservationSpace space, string roomNumber, string reservedBy,
        DateTime startTime, DateTime endTime, string? notes)
    {
        if (space == ReservationSpace.Cozinha)
        {
            var ls = startTime.Kind == DateTimeKind.Utc ? startTime.ToLocalTime() : startTime;
            var le = endTime.Kind   == DateTimeKind.Utc ? endTime.ToLocalTime()   : endTime;
            if (ls.Hour < 8)
                return (false, "A Cozinha MasterChef só abre às 08:00.", null);
            if (le.Hour > 22 || (le.Hour == 22 && le.Minute > 0))
                return (false, "A Cozinha MasterChef fecha às 22:00.", null);
        }

        var startUtc = startTime.Kind == DateTimeKind.Utc ? startTime : startTime.ToUniversalTime();
        var endUtc   = endTime.Kind   == DateTimeKind.Utc ? endTime   : endTime.ToUniversalTime();

        await using var db = await dbFactory.CreateDbContextAsync();

        var hasConflict = await db.Reservations.AnyAsync(r =>
            !r.IsCancelled && r.Space == space && r.StartTime < endUtc && r.EndTime > startUtc);
        if (hasConflict)
            return (false, "Já existe uma reserva para este espaço nesse horário.", null);

        var reservation = new Reservation
        {
            Space      = space,
            RoomNumber = roomNumber.Trim(),
            ReservedBy = reservedBy.Trim(),
            StartTime  = startUtc,
            EndTime    = endUtc,
            Notes      = notes,
            CreatedAt  = DateTime.UtcNow
        };

        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();
        sync?.TriggerBackgroundSync();
        return (true, null, reservation);
    }

    // Returns non-cancelled reservations for a room that haven't ended yet.
    public async Task<List<Reservation>> GetUpcomingByRoomAsync(string roomNumber)
    {
        var room = roomNumber.Trim().ToUpper();
        var utcNow = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Reservations
            .Include(r => r.AccessCardLoan)
            .Where(r => !r.IsCancelled && r.RoomNumber.ToUpper() == room && r.EndTime >= utcNow)
            .OrderBy(r => r.StartTime)
            .ToListAsync();
    }

    // Activates a scheduled reservation by lending the access card.
    public async Task<(bool Success, string? Error)> ActivateAsync(int reservationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reservation = await db.Reservations.FindAsync(reservationId);
        if (reservation is null || reservation.IsCancelled)
            return (false, "Reserva inválida.");
        if (reservation.AccessCardLoanId.HasValue)
            return (false, "O cartão já está registado para esta reserva.");

        var cardName = reservation.Space == ReservationSpace.Cozinha ? KitchenCardName : CinemaCardName;
        var card = await db.GeneralItems.Include(gi => gi.Loans)
            .FirstOrDefaultAsync(gi => gi.Name == cardName);

        if (card is null)
            return (false, $"Cartão '{cardName}' não encontrado.");
        if (card.AvailableCount <= 0)
            return (false, "O cartão de acesso está actualmente emprestado. Devolva-o primeiro.");

        var spaceLabel = reservation.Space == ReservationSpace.Cozinha ? "Cozinha MasterChef" : "Cinema";
        var loan = new GeneralItemLoan
        {
            GeneralItemId     = card.Id,
            GeneralItemSyncId = card.SyncId,
            RoomNumber        = reservation.RoomNumber,
            GivenBy           = reservation.ReservedBy,
            Quantity          = 1,
            Notes             = $"Reserva: {spaceLabel} — quarto {reservation.RoomNumber}",
            LoanDate          = DateTime.UtcNow
        };
        db.GeneralItemLoans.Add(loan);
        await db.SaveChangesAsync();

        reservation.AccessCardLoanId = loan.Id;
        reservation.IsActivated = true;
        await db.SaveChangesAsync();
        sync?.TriggerBackgroundSync();
        return (true, null);
    }

    // Returns the access card, completing the reservation.
    public async Task<bool> CompleteAsync(int reservationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reservation = await db.Reservations
            .Include(r => r.AccessCardLoan)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation?.AccessCardLoan is null || reservation.AccessCardLoan.IsReturned)
            return false;

        reservation.AccessCardLoan.ReturnDate = DateTime.UtcNow;
        reservation.IsCompleted = true;
        await db.SaveChangesAsync();
        sync?.TriggerBackgroundSync();
        return true;
    }

    // Cancels a reservation and returns the card if it was active.
    public async Task<bool> CancelAsync(int reservationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reservation = await db.Reservations
            .Include(r => r.AccessCardLoan)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation is null || reservation.IsCancelled) return false;

        reservation.IsCancelled = true;

        if (reservation.AccessCardLoanId.HasValue)
        {
            var loan = await db.GeneralItemLoans.FindAsync(reservation.AccessCardLoanId.Value);
            if (loan is not null && !loan.IsReturned)
                loan.ReturnDate = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        sync?.TriggerBackgroundSync();
        return true;
    }
}
