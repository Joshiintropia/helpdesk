using System.Text.RegularExpressions;

namespace HelpDesk.Infrastructure.Email;

/// <summary>Helpers to read what a person actually wrote in an email and which ticket it belongs to.</summary>
public static partial class EmailText
{
    /// <summary>Every notification subject carries "[#123]" so a reply finds its ticket.</summary>
    public static string SubjectTag(long ticketId) => $"[#{ticketId}]";

    public static long? TicketIdFromSubject(string? subject)
    {
        var match = TicketTag().Match(subject ?? "");
        return match.Success && long.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>Subject without "RE:", "RV:", "FW:" prefixes or the ticket tag.</summary>
    public static string CleanSubject(string? subject)
    {
        var clean = TicketTag().Replace(subject ?? "", "");
        clean = ReplyPrefix().Replace(clean, "");
        clean = NotificationPrefix().Replace(clean, "");
        return clean.Trim();
    }

    /// <summary>
    /// Keeps only the new text of a reply: drops quoted lines and everything after the usual
    /// "-----Original Message-----" / "De: ..." / "El ... escribió:" markers of Outlook, Gmail and others.
    /// </summary>
    public static string StripQuotedReply(string? body)
    {
        var lines = (body ?? "").Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (QuoteMarker().IsMatch(trimmed))
            {
                break;
            }

            if (trimmed.StartsWith('>'))
            {
                continue;
            }

            kept.Add(line.TrimEnd());
        }

        return string.Join('\n', kept).Trim();
    }

    [GeneratedRegex(@"\[#(\d+)\]")]
    private static partial Regex TicketTag();

    [GeneratedRegex(@"^(\s*(re|rv|fw|fwd|reenviar|responder)\s*:\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex ReplyPrefix();

    // The start of our own notification subjects, so a reply keeps only the ticket's title.
    [GeneratedRegex(@"^\s*(Recibimos tu solicitud|Respuesta a tu solicitud|Tu solicitud quedó resuelta)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex NotificationPrefix();

    [GeneratedRegex(
        @"^(-{2,}\s*(original message|mensaje original)\s*-{2,}|_{10,}|(de|from)\s*:\s*.+|(el|on)\s.+(escribió|wrote)\s*:)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex QuoteMarker();
}
