using CommunityToolkit.Mvvm.ComponentModel;

namespace SellingPoint.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public VendaViewModel Venda { get; }
    public GestaoViewModel Gestao { get; }
    public RelatoriosViewModel Relatorios { get; }
    public DefinicoesViewModel Definicoes { get; }

    [ObservableProperty] public partial int SelectedTab { get; set; }

    public MainWindowViewModel(AppServices services)
    {
        _services = services;

        Venda = new VendaViewModel(services);
        Gestao = new GestaoViewModel(services);
        Relatorios = new RelatoriosViewModel(services);
        Definicoes = new DefinicoesViewModel(services);

        Venda.Load();
    }

    /// <summary>
    /// Each tab reloads as it is opened. Prices, stock and printer settings are
    /// edited on one tab and used on another, so a stale till screen is the
    /// obvious way to get this wrong.
    /// </summary>
    partial void OnSelectedTabChanged(int value)
    {
        switch (value)
        {
            case 0: Venda.Load(); break;
            case 1: Gestao.Load(); break;
            case 2: Relatorios.Load(); break;
            case 3: Definicoes.Load(); break;
        }
    }
}
