using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows;
using Microsoft.Windows.AppNotifications;

namespace Biscotti.Win;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private CancellationTokenSource? _pipeCancellation;
    private Task? _pipeListener;
    private MainWindow? _mainWindow;
    private bool _notificationsRegistered;
    private string? _notificationError;

    internal static bool IsExitRequested { get; private set; }

    private static string UserIdentity => WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    private static string InstanceToken => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(UserIdentity)))[..24];
    private static string MutexName => $"Local\\BiscottiWin-{InstanceToken}";
    private static string PipeName => $"BiscottiWin-{InstanceToken}";

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out _ownsInstanceMutex);
        if (!_ownsInstanceMutex)
        {
            if (!await SendActivationToExistingInstanceAsync())
                System.Windows.MessageBox.Show(
                    "Biscotti Win is already running, but its window could not be activated. Try again in a moment.",
                    "Biscotti Win", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        _pipeCancellation = new CancellationTokenSource();
        _pipeListener = ListenForActivationAsync(_pipeCancellation.Token);
        RegisterNotifications();

        var mainWindow = new MainWindow();
        _mainWindow = mainWindow;
        MainWindow = mainWindow;
        mainWindow.InitializeShell();
        if (_notificationError is not null) mainWindow.SetShellStatus($"Windows notifications are unavailable: {_notificationError}");
        mainWindow.Closed += async (_, _) =>
        {
            try
            {
                await mainWindow.DisposeForShutdownAsync();
                await StopActivationListenerAsync();
            }
            finally
            {
                Shutdown();
            }
        };
        mainWindow.Show();
    }

    private async Task<bool> SendActivationToExistingInstanceAsync()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(250);
                await using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
                await writer.WriteLineAsync("ACTIVATE");
                await writer.FlushAsync();
                return true;
            }
            catch (TimeoutException) { }
            catch (IOException) { }

            await Task.Delay(100);
        }
        return false;
    }

    private async Task ListenForActivationAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                if (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) == "ACTIVATE")
                    _ = Dispatcher.BeginInvoke(new Action(() => _mainWindow?.ShowFromShell()));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException) when (!cancellationToken.IsCancellationRequested) { await Task.Delay(100, cancellationToken).ConfigureAwait(false); }
        }
    }

    private async Task StopActivationListenerAsync()
    {
        _pipeCancellation?.Cancel();
        if (_pipeListener is not null)
        {
            try { await _pipeListener; }
            catch (OperationCanceledException) { }
        }
    }

    private void RegisterNotifications()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, _) =>
                Dispatcher.BeginInvoke(() => _mainWindow?.ShowFromShell());
            AppNotificationManager.Default.Register();
            _notificationsRegistered = true;
        }
        catch (Exception ex)
        {
            _notificationError = ex.Message;
        }
    }

    internal static void RequestExit()
    {
        IsExitRequested = true;
        Current.MainWindow?.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCancellation?.Cancel();
        _pipeCancellation?.Dispose();

        if (_notificationsRegistered)
        {
            try { AppNotificationManager.Default.Unregister(); } catch { }
        }
        if (_ownsInstanceMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
