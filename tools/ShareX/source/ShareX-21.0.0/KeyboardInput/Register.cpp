// 注册专用维护分支，TSF DLL 由安装器放在受保护目录中。
#include <windows.h>
#include <objbase.h>
#include <cstdio>
#include "../NativeSetup/Diagnostics.h"
int wmain(int argc,wchar_t** argv) {
    if(argc!=3 || (wcscmp(argv[2],L"install") && wcscmp(argv[2],L"uninstall"))) return 2;
    SetDllDirectoryW(L"");
    printf("Operation=%s architecture=%s PID=%lu DLL=%s\n",setupdiag::Utf8(argv[2]).c_str(),sizeof(void*)==8?"x86_64":"i686",GetCurrentProcessId(),setupdiag::Utf8(argv[1]).c_str()); fflush(stdout);
    HRESULT initialized=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    if(FAILED(initialized)) { printf("COM initialization HRESULT=0x%08lx\n",static_cast<unsigned long>(initialized)); return 4; }
    HMODULE dll=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!dll) { printf("Load error=%lu\n",GetLastError()); CoUninitialize(); return 3; }
    const char* entry=wcscmp(argv[2],L"install")==0?"DllRegisterServer":"DllUnregisterServer";
    auto function=reinterpret_cast<HRESULT(WINAPI*)()>(GetProcAddress(dll,entry));
    HRESULT result=function?function():E_FAIL;
    if(!function) printf("Missing entry=%s\n",entry);
    printf("Registration HRESULT=0x%08lx\n",static_cast<unsigned long>(result));
    FreeLibrary(dll); CoUninitialize(); return SUCCEEDED(result)?0:4;
}
