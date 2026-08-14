using SellingPoint.App;
using SellingPoint.App.ViewModels;
using SellingPoint.Printing;

namespace SellingPoint.Tests;

/// <summary>
/// Choosing a printer should be picking from a list, not typing an exact name -
/// a name with one space too many fails silently at the counter.
/// </summary>
public class DefinicoesViewModelTests
{
    private sealed class Fixture : IDisposable
    {
        public TempDb T { get; } = new();
        public AppServices Services { get; }
        public DefinicoesViewModel Vm { get; }

        public Fixture()
        {
            Services = new AppServices(T.Path);
            Vm = new DefinicoesViewModel(Services);
            Vm.Load();
        }

        public void Choose(string transportValue) =>
            Vm.Transport = Vm.Transports.Single(t => t.Value == transportValue);

        public void Dispose()
        {
            Services.Dispose();
            T.Dispose();
        }
    }

    [Fact]
    public void Enumerating_windows_printers_off_windows_is_empty_rather_than_a_crash()
        => Assert.Empty(WindowsPrinters.List());

    [Fact]
    public void Choosing_the_windows_printer_connection_offers_the_system_printers()
    {
        using var f = new Fixture();

        f.Choose("windows");

        Assert.True(f.Vm.ShowTargetList);
        // Nothing to list on a Mac, and the hint says so rather than leaving a void.
        Assert.Equal("O Windows não tem nenhuma impressora instalada.", f.Vm.TargetListHint);
    }

    [Fact]
    public void Choosing_the_serial_connection_offers_the_com_ports()
    {
        using var f = new Fixture();

        f.Choose("serial");

        Assert.True(f.Vm.ShowTargetList);
        Assert.True(f.Vm.IsSerial);
    }

    [Fact]
    public void The_file_and_network_connections_have_nothing_to_list()
    {
        using var f = new Fixture();

        f.Choose("network");
        Assert.False(f.Vm.ShowTargetList);
        Assert.Empty(f.Vm.AvailableTargets);

        f.Choose("file");
        Assert.False(f.Vm.ShowTargetList);
    }

    [Fact]
    public void Tapping_something_in_the_list_fills_the_destination()
    {
        using var f = new Fixture();
        f.Choose("windows");

        // Stand in for whatever Windows would have listed.
        f.Vm.AvailableTargets.Add("EPSON TM-T20III Receipt");
        f.Vm.SelectedTarget = "EPSON TM-T20III Receipt";

        Assert.Equal("EPSON TM-T20III Receipt", f.Vm.Target);
    }

    [Fact]
    public void The_windows_connection_says_up_front_that_status_cannot_be_read()
    {
        // The spooler is one-way. Better said here than discovered as a fault.
        using var f = new Fixture();

        f.Choose("windows");

        Assert.Contains("não é possível consultar o estado", f.Vm.TargetHint);
    }

    [Fact]
    public void Saving_keeps_the_chosen_printer()
    {
        using var f = new Fixture();
        f.Choose("windows");
        f.Vm.AvailableTargets.Add("Impressora de Talões");
        f.Vm.SelectedTarget = "Impressora de Talões";

        f.Vm.SaveCommand.Execute(null);

        var reloaded = new DefinicoesViewModel(f.Services);
        reloaded.Load();

        Assert.Equal("windows", reloaded.Transport!.Value);
        Assert.Equal("Impressora de Talões", reloaded.Target);
    }
}
