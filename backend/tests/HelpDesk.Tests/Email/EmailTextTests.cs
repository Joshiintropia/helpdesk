using HelpDesk.Infrastructure.Email;

namespace HelpDesk.Tests.Email;

public class EmailTextTests
{
    [Theory]
    [InlineData("RE: [#42] Recibimos tu solicitud: No tengo red", 42L)]
    [InlineData("[#7]", 7L)]
    [InlineData("No funciona la impresora", null)]
    [InlineData("Folio #42 sin corchetes", null)]
    public void Finds_the_ticket_number_in_the_subject(string subject, long? expected) =>
        Assert.Equal(expected, EmailText.TicketIdFromSubject(subject));

    [Theory]
    [InlineData("RE: RV: [#42] No tengo red", "No tengo red")]
    [InlineData("Fwd: Pago no reflejado", "Pago no reflejado")]
    [InlineData("Revisión de factura", "Revisión de factura")]
    public void Cleans_reply_prefixes_and_the_tag(string subject, string expected) =>
        Assert.Equal(expected, EmailText.CleanSubject(subject));

    [Fact]
    public void Keeps_only_the_new_text_of_an_outlook_reply()
    {
        const string body = "Ya lo intenté y sigue igual.\r\nGracias\r\n\r\nDe: Mesa de ayuda <ayuda@empresa.com>\r\nEnviado: lunes\r\nRecibimos tu solicitud";
        Assert.Equal("Ya lo intenté y sigue igual.\nGracias", EmailText.StripQuotedReply(body));
    }

    [Fact]
    public void Keeps_only_the_new_text_of_a_gmail_reply()
    {
        const string body = "Listo, ya quedó.\n\nEl lun, 26 oct 2026 a las 10:00, Mesa de ayuda (<ayuda@empresa.com>) escribió:\n> Recibimos tu solicitud";
        Assert.Equal("Listo, ya quedó.", EmailText.StripQuotedReply(body));
    }

    [Fact]
    public void Drops_quoted_lines()
    {
        Assert.Equal("Sí\n\nOtra línea", EmailText.StripQuotedReply("Sí\n> citado\n\nOtra línea"));
    }
}
