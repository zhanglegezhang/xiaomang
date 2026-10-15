param([string]$Output=(Join-Path $PSScriptRoot 'bundle'))
$ErrorActionPreference='Stop'
$workspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$private=Join-Path $PSScriptRoot 'signing-private'
New-Item -ItemType Directory -Path $private -Force|Out-Null
$user=[Security.Principal.WindowsIdentity]::GetCurrent().User
$directory=[IO.DirectoryInfo]::new($private)
$acl=[IO.FileSystemAclExtensions]::GetAccessControl($directory,[Security.AccessControl.AccessControlSections]::Access)
$acl.SetAccessRuleProtection($true,$false)
foreach($rule in @($acl.GetAccessRules($true,$false,[Security.Principal.SecurityIdentifier]))){$acl.RemoveAccessRuleSpecific($rule)}
$inherit=[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
$propagate=[Security.AccessControl.PropagationFlags]::None
foreach($sid in @($user,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'),[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))){
 $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,$inherit,$propagate,[Security.AccessControl.AccessControlType]::Allow))
}
[IO.FileSystemAclExtensions]::SetAccessControl($directory,$acl)
$key=Join-Path $private 'menu-signing.pfx'
if(!(Test-Path -LiteralPath $key)) {
 $rsa=[Security.Cryptography.RSA]::Create(3072)
 $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Xiaomang Local Components',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
 $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
 $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,$true))
 $oids=[Security.Cryptography.OidCollection]::new();$oids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))|Out-Null
 $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids,$false))
 $certificate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1),[DateTimeOffset]::UtcNow.AddYears(3))
 [IO.File]::WriteAllBytes($key,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx))
 $certificate.Dispose();$rsa.Dispose()
}
$certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::new($key,'',[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
[IO.File]::WriteAllBytes((Join-Path $Output 'XiaomangMenuPublisher.cer'),$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
$tool=Join-Path $workspace 'tools/windows-sdk-buildtools-10.0.26100.6584/bin/10.0.26100.0/x64/signtool.exe'
& $tool sign /fd SHA256 /f $key (Join-Path $Output 'Xiaomang.FileFavoritesMenu.msix')
if($LASTEXITCODE -ne 0){throw 'Identity package signing failed'}
'Public certificate SHA256: '+[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificate.RawData))
'No certificate store was modified.'
$certificate.Dispose()
