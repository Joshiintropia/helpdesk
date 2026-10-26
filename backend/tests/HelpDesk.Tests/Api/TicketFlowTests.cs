using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HelpDesk.Api.Endpoints;
using HelpDesk.Domain.Tickets;

namespace HelpDesk.Tests.Api;

public class TicketFlowTests(HelpDeskApiFactory factory) : IClassFixture<HelpDeskApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Payment_ticket_travels_from_customer_happiness_to_finance_and_back_to_the_requester()
    {
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        var ch = await CreateAreaAsync(admin, "Customer Happiness");
        var finance = await CreateAreaAsync(admin, "Finanzas");

        var requester = await factory.SignInAsync("cliente@test.local");
        var chAgent = await factory.SignInAsync("agente.ch@test.local");
        var financeAgent = await factory.SignInAsync("agente.finanzas@test.local");
        await AddMemberAsync(admin, ch, "agente.ch@test.local");
        await AddMemberAsync(admin, finance, "agente.finanzas@test.local");

        // The requester only describes the problem and picks who to ask.
        var created = await requester.PostAsJsonAsync("/api/tickets", new { areaId = ch.Id, description = "Pagué ayer y no se ve reflejado" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ticket = (await created.Content.ReadFromJsonAsync<TicketDetailDto>(Json))!;
        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Null(ticket.Segments);

        // Customer Happiness sees it in its queue and escalates to Finance.
        var chQueue = await GetAsync<List<QueueItemDto>>(chAgent, "/api/tickets/queue");
        var item = Assert.Single(chQueue);
        var escalated = await chAgent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/escalate", new { segmentId = item.SegmentId, toAreaId = finance.Id, reason = "Validar en back-office" });
        escalated.EnsureSuccessStatusCode();

        // It is now Finance's turn, not Customer Happiness's.
        Assert.Empty(await GetAsync<List<QueueItemDto>>(chAgent, "/api/tickets/queue"));
        var financeItem = Assert.Single(await GetAsync<List<QueueItemDto>>(financeAgent, "/api/tickets/queue"));
        Assert.Equal(finance.Id, financeItem.AreaId);

        // The requester sees "in progress" and nothing of the internal chain.
        var seen = await GetAsync<TicketDetailDto>(requester, $"/api/tickets/{ticket.Id}");
        Assert.Equal(TicketStatus.InProgress, seen.Status);
        Assert.Null(seen.Segments);

        var internalNote = await financeAgent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/messages", new { body = "Sí está acreditado", visibility = "InternalAllAreas" });
        internalNote.EnsureSuccessStatusCode();
        Assert.Empty((await GetAsync<TicketDetailDto>(requester, $"/api/tickets/{ticket.Id}")).Messages);

        // Finance resolves the whole chain.
        var resolved = await financeAgent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/resolve", new { note = "Pago aplicado" });
        resolved.EnsureSuccessStatusCode();

        var mine = await GetAsync<List<TicketSummaryDto>>(requester, "/api/tickets/mine");
        Assert.Equal(TicketStatus.Resolved, Assert.Single(mine).Status);
        Assert.Empty(await GetAsync<List<QueueItemDto>>(financeAgent, "/api/tickets/queue"));
    }

    [Fact]
    public async Task Other_requesters_cannot_see_a_ticket()
    {
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        var area = await CreateAreaAsync(admin, "TI general");
        var owner = await factory.SignInAsync("duena@test.local");
        var stranger = await factory.SignInAsync("ajeno@test.local");

        var created = await owner.PostAsJsonAsync("/api/tickets", new { areaId = area.Id, description = "No tengo red" });
        var ticket = (await created.Content.ReadFromJsonAsync<TicketDetailDto>(Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/tickets/{ticket.Id}")).StatusCode);
    }

    [Fact]
    public async Task Business_rule_violations_return_422_with_a_readable_message()
    {
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        var area = await CreateAreaAsync(admin, "Mesa vacía");
        var requester = await factory.SignInAsync("vacio@test.local");

        var response = await requester.PostAsJsonAsync("/api/tickets", new { areaId = area.Id, description = "   " });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Cuéntanos", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anonymous_calls_get_401()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/tickets/mine")).StatusCode);
    }

    private static async Task<AreaDto> CreateAreaAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync("/api/areas", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AreaDto>(Json))!;
    }

    private static async Task AddMemberAsync(HttpClient admin, AreaDto area, string email)
    {
        var response = await admin.PostAsJsonAsync($"/api/areas/{area.Id}/members", new { email });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
}
