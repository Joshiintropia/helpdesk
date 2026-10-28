using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HelpDesk.Api.Endpoints;
using HelpDesk.Domain.Sla;
using HelpDesk.Domain.Tickets;
using HelpDesk.Tests.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpDesk.Tests.Sla;

/// <summary>The API on a controllable clock that starts on Wednesday 28 Oct 2026 at 10:00 in Mexico City.</summary>
public sealed class SlaApiFactory : HelpDeskApiFactory
{
    public static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.FromHours(-6)).ToUniversalTime();

    public TestClock Clock { get; } = new() { UtcNow = Start };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock)));
    }
}

/// <summary>A clock tests can move freely, backwards too, since the tests in a class share it.</summary>
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

public class SlaFlowTests(SlaApiFactory factory) : IClassFixture<SlaApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly TimeSpan Mexico = TimeSpan.FromHours(-6);

    // Tests in a class share the clock, so each one runs against the time it sets.
    private void SetClock(DateTimeOffset at) => factory.Clock.UtcNow = at.ToUniversalTime();

    [Fact]
    public async Task A_new_ticket_gets_due_dates_from_the_default_normal_policy()
    {
        SetClock(SlaApiFactory.Start);
        var (_, area, agent) = await SetUpAsync("ch-default");
        var requester = await factory.SignInAsync("sla.normal@test.local");

        var ticket = await CreateTicketAsync(requester, area, "No puedo descargar mi factura");

        var segment = Assert.Single((await GetAsync<TicketDetailDto>(agent, $"/api/tickets/{ticket.Id}")).Segments!);
        // Normal: first response 4 business hours, resolution 3 business days (27 h of 9-hour days).
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 14, 0, 0, Mexico), segment.FirstResponseDueAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 10, 0, 0, Mexico), segment.ResolutionDueAt);
        Assert.Equal(SlaState.OnTrack, segment.Sla.State);
        Assert.Equal(SlaTarget.FirstResponse, segment.Sla.Target);
    }

    [Fact]
    public async Task Raising_the_priority_brings_the_due_dates_closer_and_replying_switches_to_resolution()
    {
        SetClock(SlaApiFactory.Start);
        var (_, area, agent) = await SetUpAsync("ch-urgente");
        var requester = await factory.SignInAsync("sla.urgente@test.local");
        var ticket = await CreateTicketAsync(requester, area, "Se cayó el sistema de cobro");

        var raised = await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/priority", new { priority = "Urgent" });
        raised.EnsureSuccessStatusCode();
        var segment = Assert.Single((await raised.Content.ReadFromJsonAsync<TicketDetailDto>(Json))!.Segments!);
        Assert.Equal(SlaApiFactory.Start.AddMinutes(30), segment.FirstResponseDueAt);
        Assert.Equal(SlaApiFactory.Start.AddHours(4), segment.ResolutionDueAt);

        // 40 minutes later nobody has answered: the first response is late.
        SetClock(SlaApiFactory.Start.AddMinutes(40));
        var item = Assert.Single(await QueueAsync(agent), i => i.TicketId == ticket.Id);
        Assert.Equal(SlaState.Breached, item.Sla.State);
        Assert.Equal(SlaTarget.FirstResponse, item.Sla.Target);

        (await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/messages", new { body = "Ya lo estamos revisando" })).EnsureSuccessStatusCode();
        item = Assert.Single(await QueueAsync(agent), i => i.TicketId == ticket.Id);
        Assert.Equal(SlaTarget.Resolution, item.Sla.Target);
        Assert.Equal(SlaState.OnTrack, item.Sla.State);

        // Under a quarter of the four hours left.
        SetClock(SlaApiFactory.Start.AddHours(3.5));
        Assert.Equal(SlaState.AtRisk, Assert.Single(await QueueAsync(agent), i => i.TicketId == ticket.Id).Sla.State);
    }

    [Fact]
    public async Task Waiting_on_the_requester_stops_the_clock()
    {
        SetClock(SlaApiFactory.Start);
        var (_, area, agent) = await SetUpAsync("ch-espera");
        var requester = await factory.SignInAsync("sla.espera@test.local");
        var ticket = await CreateTicketAsync(requester, area, "Necesito cambiar mi RFC");
        var before = Assert.Single((await GetAsync<TicketDetailDto>(agent, $"/api/tickets/{ticket.Id}")).Segments!).ResolutionDueAt;

        SetClock(SlaApiFactory.Start.AddHours(1));
        (await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/messages", new { body = "¿Nos mandas tu constancia fiscal?" })).EnsureSuccessStatusCode();
        (await agent.PostAsync($"/api/tickets/{ticket.Id}/wait", null)).EnsureSuccessStatusCode();
        Assert.Equal(SlaState.Paused, Assert.Single(await QueueAsync(agent), i => i.TicketId == ticket.Id).Sla.State);

        // The requester answers two business hours later.
        SetClock(SlaApiFactory.Start.AddHours(3));
        (await requester.PostAsJsonAsync($"/api/tickets/{ticket.Id}/messages", new { body = "Listo, ya la adjunté" })).EnsureSuccessStatusCode();

        var after = Assert.Single((await GetAsync<TicketDetailDto>(agent, $"/api/tickets/{ticket.Id}")).Segments!);
        Assert.Equal(before!.Value.AddHours(2), after.ResolutionDueAt);
        Assert.Equal(SlaState.OnTrack, after.Sla.State);
    }

    [Fact]
    public async Task Each_area_is_measured_only_on_its_own_time()
    {
        SetClock(SlaApiFactory.Start);
        var (admin, ch, chAgent) = await SetUpAsync("ch-cadena");
        var finance = await CreateAreaAsync(admin, "Finanzas-cadena");
        var financeAgent = await AgentAsync(admin, finance, "agente.finanzas-cadena@test.local");
        var requester = await factory.SignInAsync("sla.cadena@test.local");
        var ticket = await CreateTicketAsync(requester, ch, "Pagué y no se refleja");
        var root = Assert.Single((await GetAsync<TicketDetailDto>(chAgent, $"/api/tickets/{ticket.Id}")).Segments!);

        SetClock(SlaApiFactory.Start.AddMinutes(30));
        (await chAgent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/escalate", new { segmentId = root.Id, toAreaId = finance.Id, reason = "Validar el pago" })).EnsureSuccessStatusCode();
        var detail = await GetAsync<TicketDetailDto>(chAgent, $"/api/tickets/{ticket.Id}");
        Assert.NotNull(detail.Segments!.Single(s => s.Id == root.Id).FirstRespondedAt);
        var financeSegment = detail.Segments!.Single(s => s.AreaId == finance.Id);
        Assert.Equal(SlaApiFactory.Start.AddMinutes(30).AddHours(4), financeSegment.FirstResponseDueAt);

        // Finance takes three business hours to answer its part.
        SetClock(SlaApiFactory.Start.AddMinutes(210));
        (await financeAgent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/segments/{financeSegment.Id}/resolve", new { note = "Pago acreditado" })).EnsureSuccessStatusCode();

        var rootAfter = (await GetAsync<TicketDetailDto>(chAgent, $"/api/tickets/{ticket.Id}")).Segments!.Single(s => s.Id == root.Id);
        Assert.Equal(root.ResolutionDueAt!.Value.AddHours(3), rootAfter.ResolutionDueAt);
        Assert.Equal(SlaState.Met, (await GetAsync<TicketDetailDto>(chAgent, $"/api/tickets/{ticket.Id}")).Segments!.Single(s => s.Id == financeSegment.Id).Sla.State);
    }

    [Fact]
    public async Task Admins_can_set_hours_holidays_and_targets_per_area()
    {
        SetClock(SlaApiFactory.Start);
        var (admin, area, agent) = await SetUpAsync("ti-propio");

        var settings = await GetAsync<SlaSettingsDto>(admin, "/api/sla");
        Assert.Equal("America/Mexico_City", settings.Calendar.TimeZone);
        Assert.Equal(4, settings.Policies.Count(p => p.AreaId is null));

        var policies = settings.Policies
            .Select(p => new SaveSlaPolicyRequest(p.AreaId, p.Priority, p.FirstResponseMinutes, p.ResolutionMinutes))
            .Where(p => p.AreaId != area.Id)
            .Append(new SaveSlaPolicyRequest(area.Id, TicketPriority.Normal, 60, 120))
            .ToList();
        (await admin.PutAsJsonAsync("/api/sla/policies", policies, Json)).EnsureSuccessStatusCode();

        var requester = await factory.SignInAsync("sla.propio@test.local");
        var ticket = await CreateTicketAsync(requester, area, "La VPN no conecta");
        var segment = Assert.Single((await GetAsync<TicketDetailDto>(agent, $"/api/tickets/{ticket.Id}")).Segments!);
        Assert.Equal(SlaApiFactory.Start.AddHours(1), segment.FirstResponseDueAt);
        Assert.Equal(SlaApiFactory.Start.AddHours(2), segment.ResolutionDueAt);

        var holiday = await admin.PostAsJsonAsync("/api/sla/calendar/holidays", new { date = "2026-11-16", name = "Día de la Revolución" });
        holiday.EnsureSuccessStatusCode();
        Assert.Contains((await holiday.Content.ReadFromJsonAsync<SlaSettingsDto>(Json))!.Calendar.Holidays, h => h.Date == new DateOnly(2026, 11, 16));

        var badHours = await admin.PutAsJsonAsync("/api/sla/calendar", new { timeZone = "America/Mexico_City", workdayStart = "18:00:00", workdayEnd = "09:00:00", workingDays = new[] { "Monday" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badHours.StatusCode);

        var badPolicy = await admin.PutAsJsonAsync("/api/sla/policies", new[] { new { areaId = (int?)null, priority = "Normal", firstResponseMinutes = 600, resolutionMinutes = 60 } });
        Assert.Equal(HttpStatusCode.BadRequest, badPolicy.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await agent.GetAsync("/api/sla")).StatusCode);
    }

    private async Task<(HttpClient Admin, AreaDto Area, HttpClient Agent)> SetUpAsync(string name)
    {
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        var area = await CreateAreaAsync(admin, name);
        return (admin, area, await AgentAsync(admin, area, $"agente.{name}@test.local"));
    }

    private async Task<HttpClient> AgentAsync(HttpClient admin, AreaDto area, string email)
    {
        var agent = await factory.SignInAsync(email);
        (await admin.PostAsJsonAsync($"/api/areas/{area.Id}/members", new { email })).EnsureSuccessStatusCode();
        return agent;
    }

    private static async Task<AreaDto> CreateAreaAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync("/api/areas", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AreaDto>(Json))!;
    }

    private static async Task<TicketDetailDto> CreateTicketAsync(HttpClient requester, AreaDto area, string description)
    {
        var response = await requester.PostAsJsonAsync("/api/tickets", new { areaId = area.Id, description });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TicketDetailDto>(Json))!;
    }

    private static Task<List<QueueItemDto>> QueueAsync(HttpClient agent) => GetAsync<List<QueueItemDto>>(agent, "/api/tickets/queue");

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
}
