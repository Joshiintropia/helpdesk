using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HelpDesk.Api.Endpoints;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Email;
using HelpDesk.Infrastructure.Persistence;
using HelpDesk.Tests.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Email;

public class EmailFlowTests(EmailApiFactory factory) : IClassFixture<EmailApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task An_email_from_someone_new_opens_a_ticket_and_they_get_its_number()
    {
        await EnsureAreaAsync();

        var outcome = await factory.ReceiveAsync(Email("nuevo@cliente.com", "No puedo entrar al portal", "Me sale error 500 desde ayer.", name: "Ana López"));

        Assert.Equal(InboundOutcome.TicketCreated, outcome);
        var ticket = await LastTicketOfAsync("nuevo@cliente.com");
        Assert.Equal("No puedo entrar al portal", ticket.Title);
        Assert.Equal("Me sale error 500 desde ayer.", ticket.Description);

        await factory.SendPendingAsync();
        var received = Assert.Single(factory.Mail.To("nuevo@cliente.com"));
        Assert.StartsWith($"[#{ticket.Id}] Recibimos tu solicitud", received.Subject);
        Assert.Contains("Hola Ana López,", received.Body);
        Assert.Contains($"https://ayuda.test.local/solicitudes/{ticket.Id}", received.Body);
    }

    [Fact]
    public async Task A_reply_with_the_ticket_number_lands_on_the_same_ticket_and_reopens_it()
    {
        var area = await EnsureAreaAsync();
        var agentEmail = "agente.correo@test.local";
        var agent = await SignInAgentAsync(agentEmail, area);

        await factory.ReceiveAsync(Email("responde@cliente.com", "La factura no llegó", "Necesito la factura de octubre."));
        var ticket = await LastTicketOfAsync("responde@cliente.com");
        (await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/resolve", new { note = "Reenviada" })).EnsureSuccessStatusCode();

        var outcome = await factory.ReceiveAsync(Email(
            "RESPONDE@cliente.com",
            $"RE: [#{ticket.Id}] Tu solicitud quedó resuelta: La factura no llegó",
            "Sigue sin llegar, revisen por favor.\n\nDe: Mesa de ayuda <ayuda@test.local>\nMarcamos tu solicitud como resuelta."));

        Assert.Equal(InboundOutcome.MessageAdded, outcome);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var reloaded = await db.Tickets.Include(t => t.Messages).SingleAsync(t => t.Id == ticket.Id);
        Assert.Equal(TicketStatus.InProgress, reloaded.Status);
        var message = Assert.Single(reloaded.Messages);
        Assert.Equal("Sigue sin llegar, revisen por favor.", message.Body);
        Assert.Equal(MessageSource.Email, message.Source);
    }

    [Fact]
    public async Task Agent_replies_and_resolutions_are_emailed_but_internal_notes_are_not()
    {
        var area = await EnsureAreaAsync();
        var agentEmail = "agente.avisos@test.local";
        var agent = await SignInAgentAsync(agentEmail, area);
        await factory.ReceiveAsync(Email("avisos@cliente.com", "Cambio de domicilio", "Quiero actualizar mi domicilio."));
        var ticket = await LastTicketOfAsync("avisos@cliente.com");

        (await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/messages", new { body = "Revisar en CRM", visibility = "InternalAllAreas" })).EnsureSuccessStatusCode();
        (await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/messages", new { body = "¿Nos compartes tu comprobante?", visibility = "Public" })).EnsureSuccessStatusCode();
        (await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/resolve", new { note = "Actualizado" })).EnsureSuccessStatusCode();
        await factory.SendPendingAsync();

        var subjects = factory.Mail.To("avisos@cliente.com").Select(e => e.Subject).ToList();
        Assert.Equal(3, subjects.Count);
        Assert.Contains(subjects, s => s.StartsWith($"[#{ticket.Id}] Recibimos"));
        Assert.Contains(subjects, s => s.StartsWith($"[#{ticket.Id}] Tu solicitud quedó resuelta"));
        var reply = Assert.Single(factory.Mail.To("avisos@cliente.com"), e => e.Subject.Contains("Respuesta"));
        Assert.Contains("¿Nos compartes tu comprobante?", reply.Body);
        Assert.DoesNotContain(factory.Mail.Sent, e => e.Body.Contains("Revisar en CRM"));
    }

    [Fact]
    public async Task An_agent_can_answer_by_email_and_a_stranger_cannot_write_into_someone_elses_ticket()
    {
        var area = await EnsureAreaAsync();
        var agentEmail = "agente.buzon@test.local";
        await SignInAgentAsync(agentEmail, area);
        await factory.ReceiveAsync(Email("dueno@cliente.com", "Pago duplicado", "Me cobraron dos veces."));
        var ticket = await LastTicketOfAsync("dueno@cliente.com");

        Assert.Equal(InboundOutcome.MessageAdded, await factory.ReceiveAsync(Email(agentEmail, $"RE: [#{ticket.Id}] Pago duplicado", "Ya solicitamos el reembolso.")));
        Assert.Equal(InboundOutcome.TicketCreated, await factory.ReceiveAsync(Email("intruso@cliente.com", $"RE: [#{ticket.Id}] Pago duplicado", "Quiero ver este ticket")));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var message = Assert.Single(await db.TicketMessages.Where(m => m.TicketId == ticket.Id).ToListAsync());
        Assert.Equal(MessageVisibility.Public, message.Visibility);
        Assert.Equal(area.Id, message.AuthorAreaId);
    }

    [Fact]
    public async Task Duplicates_auto_replies_and_our_own_mailbox_are_ignored()
    {
        await EnsureAreaAsync();
        var email = Email("repetido@cliente.com", "Sin internet", "No tengo internet.");

        Assert.Equal(InboundOutcome.TicketCreated, await factory.ReceiveAsync(email));
        Assert.Null(await factory.ReceiveAsync(email));
        Assert.Equal(InboundOutcome.IgnoredAutoReply, await factory.ReceiveAsync(Email("vacaciones@cliente.com", "Fuera de la oficina", "Regreso el lunes", autoReply: true)));
        Assert.Equal(InboundOutcome.IgnoredOwnMailbox, await factory.ReceiveAsync(Email(EmailApiFactory.HelpMailbox, "[#1] Recibimos tu solicitud", "Bucle")));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.Equal(1, await db.Tickets.CountAsync(t => db.Users.Any(u => u.Id == t.RequesterId && u.Email == "repetido@cliente.com")));
        Assert.False(await db.Users.AnyAsync(u => u.Email == "vacaciones@cliente.com"));
    }

    [Fact]
    public async Task A_failed_send_is_retried_later()
    {
        await EnsureAreaAsync();
        factory.Mail.Fail = true;
        try
        {
            await factory.ReceiveAsync(Email("reintento@cliente.com", "Correo caído", "Prueba"));
            await factory.SendPendingAsync();
        }
        finally
        {
            factory.Mail.Fail = false;
        }

        Assert.Empty(factory.Mail.To("reintento@cliente.com"));
        await factory.SendPendingAsync();
        Assert.Single(factory.Mail.To("reintento@cliente.com"));
    }

    private static InboundEmail Email(string from, string subject, string body, string? name = null, bool autoReply = false) =>
        new(Guid.NewGuid().ToString("N"), $"<{Guid.NewGuid():N}@cliente.com>", from, name, subject, body, DateTimeOffset.UtcNow, autoReply);

    private async Task<AreaDto> EnsureAreaAsync()
    {
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        var areas = await (await admin.GetAsync("/api/areas")).Content.ReadFromJsonAsync<List<AreaDto>>(Json);
        if (areas!.FirstOrDefault() is { } existing)
        {
            return existing;
        }

        var created = await admin.PostAsJsonAsync("/api/areas", new { name = "Customer Happiness" });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<AreaDto>(Json))!;
    }

    private async Task<HttpClient> SignInAgentAsync(string email, AreaDto area)
    {
        var agent = await factory.SignInAsync(email);
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        (await admin.PostAsJsonAsync($"/api/areas/{area.Id}/members", new { email })).EnsureSuccessStatusCode();
        return agent;
    }

    private async Task<Ticket> LastTicketOfAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return await db.Tickets.AsNoTracking().Where(t => t.RequesterId == userId).OrderByDescending(t => t.Id).FirstAsync();
    }
}
