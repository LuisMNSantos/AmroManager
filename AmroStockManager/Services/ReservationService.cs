using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class ReservationService(SupabaseClient db)
{
    private const string KitchenCardName = "Cartão de Acesso – Cozinha";
    private const string CinemaCardName  = "Cartão de Acesso – Cinema";

    public Task<List<Reservation>> GetAsync(string table, string query)
        => db.GetAsync<Reservation>(table, query);

    public async Task<Dictionary<int, int>> GetCountsByMonthAsync(int year, int month)
    {
        var utcStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var utcEnd   = utcStart.AddMonths(1);

        var reservations = await db.GetAsync<Reservation>("reservations",
            $"is_deleted=eq.false&is_cancelled=eq.false&start_time=lt.{utcEnd:O}&end_time=gt.{utcStart:O}&select=start_time,end_time");

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

    public async Task<List<Reservation>> GetByDateAsync(DateTime localDate)
    {
        var utcStart = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Local).ToUniversalTime();
        var utcEnd   = utcStart.AddDays(1);
        var list = await db.GetAsync<Reservation>("reservations",
            $"is_deleted=eq.false&is_cancelled=eq.false&start_time=lt.{utcEnd:O}&end_time=gt.{utcStart:O}&order=start_time.asc");
        await PopulateAccessCardLoansAsync(list);
        return list;
    }

    public async Task<Reservation?> GetActiveByLoanIdAsync(string loanId)
    {
        var list = await db.GetAsync<Reservation>("reservations",
            $"is_deleted=eq.false&is_cancelled=eq.false&access_card_loan_sync_id=eq.{loanId}&limit=1");
        return list.FirstOrDefault();
    }

    public async Task<List<Reservation>> GetUpcomingByRoomAsync(string roomNumber)
    {
        var room = Uri.EscapeDataString(roomNumber.Trim().ToUpper());
        // Fetch all non-completed reservations; filter in-memory so activated-but-overdue ones are included
        var list = await db.GetAsync<Reservation>("reservations",
            $"is_deleted=eq.false&is_cancelled=eq.false&is_completed=eq.false&room_number=eq.{room}&order=start_time.asc");
        var now = DateTime.UtcNow;
        list = list.Where(r => r.EndTime > now || r.IsActivated).ToList();
        await PopulateAccessCardLoansAsync(list);
        return list;
    }

    private async Task PopulateAccessCardLoansAsync(List<Reservation> reservations)
    {
        var loanIds = reservations
            .Where(r => r.AccessCardLoanId is not null)
            .Select(r => r.AccessCardLoanId!)
            .Distinct().ToList();
        if (loanIds.Count == 0) return;

        var loans = await db.GetAsync<GeneralItemLoan>("general_item_loans",
            $"sync_id=in.({string.Join(",", loanIds)})");
        var loanById = loans.ToDictionary(l => l.Id);
        foreach (var r in reservations)
            if (r.AccessCardLoanId is not null)
                r.AccessCardLoan = loanById.GetValueOrDefault(r.AccessCardLoanId);
    }

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

        var conflicts = await db.GetCountAsync("reservations",
            $"is_deleted=eq.false&is_cancelled=eq.false&space=eq.{(int)space}&start_time=lt.{endUtc:O}&end_time=gt.{startUtc:O}");
        if (conflicts > 0)
            return (false, "Já existe uma reserva para este espaço nesse horário.", null);

        var now = DateTime.UtcNow;
        var created = await db.InsertAsync<Reservation>("reservations", new
        {
            sync_id      = Guid.NewGuid().ToString(),
            space        = (int)space,
            room_number  = roomNumber.Trim(),
            reserved_by  = reservedBy.Trim(),
            start_time   = startUtc,
            end_time     = endUtc,
            notes        = notes,
            created_at   = now,
            is_cancelled = false,
            is_activated = false,
            is_completed = false,
            is_deleted   = false,
            updated_at   = now
        });
        return (true, null, created);
    }

    public async Task<(bool Success, string? Error)> ActivateAsync(string reservationId)
    {
        var resList = await db.GetAsync<Reservation>("reservations",
            $"sync_id=eq.{reservationId}&is_deleted=eq.false&select=sync_id,is_cancelled,is_activated,space,room_number,reserved_by,access_card_loan_sync_id");
        if (resList is not [var reservation])
            return (false, "Reserva inválida.");
        if (reservation.IsCancelled)
            return (false, "Reserva inválida.");
        if (reservation.AccessCardLoanId is not null)
            return (false, "O cartão já está registado para esta reserva.");

        var cardName = reservation.Space == ReservationSpace.Cozinha ? KitchenCardName : CinemaCardName;
        var cards = await db.GetAsync<GeneralItem>("general_items",
            $"name=eq.{Uri.EscapeDataString(cardName)}&is_deleted=eq.false");
        if (cards is not [var card])
            return (false, $"Cartão '{cardName}' não encontrado.");
        card.Loans = await db.GetAsync<GeneralItemLoan>("general_item_loans",
            $"general_item_sync_id=eq.{card.Id}&is_deleted=eq.false");
        if (card.AvailableCount <= 0)
            return (false, "O cartão de acesso está actualmente emprestado. Devolva-o primeiro.");

        var spaceLabel = reservation.Space == ReservationSpace.Cozinha ? "Cozinha MasterChef" : "Cinema";
        var now = DateTime.UtcNow;
        var loan = await db.InsertAsync<GeneralItemLoan>("general_item_loans", new
        {
            sync_id              = Guid.NewGuid().ToString(),
            general_item_sync_id = card.Id,
            room_number          = reservation.RoomNumber,
            given_by             = reservation.ReservedBy,
            quantity             = 1,
            notes                = $"Reserva: {spaceLabel} — quarto {reservation.RoomNumber}",
            loan_date            = now,
            is_returned          = false,
            is_deleted           = false,
            updated_at           = now
        });

        await db.PatchAsync("reservations", $"sync_id=eq.{reservationId}", new
        {
            access_card_loan_sync_id = loan!.Id,
            is_activated             = true,
            updated_at               = now
        });
        return (true, null);
    }

    public async Task<bool> CompleteAsync(string reservationId)
    {
        var resList = await db.GetAsync<Reservation>("reservations",
            $"sync_id=eq.{reservationId}&is_deleted=eq.false&select=sync_id,access_card_loan_sync_id,is_completed");
        if (resList is not [var reservation] || reservation.AccessCardLoanId is null)
            return false;

        var loanId = reservation.AccessCardLoanId;
        var loans = await db.GetAsync<GeneralItemLoan>("general_item_loans", $"sync_id=eq.{loanId}&select=sync_id,is_returned");
        if (loans is not [var loan] || loan.IsReturned) return false;

        var now = DateTime.UtcNow;
        await db.PatchAsync("general_item_loans", $"sync_id=eq.{loanId}", new { return_date = now, is_returned = true, updated_at = now });
        await db.PatchAsync("reservations", $"sync_id=eq.{reservationId}", new { is_completed = true, updated_at = now });
        return true;
    }

    public async Task<List<Reservation>> GetOverdueCardsAsync(int hoursThreshold = 2)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hoursThreshold).ToString("O");
        return await db.GetAsync<Reservation>("reservations",
            $"is_deleted=eq.false&is_cancelled=eq.false&is_activated=eq.true&is_completed=eq.false&end_time=lt.{cutoff}&order=end_time.asc");
    }

    public async Task<List<Reservation>> GetRecentByRoomAsync(string roomNumber, int limit = 5)
    {
        var room   = Uri.EscapeDataString(roomNumber.Trim().ToUpper());
        var utcNow = DateTime.UtcNow.ToString("O");
        return await db.GetAsync<Reservation>("reservations",
            $"is_deleted=eq.false&is_cancelled=eq.false&room_number=eq.{room}&end_time=lt.{utcNow}&order=end_time.desc&limit={limit}");
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(
        string reservationId, string roomNumber, DateTime startTime, DateTime endTime, string? notes)
    {
        var resList = await db.GetAsync<Reservation>("reservations",
            $"sync_id=eq.{reservationId}&is_deleted=eq.false&select=sync_id,is_activated,space");
        if (resList is not [var reservation])
            return (false, "Reserva não encontrada.");
        if (reservation.IsActivated)
            return (false, "Não é possível editar uma reserva já activada.");

        var startUtc = startTime.Kind == DateTimeKind.Utc ? startTime : startTime.ToUniversalTime();
        var endUtc   = endTime.Kind   == DateTimeKind.Utc ? endTime   : endTime.ToUniversalTime();

        if (reservation.Space == ReservationSpace.Cozinha)
        {
            var ls = startUtc.ToLocalTime();
            var le = endUtc.ToLocalTime();
            if (ls.Hour < 8)  return (false, "A Cozinha MasterChef só abre às 08:00.");
            if (le.Hour > 22 || (le.Hour == 22 && le.Minute > 0))
                return (false, "A Cozinha MasterChef fecha às 22:00.");
        }

        var conflicts = await db.GetAsync<Reservation>("reservations",
            $"is_deleted=eq.false&is_cancelled=eq.false&space=eq.{(int)reservation.Space}" +
            $"&start_time=lt.{endUtc:O}&end_time=gt.{startUtc:O}&sync_id=neq.{reservationId}&select=sync_id");
        if (conflicts.Count > 0)
            return (false, "Já existe uma reserva para este espaço nesse horário.");

        await db.PatchAsync("reservations", $"sync_id=eq.{reservationId}", new
        {
            room_number = roomNumber.Trim(),
            start_time  = startUtc,
            end_time    = endUtc,
            notes       = notes,
            updated_at  = DateTime.UtcNow
        });
        return (true, null);
    }

    public async Task<bool> CancelAsync(string reservationId)
    {
        var resList = await db.GetAsync<Reservation>("reservations",
            $"sync_id=eq.{reservationId}&is_deleted=eq.false&select=sync_id,is_cancelled,access_card_loan_sync_id");
        if (resList is not [var reservation] || reservation.IsCancelled) return false;

        var now = DateTime.UtcNow;
        await db.PatchAsync("reservations", $"sync_id=eq.{reservationId}", new { is_cancelled = true, updated_at = now });

        if (reservation.AccessCardLoanId is not null)
        {
            var loanId = reservation.AccessCardLoanId;
            var loans  = await db.GetAsync<GeneralItemLoan>("general_item_loans", $"sync_id=eq.{loanId}&select=sync_id,is_returned");
            if (loans is [var loan] && !loan.IsReturned)
                await db.PatchAsync("general_item_loans", $"sync_id=eq.{loanId}", new { return_date = now, is_returned = true, updated_at = now });
        }

        return true;
    }
}
