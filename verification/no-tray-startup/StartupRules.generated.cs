using ShareX;
namespace StartupPolicyFixture {
static class Program { public static bool Sandbox; public static Prefs Settings=new(); }
sealed class Prefs { public bool StartupPreferenceInitialized; public bool StartWithWindowsByDefault=true; }
static class DebugHelper { public static void WriteException(Exception error,string message){} }
static class StartupManager {
 public static StartupState State {get=>state;set{state=value;Writes++;}} public static StartupState state;public static int Writes;
        internal static void ApplyFirstProfileDefault(bool newProfile)
        {
            if (Program.Sandbox || Program.Settings.StartupPreferenceInitialized) return;
            // Existing profiles retain their OS startup choice. The approved current
            // machine change is performed separately, through the existing setting.
            if (newProfile && Program.Settings.StartWithWindowsByDefault)
            {
                try
                {
                    if (State == StartupState.Disabled) State = StartupState.Enabled;
                }
                catch (Exception error) { DebugHelper.WriteException(error,"Unable to apply startup default."); return; }
            }
            Program.Settings.StartupPreferenceInitialized = true;
        }

}
public static class Rules {
public static void Run(Action<bool,string> check){
 foreach(bool sandbox in new[]{false,true})foreach(bool initialized in new[]{false,true})foreach(bool fresh in new[]{false,true})foreach(bool enabled in new[]{false,true})foreach(StartupState state in Enum.GetValues<StartupState>()){
  Program.Sandbox=sandbox;Program.Settings=new Prefs{StartupPreferenceInitialized=initialized,StartWithWindowsByDefault=enabled};StartupManager.state=state;StartupManager.Writes=0;
  StartupManager.ApplyFirstProfileDefault(fresh);
  bool shouldWrite=!sandbox&&!initialized&&fresh&&enabled&&state==StartupState.Disabled;
  check(StartupManager.Writes==(shouldWrite?1:0)&&StartupManager.State==(shouldWrite?StartupState.Enabled:state),"Startup default preserves existing and OS-disabled choices");
  StartupManager.ApplyFirstProfileDefault(fresh);check(StartupManager.Writes==(shouldWrite?1:0),"Startup default is not reapplied");
 }
}
}
}
static class StartupRules {public static void Run(Action<bool,string> check)=>StartupPolicyFixture.Rules.Run(check);}