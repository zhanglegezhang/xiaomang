#pragma once
#include <windows.h>
#include <filesystem>
#include <string>
#include <system_error>
#include <cstdio>

namespace setupdiag {
namespace fs = std::filesystem;
inline std::string Utf8(const std::wstring& value) {
    if(value.empty()) return {};
    int size=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,value.data(),static_cast<int>(value.size()),nullptr,0,nullptr,nullptr);
    if(!size) throw std::system_error(GetLastError(),std::system_category(),"UTF-8 conversion");
    std::string result(size,'\0');
    if(!WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,value.data(),static_cast<int>(value.size()),result.data(),size,nullptr,nullptr))
        throw std::system_error(GetLastError(),std::system_category(),"UTF-8 conversion");
    return result;
}
inline std::wstring Wide(const std::string& value) {
    if(value.empty()) return {};
    UINT page=CP_UTF8; DWORD flags=MB_ERR_INVALID_CHARS;
    int size=MultiByteToWideChar(page,flags,value.data(),static_cast<int>(value.size()),nullptr,0);
    if(!size) { page=CP_ACP; flags=0; size=MultiByteToWideChar(page,flags,value.data(),static_cast<int>(value.size()),nullptr,0); }
    std::wstring result(size,L'\0');
    if(size) MultiByteToWideChar(page,flags,value.data(),static_cast<int>(value.size()),result.data(),size);
    return result;
}
inline std::wstring Error(DWORD code) {
    wchar_t* message=nullptr;
    FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER|FORMAT_MESSAGE_FROM_SYSTEM|FORMAT_MESSAGE_IGNORE_INSERTS,nullptr,code,0,reinterpret_cast<wchar_t*>(&message),0,nullptr);
    std::wstring result=L"Win32="+std::to_wstring(code);
    if(message) { result+=L" ("+std::wstring(message)+L")"; LocalFree(message); }
    for(auto& ch:result) if(ch==L'\r' || ch==L'\n') ch=L' ';
    return result;
}
class Log {
public:
    fs::path path;
    DWORD error=ERROR_SUCCESS;
    explicit Log(fs::path value):path(std::move(value)){}
    bool Write(const std::wstring& message) {
        // Use explicit UTF-8 bytes, independent of the C++ stream locale.
        try {
            SYSTEMTIME time{}; GetLocalTime(&time); wchar_t stamp[96];
            swprintf(stamp,96,L"%04u-%02u-%02u %02u:%02u:%02u.%03u PID=%lu ",time.wYear,time.wMonth,time.wDay,time.wHour,time.wMinute,time.wSecond,time.wMilliseconds,GetCurrentProcessId());
            auto bytes=Utf8(std::wstring(stamp)+message+L"\r\n");
            HANDLE file=CreateFileW(path.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(file==INVALID_HANDLE_VALUE) { if(!error) error=GetLastError(); return false; }
            DWORD failure=ERROR_SUCCESS;
            for(size_t offset=0;offset<bytes.size();) {
                DWORD written=0;
                if(!WriteFile(file,bytes.data()+offset,static_cast<DWORD>(bytes.size()-offset),&written,nullptr)) { failure=GetLastError(); break; }
                if(!written) { failure=ERROR_WRITE_FAULT; break; }
                offset+=written;
            }
            if(!failure && !FlushFileBuffers(file)) failure=GetLastError();
            CloseHandle(file);
            if(failure && !error) error=failure;
            return !failure;
        } catch(const std::system_error& failure) { if(!error) error=failure.code().value(); return false; }
    }
    std::wstring Notice() const {
        return L"\n\n安装日志："+path.wstring()+(error?L"\n日志写入失败："+Error(error)+L"。请将本弹窗截图提交开发者。":L"");
    }
};
struct RunResult { bool success=false; bool unfinished=false; };
inline RunResult Run(const fs::path& helper,const fs::path& dll,bool install,Log& log,DWORD timeout=30000) {
    std::wstring operation=install?L"install":L"uninstall";
    log.Write(L"Helper begin: operation="+operation+L" helper="+helper.wstring()+L" dll="+dll.wstring());
    auto outputPath=helper.parent_path()/L"registration.log";
    HANDLE output=CreateFileW(outputPath.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    STARTUPINFOW startup{}; startup.cb=sizeof(startup); startup.dwFlags=STARTF_USESHOWWINDOW; startup.wShowWindow=SW_HIDE;
    if(output==INVALID_HANDLE_VALUE) { DWORD code=GetLastError(); log.Write(L"Helper log unavailable: path="+outputPath.wstring()+L" "+Error(code)); }
    else if(!SetHandleInformation(output,HANDLE_FLAG_INHERIT,HANDLE_FLAG_INHERIT)) {
        DWORD code=GetLastError(); CloseHandle(output); output=INVALID_HANDLE_VALUE;
        log.Write(L"Helper log handle inheritance failed: "+Error(code));
    } else { startup.dwFlags|=STARTF_USESTDHANDLES; startup.hStdOutput=startup.hStdError=output; startup.hStdInput=GetStdHandle(STD_INPUT_HANDLE); }
    std::wstring command=L"\""+helper.wstring()+L"\" \""+dll.wstring()+L"\" "+operation;
    PROCESS_INFORMATION process{};
    BOOL created=CreateProcessW(helper.c_str(),command.data(),nullptr,nullptr,TRUE,CREATE_NO_WINDOW,nullptr,helper.parent_path().c_str(),&startup,&process);
    DWORD createError=created?ERROR_SUCCESS:GetLastError();
    if(output!=INVALID_HANDLE_VALUE) CloseHandle(output);
    if(!created) { log.Write(L"Helper launch failed: "+Error(createError)); return {}; }
    CloseHandle(process.hThread);
    DWORD wait=WaitForSingleObject(process.hProcess,timeout),exit=STILL_ACTIVE;
    DWORD waitError=wait==WAIT_FAILED?GetLastError():ERROR_SUCCESS;
    BOOL queried=GetExitCodeProcess(process.hProcess,&exit); DWORD queryError=queried?ERROR_SUCCESS:GetLastError();
    CloseHandle(process.hProcess);
    if(wait!=WAIT_OBJECT_0) {
        log.Write(L"Helper wait incomplete: wait="+std::to_wstring(wait)+L" timeout_ms="+std::to_wstring(timeout)+L" "+Error(waitError));
        return {false,!queried || exit==STILL_ACTIVE};
    }
    if(!queried) { log.Write(L"Helper exit query failed: "+Error(queryError)); return {}; }
    log.Write(L"Helper end: operation="+operation+L" exit_code="+std::to_wstring(exit));
    return {exit==0,false};
}
inline bool Verify(const fs::path& dll,REGSAM view,const std::wstring& com,const std::wstring& tip,
                   const std::wstring& category,const std::wstring& profile,std::wstring* detail,HKEY root=HKEY_LOCAL_MACHINE) {
    auto fail=[&](const std::wstring& reason){ if(detail) *detail=reason; return false; };
    auto open=[&](const std::wstring& path,HKEY& key){
        LONG code=RegOpenKeyExW(root,path.c_str(),0,KEY_READ|view,&key);
        if(code!=ERROR_SUCCESS) return fail(L"key="+path+L" "+Error(code));
        return true;
    };
    HKEY key=nullptr;
    if(!open(com,key)) return false;
    wchar_t path[32768]{}; DWORD type=0,size=sizeof(path);
    LONG code=RegQueryValueExW(key,nullptr,nullptr,&type,reinterpret_cast<BYTE*>(path),&size); RegCloseKey(key);
    if(code!=ERROR_SUCCESS) return fail(L"COM value read: key="+com+L" "+Error(code));
    if(type!=REG_SZ || size<sizeof(wchar_t) || size>sizeof(path) || size%sizeof(wchar_t) || path[size/sizeof(wchar_t)-1]!=0)
        return fail(L"COM value invalid: key="+com+L" type="+std::to_wstring(type)+L" bytes="+std::to_wstring(size));
    if(_wcsicmp(path,dll.c_str())) return fail(L"COM path mismatch: actual="+std::wstring(path)+L" expected="+dll.wstring());
    if(!open(category,key)) return false;
    RegCloseKey(key);
    if(!open(tip+L"\\LanguageProfile",key)) return false;
    bool found=false; std::wstring reason=L"Missing language profile="+profile+L" under="+tip+L"\\LanguageProfile";
    for(DWORD index=0;;++index) {
        wchar_t name[256]{}; DWORD length=256;
        code=RegEnumKeyExW(key,index,name,&length,nullptr,nullptr,nullptr,nullptr);
        if(code==ERROR_NO_MORE_ITEMS) break;
        if(code!=ERROR_SUCCESS) { reason=L"Language profile enumeration failed: "+Error(code); break; }
        HKEY entry=nullptr; std::wstring child=std::wstring(name)+L"\\"+profile;
        code=RegOpenKeyExW(key,child.c_str(),0,KEY_READ,&entry);
        if(code==ERROR_SUCCESS) { RegCloseKey(entry); found=true; break; }
        if(code!=ERROR_FILE_NOT_FOUND && code!=ERROR_PATH_NOT_FOUND) { reason=L"Language profile read: "+child+L" "+Error(code); break; }
    }
    RegCloseKey(key);
    if(!found) return fail(reason);
    if(detail) *detail=L"COM path, TSF category and language profile verified";
    return true;
}
}
