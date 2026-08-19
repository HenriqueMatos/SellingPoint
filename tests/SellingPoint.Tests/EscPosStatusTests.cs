using SellingPoint.Printing;

namespace SellingPoint.Tests;

public class EscPosStatusTests
{
    /// <summary>
    /// Answers canned replies keyed by the status kind byte of DLE EOT n.
    /// Null means "this printer does not answer that question".
    /// </summary>
    private sealed class FakePrinter(Dictionary<byte, byte?> answers) : IStatusQueryable
    {
        public List<byte> Asked { get; } = [];

        public byte[]? Exchange(byte[] request, int expectedBytes, int timeoutMs)
        {
            Asked.Add(request[2]);
            return answers.TryGetValue(request[2], out var answer) && answer is { } value ? [value] : null;
        }
    }

    // 0x12 has the fixed pattern (bits 0,1,4,7 = 0,1,1,0) with nothing else set.
    private const byte Ok = 0x12;

    [Fact]
    public void A_printer_with_nothing_wrong_reports_ready()
    {
        var status = EscPosStatus.Query(new FakePrinter(new() { [1] = Ok, [2] = Ok, [4] = Ok }));

        Assert.Equal(PrinterState.Ready, status.State);
        Assert.True(status.CanPrint);
    }

    [Fact]
    public void No_answer_at_all_is_a_printer_that_is_not_there()
    {
        var status = EscPosStatus.Query(new FakePrinter([]));

        Assert.Equal(PrinterState.NotFound, status.State);
        Assert.False(status.CanPrint);
    }

    [Fact]
    public void Paper_end_bits_report_out_of_paper()
    {
        // Bits 5 and 6 of the paper sensor byte, both set.
        var status = EscPosStatus.Query(new FakePrinter(new() { [1] = Ok, [4] = Ok | 0x60 }));

        Assert.Equal(PrinterState.PaperOut, status.State);
        Assert.False(status.CanPrint);
    }

    [Fact]
    public void Near_end_bits_report_low_paper_and_still_print()
    {
        var status = EscPosStatus.Query(new FakePrinter(new() { [1] = Ok, [4] = Ok | 0x0C }));

        Assert.Equal(PrinterState.PaperLow, status.State);
        Assert.True(status.CanPrint);
    }

    [Fact]
    public void An_offline_printer_is_asked_why()
    {
        // Offline (bit 3 of n=1), because the cover is open (bit 2 of n=2).
        var status = EscPosStatus.Query(new FakePrinter(new()
        {
            [1] = Ok | 0x08, [2] = Ok | 0x04, [4] = Ok
        }));

        Assert.Equal(PrinterState.CoverOpen, status.State);
    }

    [Fact]
    public void An_offline_printer_that_will_not_say_why_is_still_reported_as_offline()
    {
        var status = EscPosStatus.Query(new FakePrinter(new() { [1] = Ok | 0x08, [4] = Ok }));

        Assert.Equal(PrinterState.Offline, status.State);
    }

    [Fact]
    public void Paper_is_checked_before_the_offline_reason_because_it_is_the_useful_message()
    {
        // Out of paper also reports offline. "Sem papel" tells someone what to do;
        // "em pausa" does not.
        var status = EscPosStatus.Query(new FakePrinter(new()
        {
            [1] = Ok | 0x08, [2] = Ok | 0x20, [4] = Ok | 0x60
        }));

        Assert.Equal(PrinterState.PaperOut, status.State);
    }

    [Fact]
    public void A_reply_without_the_fixed_bit_pattern_is_some_other_device_on_that_port()
    {
        // A GPS, a scale, a card terminal - anything that answers with its own noise
        // must not be mistaken for a printer that is ready.
        var status = EscPosStatus.Query(new FakePrinter(new() { [1] = 0xFF, [4] = 0xFF }));

        Assert.Equal(PrinterState.NotFound, status.State);
    }

    [Theory]
    [InlineData(0x12, true)]
    [InlineData(0x16, true)]
    [InlineData(0x00, false)]
    [InlineData(0xFF, false)]
    [InlineData(0x92, false)]  // bit 7 set
    [InlineData(0x13, false)]  // bit 0 set
    public void The_fixed_pattern_is_what_separates_a_status_byte_from_noise(byte value, bool valid)
        => Assert.Equal(valid, EscPosStatus.IsStatusByte(value));

    [Fact]
    public void A_transport_that_throws_mid_query_reports_not_found_rather_than_crashing()
    {
        var status = EscPosStatus.Query(new ThrowingTransport());

        Assert.Equal(PrinterState.NotFound, status.State);
    }

    private sealed class ThrowingTransport : IStatusQueryable
    {
        public byte[]? Exchange(byte[] request, int expectedBytes, int timeoutMs)
            => throw new IOException("a porta desapareceu");
    }

    [Fact]
    public void Unknown_still_counts_as_printable()
    {
        // Plenty of cheap printers ignore DLE EOT entirely. Refusing to print to one
        // because it will not discuss its own health would be worse than useless.
        Assert.True(PrinterStatus.Unknown.CanPrint);
        Assert.True(PrinterStatus.NotSupported.CanPrint);
    }

    [Fact]
    public void Scanning_for_ports_on_a_machine_with_none_returns_nothing_instead_of_throwing()
        => Assert.NotNull(PrinterLocator.AvailablePorts());
}
