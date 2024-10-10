using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Sheas_Nginx.Consts;
using SheasCore;

namespace Sheas_Nginx.Utils;

internal class NginxProc : Proc
{
    private readonly string HostsPath;

    internal NginxProc(string nginxPath, string hostsPath) : base(nginxPath) => HostsPath = hostsPath;

    public override void Process_Exited(object sender, EventArgs e)
    {
        string hostsContent = File.ReadAllText(HostsPath);
        int hostsConfStartIndex = hostsContent.IndexOf(MainConst.HostsConfStartMarker);
        int hostsConfEndIndex = hostsContent.LastIndexOf(MainConst.HostsConfEndMarker);

        if (hostsConfStartIndex != -1 && hostsConfEndIndex != -1)
            File.WriteAllText(HostsPath, hostsContent.Remove(hostsConfStartIndex, hostsConfEndIndex - hostsConfStartIndex + MainConst.HostsConfEndMarker.Length));

        using X509Store certStore = new(StoreName.Root, StoreLocation.CurrentUser, OpenFlags.ReadWrite);

        foreach (X509Certificate2 storedCert in certStore.Certificates)
            if (storedCert.Subject == MainConst.NginxRootCertSubjectName)
                while (true)
                    try
                    {
                        certStore.Remove(storedCert);
                        break;
                    }
                    catch { }

        certStore.Close();
    }
}