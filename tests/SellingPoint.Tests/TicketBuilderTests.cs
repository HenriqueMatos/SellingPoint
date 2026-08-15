using SellingPoint.Printing;

namespace SellingPoint.Tests;

public class TicketBuilderTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 22, 31, 0);
    private static readonly TicketOptions Options = new();

    private static SaleLine Line(string name, int qty, int unitCents, string group, SlipMode mode) => new()
    {
        ProductName = name, Qty = qty, UnitPriceCents = unitCents,
        LineTotalCents = unitCents * qty, PrintGroup = group, SlipMode = mode,
        CategoryName = group
    };

    private static Sale SaleWith(params SaleLine[] lines) => new()
    {
        TicketNumber = 42,
        CreatedAt = Now,
        TotalCents = lines.Sum(l => l.LineTotalCents),
        Lines = [.. lines]
    };

    [Fact]
    public void Drinks_and_desserts_share_a_slip_while_food_prints_apart()
    {
        // The requirement, verbatim: same print group means same piece of paper.
        var sale = SaleWith(
            Line("Cerveja", 2, 150, "Bar", SlipMode.Grouped),
            Line("Bifana", 1, 300, "Cozinha", SlipMode.Grouped),
            Line("Bolo", 1, 150, "Bar", SlipMode.Grouped));

        var slips = TicketBuilder.Build(sale, Options).Cast<GroupedSlip>().ToList();

        Assert.Equal(2, slips.Count);

        var bar = slips.Single(s => s.PrintGroup == "Bar");
        Assert.Equal(["Cerveja", "Bolo"], bar.Items.Select(i => i.Name));
        Assert.Equal(450, bar.TotalCents);

        var kitchen = slips.Single(s => s.PrintGroup == "Cozinha");
        Assert.Equal(["Bifana"], kitchen.Items.Select(i => i.Name));
        Assert.Equal(300, kitchen.TotalCents);
    }

    [Fact]
    public void Every_slip_of_a_sale_carries_the_same_ticket_reference()
    {
        var sale = SaleWith(
            Line("Cerveja", 1, 150, "Bar", SlipMode.Grouped),
            Line("Bifana", 1, 300, "Cozinha", SlipMode.Grouped));

        Assert.All(TicketBuilder.Build(sale, Options), s => Assert.Equal("#0042", s.Reference));
    }

    [Fact]
    public void A_per_unit_category_prints_one_senha_per_item()
    {
        var sale = SaleWith(Line("Cerveja", 3, 150, "Bar", SlipMode.PerUnit));

        var senhas = TicketBuilder.Build(sale, Options).Cast<SenhaSlip>().ToList();

        Assert.Equal(3, senhas.Count);
        Assert.Equal(["#0042-1", "#0042-2", "#0042-3"], senhas.Select(s => s.Reference));
        Assert.All(senhas, s =>
        {
            Assert.Equal("Cerveja", s.ItemName);
            Assert.Equal(150, s.PriceCents);
            Assert.Equal("Bar", s.PrintGroup);
        });
    }

    [Fact]
    public void Senha_numbering_runs_across_the_whole_sale_not_per_product()
    {
        var sale = SaleWith(
            Line("Cerveja", 2, 150, "Bar", SlipMode.PerUnit),
            Line("Sumo", 2, 100, "Bar", SlipMode.PerUnit));

        var senhas = TicketBuilder.Build(sale, Options).Cast<SenhaSlip>().ToList();

        Assert.Equal(["#0042-1", "#0042-2", "#0042-3", "#0042-4"], senhas.Select(s => s.Reference));
    }

    [Fact]
    public void A_group_can_mix_both_modes_and_gets_a_list_plus_its_senhas()
    {
        var sale = SaleWith(
            Line("Cerveja", 2, 150, "Bar", SlipMode.PerUnit),
            Line("Bolo", 1, 150, "Bar", SlipMode.Grouped));

        var slips = TicketBuilder.Build(sale, Options);

        var grouped = Assert.IsType<GroupedSlip>(slips[0]);
        Assert.Equal(["Bolo"], grouped.Items.Select(i => i.Name));
        Assert.Equal(2, slips.OfType<SenhaSlip>().Count());
    }

    [Fact]
    public void A_group_with_only_senhas_produces_no_list_slip()
    {
        var sale = SaleWith(Line("Cerveja", 2, 150, "Bar", SlipMode.PerUnit));

        Assert.Empty(TicketBuilder.Build(sale, Options).OfType<GroupedSlip>());
    }

    [Fact]
    public void An_empty_sale_prints_nothing()
        => Assert.Empty(TicketBuilder.Build(new Sale { TicketNumber = 1, CreatedAt = Now }, Options));

    [Fact]
    public void The_optional_summary_slip_lists_the_whole_order_once()
    {
        var sale = SaleWith(
            Line("Cerveja", 2, 150, "Bar", SlipMode.Grouped),
            Line("Bifana", 1, 300, "Cozinha", SlipMode.Grouped));

        var slips = TicketBuilder.Build(sale, Options with { PrintSummarySlip = true });

        var summary = Assert.IsType<GroupedSlip>(slips[^1]);
        Assert.True(summary.IsSummary);
        Assert.Equal(["Cerveja", "Bifana"], summary.Items.Select(i => i.Name));
        Assert.Equal(600, summary.TotalCents);
    }

    [Fact]
    public void Groups_print_in_the_order_they_first_appear_so_a_sale_is_reproducible()
    {
        var sale = SaleWith(
            Line("Bifana", 1, 300, "Cozinha", SlipMode.Grouped),
            Line("Cerveja", 1, 150, "Bar", SlipMode.Grouped));

        Assert.Equal(["Cozinha", "Bar"], TicketBuilder.Build(sale, Options).Select(s => s.PrintGroup));
    }
}
