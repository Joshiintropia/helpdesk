using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HelpDesk.Api.Endpoints;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Maintenance;
using HelpDesk.Infrastructure.Persistence;
using HelpDesk.Tests.Api;
using HelpDesk.Tests.Sla;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Maintenance;

public class AutoCloseTests(SlaApiFactory factory) : IClassFixture<SlaApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly TimeSpan Mexico = TimeSpan.FromHours(-6);

    [Fact]
    public async Task A_resolved_ticket_closes_after_three_business_days_unless_the_requester_answers()
    {
        // Resolved Wednesday 28 Oct at 10:00: three business days later is Monday 2 Nov at 10:00.
        factory.Clock.UtcNow = SlaApiFactory.Start;
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        var area = (await (await admin.PostAsJsonAsync("/api/areas", new { name = "TI-cierre" })).Content.ReadFromJsonAsync<AreaDto>(Json))!;
        var agent = await factory.SignInAsync("agente.cierre@test.local");
        (await admin.PostAsJsonAsync($"/api/areas/{area.Id}/members", new { email = "agente.cierre@test.local" })).EnsureSuccessStatusCode();
        var requester = await factory.SignInAsync("cierre@test.local");

        var silent = await CreateAndResolveAsync(requester, agent, area, "No imprime");
        var answered = await CreateAndResolveAsync(requester, agent, area, "No escanea");

        // Sessions expire as the clock moves, so sign in again.
        factory.Clock.UtcNow = new DateTimeOffset(2026, 10, 30, 12, 0, 0, Mexico).ToUniversalTime();
        requester = await factory.SignInAsync("cierre@test.local", register: false);
        (await requester.PostAsJsonAsync($"/api/tickets/{answered}/messages", new { body = "Sigue sin escanear" })).EnsureSuccessStatusCode();

        // Friday: still waiting.
        Assert.Equal(0, await RunAsync(m => m.CloseResolvedAsync(CancellationToken.None)));

        factory.Clock.UtcNow = new DateTimeOffset(2026, 11, 2, 10, 0, 0, Mexico).ToUniversalTime();
        Assert.Equal(1, await RunAsync(m => m.CloseResolvedAsync(CancellationToken.None)));

        Assert.Equal(TicketStatus.Closed, await StatusAsync(silent));
        Assert.Equal(TicketStatus.InProgress, await StatusAsync(answered));
    }

    [Fact]
    public async Task Uploads_never_sent_are_deleted_after_a_day()
    {
        factory.Clock.UtcNow = SlaApiFactory.Start;
        var requester = await factory.SignInAsync("olvido@test.local");
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("borrador")), "file", "borrador.txt" } };
        var upload = await requester.PostAsync("/api/attachments", form);
        upload.EnsureSuccessStatusCode();
        var id = (await upload.Content.ReadFromJsonAsync<AttachmentDto>(Json))!.Id;

        factory.Clock.UtcNow = SlaApiFactory.Start.AddHours(2);
        await RunAsync(m => m.CleanupUploadsAsync(CancellationToken.None));
        Assert.True((await requester.GetAsync($"/api/attachments/{id}")).IsSuccessStatusCode);

        factory.Clock.UtcNow = SlaApiFactory.Start.AddDays(2);
        await RunAsync(m => m.CleanupUploadsAsync(CancellationToken.None));
        requester = await factory.SignInAsync("olvido@test.local", register: false);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await requester.GetAsync($"/api/attachments/{id}")).StatusCode);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(factory.StoragePath, "attachments"), "*", SearchOption.AllDirectories));
    }

    private static async Task<long> CreateAndResolveAsync(HttpClient requester, HttpClient agent, AreaDto area, string description)
    {
        var created = await requester.PostAsJsonAsync("/api/tickets", new { areaId = area.Id, description });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<TicketDetailDto>(Json))!.Id;
        (await agent.PostAsJsonAsync($"/api/tickets/{id}/resolve", new { note = "Listo" })).EnsureSuccessStatusCode();
        return id;
    }

    private async Task<int> RunAsync(Func<TicketMaintenance, Task<int>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TicketMaintenance>());
    }

    private async Task<TicketStatus> StatusAsync(long id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().Tickets.Where(t => t.Id == id).Select(t => t.Status).SingleAsync();
    }
}
