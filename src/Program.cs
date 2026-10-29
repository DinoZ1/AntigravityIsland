using System;
using System.Diagnostics;
using System.Windows;
using AntigravityIsland.Model;
using AntigravityIsland.Native;
using AntigravityIsland.Services;
using AntigravityIsland.UI;

namespace AntigravityIsland
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Process current = Process.GetCurrentProcess();
            Process[] existing = Process.GetProcessesByName(current.ProcessName);
            if (existing.Length > 1)
            {
                // Se ja houver uma instancia aberta, nao abre outra
                return;
            }

            try
            {
                // 1. Garante que os hooks estejam instalados automaticamente no Antigravity CLI
                AutoInstaller.EnsureInstalled();

                // 2. Inicializa o subsistema de High-DPI do Windows
                NativeWin32.InitDpiAwareness();

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;

                IslandWindow window = new IslandWindow();
                IpcServer ipc = new IpcServer(47823);

                ipc.OnPermissionRequested += (req, callback) =>
                {
                    window.ShowPermissionPrompt(req, callback);
                };

                ipc.OnStatusChanged += (status, message) =>
                {
                    window.SetState(status, message);
                };

                ipc.Start();

                app.Exit += (s, e) =>
                {
                    ipc.Stop();
                };

                window.Show();
                app.Run(window);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao iniciar Antigravity Island: " + ex.Message, "Antigravity Island",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
