function Close-TestApp([string]$Entry) {
 $Entry=[IO.Path]::GetFullPath($Entry)
 $deadline=[DateTime]::UtcNow.AddSeconds(60); $quiet=$null
 do {
  $running=@(Get-Process xiaomang -ErrorAction SilentlyContinue | Where-Object Path -eq $Entry)
  if($running.Count){
   $quiet=$null
   $request=Start-Process -FilePath $Entry -ArgumentList '-ExitShareX' -WindowStyle Hidden -PassThru
   $request.WaitForExit(5000)|Out-Null
   foreach($p in $running){$p.WaitForExit(5000)|Out-Null}
  } else {
   if(!$quiet){$quiet=[DateTime]::UtcNow}
   if(([DateTime]::UtcNow-$quiet).TotalSeconds -ge 3){return}
  }
  Start-Sleep -Milliseconds 200
 } while([DateTime]::UtcNow -lt $deadline)
 throw "Test process did not exit: $Entry. Stop before the next case."
}
