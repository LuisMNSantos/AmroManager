using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class ReservationService(IDbContextFactory<AppDbContext> dbFactory)
{
    private const string KitchenCardName = "Cartão de Acesso – Cozinha";
    private const string CinemaCardName  = "Cartão de Acesso – Cinema";

    public async Task<List<Reservation>> GetByDateAsync(DateTime localDate)
    {
        var utcStart = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Local).ToUniversalTime();
        var utcEnd   = utcStart.AddDays(1);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Reservations
            .Include(r => r.AccessCardLoan)
            .Where(r => !r.IsCancelled && r.StartTime >= utcStart && r.StartTime < utcEnd)
            .OrderBy(r => r.StartTime)
            .ToListAsync();
    }

    public async Task<(bool Success, string? Error, Reservation? Result)> CreateAsync(
        ReservationSpace space, string roomNumber, string reservedBy,
        DateTime startTime, DateTime endTime, string? notes)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var cardName = space == ReservationSpace.Cozinha ? KitchenCardName : CinemaCardName;
        var card = await db.GeneralItems
            .Include(gi => gi.Loans)
            .FirstOrDefaultAsync(gi => gi.Name == cardName);

        if (card is null)
            return (false, $"Cartão de acesso '{cardName}' não encontrado.", null);

        if (card.AvailableCount <= 0)
            return (false, "O cartão de acesso está actualmente emprestado.", null);

        var spaceLabel = space == ReservationSpace.Cozinha ? "Cozinha MasterChef" : "Cinema";
        var loan = new GeneralItemLoan
        {
            GeneralItemId = card.Id,
            RoomNumber    = roomNumber.Trim(),
            GivenBy       = reservedBy.Trim(),
            Quantity      = 1,
            Notes         = $"Reserva: {spaceLabel}",
            LoanDate      = DateTime.UtcNow
        };
        db.GeneralItemLoans.Add(loan);
        await db.SaveChangesAsync();

        var reservation = new Reservation
        {
            Space            = space,
            RoomNumber       = roomNumber.Trim(),
            ReservedBy       = reservedBy.Trim(),
            StartTime        = startTime.Kind == DateTimeKind.Utc ? startTime : startTime.ToUniversalTime(),
            EndTime          = endTime.Kind   == DateTimeKind.Utc ? endTime   : endTime.ToUniversalTime(),
            Notes            = notes,
            CreatedAt        = DateTime.UtcNow,
            AccessCardLoanId = loan.Id
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        return (true, null, reservation);
    }

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
        return true;
    }
}
