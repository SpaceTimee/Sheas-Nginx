using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Sheas_Nginx.Consts;
using Sheas_Nginx.Utils;
using File = System.IO.File;

namespace Sheas_Nginx.Wins;

public partial class MainWin : Window
{
    private static readonly DispatcherTimer NginxTimer = new() { Interval = TimeSpan.FromSeconds(0.1) };
    private static string? NginxPath;
    private static string? NginxConfPath;
    private static string? NginxLogsPath;
    private static string? NginxTempPath;
    private static string? NginxCertPath;
    private static string? NginxKeyPath;
    private static string? HostsConfPath;
    private static readonly string HostsPath = Path.Combine(Registry.LocalMachine.OpenSubKey(@"\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\DataBasePath")?.GetValue("DataBasePath", null)?.ToString() ?? @"C:\Windows\System32\drivers\etc", "hosts");
    private static bool IsNginxRunning = false;

    public MainWin() => InitializeComponent();
    protected override void OnSourceInitialized(EventArgs e) => IconRemover.RemoveIcon(this);
    private async void MainWin_Loaded(object sender, RoutedEventArgs e)
    {
        await Task.Run(() =>
        {
            NginxTimer.Tick += NginxTimer_Tick;
            NginxTimer.Start();
        });

        string nginxPath = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase!, "nginx.exe");
        string hostsPath = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase!, "hosts");

        if (File.Exists(nginxPath))
            NginxPathBox.Text = nginxPath;

        if (File.Exists(hostsPath))
            HostsPathBox.Text = hostsPath;
    }
    private void MainWin_Closing(object sender, CancelEventArgs e) => Application.Current.Shutdown();

    private void NginxPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (NginxConfButton.IsEnabled = File.Exists(NginxPathBox.Text) && Path.GetFileName(NginxPathBox.Text).ToLowerInvariant().EndsWith(".exe"))
        {
            NginxPath = NginxPathBox.Text;
            NginxConfPath = Path.Combine(Path.GetDirectoryName(NginxPath)!, "conf", "nginx.conf");
            NginxLogsPath = Path.Combine(Path.GetDirectoryName(NginxPath)!, "logs");
            NginxTempPath = Path.Combine(Path.GetDirectoryName(NginxPath)!, "temp");
            NginxCertPath = Path.Combine(Path.GetDirectoryName(NginxPath)!, "cert.pem");
            NginxKeyPath = Path.Combine(Path.GetDirectoryName(NginxPath)!, "key.pem");
        }

        NginxPathBrowserButton.Visibility = NginxConfButton.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
        StartButton.IsEnabled = NginxConfButton.IsEnabled && HostsConfButton.IsEnabled;
    }
    private void HostsPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (HostsConfButton.IsEnabled = File.Exists(HostsPathBox.Text))
            HostsConfPath = HostsPathBox.Text;

        HostsPathBrowserButton.Visibility = HostsConfButton.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
        StartButton.IsEnabled = NginxConfButton.IsEnabled && HostsConfButton.IsEnabled;
    }
    private void PathBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        Button? senderButton = sender as Button;

        string? pathFilter = senderButton == NginxPathBrowserButton ? "Nginx 可执行文件 (*.exe)|*.exe" : "Hosts 配置文件 (*.*)|*.*";

        OpenFileDialog browserPathDialog = new() { Filter = pathFilter };

        if (browserPathDialog.ShowDialog().GetValueOrDefault())
            if (senderButton == NginxPathBrowserButton)
                NginxPathBox.Text = browserPathDialog.FileName;
            else
                HostsPathBox.Text = browserPathDialog.FileName;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsNginxRunning)
        {
            if (MessageBox.Show("使用完请务必记得回来手动关闭代理，是否继续?", string.Empty, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            if (!File.Exists(NginxConfPath))
                File.Create(NginxConfPath!).Dispose();
            if (!File.Exists(HostsConfPath))
                File.Create(HostsConfPath!).Dispose();
            if (!Directory.Exists(NginxLogsPath))
                Directory.CreateDirectory(NginxLogsPath!);
            if (!Directory.Exists(NginxTempPath))
                Directory.CreateDirectory(NginxTempPath!);

            RSA certKey = RSA.Create(2048);

            CertificateRequest rootCertRequest = new(MainConst.NginxRootCertSubjectName, certKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            rootCertRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, false));
            using X509Certificate2 rootCert = rootCertRequest.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(100));
            using X509Store certStore = new(StoreName.Root, StoreLocation.CurrentUser, OpenFlags.ReadWrite);

            certStore.Add(rootCert);
            certStore.Close();

            CertificateRequest childCertRequest = new(MainConst.NginxChildCertSubjectName, certKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            SubjectAlternativeNameBuilder childCertSanBuilder = new();

            foreach (string hostConf in File.ReadLines(HostsConfPath!))
            {
                if (!hostConf.Trim().StartsWith("127.0.0.1"))
                    continue;

                string dnsName = hostConf.Trim().TrimStart("127.0.0.1".ToCharArray()).TrimStart();
                int commentStartIndex = dnsName.IndexOf('#');

                if (commentStartIndex != -1)
                    dnsName = dnsName.Remove(commentStartIndex).TrimEnd();

                if (!string.IsNullOrWhiteSpace(dnsName))
                {
                    childCertSanBuilder.AddDnsName(dnsName);
                    childCertSanBuilder.AddDnsName($"*.{dnsName}");
                }
            }

            childCertRequest.CertificateExtensions.Add(childCertSanBuilder.Build());
            using X509Certificate2 childCert = childCertRequest.Create(rootCert, rootCert.NotBefore, rootCert.NotAfter, Guid.NewGuid().ToByteArray());

            File.WriteAllText(NginxCertPath!, childCert.ExportCertificatePem());
            File.WriteAllText(NginxKeyPath!, certKey.ExportPkcs8PrivateKeyPem());

            File.AppendAllText(HostsPath, MainConst.HostsConfStartMarker + File.ReadAllText(HostsConfPath!) + MainConst.HostsConfEndMarker);

            await Task.Run(() =>
            {
                new NginxProc(NginxPath!, HostsPath).ShellRun(AppDomain.CurrentDomain.SetupInformation.ApplicationBase!, @$"-c ""{NginxConfPath}""");
            });
        }
        else
            foreach (Process nginxProcess in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(NginxPath)))
            {
                nginxProcess.Kill();
                await nginxProcess.WaitForExitAsync();
            }
    }

    private void ConfButton_Click(object sender, RoutedEventArgs e)
    {
        Button? senderButton = sender as Button;

        string confPath = senderButton == NginxConfButton ? NginxConfPath! : HostsConfPath!;

        if (!File.Exists(confPath))
            File.Create(confPath).Dispose();

        ProcessStartInfo processStartInfo = new(confPath) { UseShellExecute = true };
        Process.Start(processStartInfo);
    }

    private void NginxTimer_Tick(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(NginxPath) && (IsNginxRunning = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(NginxPath)).Length != 0))
        {
            StartButton.Content = "停止 Pixiv Nginx";
            StartButton.ToolTip = "点击停止 Pixiv Nginx";
        }
        else
        {
            StartButton.Content = "启动 Pixiv Nginx";
            StartButton.ToolTip = "点击启动 Pixiv Nginx";
        }
    }
    private void MainWin_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.W)
            Application.Current.Shutdown();
    }
}