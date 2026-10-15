using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Security.Principal;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Windows.Management.Deployment;

namespace ShareX
{
    internal static class ModernFileFavoritesMenu
    {
        private const string Name="Xiaomang.FileFavoritesMenu";
        private const string Publisher="CN=Xiaomang Local Components";
        private const string Fingerprint="EE869BF8FC7DF92E78DF3839E25E4641E251E555F5A021C3F697C0BB2D3B5F2F";
        private const string TrustArgument="-TrustFileMenuPublisher";
        private static string Bundle => Path.Combine(AppContext.BaseDirectory,"ThirdParty","FileFavoritesMenu");
        internal static string CertificatePath => Path.Combine(Bundle,"XiaomangMenuPublisher.cer");
        internal static string CertificateFingerprint()
        {
            using var certificate=X509CertificateLoader.LoadCertificateFromFile(CertificatePath);
            return certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
        }
        // This elevated mode accepts no paths and trusts only our exact public
        // code-signing certificate in TrustedPeople, never a root certificate.
        internal static bool TryRun(string[] args)
        {
            if(args.Length==0 || !args[0].Equals(TrustArgument,StringComparison.OrdinalIgnoreCase))return false;
            Environment.ExitCode=1;
            if(args.Length!=1)return true;
            using var identity=WindowsIdentity.GetCurrent();
            if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)){Environment.ExitCode=5;return true;}
            try
            {
                using var certificate=ValidatedCertificate();
                using var store=new X509Store(StoreName.TrustedPeople,StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                if(store.Certificates.Find(X509FindType.FindByThumbprint,certificate.Thumbprint,false).Count==0)store.Add(certificate);
                Environment.ExitCode=0;
            }
            catch { Environment.ExitCode=1; }
            return true;
        }
        private static X509Certificate2 ValidatedCertificate()
        {
            var certificate=X509CertificateLoader.LoadCertificateFromFile(CertificatePath);
            if(certificate.Subject!=Publisher || certificate.HasPrivateKey || DateTime.Now<certificate.NotBefore || DateTime.Now>certificate.NotAfter ||
                certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)!=Fingerprint ||
                !certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e=>e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(o=>o.Value=="1.3.6.1.5.5.7.3.3")) ||
                certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(e=>e.CertificateAuthority))
            {certificate.Dispose();throw new IOException("组件发布证书无效或与小蟒菜单证书不一致。");}
            return certificate;
        }
        private static bool IsMachineTrusted(X509Certificate2 certificate)
        {
            using var store=new X509Store(StoreName.TrustedPeople,StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint,certificate.Thumbprint,false).Count>0;
        }
        private static async Task TrustMachinePublisherAsync()
        {
            using var process=Process.Start(new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,TrustArgument)
            {UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=AppContext.BaseDirectory});
            if(process==null)throw new IOException("无法启动证书授权程序。");
            await process.WaitForExitAsync();
            if(process.ExitCode!=0)throw new IOException(process.ExitCode==5?"没有获得管理员权限，未添加证书。":"未能信任小蟒菜单证书，请检查系统权限后重试。");
        }
        private static async Task<(Exception Error,string Text)> AddPackageAsync(PackageManager manager,string path,AddPackageOptions options)
        {
            // WinRT can report deployment failure by throwing during await,
            // rather than returning a DeploymentResult with ExtendedErrorCode.
            try
            {
                var result=await manager.AddPackageByUriAsync(new Uri(path),options);
                return (result.ExtendedErrorCode,result.ErrorText);
            }
            catch(Exception error){return (error,error.Message);}
        }
        internal static string Status()
        {
            try
            {
                var package=new PackageManager().FindPackagesForUser("",Name,Publisher).FirstOrDefault();
                if(package==null)return "外层菜单尚未注册。";
                string location=package.EffectiveExternalLocation?.Path;
                if(string.IsNullOrEmpty(location) || !Path.TrimEndingDirectorySeparator(location).Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),StringComparison.OrdinalIgnoreCase))
                    return "外层菜单需要修复，注册位置与当前程序目录不同。";
                return "外层菜单已注册；右键文件或目录即可添加到收藏夹。";
            }
            catch(Exception error){return "无法读取外层菜单状态："+error.Message;}
        }
        internal static async Task ChangeAsync(bool install,bool trustPublisher)
        {
            var manager=new PackageManager();
            if(!install)
            {
                foreach(var package in manager.FindPackagesForUser("",Name,Publisher).ToArray())
                {
                    var removal=await manager.RemovePackageAsync(package.Id.FullName);
                    if(removal.ExtendedErrorCode!=null)throw new IOException(removal.ErrorText,removal.ExtendedErrorCode);
                }
                return;
            }
            string packagePath=Path.Combine(Bundle,"Xiaomang.FileFavoritesMenu.msix");
            if(!File.Exists(packagePath)||!File.Exists(Path.Combine(Bundle,"XiaomangFileFavoritesMenu.dll")))throw new IOException("菜单组件不完整，请重新获取完整使用包。");
            using var certificate=ValidatedCertificate();
            bool trusted=IsMachineTrusted(certificate);
            if(!trusted && !trustPublisher)throw new IOException("尚未信任组件发布证书，未更改注册。");
                if(!trusted)await TrustMachinePublisherAsync();
                if(!IsMachineTrusted(certificate))throw new IOException("本机证书信任校验失败，未注册菜单。");
                var options=new AddPackageOptions { ExternalLocationUri=new Uri(AppContext.BaseDirectory), ForceUpdateFromAnyVersion=true };
                var result=await AddPackageAsync(manager,packagePath,options);
                if(result.Error?.HResult==unchecked((int)0x80073CFB) || result.Error?.HResult==unchecked((int)0x80073D0B))
                {
                    var existing=manager.FindPackagesForUser("",Name,Publisher).SingleOrDefault();
                    if(existing==null)throw new IOException(result.Text,result.Error);
                    string previousLocation=existing.EffectiveExternalLocation.Path;
                    if(!Path.TrimEndingDirectorySeparator(previousLocation).Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),StringComparison.OrdinalIgnoreCase))
                    {
                        // Only known installed-version/external-location conflicts can
                        // repair this owned registration; other failures preserve it.
                        var removed=await manager.RemovePackageAsync(existing.Id.FullName);
                        if(removed.ExtendedErrorCode!=null)throw new IOException(removed.ErrorText,removed.ExtendedErrorCode);
                        var replacement=await AddPackageAsync(manager,packagePath,options);
                        if(replacement.Error!=null)
                        {
                            var rollbackOptions=new AddPackageOptions {ExternalLocationUri=new Uri(Path.GetFullPath(previousLocation)+Path.DirectorySeparatorChar)};
                            var restored=await AddPackageAsync(manager,packagePath,rollbackOptions);
                            if(restored.Error!=null)throw new IOException("修复和恢复旧入口均失败，请重试注册；收藏数据不受影响。",replacement.Error);
                            throw new IOException("修复失败，已恢复之前的入口。",replacement.Error);
                        }
                    }
                }
                else if(result.Error!=null)throw new IOException(result.Text,result.Error);
                var installed=manager.FindPackagesForUser("",Name,Publisher).SingleOrDefault();
                if(installed==null || !Path.TrimEndingDirectorySeparator(installed.EffectiveExternalLocation.Path).Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),StringComparison.OrdinalIgnoreCase))
                    throw new IOException("外层菜单安装位置校验失败，请重新修复。");
        }
    }
}
