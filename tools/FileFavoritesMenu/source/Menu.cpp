#include <windows.h>
#include <shobjidl.h>
#include <string>
#include <new>

static HMODULE module;
static LONG references;
static const CLSID commandId={0x694cb3cb,0x9654,0x4c7e,{0x94,0x8a,0x71,0xc2,0x57,0x6f,0xae,0x51}};
static HRESULT Copy(const std::wstring& value, PWSTR* result) {
    if(!result) return E_POINTER;
    *result=static_cast<PWSTR>(CoTaskMemAlloc((value.size()+1)*sizeof(wchar_t)));
    if(!*result)return E_OUTOFMEMORY;
    CopyMemory(*result,value.c_str(),(value.size()+1)*sizeof(wchar_t));return S_OK;
}
static std::wstring Executable() {
    wchar_t path[32768];DWORD length=GetModuleFileNameW(module,path,32768);
    if(!length || length>=32768)return L"";
    std::wstring value(path,length);
    // app/ThirdParty/FileFavoritesMenu/command.dll -> app/xiaomang.exe
    for(int i=0;i<3;i++){auto slash=value.find_last_of(L"\\/");if(slash==std::wstring::npos)return L"";value.resize(slash);}
    return value+L"\\xiaomang.exe";
}
static std::wstring Quoted(const std::wstring& value) {
    std::wstring result=L"\"";size_t backslashes=0;
    for(wchar_t character:value){
        if(character==L'\\'){backslashes++;continue;}
        result.append(backslashes,L'\\');backslashes=0;result+=character;
    }
    result.append(backslashes*2,L'\\');result+=L'"';return result;
}
class Command final:public IExplorerCommand {
    LONG count=1;
public:
    Command(){InterlockedIncrement(&references);} ~Command(){InterlockedDecrement(&references);}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid==IID_IUnknown || iid==IID_IExplorerCommand){*result=static_cast<IExplorerCommand*>(this);AddRef();return S_OK;}return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return InterlockedIncrement(&count);}
    ULONG STDMETHODCALLTYPE Release() override{LONG n=InterlockedDecrement(&count);if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray*,PWSTR* result) override{try{return Copy(L"添加到小蟒收藏夹",result);}catch(...){if(result)*result=nullptr;return E_OUTOFMEMORY;}}
    HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*,PWSTR* result) override{try{return Copy(Executable()+L",0",result);}catch(...){if(result)*result=nullptr;return E_OUTOFMEMORY;}}
    HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*,PWSTR* result) override{try{return Copy(L"固定文件或文件夹；不会移动或删除源文件",result);}catch(...){if(result)*result=nullptr;return E_OUTOFMEMORY;}}
    HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* result) override{if(!result)return E_POINTER;*result=commandId;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* items,BOOL,EXPCMDSTATE* result) override {
        if(!result)return E_POINTER;*result=ECS_HIDDEN;if(!items)return S_OK;
        DWORD count=0;if(FAILED(items->GetCount(&count)))return S_OK;
        *result=count>0 && count<=128?ECS_ENABLED:ECS_DISABLED;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* items,IBindCtx*) override {
        try {
        if(!items)return E_INVALIDARG;DWORD total=0;HRESULT hr=items->GetCount(&total);
        if(FAILED(hr)||total==0||total>128)return E_INVALIDARG;
        std::wstring executable=Executable();
        if(executable.empty() || GetFileAttributesW(executable.c_str())==INVALID_FILE_ATTRIBUTES)return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        std::wstring arguments=Quoted(executable);
        for(DWORD i=0;i<total;i++){
            IShellItem* item=nullptr;hr=items->GetItemAt(i,&item);if(FAILED(hr))return hr;
            PWSTR path=nullptr;hr=item->GetDisplayName(SIGDN_FILESYSPATH,&path);item->Release();
            if(FAILED(hr))return hr;
            std::wstring selected(path);CoTaskMemFree(path);
            if(selected.find(L'"')!=std::wstring::npos)return E_INVALIDARG;
            arguments+=L" -PinFile "+Quoted(selected);
            if(arguments.size()>32000)return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
        }
        STARTUPINFOW startup={sizeof(startup)};PROCESS_INFORMATION process={};
        if(!CreateProcessW(executable.c_str(),arguments.data(),nullptr,nullptr,FALSE,0,nullptr,nullptr,&startup,&process))return HRESULT_FROM_WIN32(GetLastError());
        CloseHandle(process.hThread);CloseHandle(process.hProcess);return S_OK;
        } catch(...){return E_OUTOFMEMORY;}
    }
    HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* result) override{if(!result)return E_POINTER;*result=ECF_DEFAULT;return S_OK;}
    HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** result) override{if(result)*result=nullptr;return E_NOTIMPL;}
};
class Factory final:public IClassFactory {
    LONG count=1;
public:
    Factory(){InterlockedIncrement(&references);}~Factory(){InterlockedDecrement(&references);}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;if(iid==IID_IUnknown||iid==IID_IClassFactory){*result=static_cast<IClassFactory*>(this);AddRef();return S_OK;}return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return InterlockedIncrement(&count);}
    ULONG STDMETHODCALLTYPE Release() override{LONG n=InterlockedDecrement(&count);if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,REFIID iid,void** result) override {
        if(outer)return CLASS_E_NOAGGREGATION;auto command=new(std::nothrow) Command();if(!command)return E_OUTOFMEMORY;
        HRESULT hr=command->QueryInterface(iid,result);command->Release();return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override{if(lock)InterlockedIncrement(&references);else InterlockedDecrement(&references);return S_OK;}
};
extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid,REFIID iid,void** result){
    if(clsid!=commandId)return CLASS_E_CLASSNOTAVAILABLE;
    auto factory=new(std::nothrow) Factory();if(!factory)return E_OUTOFMEMORY;
    HRESULT hr=factory->QueryInterface(iid,result);factory->Release();return hr;
}
extern "C" HRESULT __stdcall DllCanUnloadNow(){return InterlockedCompareExchange(&references,0,0)==0?S_OK:S_FALSE;}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){module=instance;DisableThreadLibraryCalls(instance);}return TRUE;}
