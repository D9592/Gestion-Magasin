namespace StoreManager.App.Helpers;

internal static class GlobalExceptionHandler
{
    public static void Register()
    {
        Application.ThreadException += (_, e) => Show(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Show(e.ExceptionObject as Exception ?? new Exception("Erreur inconnue."));
        TaskScheduler.UnobservedTaskException += (_, e) => { Show(e.Exception); e.SetObserved(); };
    }

    private static void Show(Exception exception)
    {
        _ = exception;
        MessageBox.Show("Une erreur inattendue est survenue. Consultez l’administrateur si le problème persiste.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
