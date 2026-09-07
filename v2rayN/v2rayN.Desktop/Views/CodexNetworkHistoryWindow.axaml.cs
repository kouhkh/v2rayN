using v2rayN.Desktop.Base;

namespace v2rayN.Desktop.Views;

public partial class CodexNetworkHistoryWindow : WindowBase<CodexNetworkHistoryViewModel>
{
    public CodexNetworkHistoryWindow()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            this.BindCommand(ViewModel, vm => vm.RefreshCmd, v => v.btnRefresh).DisposeWith(disposables);
        });
    }
}
