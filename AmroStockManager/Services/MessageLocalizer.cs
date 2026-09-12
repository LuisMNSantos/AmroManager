using System.Globalization;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public static class MessageLocalizer
{
    // Ordered longest-prefix-first so "+351..." is matched before "+35..."
    private static readonly (string Prefix, string Lang)[] PrefixMap =
    [
        ("351", "pt"), ("244", "pt"), ("238", "pt"), ("245", "pt"), ("258", "pt"), // Portugal + PALOP
        ("353", "en"),                                                               // Ireland
        ("221", "fr"), ("212", "fr"),                                               // Senegal, Morocco
        ("34",  "es"),                                                               // Spain
        ("44",  "en"),                                                               // UK
        ("33",  "fr"),                                                               // France
        ("39",  "it"),                                                               // Italy
        ("55",  "pt"),                                                               // Brazil
        ("32",  "fr"),                                                               // Belgium
        ("1",   "en"),                                                               // USA / Canada
    ];

    /// <summary>Returns "pt" | "es" | "en" | "fr" | "it" from a phone number's dial prefix.</summary>
    public static string DetectLang(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "pt";
        var digits = phone.TrimStart('+').Replace(" ", "").Replace("-", "");
        foreach (var (prefix, lang) in PrefixMap)
            if (digits.StartsWith(prefix))
                return lang;
        return "pt";
    }

    /// <summary>Message used right after registering a delivery (no relative time, just clock time).</summary>
    public static string DeliveryArrival(string name, DeliveryType type, int qty, DateTime arrivedAt, string lang)
    {
        var item = FormatItem(type, qty, lang);
        var time = arrivedAt.ToString("HH:mm");
        return lang switch
        {
            "es" => $"¡Hola {name}! Tienes {item} para recoger en recepción. Llegó a las {time}.",
            "en" => $"Hi {name}! You have {item} to pick up at reception. It arrived at {time}.",
            "fr" => $"Bonjour {name} ! Vous avez {item} à récupérer à la réception. Arrivé à {time}.",
            "it" => $"Ciao {name}! Hai {item} da ritirare alla reception. È arrivato alle {time}.",
            _    => $"Olá {name}! Tem {item} para levantar na receção. Chegou em {arrivedAt:dd/MM HH:mm}.",
        };
    }

    /// <summary>Message used from the pending-deliveries list (includes relative arrival time).</summary>
    public static string DeliveryNotify(string name, DeliveryType type, int qty, DateTime arrivedAt, string lang)
    {
        var item   = FormatItem(type, qty, lang);
        var quando = FormatAge(DateTime.Now - arrivedAt, lang);
        var time   = arrivedAt.ToString("dd/MM HH:mm");
        return lang switch
        {
            "es" => $"¡Hola {name}! Tienes {item} para recoger en recepción. Llegó {quando} ({time}).",
            "en" => $"Hi {name}! You have {item} to pick up at reception. It arrived {quando} ({time}).",
            "fr" => $"Bonjour {name} ! Vous avez {item} à récupérer à la réception. Arrivé {quando} ({time}).",
            "it" => $"Ciao {name}! Hai {item} da ritirare alla reception. È arrivato {quando} ({time}).",
            _    => $"Olá {name}! Tem {item} para levantar na receção. Chegou {quando} ({time}).",
        };
    }

    /// <summary>Reservation confirmation message.</summary>
    public static string Reservation(string name, ReservationSpace space, DateTime startLocal, DateTime endLocal, string lang)
    {
        var spaceLabel = FormatSpace(space, lang);
        var culture    = GetCulture(lang);
        var start      = startLocal.ToString("HH:mm");
        var end        = endLocal.ToString("HH:mm");
        return lang switch
        {
            "es" => $"¡Hola {name}! Tu reserva de {spaceLabel} ha sido confirmada para el " +
                    $"{startLocal.ToString("dddd d 'de' MMMM", culture)}, de {start} a {end}. " +
                    $"La tarjeta de acceso se entregará en el momento. — AMRO",
            "en" => $"Hi {name}! Your {spaceLabel} booking has been confirmed for " +
                    $"{startLocal.ToString("dddd, d MMMM", culture)}, from {start} to {end}. " +
                    $"The access card will be given at the time. — AMRO",
            "fr" => $"Bonjour {name} ! Votre réservation de {spaceLabel} est confirmée pour le " +
                    $"{startLocal.ToString("dddd d MMMM", culture)}, de {start} à {end}. " +
                    $"La carte d'accès vous sera remise sur place. — AMRO",
            "it" => $"Ciao {name}! La tua prenotazione per {spaceLabel} è confermata per " +
                    $"{startLocal.ToString("dddd d MMMM", culture)}, dalle {start} alle {end}. " +
                    $"La tessera d'accesso verrà consegnata sul posto. — AMRO",
            _    => $"Olá {name}! A sua reserva da {spaceLabel} foi confirmada para " +
                    $"{startLocal.ToString("dddd d 'de' MMMM", culture)}, das {start} às {end}. " +
                    $"O cartão de acesso será entregue na hora. — AMRO",
        };
    }

    private static string FormatItem(DeliveryType type, int qty, string lang)
    {
        if (type == DeliveryType.Encomenda)
            return lang switch
            {
                "es" => qty > 1 ? $"{qty} paquetes"   : "1 paquete",
                "en" => qty > 1 ? $"{qty} packages"   : "1 package",
                "fr" => qty > 1 ? $"{qty} colis"      : "1 colis",
                "it" => qty > 1 ? $"{qty} pacchi"     : "1 pacco",
                _    => qty > 1 ? $"{qty} encomendas" : "1 encomenda",
            };
        return lang switch
        {
            "es" => qty > 1 ? $"{qty} cartas"   : "1 carta",
            "en" => qty > 1 ? $"{qty} letters"  : "1 letter",
            "fr" => qty > 1 ? $"{qty} lettres"  : "1 lettre",
            "it" => qty > 1 ? $"{qty} lettere"  : "1 lettera",
            _    => qty > 1 ? $"{qty} cartas"   : "1 carta",
        };
    }

    private static string FormatAge(TimeSpan age, string lang)
    {
        if (age.TotalMinutes < 60)
            return lang switch
            {
                "es" => "ahora mismo",
                "en" => "just now",
                "fr" => "à l'instant",
                "it" => "adesso",
                _    => "agora mesmo",
            };
        if (age.TotalDays < 1)
        {
            var h = (int)age.TotalHours;
            return lang switch
            {
                "es" => $"hace {h}h",
                "en" => $"{h}h ago",
                "fr" => $"il y a {h}h",
                "it" => $"{h}h fa",
                _    => $"há {h}h",
            };
        }
        var d = (int)age.TotalDays;
        return lang switch
        {
            "es" => d == 1 ? "hace 1 día"       : $"hace {d} días",
            "en" => d == 1 ? "1 day ago"         : $"{d} days ago",
            "fr" => d == 1 ? "il y a 1 jour"     : $"il y a {d} jours",
            "it" => d == 1 ? "1 giorno fa"       : $"{d} giorni fa",
            _    => d == 1 ? "há 1 dia"           : $"há {d} dias",
        };
    }

    private static string FormatSpace(ReservationSpace space, string lang) =>
        space == ReservationSpace.Cozinha
            ? lang switch
            {
                "es" => "Cocina MasterChef",
                "en" => "MasterChef Kitchen",
                "fr" => "Cuisine MasterChef",
                "it" => "Cucina MasterChef",
                _    => "Cozinha MasterChef",
            }
            : "Cinema";

    private static CultureInfo GetCulture(string lang) => lang switch
    {
        "es" => CultureInfo.GetCultureInfo("es-ES"),
        "en" => CultureInfo.GetCultureInfo("en-GB"),
        "fr" => CultureInfo.GetCultureInfo("fr-FR"),
        "it" => CultureInfo.GetCultureInfo("it-IT"),
        _    => CultureInfo.GetCultureInfo("pt-PT"),
    };
}
