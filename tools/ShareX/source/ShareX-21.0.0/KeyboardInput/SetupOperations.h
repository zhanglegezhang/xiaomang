#pragma once
#include "../NativeSetup/Diagnostics.h"
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <array>
#include <algorithm>
#include <exception>
#include <system_error>
#include <vector>
#include <stdexcept>
#include <functional>
namespace fs=std::filesystem;
struct PayloadFile { fs::path relative; bool changed; };
static bool SameFile(const fs::path& source,const fs::path& destination) {
    if(!fs::is_regular_file(source)) throw fs::filesystem_error("Source payload file is missing",source,std::make_error_code(std::errc::no_such_file_or_directory));
    if(!fs::is_regular_file(destination) || fs::file_size(source)!=fs::file_size(destination)) return false;
    std::ifstream a(source,std::ios::binary),b(destination,std::ios::binary);
    if(!a || !b) throw fs::filesystem_error("Cannot read payload for comparison",source,destination,std::make_error_code(std::errc::io_error));
    std::array<char,65536> left{},right{};
    do {
        a.read(left.data(),left.size()); b.read(right.data(),right.size());
        if(a.gcount()!=b.gcount() || !std::equal(left.begin(),left.begin()+a.gcount(),right.begin())) return false;
    } while(a.gcount());
    if(a.bad() || b.bad()) throw fs::filesystem_error("Payload comparison failed",source,destination,std::make_error_code(std::errc::io_error));
    return true;
}
static std::vector<PayloadFile> Payload(const fs::path& source,const fs::path& root) {
    std::vector<PayloadFile> files;
    for(auto architecture:{L"x86_64",L"i686"}) for(auto file:{L"XiaomangInputMethod.dll",L"RegisterInputMethod.exe"}) {
        fs::path relative=fs::path(architecture)/file;
        files.push_back({relative,!SameFile(source/relative,root/relative)});
    }
    size_t count=0;
    for(const auto& license:fs::directory_iterator(source/L"Licenses")) if(license.is_regular_file()) {
        fs::path relative=fs::path(L"Licenses")/license.path().filename();
        files.push_back({relative,!SameFile(source/relative,root/relative)}); ++count;
    }
    if(!count) throw std::runtime_error("License payload is missing");
    return files;
}
// Check every destination before changing any file. Equal files are never opened for writing.
static void CopyChanged(const fs::path& source,const fs::path& root,const std::vector<PayloadFile>& files) {
    struct Handles {
        std::vector<HANDLE> items;
        ~Handles(){ for(auto handle:items) CloseHandle(handle); }
    } locks;
    size_t changed=0;
    for(const auto& file:files) if(file.changed) {
        ++changed;
        if(fs::exists(root/file.relative)) {
            HANDLE handle=CreateFileW((root/file.relative).c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(handle==INVALID_HANDLE_VALUE) throw fs::filesystem_error("Destination is locked or not writable; existing installation retained",root/file.relative,std::error_code(GetLastError(),std::system_category()));
            locks.items.push_back(handle);
        }
    }
    if(!changed) return;
    fs::create_directories(root);
    fs::path stage=root/(L".setup-"+std::to_wstring(GetCurrentProcessId())+L"-"+std::to_wstring(GetTickCount64()));
    fs::create_directory(stage);
    std::vector<fs::path> touched;
    try {
        // Stage all new files and backups before replacing the first destination.
        for(auto handle:locks.items) CloseHandle(handle);
        locks.items.clear();
        for(const auto& file:files) if(file.changed) {
            fs::create_directories((stage/L"new"/file.relative).parent_path());
            fs::copy_file(source/file.relative,stage/L"new"/file.relative);
            if(fs::exists(root/file.relative)) {
                fs::create_directories((stage/L"old"/file.relative).parent_path());
                fs::copy_file(root/file.relative,stage/L"old"/file.relative);
            }
        }
        for(const auto& file:files) if(file.changed) {
            fs::create_directories((root/file.relative).parent_path());
            if(!MoveFileExW((stage/L"new"/file.relative).c_str(),(root/file.relative).c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH))
                throw fs::filesystem_error("Cannot replace payload",stage/L"new"/file.relative,root/file.relative,std::error_code(GetLastError(),std::system_category()));
            touched.push_back(file.relative);
        }
    } catch(...) {
        auto failure=std::current_exception(); bool restored=true;
        for(auto it=touched.rbegin();it!=touched.rend();++it) {
            if(fs::exists(stage/L"old"/(*it))) restored=!!MoveFileExW((stage/L"old"/(*it)).c_str(),(root/(*it)).c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH)&&restored;
            else { std::error_code error; fs::remove(root/(*it),error); restored=!error&&restored; }
        }
        if(!restored) throw std::runtime_error("File rollback failed; recovery files retained in .setup directory");
        std::error_code ignored; fs::remove_all(stage,ignored); std::rethrow_exception(failure);
    }
    std::error_code ignored; fs::remove_all(stage,ignored);
}
using DeleteTreeFunction=LSTATUS(WINAPI*)(HKEY,LPCWSTR);
static bool MachineAbsent(REGSAM view) {
    for(auto path:{L"Software\\Classes\\CLSID\\{745D01A8-3F53-41D6-969B-42A734F28B1E}",L"Software\\Microsoft\\CTF\\TIP\\{745D01A8-3F53-41D6-969B-42A734F28B1E}"}) {
        HKEY key=nullptr;
        LONG result=RegOpenKeyExW(HKEY_LOCAL_MACHINE,path,0,KEY_READ|view,&key);
        if(key) RegCloseKey(key);
        if(result!=ERROR_FILE_NOT_FOUND && result!=ERROR_PATH_NOT_FOUND) return false;
    }
    return true;
}
// A successful API call alone is insufficient: verify the key is really gone.
static LONG RemoveOverride(HKEY parent,const wchar_t* child,DeleteTreeFunction remove=RegDeleteTreeW) {
    LONG result=remove(parent,child);
    if(result!=ERROR_SUCCESS && result!=ERROR_FILE_NOT_FOUND && result!=ERROR_PATH_NOT_FOUND) return result;
    HKEY remaining=nullptr;
    result=RegOpenKeyExW(parent,child,0,KEY_READ,&remaining);
    if(remaining) RegCloseKey(remaining);
    return result==ERROR_FILE_NOT_FOUND || result==ERROR_PATH_NOT_FOUND?ERROR_SUCCESS:result==ERROR_SUCCESS?ERROR_ALREADY_EXISTS:result;
}
[[maybe_unused]] static LONG CleanupUserOverride(REGSAM view) {
    HKEY parent=nullptr;
    LONG result=RegOpenKeyExW(HKEY_CURRENT_USER,L"Software\\Classes\\CLSID",0,DELETE|KEY_ENUMERATE_SUB_KEYS|KEY_QUERY_VALUE|KEY_SET_VALUE|view,&parent);
    if(result==ERROR_FILE_NOT_FOUND || result==ERROR_PATH_NOT_FOUND) return ERROR_SUCCESS;
    if(result!=ERROR_SUCCESS) return result;
    result=RemoveOverride(parent,L"{745D01A8-3F53-41D6-969B-42A734F28B1E}");
    RegCloseKey(parent); return result;
}
static bool MachineRegistered(const fs::path& dll,REGSAM view,std::wstring* detail=nullptr) {
    const std::wstring com=L"Software\\Classes\\CLSID\\{745D01A8-3F53-41D6-969B-42A734F28B1E}\\InprocServer32";
    const std::wstring tip=L"Software\\Microsoft\\CTF\\TIP\\{745D01A8-3F53-41D6-969B-42A734F28B1E}";
    return setupdiag::Verify(dll,view,com,tip,tip+L"\\Category\\Category\\{34745C63-B2F0-4784-8B67-5E12C8701A31}\\{745D01A8-3F53-41D6-969B-42A734F28B1E}",L"{81C29015-D151-48C4-9643-EA1BA891612E}",detail);
}
