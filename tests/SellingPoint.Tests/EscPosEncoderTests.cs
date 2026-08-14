using SellingPoint.Printing;

namespace SellingPoint.Tests;

public class EscPosEncoderTests
{
    private static readonly TicketOptions Options = new() { Columns = 48 };

    private static bool Contains(byte[] haystack, params byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.Skip(i).Take(needle.Length).SequenceEqual(needle)) return true;
        }
        return false;
    }

    [Fact]
    public void Every_ticket_starts_by_resetting_the_printer()
    {
        var bytes = EscPosEncoder.Encode([new SlipTextLine("Cerveja")], Options);

        Assert.Equal(0x1B, bytes[0]);
        Assert.Equal((byte)'@', bytes[1]);
    }

    [Theory]
    [InlineData(858, 19)]
    [InlineData(860, 3)]
    [InlineData(437, 0)]
    public void The_code_page_is_selected_by_its_printer_slot(int codePage, byte slot)
    {
        // Getting this wrong is what turns accented product names into
        // line-drawing characters on the paper.
        var bytes = EscPosEncoder.Encode([new SlipTextLine("Cerveja")], Options with { CodePage = codePage });

        Assert.True(Contains(bytes, 0x1B, (byte)'t', slot));
    }

    [Fact]
    public void The_default_code_page_carries_both_the_accents_and_the_euro()
    {
        // CP858 is the only widely supported thermal code page with both. CP860 is
        // "the Portuguese one" but predates the euro entirely.
        var bytes = EscPosEncoder.Encode([new SlipTextLine("Ração 1,50 €")], new TicketOptions());

        Assert.True(Contains(bytes, 0x87));        // c-cedilla
        Assert.True(Contains(bytes, 0xC6));        // a-tilde
        Assert.True(Contains(bytes, 0xD5));        // euro sign
        Assert.False(Contains(bytes, (byte)'?'));
    }

    [Fact]
    public void On_a_code_page_without_a_euro_the_sign_degrades_to_a_letter_of_the_same_width()
    {
        // "?" reads as a fault; "EUR" is three characters and would shove every
        // right-aligned price off the end of a line already padded to the margin.
        var line = Layout.LeftRight("2x Cerveja", Money.Format(300), 32);
        var bytes = EscPosEncoder.Encode([new SlipTextLine(line)], Options with { CodePage = 860 });

        Assert.False(Contains(bytes, (byte)'?'));
        Assert.True(Contains(bytes, "3,00 E"u8.ToArray()));
    }

    [Fact]
    public void Accented_characters_become_single_cp860_bytes()
    {
        var bytes = EscPosEncoder.Encode([new SlipTextLine("çã")], Options with { CodePage = 860 });

        Assert.True(Contains(bytes, 0x87, 0x84)); // c-cedilla, a-tilde in CP860
    }

    [Fact]
    public void Money_is_formatted_with_a_plain_space_the_printer_understands()
    {
        // A culture-aware currency format inserts U+00A0 between the amount and the
        // sign. Money.Format uses a normal space, so what is measured for column
        // alignment is exactly what is encoded.
        var bytes = EscPosEncoder.Encode([new SlipTextLine(Money.Format(150))], new TicketOptions());

        Assert.True(Contains(bytes, "1,50 "u8.ToArray()));
        Assert.False(Contains(bytes, (byte)'?'));
    }

    [Fact]
    public void Accents_can_be_folded_away_for_printers_that_lack_them()
    {
        var bytes = EscPosEncoder.Encode(
            [new SlipTextLine("Bifana à Moda")], Options with { FoldAccents = true, CodePage = 437 });

        Assert.True(Contains(bytes, "Bifana a Moda"u8.ToArray()));
    }

    [Fact]
    public void Double_width_and_height_are_packed_into_one_size_byte()
    {
        var bytes = EscPosEncoder.Encode(
            [new SlipTextLine("CERVEJA", SlipAlign.Center, SlipStyle.DoubleWidth | SlipStyle.DoubleHeight)],
            Options);

        Assert.True(Contains(bytes, 0x1D, (byte)'!', 0x11));
        Assert.True(Contains(bytes, 0x1B, (byte)'a', 1)); // centred
    }

    [Fact]
    public void Bold_is_switched_on_and_back_off_again()
    {
        var bytes = EscPosEncoder.Encode([new SlipTextLine("TOTAL", Style: SlipStyle.Bold)], Options);

        Assert.True(Contains(bytes, 0x1B, (byte)'E', 1));
        Assert.True(Contains(bytes, 0x1B, (byte)'E', 0));
    }

    [Fact]
    public void Every_ticket_ends_by_feeding_clear_of_the_cutter_and_cutting()
    {
        var bytes = EscPosEncoder.Encode([new SlipTextLine("Cerveja")], Options);

        Assert.True(Contains(bytes, 0x1B, (byte)'d', 4));
        Assert.True(Contains(bytes, 0x1D, (byte)'V', 66, 0));
    }

    [Fact]
    public void The_cash_drawer_only_fires_when_asked()
    {
        var without = EscPosEncoder.Encode([new SlipTextLine("Cerveja")], Options);
        var with = EscPosEncoder.Encode([new SlipTextLine("Cerveja")], Options, openCashDrawer: true);

        Assert.False(Contains(without, 0x1B, (byte)'p', 0, 25, 250));
        Assert.True(Contains(with, 0x1B, (byte)'p', 0, 25, 250));
    }

    [Fact]
    public void An_unknown_code_page_degrades_to_ascii_instead_of_throwing()
    {
        var bytes = EscPosEncoder.Encode([new SlipTextLine("Cerveja")], Options with { CodePage = 999999 });

        Assert.True(Contains(bytes, "Cerveja"u8.ToArray()));
    }

    [Theory]
    [InlineData("Cerveja", "Cerveja")]
    [InlineData("Bifana à Moda", "Bifana a Moda")]
    [InlineData("Ração São João", "Racao Sao Joao")]
    [InlineData("", "")]
    public void Folding_strips_accents_and_leaves_everything_else(string input, string expected)
        => Assert.Equal(expected, Accents.Fold(input));
}
