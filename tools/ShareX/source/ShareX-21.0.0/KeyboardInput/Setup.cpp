// Elevated once, using fixed protected installation paths. The portable app is not moved.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <filesystem>
#include <fstream>
#include <string>
#include "SetupOperations.h"
#include "../NativeSetup/Diagnostics.h"
int wmain(int argc,wchar_t** argv) {
    WCHAR temporary[32768]{}; DWORD tempLength=GetTempPathW(32768,temporary);
    if(!tempLength || tempLength>=32768) {
        MessageBoxW(nullptr,L"无法取得安装日志目录，尚未修改组件。请将本弹窗截图提交开发者。",L"小蟒输入法",MB_OK|MB_ICONERROR); return 8;
    }
    setupdiag::Log diagnostics(fs::path(temporary)/L"XiaomangInputMethod-setup.log");
    auto log=[&](const std::wstring& message){ return diagnostics.Write(message); };
    auto notify=[&](const std::wstring& message,bool success){ MessageBoxW(nullptr,(message+diagnostics.Notice()).c_str(),L"小蟒输入法",MB_OK|(success?MB_ICONINFORMATION:MB_ICONERROR)); };
    if(!log(L"Session begin: operation="+std::wstring(argc>1?argv[1]:L"install"))) {
        notify(L"无法写入安装日志，尚未修改组件。",false); return 8;
    }
    if(argc>2 || (argc==2 && wcscmp(argv[1],L"install") && wcscmp(argv[1],L"uninstall"))) return 2;
    SetDllDirectoryW(L""); SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);
    WCHAR executable[32768]; GetModuleFileNameW(nullptr,executable,32768);
    PWSTR programFiles=nullptr;
    HRESULT folderResult=SHGetKnownFolderPath(FOLDERID_ProgramFiles,0,nullptr,&programFiles);
    if(FAILED(folderResult)) { log(L"ProgramFiles lookup failed: HRESULT="+std::to_wstring(static_cast<unsigned long>(folderResult))); notify(L"无法取得系统组件安装目录，尚未修改组件。",false); return 3; }
    // 新补丁使用独立受保护目录，避免覆盖仍被其他应用加载的 DLL。
    fs::path productRoot=fs::path(programFiles)/L"XiaomangInputMethod";
    fs::path root=productRoot/L"0.1.4-hotkey1"; CoTaskMemFree(programFiles);
    fs::path source=fs::path(executable).parent_path(); bool install=argc==1 || !wcscmp(argv[1],L"install");
    if(!install && !fs::exists(root)) root=productRoot/L"0.1.4";
    // Serialize installers; do not race another instance's file or registry operations.
    HANDLE mutex=CreateMutexW(nullptr,FALSE,L"Global\\XiaomangInputMethod.Setup.v1");
    if(!mutex) { DWORD code=GetLastError(); log(L"Setup mutex failed: "+setupdiag::Error(code)); notify(L"无法启动组件安装："+setupdiag::Error(code),false); return 7; }
    DWORD acquired=WaitForSingleObject(mutex,0);
    if(acquired!=WAIT_OBJECT_0 && acquired!=WAIT_ABANDONED) {
        MessageBoxW(nullptr,L"另一个输入法组件安装器正在运行，请等待它完成。",L"小蟒输入法",MB_OK|MB_ICONINFORMATION);
        CloseHandle(mutex); return 7;
    }
    struct Lease { HANDLE handle; ~Lease(){ ReleaseMutex(handle); CloseHandle(handle); } } lease{mutex};
    std::wstring stage=L"读取现有注册";
    try {
        bool registered64=MachineRegistered(root/L"x86_64"/L"XiaomangInputMethod.dll",KEY_WOW64_64KEY);
        bool registered32=MachineRegistered(root/L"i686"/L"XiaomangInputMethod.dll",KEY_WOW64_32KEY);
        bool previousUsable64=registered64 || MachineRegistered(productRoot/L"0.1.4"/L"x86_64"/L"XiaomangInputMethod.dll",KEY_WOW64_64KEY);
        bool previousUsable32=registered32 || MachineRegistered(productRoot/L"0.1.4"/L"i686"/L"XiaomangInputMethod.dll",KEY_WOW64_32KEY);
        log(L"Payload source="+source.wstring()+L" destination="+root.wstring());
        stage=L"检查组件文件"; std::wstring failure;
        bool unchanged=false;
        if(install) {
            auto files=Payload(source,root);
            unchanged=std::none_of(files.begin(),files.end(),[](const PayloadFile& file){return file.changed;});
            for(const auto& file:files) log(L"Payload "+std::wstring(file.changed?L"update":L"retain")+L": "+(root/file.relative).wstring());
            stage=L"更新组件文件"; CopyChanged(source,root,files);
            log(unchanged?L"Identical payload: no files overwritten":L"Changed payload copied");
        }
        bool success=true, unfinished=false; stage=install?L"注册组件":L"撤销注册";
        for(auto architecture : {L"x86_64",L"i686"}) {
            auto folder=root/architecture;
            bool registered=architecture==std::wstring(L"x86_64")?registered64:registered32;
            if(install && unchanged && registered) { log(std::wstring(architecture)+L": existing registration retained"); continue; }
            auto outcome=setupdiag::Run(folder/L"RegisterInputMethod.exe",folder/L"XiaomangInputMethod.dll",install,diagnostics);
            bool result=outcome.success; unfinished=unfinished || outcome.unfinished;
            if(!result) failure+=std::wstring(architecture)+L" "+stage+L"失败；";
            log(std::wstring(architecture)+L": registration "+(result?L"succeeded":L"failed"));
            success=result && success;
        }
        // 验证两种架构的 COM、语言配置和 TSF 类别，不能仅凭注册函数返回值判断成功。
        if(install && success) {
            stage=L"核验注册";
            for(auto architecture:{L"x86_64",L"i686"}) {
                std::wstring detail;
                bool verified=MachineRegistered(root/architecture/L"XiaomangInputMethod.dll",architecture==std::wstring(L"x86_64")?KEY_WOW64_64KEY:KEY_WOW64_32KEY,&detail);
                log(std::wstring(architecture)+L" verification="+(verified?L"success: ":L"failed: ")+detail);
                if(!verified) failure+=std::wstring(architecture)+L" 核验失败："+detail+L"；";
                success=verified && success;
            }
            log(success?L"Both architecture registrations verified":L"Registration verification failed");
        }
        // Never unregister an existing usable installation because a repair failed.
        if(!success && install && !previousUsable64 && !previousUsable32 && !unfinished) {
            log(L"Rollback begin: fresh registration failed; unregister both architectures");
            for(auto architecture:{L"x86_64",L"i686"}) {
                auto undone=setupdiag::Run(root/architecture/L"RegisterInputMethod.exe",root/architecture/L"XiaomangInputMethod.dll",false,diagnostics);
                bool absent=undone.success && MachineAbsent(architecture==std::wstring(L"x86_64")?KEY_WOW64_64KEY:KEY_WOW64_32KEY);
                log(std::wstring(architecture)+L" rollback="+(absent?L"verified":L"incomplete; files retained"));
                if(!absent) failure+=std::wstring(architecture)+L" 回退未完成，组件文件已保留；";
            }
        } else if(!success) log(unfinished?L"Rollback skipped: helper may still be running; files retained":L"Rollback skipped: existing registration retained");
        if(success) {
            stage=L"清理旧注册项";
            log(L"Cleanup HKCU key=Software\\Classes\\CLSID\\{745D01A8-3F53-41D6-969B-42A734F28B1E}");
            LONG clean64=CleanupUserOverride(KEY_WOW64_64KEY),clean32=CleanupUserOverride(KEY_WOW64_32KEY);
            log(L"Current-user override cleanup: x64="+std::to_wstring(clean64)+L", x86="+std::to_wstring(clean32));
            if(clean64!=ERROR_SUCCESS || clean32!=ERROR_SUCCESS) {
                notify(std::wstring(install?L"组件注册已完成":L"组件注册已撤销")+L"，但本账户的旧注册项未能完全清理（64位："+std::to_wstring(clean64)+L"，32位："+std::to_wstring(clean32)+L"）。组件文件已保留，请根据日志处理后重试。",false);
                return 6;
            }
        }
        if(!install && success) {
            if(!MachineAbsent(KEY_WOW64_64KEY) || !MachineAbsent(KEY_WOW64_32KEY)) {
                log(L"Registry verification after unregister failed; files retained");
                notify(L"系统注册仍未完全撤销，组件文件已保留，请检查安装日志后重试。",false);
                return 6;
            }
            // Loaded TIP DLLs cannot be deleted until their hosting applications exit.
            // Remove registration now; leave locked inert files for later cleanup.
            for(auto architecture : {L"x86_64",L"i686"}) for(auto file : {L"XiaomangInputMethod.dll",L"RegisterInputMethod.exe"}) {
                std::error_code ignored; fs::remove(root/architecture/file,ignored);
            }
        }
        log(L"Session end: exit_code="+std::to_wstring(success?0:4));
        notify(success?(install?(unchanged&&registered64&&registered32?L"系统输入法组件已安装，无需重复安装。旧注册项已检查并清理。":L"系统输入法组件已安装，旧注册项已检查并清理。"):L"系统输入法组件已卸载。正在使用的文件将在宿主退出后才能删除。"):
            L"组件操作失败："+failure+(unfinished?L"注册进程尚未结束，请等待后重试。":L"")+L"组件文件已保留，请将安装日志和本弹窗截图提交开发者。",success);
        return success?0:4;
    } catch(const std::exception& error) {
        std::string detail=error.what(); log(L"Failure stage="+stage+L": "+setupdiag::Wide(detail));
        if(auto file=dynamic_cast<const fs::filesystem_error*>(&error)) log(L"File paths: source="+file->path1().wstring()+L" destination="+file->path2().wstring());
        if(auto system=dynamic_cast<const std::system_error*>(&error)) log(L"File error: code="+std::to_wstring(system->code().value())+L" category="+setupdiag::Wide(system->code().category().name()));
        notify(stage+L"失败："+setupdiag::Wide(detail)+L"\n请将安装日志和本弹窗截图提交开发者。",false);
        return 5;
    }
}
