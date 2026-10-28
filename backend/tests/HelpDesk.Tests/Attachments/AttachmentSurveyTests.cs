using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HelpDesk.Api.Endpoints;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Attachments;
using HelpDesk.Infrastructure.Email;
using HelpDesk.Infrastructure.Persistence;
using HelpDesk.Tests.Api;
using HelpDesk.Tests.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Attachments;

public class AttachmentSurveyTests(EmailApiFactory factory) : IClassFixture<EmailApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task A_requester_sends_a_screenshot_with_the_request_and_the_agent_downloads_it()
    {
        var (area, agent) = await SetUpAsync("adjunto");
        var requester = await factory.SignInAsync("adjunta@test.local");

        var file = await UploadAsync(requester, "captura.png", "image/png", [0x89, 0x50, 0x4E, 0x47]);
        Assert.True(file.IsImage);
        var created = await requester.PostAsJsonAsync("/api/tickets", new { areaId = area.Id, description = "Me sale este error", attachmentIds = new[] { file.Id } });
        created.EnsureSuccessStatusCode();
        var ticket = (await created.Content.ReadFromJsonAsync<TicketDetailDto>(Json))!;
        Assert.Equal(file.Id, Assert.Single(ticket.Attachments).Id);

        var download = await agent.GetAsync($"/api/attachments/{file.Id}");
        download.EnsureSuccessStatusCode();
        Assert.Equal("image/png", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, await download.Content.ReadAsByteArrayAsync());

        var stranger = await factory.SignInAsync("ajeno.adjunto@test.local");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/attachments/{file.Id}")).StatusCode);
    }

    [Fact]
    public async Task Files_on_an_internal_note_stay_internal_and_other_files_download_as_files()
    {
        var (area, agent) = await SetUpAsync("nota");
        var requester = await factory.SignInAsync("nota.interna@test.local");
        var ticket = await CreateTicketAsync(requester, area.Id, "Revisen mi contrato");

        var note = await UploadAsync(agent, "contrato.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-1.7"));
        Assert.False(note.IsImage);
        (await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/messages", new { visibility = "InternalAllAreas", attachmentIds = new[] { note.Id } })).EnsureSuccessStatusCode();

        var seenByAgent = await GetAsync<TicketDetailDto>(agent, $"/api/tickets/{ticket.Id}");
        var message = Assert.Single(seenByAgent.Messages);
        Assert.Equal(EmailIngestionService.AttachmentsOnlyText, message.Body);
        Assert.Equal(note.Id, Assert.Single(message.Attachments).Id);

        Assert.Empty((await GetAsync<TicketDetailDto>(requester, $"/api/tickets/{ticket.Id}")).Messages);
        Assert.Equal(HttpStatusCode.NotFound, (await requester.GetAsync($"/api/attachments/{note.Id}")).StatusCode);

        var download = await agent.GetAsync($"/api/attachments/{note.Id}");
        // Anything that is not an image is served as an opaque download.
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Fact]
    public async Task Dangerous_or_borrowed_files_are_refused()
    {
        var (area, _) = await SetUpAsync("bloqueo");
        var requester = await factory.SignInAsync("bloqueo@test.local");
        var other = await factory.SignInAsync("otro.bloqueo@test.local");

        var blocked = await PostFileAsync(requester, "factura.exe", "application/octet-stream", [1, 2, 3]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);

        var empty = await PostFileAsync(requester, "vacio.txt", "text/plain", []);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);

        // Someone else's upload cannot be sent with your request.
        var theirs = await UploadAsync(other, "suyo.txt", "text/plain", Encoding.UTF8.GetBytes("privado"));
        var borrowed = await requester.PostAsJsonAsync("/api/tickets", new { areaId = area.Id, description = "Con archivo ajeno", attachmentIds = new[] { theirs.Id } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, borrowed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await requester.GetAsync($"/api/attachments/{theirs.Id}")).StatusCode);
    }

    [Fact]
    public async Task Files_that_arrive_by_email_are_attached_and_blocked_ones_are_skipped()
    {
        await SetUpAsync("correo-adjunto");
        var files = new List<InboundFile>
        {
            new("recibo.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF]),
            new("macro.js", "text/javascript", Encoding.UTF8.GetBytes("alert(1)")),
        };

        Assert.Equal(InboundOutcome.TicketCreated, await factory.ReceiveAsync(Email("adjuntos@cliente.com", "Mi recibo", "Les mando el recibo.", files)));
        var ticket = await LastTicketOfAsync("adjuntos@cliente.com");

        Assert.Equal(InboundOutcome.MessageAdded, await factory.ReceiveAsync(Email(
            "adjuntos@cliente.com", $"RE: [#{ticket.Id}] Mi recibo", "", [new("otro.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF"))])));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.TicketAttachments.Where(a => a.TicketId == ticket.Id).OrderBy(a => a.CreatedAt).ToListAsync();
        Assert.Equal(["recibo.jpg", "otro.pdf"], stored.Select(a => a.FileName));
        Assert.Null(stored[0].MessageId);
        var reply = await db.TicketMessages.SingleAsync(m => m.Id == stored[1].MessageId);
        Assert.Equal(EmailIngestionService.AttachmentsOnlyText, reply.Body);
    }

    [Fact]
    public async Task The_requester_rates_from_the_resolution_email_link_and_only_once_after_closing()
    {
        var (_, agent) = await SetUpAsync("encuesta");
        await factory.ReceiveAsync(Email("encuesta@cliente.com", "Sin acceso a la nómina", "No veo mi recibo de nómina."));
        var ticket = await LastTicketOfAsync("encuesta@cliente.com");
        (await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/resolve", new { note = "Permiso otorgado" })).EnsureSuccessStatusCode();
        await factory.SendPendingAsync();

        var resolvedEmail = Assert.Single(factory.Mail.To("encuesta@cliente.com"), e => e.Subject.Contains("resuelta"));
        var token = Regex.Match(resolvedEmail.Body, @"/encuesta/(\S+)").Groups[1].Value;
        Assert.NotEmpty(token);

        // No sign-in: the link is the access.
        var anonymous = factory.CreateClient();
        var survey = await GetAsync<SurveyDto>(anonymous, $"/api/survey/{token}");
        Assert.Equal(ticket.Id, survey.TicketId);
        Assert.True(survey.CanRate);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anonymous.PostAsJsonAsync($"/api/survey/{token}", new { rating = 6 })).StatusCode);
        var rated = await anonymous.PostAsJsonAsync($"/api/survey/{token}", new { rating = 5, comment = "Muy rápido" });
        rated.EnsureSuccessStatusCode();
        Assert.Equal(5, (await rated.Content.ReadFromJsonAsync<SurveyDto>(Json))!.Rating);

        var detail = await GetAsync<TicketDetailDto>(agent, $"/api/tickets/{ticket.Id}");
        Assert.Equal(5, detail.Satisfaction!.Rating);
        Assert.Equal("Muy rápido", detail.Satisfaction.Comment);

        (await agent.PostAsync($"/api/tickets/{ticket.Id}/close", null)).EnsureSuccessStatusCode();
        Assert.False((await GetAsync<SurveyDto>(anonymous, $"/api/survey/{token}")).CanRate);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anonymous.PostAsJsonAsync($"/api/survey/{token}", new { rating = 1 })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/survey/{token}x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/survey/no-es-un-token")).StatusCode);
    }

    [Fact]
    public async Task A_signed_in_requester_rates_in_the_app_but_not_before_it_is_resolved()
    {
        var (area, agent) = await SetUpAsync("califica");
        var requester = await factory.SignInAsync("califica@test.local");
        var ticket = await CreateTicketAsync(requester, area.Id, "Cambiar mi contraseña");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await requester.PostAsJsonAsync($"/api/tickets/{ticket.Id}/rate", new { rating = 4 })).StatusCode);
        (await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/resolve", new { note = "Listo" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/rate", new { rating = 1 })).StatusCode);
        var rated = await requester.PostAsJsonAsync($"/api/tickets/{ticket.Id}/rate", new { rating = 4 });
        rated.EnsureSuccessStatusCode();
        Assert.Equal(4, (await rated.Content.ReadFromJsonAsync<TicketDetailDto>(Json))!.Satisfaction!.Rating);
    }

    [Fact]
    public async Task Replying_to_a_closed_request_opens_a_follow_up()
    {
        var (_, agent) = await SetUpAsync("seguimiento");
        await factory.ReceiveAsync(Email("seguimiento@cliente.com", "Alta en el sistema", "Necesito usuario."));
        var ticket = await LastTicketOfAsync("seguimiento@cliente.com");
        (await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/resolve", new { note = "Creado" })).EnsureSuccessStatusCode();
        (await agent.PostAsync($"/api/tickets/{ticket.Id}/close", null)).EnsureSuccessStatusCode();

        var outcome = await factory.ReceiveAsync(Email(
            "seguimiento@cliente.com",
            $"RE: [#{ticket.Id}] Tu solicitud quedó resuelta: Alta en el sistema",
            "Ya entré pero no tengo permisos.\n\nDe: Mesa de ayuda <ayuda@test.local>\nMarcamos tu solicitud como resuelta."));

        Assert.Equal(InboundOutcome.TicketCreated, outcome);
        var followUp = await LastTicketOfAsync("seguimiento@cliente.com");
        Assert.NotEqual(ticket.Id, followUp.Id);
        Assert.Equal("Alta en el sistema", followUp.Title);
        Assert.Equal($"Seguimiento de la solicitud #{ticket.Id}.\n\nYa entré pero no tengo permisos.", followUp.Description);
    }

    private static InboundEmail Email(string from, string subject, string body, IReadOnlyList<InboundFile>? files = null) =>
        new(Guid.NewGuid().ToString("N"), $"<{Guid.NewGuid():N}@cliente.com>", from, null, subject, body, DateTimeOffset.UtcNow, false, files);

    private async Task<(AreaDto Area, HttpClient Agent)> SetUpAsync(string name)
    {
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        var areas = await GetAsync<List<AreaDto>>(admin, "/api/areas");
        var area = areas.FirstOrDefault();
        if (area is null)
        {
            var created = await admin.PostAsJsonAsync("/api/areas", new { name = "Customer Happiness" });
            created.EnsureSuccessStatusCode();
            area = (await created.Content.ReadFromJsonAsync<AreaDto>(Json))!;
        }

        var email = $"agente.{name}@test.local";
        var agent = await factory.SignInAsync(email);
        (await admin.PostAsJsonAsync($"/api/areas/{area.Id}/members", new { email })).EnsureSuccessStatusCode();
        return (area, agent);
    }

    private static async Task<HttpResponseMessage> PostFileAsync(HttpClient client, string name, string type, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(part, "file", name);
        return await client.PostAsync("/api/attachments", form);
    }

    private static async Task<AttachmentDto> UploadAsync(HttpClient client, string name, string type, byte[] content)
    {
        var response = await PostFileAsync(client, name, type, content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AttachmentDto>(Json))!;
    }

    private static async Task<TicketDetailDto> CreateTicketAsync(HttpClient requester, int areaId, string description)
    {
        var response = await requester.PostAsJsonAsync("/api/tickets", new { areaId, description });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TicketDetailDto>(Json))!;
    }

    private async Task<Ticket> LastTicketOfAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return await db.Tickets.AsNoTracking().Where(t => t.RequesterId == userId).OrderByDescending(t => t.Id).FirstAsync();
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
}
