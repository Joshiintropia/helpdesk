using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HelpDesk.Api.Endpoints;
using HelpDesk.Tests.Api;
using HelpDesk.Tests.Sla;

namespace HelpDesk.Tests.Metrics;

public class MetricsTests(SlaApiFactory factory) : IClassFixture<SlaApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task The_dashboard_counts_volume_sla_and_escalations_per_area()
    {
        var start = SlaApiFactory.Start;
        factory.Clock.UtcNow = start;
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        var ch = await CreateAreaAsync(admin, "Customer Happiness");
        var finance = await CreateAreaAsync(admin, "Finanzas");
        var chAgent = await AgentAsync(admin, ch, "agente.ch.metricas@test.local");
        var financeAgent = await AgentAsync(admin, finance, "agente.fin.metricas@test.local");
        var requester = await factory.SignInAsync("cliente.metricas@test.local");

        // Ticket 1: answered in one hour (on time) and resolved by Customer Happiness.
        var fast = await CreateTicketAsync(requester, ch, "No llega mi factura");
        factory.Clock.UtcNow = start.AddHours(1);
        (await chAgent.PostAsJsonAsync($"/api/tickets/{fast.Id}/messages", new { body = "Te la reenviamos" })).EnsureSuccessStatusCode();
        (await chAgent.PostAsJsonAsync($"/api/tickets/{fast.Id}/resolve", new { note = "Reenviada" })).EnsureSuccessStatusCode();
        (await requester.PostAsJsonAsync($"/api/tickets/{fast.Id}/rate", new { rating = 4 })).EnsureSuccessStatusCode();

        // Ticket 2: escalated to Finance after 5 business hours (first response late); still open.
        factory.Clock.UtcNow = start;
        var slow = await CreateTicketAsync(requester, ch, "Pagué y no se refleja");
        factory.Clock.UtcNow = start.AddHours(5);
        var root = (await GetAsync<TicketDetailDto>(chAgent, $"/api/tickets/{slow.Id}")).Segments!.Single();
        (await chAgent.PostAsJsonAsync($"/api/tickets/{slow.Id}/escalate", new { segmentId = root.Id, toAreaId = finance.Id, reason = "Validar el pago" })).EnsureSuccessStatusCode();

        factory.Clock.UtcNow = start.AddHours(6);
        var metrics = await GetAsync<MetricsDto>(admin, "/api/metrics?days=7");

        Assert.Equal(7, metrics.Daily.Count);
        Assert.Equal(2, metrics.Totals.Received);
        Assert.Equal(1, metrics.Totals.Resolved);
        Assert.Equal(1, metrics.Totals.Open);
        Assert.Equal(1, metrics.Totals.Satisfaction.Responses);
        Assert.Equal(4, metrics.Totals.Satisfaction.Average);
        Assert.Equal([0, 0, 0, 1, 0], metrics.Totals.Satisfaction.ByRating);
        Assert.Equal(2, metrics.Daily.Single(d => d.Date == new DateOnly(2026, 10, 28)).Received);

        var chRow = metrics.Areas.Single(a => a.AreaId == ch.Id);
        Assert.Equal(2, chRow.Received);
        Assert.Equal(1, chRow.EscalatedOut);
        Assert.Equal(1, chRow.Sla.FirstResponseMet);
        Assert.Equal(2, chRow.Sla.FirstResponseTotal);
        Assert.Equal(180, chRow.Sla.MedianFirstResponseMinutes); // 60 and 300 minutes
        Assert.Equal(1, chRow.Sla.ResolutionMet);
        Assert.Equal(1, chRow.Sla.ResolutionTotal);

        var financeRow = metrics.Areas.Single(a => a.AreaId == finance.Id);
        Assert.Equal(1, financeRow.Open);
        Assert.Equal(1, financeRow.Backlog.OnTrack);

        var flow = Assert.Single(metrics.Escalations);
        Assert.Equal((ch.Id, finance.Id, 1), (flow.FromAreaId, flow.ToAreaId, flow.Count));

        // Finance's agent only sees Finance; requesters do not see the dashboard at all.
        var financeView = await GetAsync<MetricsDto>(financeAgent, "/api/metrics");
        Assert.Equal([finance.Id], financeView.Areas.Select(a => a.AreaId));
        Assert.Equal(HttpStatusCode.Forbidden, (await financeAgent.GetAsync($"/api/metrics?areaId={ch.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.GetAsync("/api/metrics")).StatusCode);
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

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
}
