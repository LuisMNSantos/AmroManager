using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using AmroStockManager.Data.Models;
using Microsoft.Maui.Storage;

namespace AmroStockManager.Services;

public sealed class ChatbotService(
    ResidentService    residentSvc,
    DeliveryService    deliverySvc,
    ReservationService reservationSvc,
    VisitService       visitSvc)
{
    private const string ApiKeyPref = "chatbot_api_key";
    private const string ModelId    = "claude-haiku-4-5-20251001";
    private const int    MaxTokens  = 2048;

    private readonly List<MessageParam> _history = [];
    private AnthropicClient?            _client;

    // ── Tool definitions ───────────────────────────────────────────────────

    private static readonly Tool[] _tools =
    [
        new Tool
        {
            Name        = "search_residents",
            Description = "Procura residentes por nome ou número de quarto. Usa quando o utilizador pergunta quem está num quarto, dados de contacto, ou quantos residentes existem.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["query"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Nome ou parte do nome do residente" }),
                    ["room"]  = JsonSerializer.SerializeToElement(new { type = "string", description = "Número exacto do quarto (ex: 101, 202A)" }),
                },
            },
        },
        new Tool
        {
            Name        = "get_pending_deliveries",
            Description = "Lista encomendas e cartas por levantar. Usa quando se pergunta sobre pacotes pendentes ou entregas de um quarto específico.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["room"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Filtrar por número de quarto (opcional)" }),
                },
            },
        },
        new Tool
        {
            Name        = "get_todays_reservations",
            Description = "Lista as reservas de espaços comuns (cozinha, cinema) para hoje.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>(),
            },
        },
        new Tool
        {
            Name        = "get_active_visits",
            Description = "Lista as visitas atualmente ativas na residência. Os IDs retornados podem ser usados em checkout_visitor.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>(),
            },
        },

        // ── Ferramentas de escrita ─────────────────────────────────────────

        new Tool
        {
            Name        = "register_resident",
            Description = "Regista um novo residente ou membro de staff (Renovador, Concierge, Admin). Pede sempre confirmação antes de executar.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["name"]            = JsonSerializer.SerializeToElement(new { type = "string", description = "Nome completo" }),
                    ["room"]            = JsonSerializer.SerializeToElement(new { type = "string", description = "Número de quarto (ex: 101, 202A). Pode ser vazio para staff sem quarto atribuído" }),
                    ["phone"]           = JsonSerializer.SerializeToElement(new { type = "string", description = "Telemóvel em formato internacional, ex: +351912345678" }),
                    ["type"]            = JsonSerializer.SerializeToElement(new { type = "string", description = "Tipo de utilizador: 'residente', 'renovador', 'concierge' ou 'admin'. Default: 'residente'" }),
                    ["free_overnights"] = JsonSerializer.SerializeToElement(new { type = "integer", description = "Noites gratuitas por mês (apenas para renovadores)" }),
                },
                Required = ["name"],
            },
        },
        new Tool
        {
            Name        = "register_delivery",
            Description = "Regista a chegada de uma encomenda ou carta para um quarto.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["room"]          = JsonSerializer.SerializeToElement(new { type = "string", description = "Número de quarto destino" }),
                    ["type"]          = JsonSerializer.SerializeToElement(new { type = "string", description = "'encomenda' ou 'carta'" }),
                    ["quantity"]      = JsonSerializer.SerializeToElement(new { type = "integer", description = "Quantidade (default: 1)" }),
                    ["notes"]         = JsonSerializer.SerializeToElement(new { type = "string", description = "Notas opcionais, ex: 'frágil'" }),
                    ["registered_by"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Nome de quem regista" }),
                },
                Required = ["room", "type"],
            },
        },
        new Tool
        {
            Name        = "mark_delivery_collected",
            Description = "Marca uma encomenda ou carta como levantada. Usa o ID obtido de get_pending_deliveries.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["delivery_id"] = JsonSerializer.SerializeToElement(new { type = "string", description = "ID da encomenda (campo id nos resultados de get_pending_deliveries)" }),
                },
                Required = ["delivery_id"],
            },
        },
        new Tool
        {
            Name        = "create_reservation",
            Description = "Cria uma reserva de espaço comum (Cozinha MasterChef 08:00-22:00 ou Cinema). Pede sempre confirmação antes de executar.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["space"]       = JsonSerializer.SerializeToElement(new { type = "string", description = "'cozinha' ou 'cinema'" }),
                    ["room"]        = JsonSerializer.SerializeToElement(new { type = "string", description = "Número de quarto" }),
                    ["reserved_by"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Nome de quem faz a reserva" }),
                    ["date"]        = JsonSerializer.SerializeToElement(new { type = "string", description = "Data no formato YYYY-MM-DD" }),
                    ["start_time"]  = JsonSerializer.SerializeToElement(new { type = "string", description = "Hora início no formato HH:MM" }),
                    ["end_time"]    = JsonSerializer.SerializeToElement(new { type = "string", description = "Hora fim no formato HH:MM" }),
                    ["notes"]       = JsonSerializer.SerializeToElement(new { type = "string", description = "Notas opcionais" }),
                },
                Required = ["space", "room", "reserved_by", "date", "start_time", "end_time"],
            },
        },
        new Tool
        {
            Name        = "cancel_reservation",
            Description = "Cancela uma reserva existente. Usa o ID obtido de get_todays_reservations.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["reservation_id"] = JsonSerializer.SerializeToElement(new { type = "string", description = "ID da reserva (campo id nos resultados de get_todays_reservations)" }),
                },
                Required = ["reservation_id"],
            },
        },
        new Tool
        {
            Name        = "register_visit",
            Description = "Regista a entrada (check-in) de um visitante. Pede sempre confirmação antes de executar.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["visitor_name"]  = JsonSerializer.SerializeToElement(new { type = "string", description = "Nome completo do visitante" }),
                    ["room"]          = JsonSerializer.SerializeToElement(new { type = "string", description = "Número do quarto a visitar" }),
                    ["registered_by"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Nome de quem regista" }),
                    ["notes"]         = JsonSerializer.SerializeToElement(new { type = "string", description = "Notas opcionais" }),
                },
                Required = ["visitor_name", "room"],
            },
        },
        new Tool
        {
            Name        = "checkout_visitor",
            Description = "Faz checkout (saída) de um visitante ativo. Usa o ID obtido de get_active_visits.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["visit_id"] = JsonSerializer.SerializeToElement(new { type = "string", description = "ID da visita (campo id nos resultados de get_active_visits)" }),
                },
                Required = ["visit_id"],
            },
            CacheControl = new CacheControlEphemeral(),
        },
    ];

    // ── API key ────────────────────────────────────────────────────────────

    public async Task<bool> HasApiKeyAsync()
    {
        try
        {
            var k = await SecureStorage.Default.GetAsync(ApiKeyPref);
            return !string.IsNullOrWhiteSpace(k);
        }
        catch { return false; }
    }

    public async Task SetApiKeyAsync(string key)
    {
        await SecureStorage.Default.SetAsync(ApiKeyPref, key.Trim());
        _client = null;
    }

    public void ClearHistory() => _history.Clear();

    private async Task<AnthropicClient> EnsureClientAsync()
    {
        if (_client is not null) return _client;
        var key = await SecureStorage.Default.GetAsync(ApiKeyPref) ?? string.Empty;
        return _client = new AnthropicClient { ApiKey = key };
    }

    // ── Send (tool loop + simulated streaming) ─────────────────────────────

    public async IAsyncEnumerable<string> SendAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _history.Add(new MessageParam { Role = Role.User, Content = userMessage });

        var client    = await EnsureClientAsync();
        var finalText = string.Empty;

        for (int iter = 0; iter < 6; iter++)
        {
            ct.ThrowIfCancellationRequested();

            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model     = ModelId,
                MaxTokens = MaxTokens,
                System    = new List<TextBlockParam>
                {
                    new() { Text = SystemPrompt, CacheControl = new CacheControlEphemeral() }
                },
                Tools    = [.. _tools],
                Messages = [.. _history],
            });

            var assistantBlocks = new List<ContentBlockParam>();
            var toolUseBlocks   = new List<ToolUseBlock>();

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var text))
                {
                    assistantBlocks.Add(new TextBlockParam { Text = text.Text });
                    finalText = text.Text;
                }
                else if (block.TryPickToolUse(out var toolUse))
                {
                    assistantBlocks.Add(new ToolUseBlockParam
                    {
                        ID    = toolUse.ID,
                        Name  = toolUse.Name,
                        Input = toolUse.Input,
                    });
                    toolUseBlocks.Add(toolUse);
                }
            }

            _history.Add(new MessageParam { Role = Role.Assistant, Content = assistantBlocks });

            if (response.StopReason != "tool_use" || toolUseBlocks.Count == 0)
                break;

            var toolResults = new List<ContentBlockParam>();
            foreach (var tu in toolUseBlocks)
            {
                ct.ThrowIfCancellationRequested();
                var result = await ExecuteToolAsync(tu.Name, tu.Input, ct);
                toolResults.Add(new ToolResultBlockParam { ToolUseID = tu.ID, Content = result });
            }

            _history.Add(new MessageParam { Role = Role.User, Content = toolResults });
        }

        // Simulate streaming: yield in 15-char chunks
        const int chunkSize = 15;
        for (int i = 0; i < finalText.Length; i += chunkSize)
        {
            ct.ThrowIfCancellationRequested();
            yield return finalText[i..Math.Min(i + chunkSize, finalText.Length)];
            await Task.Delay(35, ct);
        }
    }

    // ── Tool execution ─────────────────────────────────────────────────────

    private async Task<string> ExecuteToolAsync(
        string name,
        IReadOnlyDictionary<string, JsonElement> input,
        CancellationToken ct)
    {
        _ = ct;
        return name switch
        {
            "search_residents"        => await SearchResidentsAsync(input),
            "get_pending_deliveries"  => await GetPendingDeliveriesAsync(input),
            "get_todays_reservations" => await GetTodaysReservationsAsync(),
            "get_active_visits"       => await GetActiveVisitsAsync(),
            "register_resident"       => await RegisterResidentAsync(input),
            "register_delivery"       => await RegisterDeliveryAsync(input),
            "mark_delivery_collected" => await MarkDeliveryCollectedAsync(input),
            "create_reservation"      => await CreateReservationAsync(input),
            "cancel_reservation"      => await CancelReservationAsync(input),
            "register_visit"          => await RegisterVisitAsync(input),
            "checkout_visitor"        => await CheckoutVisitorAsync(input),
            _                         => $"Ferramenta desconhecida: {name}",
        };
    }

    private async Task<string> SearchResidentsAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        if (input.TryGetValue("room", out var roomEl) &&
            !string.IsNullOrWhiteSpace(roomEl.GetString()))
        {
            var r = await residentSvc.GetByRoomAsync(roomEl.GetString()!);
            return r is null
                ? $"Nenhum residente no quarto {roomEl.GetString()!.ToUpper()}."
                : FormatResident(r);
        }

        if (input.TryGetValue("query", out var queryEl) &&
            !string.IsNullOrWhiteSpace(queryEl.GetString()))
        {
            var list = await residentSvc.SearchAsync(queryEl.GetString()!);
            return list.Count == 0
                ? $"Nenhum residente encontrado para '{queryEl.GetString()}'."
                : string.Join("\n", list.Select(FormatResident));
        }

        var all    = await residentSvc.GetAllAsync();
        var active = all.Where(r => !r.IsDeleted).ToList();
        return $"Total: {active.Count} residente(s) ativo(s).\n" +
               string.Join("\n", active.Select(FormatResident));
    }

    private async Task<string> GetPendingDeliveriesAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        List<Delivery> deliveries;
        if (input.TryGetValue("room", out var roomEl) &&
            !string.IsNullOrWhiteSpace(roomEl.GetString()))
            deliveries = await deliverySvc.GetPendingByRoomAsync(roomEl.GetString()!);
        else
            deliveries = await deliverySvc.GetPendingAsync();

        if (deliveries.Count == 0)
            return "Sem encomendas ou cartas pendentes.";

        return $"{deliveries.Count} pendente(s):\n" +
               string.Join("\n", deliveries.Select(d =>
                   $"- [id:{d.Id}] Quarto {d.RoomNumber}: " +
                   $"{(d.Type == DeliveryType.Encomenda ? "Encomenda" : "Carta")} ({d.Quantity}x)" +
                   $" — chegou {d.ArrivedAt.ToLocalTime():dd/MM HH:mm}" +
                   (d.Notes is not null ? $" — {d.Notes}" : "")));
    }

    private async Task<string> GetTodaysReservationsAsync()
    {
        var list = await reservationSvc.GetByDateAsync(DateTime.Today);
        if (list.Count == 0) return "Sem reservas para hoje.";

        return $"{list.Count} reserva(s) hoje:\n" +
               string.Join("\n", list.Select(r =>
                   $"- [id:{r.Id}] {r.StartTime.ToLocalTime():HH:mm}–{r.EndTime.ToLocalTime():HH:mm} " +
                   $"{(r.Space == ReservationSpace.Cozinha ? "Cozinha" : "Cinema")} " +
                   $"— Quarto {r.RoomNumber} ({r.ReservedBy})" +
                   (r.IsActivated ? " [ativa]" : r.IsCancelled ? " [cancelada]" : "")));
    }

    private async Task<string> GetActiveVisitsAsync()
    {
        var list = await visitSvc.GetActiveAsync();
        if (list.Count == 0) return "Nenhuma visita ativa neste momento.";

        return $"{list.Count} visita(s) ativa(s):\n" +
               string.Join("\n", list.Select(v =>
                   $"- [id:{v.Id}] {v.VisitorName} → Quarto {v.RoomNumber} " +
                   $"(entrada: {v.CheckedInAt.ToLocalTime():dd/MM HH:mm}" +
                   (v.LiveOvernights > 0 ? $", {v.LiveOvernights} noite(s)" : "") + ")"));
    }

    private async Task<string> RegisterResidentAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        var name  = input.TryGetValue("name",  out var nEl) ? nEl.GetString() ?? "" : "";
        var room  = input.TryGetValue("room",  out var rEl) ? rEl.GetString() ?? "" : "";
        var phone = input.TryGetValue("phone", out var pEl) ? pEl.GetString() : null;
        var type  = input.TryGetValue("type",  out var tEl) ? tEl.GetString() ?? "residente" : "residente";
        var freeOvernights = input.TryGetValue("free_overnights", out var foEl) ? foEl.GetInt32() : 0;

        if (string.IsNullOrWhiteSpace(name)) return "Erro: o nome é obrigatório.";

        var isCollaborator   = type is "concierge" or "admin";
        var collaboratorRole = type is "admin" ? "admin" : type is "concierge" ? "concierge" : null;
        var isRenewer        = type == "renovador";

        var resident = new Resident
        {
            Id               = string.Empty,
            Name             = name,
            RoomNumber       = room,
            PhoneNumber      = phone,
            IsCollaborator   = isCollaborator,
            CollaboratorRole = collaboratorRole,
            IsRenewer        = isRenewer,
            FreeOvernights   = isRenewer ? freeOvernights : 0,
            UpdatedAt        = DateTime.UtcNow,
            IsDeleted        = false,
        };

        await residentSvc.AddOrUpdateAsync(resident);

        var label = type switch
        {
            "renovador" => "Renovador",
            "concierge" => "Concierge",
            "admin"     => "Administrador",
            _           => "Residente",
        };
        return $"{label} registado com sucesso: {name}" +
               (string.IsNullOrWhiteSpace(room) ? "" : $", Quarto {room.ToUpper()}") + ".";
    }

    private async Task<string> RegisterDeliveryAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        var room  = input.TryGetValue("room",          out var rEl)  ? rEl.GetString()  ?? "" : "";
        var type  = input.TryGetValue("type",          out var tEl)  ? tEl.GetString()  ?? "encomenda" : "encomenda";
        var qty   = input.TryGetValue("quantity",      out var qEl)  ? qEl.GetInt32()   : 1;
        var notes = input.TryGetValue("notes",         out var nEl)  ? nEl.GetString()  : null;
        var regBy = input.TryGetValue("registered_by", out var rbEl) ? rbEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(room)) return "Erro: o número de quarto é obrigatório.";

        var deliveryType = type.ToLower() == "carta" ? DeliveryType.Carta : DeliveryType.Encomenda;
        var d = await deliverySvc.RegisterAsync(deliveryType, room, qty, notes, regBy);
        return $"{(deliveryType == DeliveryType.Carta ? "Carta" : "Encomenda")} registada para o Quarto " +
               $"{room.ToUpper()} ({qty}x) às {d.ArrivedAt.ToLocalTime():HH:mm}.";
    }

    private async Task<string> MarkDeliveryCollectedAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        if (!input.TryGetValue("delivery_id", out var idEl) || string.IsNullOrWhiteSpace(idEl.GetString()))
            return "Erro: delivery_id é obrigatório.";
        await deliverySvc.MarkCollectedAsync(idEl.GetString()!);
        return "Encomenda/carta marcada como levantada.";
    }

    private async Task<string> CreateReservationAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        var spaceStr   = input.TryGetValue("space",       out var spEl) ? spEl.GetString()  ?? "" : "";
        var room       = input.TryGetValue("room",        out var rEl)  ? rEl.GetString()   ?? "" : "";
        var reservedBy = input.TryGetValue("reserved_by", out var rbEl) ? rbEl.GetString()  ?? "" : "";
        var dateStr    = input.TryGetValue("date",        out var dEl)  ? dEl.GetString()   ?? "" : "";
        var startStr   = input.TryGetValue("start_time",  out var stEl) ? stEl.GetString()  ?? "" : "";
        var endStr     = input.TryGetValue("end_time",    out var etEl) ? etEl.GetString()  ?? "" : "";
        var notes      = input.TryGetValue("notes",       out var nEl)  ? nEl.GetString()   : null;

        if (!DateTime.TryParse(dateStr, out var date))
            return "Erro: data inválida. Usa o formato YYYY-MM-DD.";
        if (!TimeSpan.TryParse(startStr, out var startTs))
            return "Erro: hora de início inválida. Usa o formato HH:MM.";
        if (!TimeSpan.TryParse(endStr, out var endTs))
            return "Erro: hora de fim inválida. Usa o formato HH:MM.";

        var space     = spaceStr.ToLower() == "cinema" ? ReservationSpace.Cinema : ReservationSpace.Cozinha;
        var startTime = DateTime.SpecifyKind(date.Date.Add(startTs), DateTimeKind.Local).ToUniversalTime();
        var endTime   = DateTime.SpecifyKind(date.Date.Add(endTs),   DateTimeKind.Local).ToUniversalTime();
        if (endTs <= startTs && space == ReservationSpace.Cinema)
            endTime = endTime.AddDays(1);

        var (success, error, _) = await reservationSvc.CreateAsync(space, room, reservedBy, startTime, endTime, notes);
        if (!success) return $"Erro ao criar reserva: {error}";

        return $"Reserva criada: {(space == ReservationSpace.Cozinha ? "Cozinha" : "Cinema")} " +
               $"para Quarto {room.ToUpper()} — {startTime.ToLocalTime():HH:mm}–{endTime.ToLocalTime():HH:mm} " +
               $"em {date:dd/MM/yyyy}.";
    }

    private async Task<string> CancelReservationAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        if (!input.TryGetValue("reservation_id", out var idEl) || string.IsNullOrWhiteSpace(idEl.GetString()))
            return "Erro: reservation_id é obrigatório.";
        var ok = await reservationSvc.CancelAsync(idEl.GetString()!);
        return ok ? "Reserva cancelada com sucesso." : "Não foi possível cancelar (já cancelada ou concluída).";
    }

    private async Task<string> RegisterVisitAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        var name  = input.TryGetValue("visitor_name",  out var nEl)  ? nEl.GetString()  ?? "" : "";
        var room  = input.TryGetValue("room",          out var rEl)  ? rEl.GetString()  ?? "" : "";
        var regBy = input.TryGetValue("registered_by", out var rbEl) ? rbEl.GetString() : null;
        var notes = input.TryGetValue("notes",         out var noEl) ? noEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(room))
            return "Erro: nome do visitante e quarto são obrigatórios.";

        var visit = await visitSvc.CreateAsync(name, room, regBy, notes);
        return $"Check-in registado: {name} → Quarto {room.ToUpper()} " +
               $"(entrada às {visit!.CheckedInAt.ToLocalTime():HH:mm}).";
    }

    private async Task<string> CheckoutVisitorAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        if (!input.TryGetValue("visit_id", out var idEl) || string.IsNullOrWhiteSpace(idEl.GetString()))
            return "Erro: visit_id é obrigatório.";
        await visitSvc.CheckOutAsync(idEl.GetString()!);
        return "Check-out registado com sucesso.";
    }

    private static string FormatResident(Resident r)
    {
        var type  = r.IsCollaborator
            ? (r.CollaboratorRole == "admin" ? "Admin" : "Concierge")
            : (r.IsRenewer ? "Renovante" : "Residente");
        var phone = string.IsNullOrWhiteSpace(r.PhoneNumber) ? "sem tel." : r.PhoneNumber;
        return $"Quarto {(string.IsNullOrWhiteSpace(r.RoomNumber) ? "—" : r.RoomNumber)}: " +
               $"{r.Name} ({type}) — {phone}";
    }

    // ── System prompt ──────────────────────────────────────────────────────

    private const string SystemPrompt = """
        És a G.A.I.A. — Gestora Assistente Inteligente AMRO — o assistente virtual do AMRO Porto Manager, uma aplicação desktop Windows (.NET MAUI + Blazor) de gestão de residência estudantil, desenvolvida por Luis Santos para o AMRO Porto. O teu nome é também uma referência a Gaia, a cidade do Porto.

        Responde sempre em Português de Portugal. Sê conciso, claro e prático. Podes ser ligeiramente bem-humorado mas mantém-te profissional. Usa listas quando listares passos ou funcionalidades.

        ## Ferramentas Disponíveis

        Tens ferramentas de leitura E de escrita para interagir com os dados reais da aplicação.

        ### Leitura
        - search_residents: procurar residentes por nome ou número de quarto
        - get_pending_deliveries: encomendas e cartas por levantar (inclui IDs para operações de escrita)
        - get_todays_reservations: reservas de espaços comuns para hoje (inclui IDs para cancelamento)
        - get_active_visits: visitas ativas (inclui IDs para checkout)

        ### Escrita — pede SEMPRE confirmação ao utilizador antes de executar qualquer operação de escrita
        - register_resident: registar novo residente, renovador, concierge ou admin
        - register_delivery: registar chegada de encomenda ou carta
        - mark_delivery_collected: marcar encomenda/carta como levantada (usa ID de get_pending_deliveries)
        - create_reservation: criar reserva de Cozinha ou Cinema
        - cancel_reservation: cancelar uma reserva (usa ID de get_todays_reservations)
        - register_visit: registar check-in de visitante
        - checkout_visitor: fazer checkout de visitante (usa ID de get_active_visits)

        Regra de confirmação: antes de criar ou modificar qualquer dado, resume o que vais fazer e pergunta "Confirmas?" ao utilizador. Só executa a ferramenta após confirmação explícita. Exceção: operações de leitura não precisam de confirmação.

        ## Tipos de Utilizadores

        A app distingue quatro perfis, todos geridos em [Administração](/administracao):
        - **Residente**: utilizador padrão da residência
        - **Renovador**: residente com contrato de renovação; tem um número de noites de visita gratuitas por mês
        - **Concierge**: membro do staff com acesso operacional
        - **Admin**: acesso total ao sistema, incluindo áreas protegidas por PIN de 4 dígitos

        O botão "Novo Residente" na Administração serve para registar qualquer um destes perfis — residentes normais, renovadores, concierges e administradores.

        ## Navegação — Links Clicáveis

        Quando referes uma secção da app, usa SEMPRE o formato de link markdown [Nome](/rota) para que o utilizador possa clicar e navegar diretamente.

        Rotas disponíveis:
        - [Painel](/) — dashboard com estatísticas em tempo real
        - [Produtos](/products) — catálogo, stock e kits
        - [Artigos Gerais](/general-items) — empréstimos de equipamentos e cartões de acesso
        - [Reservas](/calendario) — calendário de reservas de espaços comuns
        - [Encomendas & Cartas](/encomendas) — registo e levantamento de entregas
        - [Visitas](/visitas) — check-in/check-out e overnights
        - [WhatsApp](/whatsapp) — WhatsApp Web integrado
        - [Administração](/administracao) — gestão de residentes e operações avançadas

        Pesquisa global: Ctrl+K ou ícone de lupa na barra superior.

        ## Administração (/administracao) — Protegida por PIN

        ### Gestão de Residentes
        - Listar residentes com filtros: piso, tipo, BIS ativo, renovadores, quartos livres
        - Criar novo residente/staff (botão "+ Novo Residente"): escolher tipo (Residente, Renovador, Concierge, Admin), nome, quarto, telemóvel, noites gratuitas (renovadores)
        - Editar dados de qualquer residente
        - Eliminar residente
        - Importar lista via CSV (substitui lista existente)
        - Exportar lista em Excel

        ### Checkout de Residentes
        - Selecionar múltiplos residentes em simultâneo (filtrável por piso)
        - O sistema avisa automaticamente se houver encomendas por levantar, empréstimos ativos ou reservas futuras antes de confirmar
        - Registado em auditoria com data/hora

        ### Troca de Quartos
        - Trocar dois residentes entre si, ou mover um residente para um quarto vago

        ### Cartões BIS
        - Entregar cartão BIS a um residente e registar o empréstimo
        - Devolver cartão BIS
        - Ver quantos dias o cartão está em uso
        - Filtrar residentes com BIS ativo

        ### Pedidos de Registo Pendentes
        - Aprovar ou rejeitar pedidos de registo externos
        - Ao aprovar: adicionar como novo residente ou substituir um existente

        ### Reembolsos
        - Registar e acompanhar reembolsos a residentes

        ### Histórico de Operações (Auditoria)
        - Log completo de todas as operações: quem fez o quê e quando
        - Filtrável por categoria (residentes, encomendas, visitas, reservas, produtos, artigos, etc.)

        ### Kit de Renovação
        - Configurar quais produtos fazem parte do kit de boas-vindas para renovadores
        - Registar entregas de kits (residente, tamanhos, responsável pela entrega)
        - Histórico de entregas com exportação Excel

        ### Outras Operações Admin
        - Alterar PIN de acesso (4 dígitos)
        - Gerar PINs de visita para quartos
        - Limpar lista de residentes

        ## Encomendas & Cartas (/encomendas)

        - Registar chegada de encomenda ou carta: tipo, quarto, quantidade (até 99), notas, responsável
        - Notificação automática ao residente via WhatsApp ao registar
        - Marcar como levantado (com auditoria)
        - Eliminar registo
        - Ver pendentes e histórico de levantamentos
        - Cada entrada mostra há quantos dias/horas a encomenda está à espera

        ## Visitas (/visitas)

        - Registar nova visita: nome do visitante, quarto, responsável, notas
        - Check-out manual de visitantes (suporta múltiplos visitantes no mesmo quarto)
        - Editar ou eliminar visitas (requer PIN admin)
        - Cálculo automático de overnights: check-in entre 00:00–07:59 conta como noite anterior
        - Para renovadores: sistema compara overnights usados com as noites gratuitas do contrato e mostra o saldo (positivo = ainda tem direito, negativo = excedeu)
        - Exportação Excel com folha de detalhes + folha resumo por quarto
        - Filtros: Todas | Ativas | Hoje
        - Estatísticas: total de visitantes ativos, entradas hoje, saídas hoje, overnights do mês

        ## Reservas (/calendario)

        Espaços disponíveis:
        - **Cozinha MasterChef**: reservável das 08:00 às 22:00
        - **Cinema**: sem limitação horária (pode atravessar meia-noite)

        Funcionalidades:
        - Vista calendário mensal com indicadores de ocupação por dia (azul=1, laranja=2, vermelho=3+)
        - Vista diária com cards de reserva para cada espaço
        - Criar reserva: espaço, quarto, responsável, hora início/fim, notas; notificação WhatsApp automática
        - Fluxo completo da reserva:
          1. **Criar** → reserva fica "Pendente"
          2. **Ativar** (à hora marcada) → sistema gera empréstimo de cartão de acesso automaticamente; residente recebe o cartão fisicamente
          3. **Completar** → residente devolve o cartão; empréstimo fechado; reserva "Completa"
          4. **Cancelar** → reserva anulada; cartão não é entregue
        - Editar reserva (horários, responsável, notas)
        - Estatísticas de uso dos últimos 90 dias: gráfico por dia da semana e por hora

        ## Artigos Gerais (/general-items)

        Exemplos de artigos: cartões de acesso ao Cinema/Cozinha, controles remotos, chaves, equipamentos.

        - Listar artigos com contagem de disponíveis vs emprestados
        - Emprestar artigo a um quarto: quantidade, responsável, notas
        - Suporte para artigos "linked" (ex: cartão de acesso + controle remoto emprestados em conjunto)
        - Devolver artigo; se for cartão de acesso associado a reserva, a reserva é automaticamente completada
        - Histórico completo de empréstimos por artigo
        - Criar, editar e eliminar artigos (com aviso se houver empréstimos ativos)

        ## Produtos (/products)

        - Catálogo de produtos com variantes de tamanho (XS, S, M, L, XL, etc.) e quantidade por tamanho
        - Alertas de stock baixo
        - Movimentos de stock: Saída (venda/distribuição), Entrada (reabastecimento), Ajuste personalizado
        - Troca de artigos: registar artigo que sai e artigo que entra (ex: troca de tamanho)
        - Campanhas de distribuição: criar campanha, rastrear quantidades distribuídas
        - Pesquisa por nome, cor ou SKU
        - Criar, editar e eliminar produtos
        - Exportação Excel

        ## WhatsApp (/whatsapp)

        - WhatsApp Web integrado diretamente na app (WebView)
        - Sessão mantida entre navegações
        - Atalho direto a partir da lista de residentes (botão WhatsApp junto ao telemóvel)
        - Notificações automáticas enviadas para residentes nas operações de encomendas e reservas (mensagem pré-composta com o número do quarto)

        ## Contacto e Suporte

        Desenvolvido por **Luis Santos** (lmname999@gmail.com). Para suporte técnico ou novas funcionalidades, contacta o Luis Santos.

        ## Regras de Resposta

        - Usa as ferramentas para responder a perguntas sobre dados reais (quem está onde, encomendas, reservas, visitas)
        - Para funcionalidades não existentes, sugere que podem ser desenvolvidas no futuro pelo Luis Santos
        - Mantém as respostas curtas e práticas
        - Usa sempre links clicáveis [Nome](/rota) ao mencionar secções da app
        """;
}
