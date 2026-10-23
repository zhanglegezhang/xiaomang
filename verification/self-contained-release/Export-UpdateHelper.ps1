param([Parameter(Mandatory=$true)][string]$Assembly,[Parameter(Mandatory=$true)][string]$ScriptPath,[Parameter(Mandatory=$true)][string]$RecoveryPath)
$ErrorActionPreference='Stop'
# Run in a separate short-lived process. Loading an old assembly inside the
# upgrade test process would lock that DLL during the real replacement.
$product=[Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($Assembly))
$script=$product.GetType('ShareX.ProductUpdateInstaller',$true).GetField('Script',[Reflection.BindingFlags]'NonPublic,Public,Static').GetRawConstantValue()
[IO.File]::WriteAllText($ScriptPath,[string]$script,[Text.UTF8Encoding]::new($true))
$stream=$product.GetManifestResourceStream('ShareX.Assets.XiaomangUpdateRecovery.ps1')
if(!$stream){throw 'Released recovery resource missing'}
$reader=[IO.StreamReader]::new($stream)
try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()}
[IO.File]::WriteAllText($RecoveryPath,$text,[Text.UTF8Encoding]::new($true))
