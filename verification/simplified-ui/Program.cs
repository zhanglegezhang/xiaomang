using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShareX;
using ShareX.HelpersLib;
class Tests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
    static int checks;
    static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
    [STAThread] static void Main(){
        string dir=Path.GetFullPath("artifacts/Desktop");
        AssemblyLoadContext.Default.Resolving+=(_,n)=>File.Exists(Path.Combine(dir,n.Name+".dll"))?Assembly.LoadFrom(Path.Combine(dir,n.Name+".dll")):null;
        Run();
    }
    static void Run(){
        var type=typeof(ApplicationConfig).Assembly.GetType("ShareX.PersonalDataPreferences");
        var export=type.GetMethod("Export",All);var merge=type.GetMethod("Merge",All);
        var serializer=(JsonSerializer)type.GetMethod("Serializer",All).Invoke(null,null);
        Check(new ApplicationConfig().Language==SupportedLanguage.SimplifiedChinese,"default");
        foreach(var language in Enum.GetValues<SupportedLanguage>()){
            foreach(JToken token in new JToken[]{new JValue(language.ToString()),new JValue((int)language)}){
                var json=new JObject{["Language"]=token,["ClipboardMarker"]="keep",["DisableHotkeysOnFullscreen"]=true,
                    ["TodoSettings"]=new JObject{["VisibleModules"]=new JArray("Todo","Diary"),["Items"]=new JArray(new JObject{["Text"]="保留待办"})}};
                var config=json.ToObject<ApplicationConfig>(serializer);
                Check(config.Language==SupportedLanguage.SimplifiedChinese,"legacy "+language);
                Check(config.DisableHotkeysOnFullscreen&&config.TodoSettings.Items.Single().Text=="保留待办"&&config.TodoSettings.VisibleModules.SequenceEqual(new[]{"Todo","Diary"}),"preserve");
                var portable=(JObject)export.Invoke(null,new object[]{json});
                Check((string)portable["Language"]=="SimplifiedChinese","export");
                var target=new JObject{["Language"]="English",["Untouched"]="keep"};merge.Invoke(null,new object[]{target,json});
                Check((string)target["Language"]=="SimplifiedChinese"&&(string)target["Untouched"]=="keep","import");
            }
        }
        var noLanguage=JObject.Parse("{}").ToObject<ApplicationConfig>(serializer);
        Check(noLanguage.Language==SupportedLanguage.SimplifiedChinese,"missing");
        foreach(string culture in new[]{"en-US","zh-TW","ja-JP"}){
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(culture);
            LanguageHelper.ChangeLanguage(SupportedLanguage.SimplifiedChinese);
            Check(CultureInfo.CurrentUICulture.Name=="zh-CN","runtime culture");
        }
        string source=File.ReadAllText("src/Desktop/ShareX/Forms/ApplicationSettingsForm.cs");
        Check(!source.Contains("ChangeLanguage(")&&!source.Contains("界面语言")&&!source.Contains("btnLanguages"),"language entry removed");
        string result=$"PASS {checks} checks: defaults, all legacy language enums, preferences export/import, other setting preservation, runtime UI culture.\n";
        File.WriteAllText("test-results/simplified-ui/check-result.txt",result);Console.WriteLine(result);
    }
}
