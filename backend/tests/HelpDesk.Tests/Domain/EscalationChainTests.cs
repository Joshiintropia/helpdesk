using HelpDesk.Domain;
using HelpDesk.Domain.Tickets;

namespace HelpDesk.Tests.Domain;

/// <summary>
/// The "pago no reflejado" chain from the proposal: Customer Happiness → Finance → IT → Development,
/// where each area can resolve its own part or the whole ticket.
/// </summary>
public class EscalationChainTests
{
    private const int CustomerHappiness = 1;
    private const int Finance = 2;
    private const int It = 3;
    private const int Development = 4;

    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid ChAgent = Guid.NewGuid();
    private static readonly Guid FinanceAgent = Guid.NewGuid();
    private static readonly Guid ItAgent = Guid.NewGuid();

    private DateTimeOffset _now = new(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(-6));

    private DateTimeOffset Later() => _now = _now.AddMinutes(15);

    private Ticket OpenPaymentTicket() =>
        Ticket.Open(Requester, CustomerHappiness, "Hice un pago ayer y no se ve reflejado", null, TicketPriority.Normal, _now);

    [Fact]
    public void New_ticket_has_one_open_root_segment_in_the_receiving_area()
    {
        var ticket = OpenPaymentTicket();

        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Equal(CustomerHappiness, ticket.CurrentAreaId);
        var root = Assert.Single(ticket.Segments);
        Assert.Null(root.ParentSegmentId);
        Assert.Equal(SegmentStatus.Open, root.Status);
        Assert.Equal("Hice un pago ayer y no se ve reflejado", ticket.Title);
    }

    [Fact]
    public void Escalating_opens_a_child_segment_and_moves_the_turn_to_the_next_area()
    {
        var ticket = OpenPaymentTicket();

        var finance = ticket.Escalate(ticket.RootSegment.Id, Finance, ChAgent, "Validar si el pago se acreditó", Later());

        Assert.Equal(Finance, ticket.CurrentAreaId);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(ticket.RootSegment.Id, finance.ParentSegmentId);
        Assert.Equal(SegmentStatus.Open, ticket.RootSegment.Status);
        Assert.Contains(ticket.Events, e => e.Type == TicketEventType.Escalated && e.AreaId == Finance);
    }

    [Fact]
    public void Resolving_a_part_returns_the_ticket_to_the_area_that_escalated_it()
    {
        var ticket = OpenPaymentTicket();
        var finance = ticket.Escalate(ticket.RootSegment.Id, Finance, ChAgent, "Validar pago", Later());
        var it = ticket.Escalate(finance.Id, It, FinanceAgent, "No aparece en back-office", Later());

        ticket.ResolveSegment(it.Id, ItAgent, "El pago está en logs, se reprocesó", Later());

        Assert.Equal(SegmentStatus.Resolved, it.Status);
        Assert.Equal(SegmentStatus.Open, finance.Status);
        Assert.Equal(Finance, ticket.CurrentAreaId);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
    }

    [Fact]
    public void An_area_cannot_resolve_its_part_while_the_area_it_escalated_to_is_still_working()
    {
        var ticket = OpenPaymentTicket();
        var finance = ticket.Escalate(ticket.RootSegment.Id, Finance, ChAgent, "Validar pago", Later());
        ticket.Escalate(finance.Id, It, FinanceAgent, "No aparece en back-office", Later());

        var error = Assert.Throws<DomainException>(() => ticket.ResolveSegment(finance.Id, FinanceAgent, null, Later()));
        Assert.Contains("Otra área", error.Message);
    }

    [Fact]
    public void Resolving_everything_closes_the_whole_chain()
    {
        var ticket = OpenPaymentTicket();
        var finance = ticket.Escalate(ticket.RootSegment.Id, Finance, ChAgent, "Validar pago", Later());
        var it = ticket.Escalate(finance.Id, It, FinanceAgent, "No aparece", Later());
        var dev = ticket.Escalate(it.Id, Development, ItAgent, "Error al registrar pagos SPEI", Later());

        ticket.ResolveAll(ItAgent, "Se corrigió el error y se aplicó el pago", Later());

        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.All(ticket.Segments, s => Assert.Equal(SegmentStatus.Resolved, s.Status));
        Assert.Equal(CustomerHappiness, ticket.CurrentAreaId);
        Assert.NotNull(ticket.ResolvedAt);
        Assert.Equal(4, ticket.Segments.Count);
        Assert.Equal(Development, dev.AreaId);
    }

    [Fact]
    public void Resolving_the_root_segment_resolves_the_ticket()
    {
        var ticket = OpenPaymentTicket();

        ticket.ResolveSegment(ticket.RootSegment.Id, ChAgent, "Ya se ve reflejado", Later());

        Assert.Equal(TicketStatus.Resolved, ticket.Status);
    }

    [Fact]
    public void Escalating_to_the_same_area_is_rejected()
    {
        var ticket = OpenPaymentTicket();

        Assert.Throws<DomainException>(() => ticket.Escalate(ticket.RootSegment.Id, CustomerHappiness, ChAgent, "x", Later()));
    }

    [Fact]
    public void Two_areas_can_work_in_parallel_from_the_same_segment()
    {
        var ticket = OpenPaymentTicket();
        var finance = ticket.Escalate(ticket.RootSegment.Id, Finance, ChAgent, "Validar pago", Later());
        var it = ticket.Escalate(ticket.RootSegment.Id, It, ChAgent, "Revisar logs en paralelo", Later());

        ticket.ResolveSegment(it.Id, ItAgent, "Nada en logs", Later());

        Assert.Equal(Finance, ticket.CurrentAreaId);
        Assert.Equal(SegmentStatus.Open, finance.Status);
        Assert.Equal(SegmentStatus.Open, ticket.RootSegment.Status);
    }

    [Fact]
    public void Requester_reply_after_resolution_reopens_the_ticket_in_the_first_area()
    {
        var ticket = OpenPaymentTicket();
        var finance = ticket.Escalate(ticket.RootSegment.Id, Finance, ChAgent, "Validar pago", Later());
        ticket.ResolveAll(FinanceAgent, "Aplicado", Later());

        ticket.AddRequesterMessage("Sigue sin verse en mi cuenta", MessageSource.Portal, Later());

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(CustomerHappiness, ticket.CurrentAreaId);
        Assert.Equal(SegmentStatus.Open, ticket.RootSegment.Status);
        Assert.Equal(SegmentStatus.Resolved, finance.Status);
        Assert.Null(ticket.ResolvedAt);
    }

    [Fact]
    public void Waiting_on_the_requester_ends_when_they_answer()
    {
        var ticket = OpenPaymentTicket();
        ticket.AddAgentMessage(ChAgent, CustomerHappiness, "¿Nos compartes tu comprobante?", MessageVisibility.Public, Later());
        ticket.WaitForRequester(ChAgent, Later());

        ticket.AddRequesterMessage("Aquí está", MessageSource.Portal, Later());

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.NotNull(ticket.FirstRespondedAt);
    }

    [Fact]
    public void Internal_notes_are_hidden_from_the_requester_and_own_area_notes_from_other_areas()
    {
        var ticket = OpenPaymentTicket();
        var allAreas = ticket.AddAgentMessage(ChAgent, CustomerHappiness, "Cliente VIP", MessageVisibility.InternalAllAreas, Later());
        var ownArea = ticket.AddAgentMessage(ChAgent, CustomerHappiness, "Nota de CH", MessageVisibility.InternalOwnArea, Later());

        Assert.False(allAreas.IsVisibleTo([]));
        Assert.True(allAreas.IsVisibleTo([Finance]));
        Assert.False(ownArea.IsVisibleTo([Finance]));
        Assert.True(ownArea.IsVisibleTo([CustomerHappiness]));
        Assert.Null(ticket.FirstRespondedAt);
    }
}
