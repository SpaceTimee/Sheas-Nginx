namespace Sheas_Nginx.Consts;

internal class MainConst
{
    internal static string HostsConfStartMarker => "# Pixiv Nginx Start\n";
    internal static string HostsConfEndMarker => "# Pixiv Nginx End";
    internal static string NginxRootCertSubjectName => "CN=Pixiv Nginx Cert Root";
    internal static string NginxChildCertSubjectName => "CN=Pixiv Nginx Cert Child";
}