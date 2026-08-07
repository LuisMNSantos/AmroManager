using AmroStockManager.Services;
using AmroStockManager.Tests.Helpers;

namespace AmroStockManager.Tests.Services;

public class ResidentServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly ResidentService _svc;

    public ResidentServiceTests() => _svc = new ResidentService(_factory);

    // ── ParseRoomNumber (static, no DB) ───────────────────────────────────────

    [Theory]
    [InlineData("#101 - João Silva", "101")]
    [InlineData("#101 - Maria",      "101")]
    [InlineData("101",               "101")]
    [InlineData("404a",              "404A")]
    [InlineData("#404A - Maria",     "404A")]
    public void ParseRoomNumber_Various_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, ResidentService.ParseRoomNumber(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseRoomNumber_NullOrBlank_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, ResidentService.ParseRoomNumber(input));
    }

    // ── GetSuggestionsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetSuggestionsAsync_MatchingRoom_ReturnsSuggestion()
    {
        _factory.SeedResident("João Silva", "101", "+351912345678");

        var results = (await _svc.GetSuggestionsAsync("101")).ToList();

        Assert.Single(results);
        Assert.Equal("#101 - João Silva", results[0]);
    }

    [Fact]
    public async Task GetSuggestionsAsync_MatchingName_ReturnsSuggestion()
    {
        _factory.SeedResident("Maria Santos", "202");

        var results = (await _svc.GetSuggestionsAsync("maria")).ToList();

        Assert.Single(results);
        Assert.Contains("Maria Santos", results[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetSuggestionsAsync_EmptyQuery_ReturnsEmpty(string query)
    {
        _factory.SeedResident("João Silva", "101");

        var results = await _svc.GetSuggestionsAsync(query);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetSuggestionsAsync_NoMatch_ReturnsEmpty()
    {
        _factory.SeedResident("João Silva", "101");

        var results = await _svc.GetSuggestionsAsync("999");

        Assert.Empty(results);
    }

    // ── GetByRoomAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByRoomAsync_CaseInsensitive_FindsResident()
    {
        _factory.SeedResident("João Silva", "101");

        var lower = await _svc.GetByRoomAsync("101");
        var upper = await _svc.GetByRoomAsync("101");

        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.Equal("João Silva", lower!.Name);
    }

    [Fact]
    public async Task GetByRoomAsync_UnknownRoom_ReturnsNull()
    {
        var result = await _svc.GetByRoomAsync("999");
        Assert.Null(result);
    }

    // ── ImportFromCsvAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ImportFromCsvAsync_ValidData_ImportsResidents()
    {
        const string csv = "João Silva,101,+351912345678,false\nMaria Santos,202,,true";
        using var stream = ToStream(csv);

        var (imported, skipped) = await _svc.ImportFromCsvAsync(stream);

        Assert.Equal(2, imported);
        Assert.Equal(0, skipped);

        var all = await _svc.GetAllAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task ImportFromCsvAsync_WithHeader_SkipsHeaderLine()
    {
        const string csv = "Nome,Quarto,Telemóvel,Colaborador\nJoão Silva,101,+351912345678,false";
        using var stream = ToStream(csv);

        var (imported, _) = await _svc.ImportFromCsvAsync(stream);

        Assert.Equal(1, imported);
    }

    [Fact]
    public async Task ImportFromCsvAsync_ClearsExistingData()
    {
        _factory.SeedResident("Residente Antigo", "999");

        const string csv = "João Silva,101,,false";
        using var stream = ToStream(csv);

        await _svc.ImportFromCsvAsync(stream);
        var all = await _svc.GetAllAsync();

        Assert.Single(all);
        Assert.Equal("João Silva", all[0].Name);
    }

    [Fact]
    public async Task ImportFromCsvAsync_CollaboratorField_ParsedCorrectly()
    {
        const string csv = "João Silva,101,,true\nMaria Santos,202,,false";
        using var stream = ToStream(csv);

        await _svc.ImportFromCsvAsync(stream);
        var all = await _svc.GetAllAsync();

        Assert.True(all.First(r => r.Name == "João Silva").IsCollaborator);
        Assert.False(all.First(r => r.Name == "Maria Santos").IsCollaborator);
    }

    [Fact]
    public async Task ImportFromCsvAsync_EmptyLines_Skipped()
    {
        const string csv = "João Silva,101,,false\n\n   \nMaria Santos,202,,false";
        using var stream = ToStream(csv);

        var (imported, skipped) = await _svc.ImportFromCsvAsync(stream);

        Assert.Equal(2, imported);
        Assert.Equal(2, skipped);
    }

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_OrdersResidentsThenCollaborators()
    {
        _factory.SeedResident("Colaborador A", "900", isCollaborator: true);
        _factory.SeedResident("Residente B", "101");

        var all = await _svc.GetAllAsync();

        Assert.Equal("Residente B", all[0].Name);
        Assert.Equal("Colaborador A", all[1].Name);
    }

    private static MemoryStream ToStream(string text)
    {
        var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write(text);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    public void Dispose() => _factory.Dispose();
}
