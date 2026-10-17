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
                return (result.ExtendedErrorCode,result.ErrorText+(result.ExtendedErrorCode!=null?"\nWindows 部署活动 ID："+result.ActivityId:""));
            }
            catch(Exception error){return (error,error.Message);}
        }
        internal static string Status()
        {
            try
            {
                var package=new PackageManager().FindPackagesForUser("",Name,Publisher).FirstOrDefault();
                if(package==null)return "外层菜单尚未注册。";
                string location=ReadRegisteredLocation(()=>package.EffectiveExternalPath);
                if(location==null)return "外层菜单需要修复，旧注册目录已失效。";
                if(!SameLocation(location,AppContext.BaseDirectory))
                    return "外层菜单需要修复，注册位置与当前程序目录不同。";
                return "外层菜单已注册；右键文件或目录即可添加到收藏夹。";
            }
            catch(Exception error){return "无法读取外层菜单状态："+error.Message;}
        }
        private static bool SameLocation(string first,string second)=>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),StringComparison.OrdinalIgnoreCase);

        internal static string ReadRegisteredLocation(Func<string> read)
        {
            try
            {
                string location=read();
                if(string.IsNullOrWhiteSpace(location))throw new IOException("系统未返回菜单注册目录，保留原入口。请查看诊断日志。");
                string full=Path.GetFullPath(location);
                // Unlike Directory.Exists, this preserves access-denied and other errors.
                if((File.GetAttributes(full)&FileAttributes.Directory)==0)throw new IOException("菜单注册位置不是目录，保留原入口："+full);
                return full;
            }
            catch(Exception error) when(error.HResult==unchecked((int)0x80070002)||error.HResult==unchecked((int)0x80070003))
            {
                return null; // Only a confirmed missing file/path permits stale-entry repair.
            }
        }

        internal static async Task RepairConflictAsync(Func<string> readLocation,string current,Func<Task> remove,
            Func<string,Task> add,Action<string,string> stage)
        {
            stage("读取旧菜单注册目录",Name+"（Windows 已注册菜单）");
            string previous=ReadRegisteredLocation(readLocation);
            if(previous!=null && SameLocation(previous,current))return;
            stage(previous==null?"清理小蟒失效菜单入口":"移除旧位置菜单入口",previous??Name);
            await remove();
            stage("将菜单注册到当前目录",current);
            try { await add(current); }
            catch(Exception failure)
            {
                if(previous==null)throw new IOException("旧菜单目录已失效，已清理失效入口，但注册当前目录失败；收藏数据不受影响，请按诊断信息重试。",failure);
                stage("恢复旧位置菜单入口",previous);
                try { await add(previous); }
                catch(Exception rollback)
                {
                    throw new IOException("修复和恢复旧入口均失败；收藏数据不受影响。恢复错误："+rollback.Message+
                        "（0x"+rollback.HResult.ToString("X8")+"）",failure);
                }
                throw new IOException("修复失败，已恢复之前的入口。",failure);
            }
        }
        internal static async Task ChangeAsync(bool install,bool trustPublisher)
        {
            string step=install?"检查菜单文件":"读取已注册菜单";
            string related=AppContext.BaseDirectory;
            try
            {
                if(install) ValidateFiles(AppContext.BaseDirectory);
                await ChangeCoreAsync(install,trustPublisher,(name,path)=>{step=name;related=path;});
            }
            catch(System.ComponentModel.Win32Exception error) when(error.NativeErrorCode==1223){throw;}
            catch(Exception error)
            {
                string detail=FormatFailure(step,related,error);
                string log=SaveFailure(detail,error);
                throw new IOException(detail+"\n"+log,error);
            }
        }
        internal static void ValidateFiles(string root)
        {
            string bundle=Path.Combine(root,"ThirdParty","FileFavoritesMenu");
            foreach(string path in new[]{Path.Combine(root,"xiaomang.exe"),Path.Combine(root,"resources.pri"),
                Path.Combine(bundle,"Xiaomang.FileFavoritesMenu.msix"),Path.Combine(bundle,"XiaomangFileFavoritesMenu.dll"),
                Path.Combine(bundle,"XiaomangMenuPublisher.cer"),Path.Combine(bundle,"Assets","Logo.png"),
                Path.Combine(bundle,"Assets","Square44x44Logo.scale-100.png"),Path.Combine(bundle,"Assets","Square150x150Logo.scale-100.png")})
            {
                // Open rather than File.Exists so access failures retain their native error.
                using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                if(file.Length==0)throw new IOException("菜单所需文件为空："+path);
            }
        }
        internal static string FormatFailure(string step,string related,Exception error)
        {
            Exception cause=error;
            while(cause.InnerException!=null)cause=cause.InnerException;
            string missing=cause is FileNotFoundException file?file.FileName:null;
            return "鼠标右键收藏注册失败\n步骤："+step+"\n错误码：0x"+cause.HResult.ToString("X8")+
                (cause is System.ComponentModel.Win32Exception native?"（系统错误 "+native.NativeErrorCode+"）":"")+
                "\n相关路径："+(string.IsNullOrEmpty(missing)?related:missing)+"\n详情："+error.Message;
        }
        private static string SaveFailure(string detail,Exception error)
        {
            string temporary=Path.Combine(Path.GetTempPath(),"Xiaomang","Logs");
            string[] folders={string.IsNullOrEmpty(Program.PersonalFolder)?temporary:Path.Combine(Program.PersonalFolder,"Logs"),temporary};
            foreach(string folder in folders)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    string path=Path.Combine(folder,"FileFavoritesMenu-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".txt");
                    File.WriteAllText(path,"时间："+DateTimeOffset.Now.ToString("O")+"\n版本："+Program.ProductVersion+
                        "\n系统："+Environment.OSVersion+"\n程序目录："+AppContext.BaseDirectory+"\n"+detail+"\n\n"+error,new System.Text.UTF8Encoding(false));
                    return "诊断日志："+path;
                }
                catch { /* Keep the original failure visible even if logging is unavailable. */ }
            }
            return "诊断日志无法写入，请截图保存以上错误信息。";
        }
        private static async Task ChangeCoreAsync(bool install,bool trustPublisher,Action<string,string> stage)
        {
            stage("读取系统菜单注册状态",AppContext.BaseDirectory);
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
            stage("校验菜单发布证书",CertificatePath);
            using var certificate=ValidatedCertificate();
            stage("检查本机证书信任",CertificatePath);
            bool trusted=IsMachineTrusted(certificate);
            if(!trusted && !trustPublisher)throw new IOException("尚未信任组件发布证书，未更改注册。");
                if(!trusted){stage("请求管理员授权并信任证书",System.Windows.Forms.Application.ExecutablePath);await TrustMachinePublisherAsync();}
                stage("复核本机证书信任",CertificatePath);
                if(!IsMachineTrusted(certificate))throw new IOException("本机证书信任校验失败，未注册菜单。");
                var options=new AddPackageOptions { ExternalLocationUri=new Uri(AppContext.BaseDirectory), ForceUpdateFromAnyVersion=true };
                stage("注册 Windows 外层菜单",packagePath);
                var result=await AddPackageAsync(manager,packagePath,options);
                if(result.Error?.HResult==unchecked((int)0x80073CFB) || result.Error?.HResult==unchecked((int)0x80073D0B))
                {
                    var existing=manager.FindPackagesForUser("",Name,Publisher).SingleOrDefault();
                    if(existing==null)throw new IOException(result.Text,result.Error);
                    await RepairConflictAsync(()=>existing.EffectiveExternalPath,AppContext.BaseDirectory,async()=>
                    {
                        var removed=await manager.RemovePackageAsync(existing.Id.FullName);
                        if(removed.ExtendedErrorCode!=null)throw new IOException(removed.ErrorText,removed.ExtendedErrorCode);
                    },async location=>
                    {
                        var repairOptions=new AddPackageOptions {ExternalLocationUri=new Uri(Path.GetFullPath(location)+Path.DirectorySeparatorChar),ForceUpdateFromAnyVersion=true};
                        var added=await AddPackageAsync(manager,packagePath,repairOptions);
                        if(added.Error!=null)throw new IOException(added.Text,added.Error);
                    },stage);
                }
                else if(result.Error!=null)throw new IOException(result.Text,result.Error);
                stage("核对注册后的程序目录",AppContext.BaseDirectory);
                var installed=manager.FindPackagesForUser("",Name,Publisher).SingleOrDefault();
                string installedLocation=installed==null?null:ReadRegisteredLocation(()=>installed.EffectiveExternalPath);
                if(installedLocation==null || !SameLocation(installedLocation,AppContext.BaseDirectory))
                    throw new IOException("外层菜单安装位置校验失败，请重新修复。");
        }
    }
}
