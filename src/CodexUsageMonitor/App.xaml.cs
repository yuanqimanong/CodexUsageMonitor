using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace CodexUsageMonitor
{
    public partial class App : Application
    {
        private const string MutexName =
            "Local\\CodexUsageMonitor-57EB66F7-53A4-4EE9-B68C-52D2379E38A7";
        private const string ShowEventName =
            "Local\\CodexUsageMonitor-Show-57EB66F7-53A4-4EE9-B68C-52D2379E38A7";
        private const string ShutdownEventName =
            "Local\\CodexUsageMonitor-Shutdown-57EB66F7-53A4-4EE9-B68C-52D2379E38A7";

        private Mutex singleInstanceMutex;
        private EventWaitHandle showEvent;
        private RegisteredWaitHandle showRegistration;
        private EventWaitHandle shutdownEvent;
        private RegisteredWaitHandle shutdownRegistration;
        private MainWindow monitorWindow;
        private bool handlingFatalException;

        protected override void OnStartup(StartupEventArgs eventArgs)
        {
            bool shutdownRequested = Array.Exists(
                eventArgs.Args,
                argument => string.Equals(
                    argument,
                    "--shutdown",
                    StringComparison.OrdinalIgnoreCase));
            bool ownsMutex;
            singleInstanceMutex = new Mutex(true, MutexName, out ownsMutex);
            if (!ownsMutex)
            {
                try
                {
                    string eventName = shutdownRequested
                        ? ShutdownEventName
                        : ShowEventName;
                    using (EventWaitHandle existing = EventWaitHandle.OpenExisting(eventName))
                    {
                        existing.Set();
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                }

                Shutdown(0);
                return;
            }

            base.OnStartup(eventArgs);
            if (shutdownRequested)
            {
                Shutdown(0);
                return;
            }

            DispatcherUnhandledException += HandleDispatcherException;

            showEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                ShowEventName);
            showRegistration = ThreadPool.RegisterWaitForSingleObject(
                showEvent,
                delegate
                {
                    Dispatcher.BeginInvoke(
                        new Action(delegate
                        {
                            if (monitorWindow != null)
                            {
                                monitorWindow.ShowFromTray();
                            }
                        }));
                },
                null,
                Timeout.Infinite,
                false);

            shutdownEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                ShutdownEventName);
            shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
                shutdownEvent,
                delegate
                {
                    Dispatcher.BeginInvoke(new Action(ExitApplication));
                },
                null,
                Timeout.Infinite,
                false);

            monitorWindow = new MainWindow();
            MainWindow = monitorWindow;

            bool startInBackground =
                Array.Exists(
                    eventArgs.Args,
                    argument => string.Equals(
                        argument,
                        "--background",
                        StringComparison.OrdinalIgnoreCase));
            if (!startInBackground)
            {
                monitorWindow.Show();
            }
            else
            {
                monitorWindow.StartHidden();
            }
        }

        public void ExitApplication()
        {
            if (monitorWindow != null)
            {
                monitorWindow.PrepareForExit();
            }
            Shutdown(0);
        }

        protected override void OnExit(ExitEventArgs eventArgs)
        {
            if (showRegistration != null)
            {
                showRegistration.Unregister(null);
            }
            if (shutdownRegistration != null)
            {
                shutdownRegistration.Unregister(null);
            }
            if (showEvent != null)
            {
                showEvent.Close();
            }
            if (shutdownEvent != null)
            {
                shutdownEvent.Close();
            }
            if (singleInstanceMutex != null)
            {
                try
                {
                    singleInstanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
                singleInstanceMutex.Close();
            }

            base.OnExit(eventArgs);
        }

        private void HandleDispatcherException(
            object sender,
            DispatcherUnhandledExceptionEventArgs eventArgs)
        {
            eventArgs.Handled = true;
            if (handlingFatalException)
            {
                Shutdown(-1);
                return;
            }

            handlingFatalException = true;
            WriteFatalLog(eventArgs.Exception);
            try
            {
                MessageBox.Show(
                    "Codex Usage Monitor 遇到不可恢复的错误，即将安全退出。\n\n" +
                    eventArgs.Exception.Message,
                    BuildInfo.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception)
            {
            }

            try
            {
                ExitApplication();
            }
            catch (Exception)
            {
                Shutdown(-1);
            }
        }

        private static void WriteFatalLog(Exception error)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "CodexUsageMonitor");
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, "last-error.txt"),
                    DateTimeOffset.Now.ToString("O") +
                    Environment.NewLine +
                    error);
            }
            catch (Exception)
            {
            }
        }
    }
}
