using System.Security.Cryptography;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class ResidentService(ISupabaseClient db, CacheService cache)
{
    private static readonly string PinFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AmroStockManager", "admin.pin");

    private const string _hashPrefix = "pbkdf2:";
    private const int    _iterations = 100_000;

    public bool VerifyPin(string input)
    {
        var stored = File.Exists(PinFile) ? File.ReadAllText(PinFile).Trim() : "1234";

        // Legacy plaintext file — verify and transparently rehash on success
        if (!stored.StartsWith(_hashPrefix))
        {
            if (input.Trim() != stored) return false;
            SavePin(input.Trim());
            return true;
        }

        var parts = stored[_hashPrefix.Length..].Split(':');
        if (parts.Length != 2) return false;
        try
        {
            var salt         = Convert.FromBase64String(parts[0]);
            var expectedHash = Convert.FromBase64String(parts[1]);
            var actualHash   = Rfc2898DeriveBytes.Pbkdf2(input.Trim(), salt, _iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch { return false; }
    }

    public void SavePin(string pin)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PinFile)!);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin.Trim(), salt, _iterations, HashAlgorithmName.SHA256, 32);
        File.WriteAllText(PinFile, $"{_hashPrefix}{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}");
    }

    private static readonly TimeSpan _residentsTtl = TimeSpan.FromSeconds(60);
    private const string _residentsKey = "residents:all";

    public Task<List<Resident>> GetAllAsync() =>
        cache.GetOrFetchAsync(_residentsKey,
            () => db.GetAsync<Resident>("residents",
                "is_deleted=eq.false&order=is_collaborator.asc,room_number.asc,name.asc"),
            _residentsTtl);

    public async Task<Resident?> GetByRoomAsync(string roomNumber)
    {
        if (string.IsNullOrWhiteSpace(roomNumber)) return null;
        var room = Uri.EscapeDataString(roomNumber.Trim().ToUpper());
        var list = await db.GetAsync<Resident>("residents", $"is_deleted=eq.false&room_number=eq.{room}&limit=1");
        return list.FirstOrDefault();
    }

    public async Task<IEnumerable<string>> GetSuggestionsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var q = Uri.EscapeDataString(query.TrimStart('#').Trim());
        var residents = await db.GetAsync<Resident>("residents",
            $"is_deleted=eq.false&or=(room_number.ilike.*{q}*,name.ilike.*{q}*)&order=room_number.asc&limit=10");
        return residents.Select(r => $"#{r.RoomNumber} - {r.Name}");
    }

    public async Task<List<Resident>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var q = Uri.EscapeDataString(query.TrimStart('#').Trim());
        return await db.GetAsync<Resident>("residents",
            $"is_deleted=eq.false&or=(room_number.ilike.*{q}*,name.ilike.*{q}*)&order=room_number.asc&limit=10");
    }

    public static string ParseRoomNumber(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        input = input.Trim();
        if (input.StartsWith('#'))
        {
            var dash = input.IndexOf(" - ");
            return (dash > 0 ? input[1..dash] : input[1..]).Trim().ToUpper();
        }
        return input.ToUpper();
    }

    public async Task<(int Imported, int Skipped)> ImportFromCsvAsync(Stream csvStream)
    {
        using var reader = new StreamReader(csvStream);
        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line is not null) lines.Add(line);
        }

        int startLine = 0;
        if (lines.Count > 0)
        {
            var first = lines[0].ToLower();
            if (first.Contains("nome") || first.Contains("name") || first.Contains("quarto"))
                startLine = 1;
        }

        var residents = new List<object>();
        int skipped = 0;

        for (int i = startLine; i < lines.Count; i++)
        {
            var parts = ParseCsvLine(lines[i]);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) { skipped++; continue; }

            var name    = parts[0].Trim();
            var room    = parts.Length > 1 ? parts[1].Trim().ToUpper() : string.Empty;
            var phone   = parts.Length > 2 ? parts[2].Trim() : null;
            var collab  = parts.Length > 3 &&
                          parts[3].Trim().ToLower() is "true" or "1" or "sim" or "yes" or "verdadeiro";
            var renewer = parts.Length > 4 &&
                          parts[4].Trim().ToLower() is "true" or "1" or "sim" or "yes" or "verdadeiro";

            if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }

            residents.Add(new
            {
                sync_id         = Guid.NewGuid().ToString(),
                name            = name,
                room_number     = room,
                phone_number    = string.IsNullOrWhiteSpace(phone) ? (string?)null : phone,
                is_collaborator = collab,
                is_renewer      = !collab && renewer,
                is_deleted      = false,
                updated_at      = DateTime.UtcNow
            });
        }

        await db.PatchAsync("residents", "is_deleted=eq.false", new { is_deleted = true, updated_at = DateTime.UtcNow });

        foreach (var r in residents)
            await db.InsertAsync<Resident>("residents", r);

        cache.Invalidate(_residentsKey);
        return (residents.Count, skipped);
    }

    public async Task AddOrUpdateAsync(Resident r)
    {
        var room = r.RoomNumber?.Trim().ToUpper() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(room))
        {
            var conflict = await GetByRoomAsync(room);
            if (conflict is not null && conflict.Id != r.Id)
                throw new InvalidOperationException(
                    $"Já existe um residente no quarto {room} ({conflict.Name}).");
        }

        if (string.IsNullOrEmpty(r.Id) && r.IsCollaborator && string.IsNullOrWhiteSpace(room))
        {
            var nameQ = Uri.EscapeDataString(r.Name.Trim());
            var same  = await db.GetAsync<Resident>("residents",
                $"is_deleted=eq.false&is_collaborator=eq.true&name=ilike.{nameQ}&limit=1");
            if (same.Any())
                throw new InvalidOperationException(
                    $"Já existe um colaborador com o nome '{r.Name.Trim()}'.");
        }

        if (string.IsNullOrEmpty(r.Id))
        {
            await db.InsertAsync<Resident>("residents", new
            {
                sync_id           = Guid.NewGuid().ToString(),
                name              = r.Name.Trim(),
                room_number       = room,
                phone_number      = string.IsNullOrWhiteSpace(r.PhoneNumber) ? (string?)null : r.PhoneNumber.Trim(),
                is_collaborator   = r.IsCollaborator,
                collaborator_role = r.IsCollaborator ? r.CollaboratorRole : null,
                is_renewer        = r.IsRenewer,
                free_overnights   = r.FreeOvernights,
                is_deleted        = false,
                updated_at        = DateTime.UtcNow
            });
        }
        else
        {
            await db.PatchAsync("residents", $"sync_id=eq.{r.Id}", new
            {
                name              = r.Name.Trim(),
                room_number       = room,
                phone_number      = string.IsNullOrWhiteSpace(r.PhoneNumber) ? (string?)null : r.PhoneNumber.Trim(),
                is_collaborator   = r.IsCollaborator,
                collaborator_role = r.IsCollaborator ? r.CollaboratorRole : null,
                is_renewer        = r.IsRenewer,
                free_overnights   = r.FreeOvernights,
                updated_at        = DateTime.UtcNow
            });
        }
        cache.Invalidate(_residentsKey);
    }

    public async Task<(int PendingDeliveries, int ActiveLoans, int UpcomingReservations)> GetActiveItemCountsForRoomAsync(string roomNumber)
    {
        var room   = Uri.EscapeDataString(roomNumber.Trim().ToUpper());
        var utcNow = Uri.EscapeDataString(DateTime.UtcNow.ToString("O"));
        var t1 = db.GetCountAsync("deliveries",         $"is_deleted=eq.false&is_delivered=eq.false&room_number=eq.{room}");
        var t2 = db.GetCountAsync("general_item_loans", $"is_deleted=eq.false&is_returned=eq.false&room_number=eq.{room}");
        var t3 = db.GetCountAsync("reservations",       $"is_deleted=eq.false&is_cancelled=eq.false&room_number=eq.{room}&end_time=gte.{utcNow}");
        await Task.WhenAll(t1, t2, t3);
        return (t1.Result, t2.Result, t3.Result);
    }

    public async Task DeleteResidentAsync(string id)
    {
        await db.PatchAsync("residents", $"sync_id=eq.{id}", new { is_deleted = true, updated_at = DateTime.UtcNow });
        cache.Invalidate(_residentsKey);
    }

    public async Task DeleteAllAsync()
    {
        await db.PatchAsync("residents", "is_deleted=eq.false", new { is_deleted = true, updated_at = DateTime.UtcNow });
        cache.Invalidate(_residentsKey);
    }

    public async Task SwapRoomsAsync(string roomA, string roomB)
    {
        roomA = roomA.Trim().ToUpper();
        roomB = roomB.Trim().ToUpper();
        if (roomA == roomB) throw new InvalidOperationException("Os quartos de origem e destino são iguais.");

        // Use a temporary room key that won't collide with real room numbers
        var temp = "SWAP" + Guid.NewGuid().ToString("N")[..8].ToUpper();

        string[] relatedTables =
        [
            "deliveries", "visits", "bis_loans", "general_item_loans",
            "reservations", "renewer_kit_deliveries", "reimbursements"
        ];

        var fA = $"room_number=eq.{Uri.EscapeDataString(roomA)}";
        var fB = $"room_number=eq.{Uri.EscapeDataString(roomB)}";
        var fT = $"room_number=eq.{Uri.EscapeDataString(temp)}";

        // Step 1: roomA → temp
        await db.PatchAsync("residents", fA, new { room_number = temp, updated_at = DateTime.UtcNow });
        foreach (var t in relatedTables)
            await db.PatchAsync(t, fA, new { room_number = temp });

        // Step 2: roomB → roomA
        await db.PatchAsync("residents", fB, new { room_number = roomA, updated_at = DateTime.UtcNow });
        foreach (var t in relatedTables)
            await db.PatchAsync(t, fB, new { room_number = roomA });

        // Step 3: temp → roomB
        await db.PatchAsync("residents", fT, new { room_number = roomB, updated_at = DateTime.UtcNow });
        foreach (var t in relatedTables)
            await db.PatchAsync(t, fT, new { room_number = roomB });

        cache.Invalidate(_residentsKey);
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();
        foreach (char c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; }
            else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
            else { current.Append(c); }
        }
        result.Add(current.ToString());
        return [.. result];
    }
}
