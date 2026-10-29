using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace TechnicsControl.App;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // RACE チャネルは同時に 1 つしか使えないので、二重起動しない。2 つ目はウィンドウを開くよう 1 つ目に頼んで終わる
        var instance = AppInstance.FindOrRegisterForKey("TechnicsControl");
        if (!instance.IsCurrent)
        {
            instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs()).AsTask().Wait();
            return 0;
        }

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }
}
