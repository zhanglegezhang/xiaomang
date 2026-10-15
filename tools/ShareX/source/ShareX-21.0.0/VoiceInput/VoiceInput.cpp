// Independent TSF speech service. No recognizer, keyboard IME or dictionary is embedded.
// Commands are consumed on the application's TSF apartment, never on a worker thread.
#define WIN32_LEAN_AND_MEAN
#define INITGUID
#include <windows.h>
#include <msctf.h>
#include <inputscope.h>
#include <sddl.h>
#include <string>
#include <cstring>
#include <new>
#include <vector>
#ifndef TF_CLIENTID_NULL
#define TF_CLIENTID_NULL ((TfClientId)0)
#endif
#ifndef TF_IPPMF_FORSESSION
#define TF_IPPMF_FORSESSION 0x20000000
#define TF_IPPMF_ENABLEPROFILE 0x00000001
#define TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE 0x00000004
#endif

static const CLSID ServiceId = {0x7fd8708d,0xa55c,0x492d,{0x91,0x8f,0x18,0x02,0xdc,0x25,0x9b,0x3a}};
static const GUID ProfileId = {0x96a307e7,0xe071,0x429b,{0xa3,0x84,0xd9,0x48,0x58,0x84,0x51,0x5f}};
static HMODULE module;
static LONG objects;
static constexpr DWORD Capacity = 4096;
struct Command { DWORD sequence, action; ULONGLONG session; DWORD pid,length; ULONGLONG window,expires; WCHAR text[Capacity]; };
struct Ack { DWORD sequence,status; ULONGLONG session; DWORD pid,length; };
struct Channel { Command command{}; Ack ack{}; };
static Channel* channel;
static SRWLOCK channelLock=SRWLOCK_INIT;
static HANDLE brokerStop,brokerThread;
static std::vector<HANDLE> connections;
static DWORD FocusProcess(HWND window) {
    DWORD pid=0; DWORD thread=GetWindowThreadProcessId(window,&pid);
    GUITHREADINFO info{}; info.cbSize=sizeof(info);
    if(thread && GetGUIThreadInfo(thread,&info) && info.hwndFocus) GetWindowThreadProcessId(info.hwndFocus,&pid);
    return pid;
}
static_assert(sizeof(Command)==8232 && sizeof(Ack)==24,"IPC layout must match x86 and x64");

template<class T> static void Drop(T*& p) { if(p) { p->Release(); p=nullptr; } }
static std::wstring ChannelName() {
    HANDLE token=nullptr; DWORD size=0;
    if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token)) return {};
    GetTokenInformation(token,TokenUser,nullptr,0,&size);
    auto buffer=new(std::nothrow) BYTE[size];
    std::wstring name;
    if(buffer && GetTokenInformation(token,TokenUser,buffer,size,&size)) {
        LPWSTR sid=nullptr;
        if(ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(buffer)->User.Sid,&sid)) {
            name=L"\\\\.\\pipe\\XiaomangVoiceInput.v1."; name+=sid; LocalFree(sid);
        }
    }
    delete[] buffer; CloseHandle(token); return name;
}
static bool PipeWrite(HANDLE pipe,const void* data,DWORD size,bool asynchronous) {
    DWORD written=0; OVERLAPPED operation{};
    if(asynchronous) operation.hEvent=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    BOOL result=WriteFile(pipe,data,size,&written,asynchronous?&operation:nullptr);
    if(!result && asynchronous && GetLastError()==ERROR_IO_PENDING) {
        HANDLE events[]={operation.hEvent,brokerStop};
        if(WaitForMultipleObjects(2,events,FALSE,500)==WAIT_OBJECT_0) result=GetOverlappedResult(pipe,&operation,&written,FALSE);
        else { CancelIoEx(pipe,&operation); GetOverlappedResult(pipe,&operation,&written,TRUE); }
    }
    if(operation.hEvent) CloseHandle(operation.hEvent);
    return result && written==size;
}
static void WriteAck(HANDLE pipe,const Command& command,DWORD status,DWORD length) {
    Ack ack{command.sequence,status,command.session,GetCurrentProcessId(),length};
    PipeWrite(pipe,&ack,sizeof(ack),false);
}
static DWORD WINAPI ServeConnection(void* parameter) {
    HANDLE pipe=parameter; ULONG pid=0;
    if(!GetNamedPipeClientProcessId(pipe,&pid)) { CloseHandle(pipe); return 0; }
    DWORD sent=0;
    while(WaitForSingleObject(brokerStop,20)==WAIT_TIMEOUT) {
        Command command{};
        AcquireSRWLockShared(&channelLock); if(channel) command=channel->command; ReleaseSRWLockShared(&channelLock);
        if(command.sequence!=sent && command.pid==pid && command.expires>=GetTickCount64()) {
            sent=command.sequence; if(!PipeWrite(pipe,&command,sizeof(command),true)) break;
        }
        DWORD available=0;
        if(!PeekNamedPipe(pipe,nullptr,0,nullptr,&available,nullptr)) break;
        if(available>=sizeof(Ack)) {
            Ack ack{}; DWORD read=0; OVERLAPPED operation{}; operation.hEvent=CreateEventW(nullptr,TRUE,FALSE,nullptr);
            BOOL success=ReadFile(pipe,&ack,sizeof(ack),&read,&operation);
            if(!success && GetLastError()==ERROR_IO_PENDING) {
                if(WaitForSingleObject(operation.hEvent,100)==WAIT_OBJECT_0) success=GetOverlappedResult(pipe,&operation,&read,FALSE);
                else { CancelIoEx(pipe,&operation); GetOverlappedResult(pipe,&operation,&read,TRUE); }
            }
            CloseHandle(operation.hEvent);
            if(!success || read!=sizeof(Ack)) break;
            AcquireSRWLockExclusive(&channelLock);
            if(channel && ack.pid==pid && channel->command.pid==pid && ack.sequence==channel->command.sequence && ack.session==channel->command.session) channel->ack=ack;
            ReleaseSRWLockExclusive(&channelLock);
        }
    }
    DisconnectNamedPipe(pipe); CloseHandle(pipe); return 0;
}
static DWORD WINAPI AcceptConnections(void*) {
    auto name=ChannelName(); auto sid=name.substr(name.find(L".v1.")+4);
    std::wstring sddl=L"D:(D;;GA;;;NU)(A;;GA;;;"+sid+L")(A;;GRGW;;;AC)S:(ML;;NW;;;LW)";
    PSECURITY_DESCRIPTOR descriptor=nullptr;
    if(!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(),SDDL_REVISION_1,&descriptor,nullptr)) return GetLastError();
    SECURITY_ATTRIBUTES security{sizeof(security),descriptor,FALSE};
    while(WaitForSingleObject(brokerStop,0)==WAIT_TIMEOUT) {
        HANDLE pipe=CreateNamedPipeW(name.c_str(),PIPE_ACCESS_DUPLEX|FILE_FLAG_OVERLAPPED,PIPE_TYPE_MESSAGE|PIPE_READMODE_MESSAGE|PIPE_WAIT|PIPE_REJECT_REMOTE_CLIENTS,64,65536,65536,0,&security);
        if(pipe==INVALID_HANDLE_VALUE) { WaitForSingleObject(brokerStop,200); continue; }
        OVERLAPPED operation{}; operation.hEvent=CreateEventW(nullptr,TRUE,FALSE,nullptr);
        BOOL connected=ConnectNamedPipe(pipe,&operation); DWORD error=GetLastError();
        if(!connected && error==ERROR_IO_PENDING) {
            HANDLE events[]={operation.hEvent,brokerStop}; DWORD result=WaitForMultipleObjects(2,events,FALSE,INFINITE);
            if(result==WAIT_OBJECT_0) { DWORD ignored=0; connected=GetOverlappedResult(pipe,&operation,&ignored,FALSE); }
            else { CancelIoEx(pipe,&operation); DWORD ignored=0; GetOverlappedResult(pipe,&operation,&ignored,TRUE); }
        } else if(error==ERROR_PIPE_CONNECTED) connected=TRUE;
        CloseHandle(operation.hEvent);
        if(connected && WaitForSingleObject(brokerStop,0)==WAIT_TIMEOUT) {
            HANDLE thread=CreateThread(nullptr,0,ServeConnection,pipe,0,nullptr);
            if(thread) connections.push_back(thread); else CloseHandle(pipe);
            // Reap finished connections so long-running use does not accumulate handles.
            for(auto i=connections.begin();i!=connections.end();) {
                if(WaitForSingleObject(*i,0)==WAIT_OBJECT_0) { CloseHandle(*i); i=connections.erase(i); } else ++i;
            }
        } else CloseHandle(pipe);
    }
    LocalFree(descriptor); return 0;
}

class Service;
class Edit final : public ITfEditSession {
    LONG refs=1; Service* service; ITfContext* context; Command command;
public:
    Edit(Service*,ITfContext*,const Command&);
    ~Edit();
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out) override;
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { ULONG r=InterlockedDecrement(&refs); if(!r) delete this; return r; }
    HRESULT STDMETHODCALLTYPE DoEditSession(TfEditCookie cookie) override;
};

class Service final : public ITfTextInputProcessor, public ITfCompositionSink, public ITfThreadMgrEventSink {
    LONG refs=1; ITfThreadMgr* manager=nullptr; TfClientId client=TF_CLIENTID_NULL;
    ITfContext* activeContext=nullptr; ITfComposition* composition=nullptr;
    HWND pollWindow=nullptr; HANDLE pipe=INVALID_HANDLE_VALUE;
    DWORD seen=0; ULONGLONG activeSession=0; bool pending=false, invalid=false;
    std::wstring last;
    DWORD sinkCookie=TF_INVALID_COOKIE;
    HRESULT EndOwned(TfEditCookie cookie) {
        ITfComposition* ending=composition; composition=nullptr;
        Drop(activeContext); activeSession=0;
        HRESULT result=S_OK;
        if(ending) { result=ending->EndComposition(cookie); ending->Release(); }
        return result;
    }
    void ScheduleEnd() {
        invalid=true;
        if(!composition || !activeContext) return;
        Command end{}; end.action=5; end.session=activeSession;
        Edit* edit=new(std::nothrow) Edit(this,activeContext,end);
        if(edit) { HRESULT ignored; activeContext->RequestEditSession(client,edit,TF_ES_ASYNCDONTCARE|TF_ES_READWRITE,&ignored); edit->Release(); }
    }
    static LRESULT CALLBACK WindowProc(HWND w,UINT m,WPARAM p,LPARAM l) {
        Service* self=reinterpret_cast<Service*>(GetWindowLongPtrW(w,GWLP_USERDATA));
        if(m==WM_NCCREATE) { self=static_cast<Service*>(reinterpret_cast<CREATESTRUCTW*>(l)->lpCreateParams); SetWindowLongPtrW(w,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(self)); }
        if(m==WM_TIMER && self) self->Poll();
        return DefWindowProcW(w,m,p,l);
    }
    bool Foreground(const Command& c) {
        HWND foreground=GetForegroundWindow();
        BOOL focused=FALSE;
        return foreground && c.pid==GetCurrentProcessId() && reinterpret_cast<ULONGLONG>(foreground)==c.window && manager && SUCCEEDED(manager->IsThreadFocus(&focused)) && focused;
    }
    bool ContextMatches(ITfContext* c) {
        ITfDocumentMgr* doc=nullptr; ITfContext* top=nullptr;
        bool ok=manager && SUCCEEDED(manager->GetFocus(&doc)) && doc && SUCCEEDED(doc->GetTop(&top)) && top==c;
        Drop(top); Drop(doc); return ok;
    }
    bool PrivateScope(ITfContext* c,TfEditCookie cookie,ITfRange* range) {
        ITfReadOnlyProperty* property=nullptr; VARIANT value; VariantInit(&value); bool denied=false;
        if(SUCCEEDED(c->GetAppProperty(GUID_PROP_INPUTSCOPE,&property)) && property &&
           SUCCEEDED(property->GetValue(cookie,range,&value)) && value.vt==VT_UNKNOWN && value.punkVal) {
            ITfInputScope* scope=nullptr;
            if(SUCCEEDED(value.punkVal->QueryInterface(IID_ITfInputScope,reinterpret_cast<void**>(&scope)))) {
                InputScope* values=nullptr; UINT count=0;
                if(SUCCEEDED(scope->GetInputScopes(&values,&count))) {
                    for(UINT i=0;i<count;i++) if(values[i]==IS_PASSWORD || values[i]==IS_PRIVATE || static_cast<int>(values[i])==63 || static_cast<int>(values[i])==64 || static_cast<int>(values[i])==65) denied=true;
                    CoTaskMemFree(values);
                }
                Drop(scope);
            }
        }
        VariantClear(&value); Drop(property); return denied;
    }
    bool Disabled(ITfContext* c) {
        ITfCompartmentMgr* cm=nullptr; bool disabled=false;
        if(SUCCEEDED(c->QueryInterface(IID_ITfCompartmentMgr,reinterpret_cast<void**>(&cm)))) {
            for(const GUID* id : { &GUID_COMPARTMENT_KEYBOARD_DISABLED,&GUID_COMPARTMENT_EMPTYCONTEXT }) {
                ITfCompartment* compartment=nullptr; VARIANT v; VariantInit(&v);
                if(SUCCEEDED(cm->GetCompartment(*id,&compartment)) && SUCCEEDED(compartment->GetValue(&v)) && v.vt==VT_I4 && v.lVal) disabled=true;
                VariantClear(&v); Drop(compartment);
            }
            Drop(cm);
        }
        return disabled;
    }
    bool LegacyEditDenied() {
        // Windows' legacy EDIT-to-TSF adapter can expose an empty TSF range while
        // an EM_SETSEL selection is pending. Query the standard control contract;
        // this is a Windows control guard, not an application-specific adapter.
        GUITHREADINFO info{}; info.cbSize=sizeof(info); WCHAR name[128]{};
        if(!GetGUIThreadInfo(GetCurrentThreadId(),&info) || !info.hwndFocus) return false;
        RealGetWindowClassW(info.hwndFocus,name,128);
        std::wstring lower=name; for(auto& letter:lower) letter=towlower(letter);
        bool edit=lower==L"edit" || lower.rfind(L"richedit",0)==0 || (lower.rfind(L"windowsforms",0)==0 && lower.find(L".edit.")!=std::wstring::npos);
        if(!edit) return false;
        LONG_PTR style=GetWindowLongPtrW(info.hwndFocus,GWL_STYLE);
        if(style & (ES_PASSWORD|ES_READONLY)) return true;
        DWORD start=0,end=0; DWORD_PTR ignored=0;
        if(!SendMessageTimeoutW(info.hwndFocus,EM_GETSEL,reinterpret_cast<WPARAM>(&start),reinterpret_cast<LPARAM>(&end),SMTO_ABORTIFHUNG,100,&ignored)) return true;
        return start!=end;
    }
    void Poll() {
        if(pipe==INVALID_HANDLE_VALUE) {
            DWORD pid=FocusProcess(GetForegroundWindow());
            if(pid!=GetCurrentProcessId()) return;
            auto name=ChannelName(); pipe=CreateFileW(name.c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);
            if(pipe!=INVALID_HANDLE_VALUE) { DWORD mode=PIPE_READMODE_MESSAGE|PIPE_NOWAIT; if(!SetNamedPipeHandleState(pipe,&mode,nullptr,nullptr)) { CloseHandle(pipe); pipe=INVALID_HANDLE_VALUE; } }
        }
        if(pipe==INVALID_HANDLE_VALUE || pending) return;
        Command c{}; DWORD available=0,read=0;
        if(!PeekNamedPipe(pipe,nullptr,0,nullptr,&available,nullptr)) { ScheduleEnd(); CloseHandle(pipe); pipe=INVALID_HANDLE_VALUE; return; }
        if(available<sizeof(Command) || !ReadFile(pipe,&c,sizeof(Command),&read,nullptr) || read!=sizeof(Command) || c.length>=Capacity || !c.sequence || c.sequence==seen) return;
        // Each process sees the channel, but only the focused TSF context may act.
        if(!Foreground(c)) return;
        ITfDocumentMgr* doc=nullptr; ITfContext* context=nullptr;
        if(!manager || FAILED(manager->GetFocus(&doc)) || !doc || FAILED(doc->GetTop(&context)) || !context) { Drop(context); Drop(doc); return; }
        Drop(doc);
        seen=c.sequence;
        TF_STATUS status{};
        if(c.action==1 && (LegacyEditDenied() || FAILED(context->GetStatus(&status)) || (status.dwDynamicFlags & TF_SD_READONLY) || Disabled(context))) {
            WriteAck(pipe,c,2,0); Drop(context); return;
        }
        if(c.expires<GetTickCount64() || (activeSession && c.action!=1 && (c.session!=activeSession || context!=activeContext))) {
            WriteAck(pipe,c,3,static_cast<DWORD>(last.size())); Drop(context); return;
        }
        pending=true; Edit* edit=new(std::nothrow) Edit(this,context,c);
        HRESULT result=E_OUTOFMEMORY, sessionResult=E_OUTOFMEMORY;
        if(edit) { result=context->RequestEditSession(client,edit,TF_ES_ASYNCDONTCARE|TF_ES_READWRITE,&sessionResult); edit->Release(); }
        if(FAILED(result) || FAILED(sessionResult)) { pending=false; WriteAck(pipe,c,4,static_cast<DWORD>(last.size())); }
        Drop(context);
    }
public:
    Service() { InterlockedIncrement(&objects); }
    ~Service() { Deactivate(); InterlockedDecrement(&objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out) override {
        if(!out) return E_POINTER; *out=nullptr;
        if(id==IID_IUnknown || id==IID_ITfTextInputProcessor) *out=static_cast<ITfTextInputProcessor*>(this);
        else if(id==IID_ITfCompositionSink) *out=static_cast<ITfCompositionSink*>(this);
        else if(id==IID_ITfThreadMgrEventSink) *out=static_cast<ITfThreadMgrEventSink*>(this);
        else return E_NOINTERFACE; AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { ULONG r=InterlockedDecrement(&refs); if(!r) delete this; return r; }
    HRESULT STDMETHODCALLTYPE Activate(ITfThreadMgr* tm,TfClientId id) override {
        if(manager) return E_UNEXPECTED;
        // Do not introduce an input route from an ordinary process into an elevated one.
        HANDLE token=nullptr; DWORD needed=0; BYTE integrity[256]{};
        if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token)) return E_ACCESSDENIED;
        bool elevated=!GetTokenInformation(token,TokenIntegrityLevel,integrity,sizeof(integrity),&needed);
        if(!elevated) { PSID sid=reinterpret_cast<TOKEN_MANDATORY_LABEL*>(integrity)->Label.Sid; elevated=*GetSidSubAuthority(sid,*GetSidSubAuthorityCount(sid)-1)>=SECURITY_MANDATORY_HIGH_RID; }
        CloseHandle(token); if(elevated) return E_ACCESSDENIED;
        manager=tm; manager->AddRef(); client=id;
        ITfSource* source=nullptr;
        if(SUCCEEDED(manager->QueryInterface(IID_ITfSource,reinterpret_cast<void**>(&source)))) {
            source->AdviseSink(IID_ITfThreadMgrEventSink,static_cast<ITfThreadMgrEventSink*>(this),&sinkCookie); Drop(source);
        }
        WNDCLASSW wc{}; wc.hInstance=module; wc.lpfnWndProc=WindowProc; wc.lpszClassName=L"XiaomangVoiceInput.Poll.v1";
        RegisterClassW(&wc);
        pollWindow=CreateWindowExW(0,wc.lpszClassName,L"",0,0,0,0,0,HWND_MESSAGE,nullptr,module,this);
        if(!pollWindow || !SetTimer(pollWindow,1,35,nullptr)) { Deactivate(); return E_FAIL; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Deactivate() override {
        if(manager && sinkCookie!=TF_INVALID_COOKIE) { ITfSource* source=nullptr; if(SUCCEEDED(manager->QueryInterface(IID_ITfSource,reinterpret_cast<void**>(&source)))) { source->UnadviseSink(sinkCookie); Drop(source); } sinkCookie=TF_INVALID_COOKIE; }
        if(pollWindow) { KillTimer(pollWindow,1); DestroyWindow(pollWindow); pollWindow=nullptr; }
        Drop(composition); Drop(activeContext); Drop(manager); client=TF_CLIENTID_NULL;
        if(pipe!=INVALID_HANDLE_VALUE) { CloseHandle(pipe); pipe=INVALID_HANDLE_VALUE; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OnInitDocumentMgr(ITfDocumentMgr*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnUninitDocumentMgr(ITfDocumentMgr* doc) override { ITfDocumentMgr* own=nullptr; if(activeContext && SUCCEEDED(activeContext->GetDocumentMgr(&own)) && own==doc) ScheduleEnd(); Drop(own); return S_OK; }
    HRESULT STDMETHODCALLTYPE OnSetFocus(ITfDocumentMgr*,ITfDocumentMgr*) override { if(activeContext && !ContextMatches(activeContext)) ScheduleEnd(); return S_OK; }
    HRESULT STDMETHODCALLTYPE OnPushContext(ITfContext*) override { if(activeContext && !ContextMatches(activeContext)) ScheduleEnd(); return S_OK; }
    HRESULT STDMETHODCALLTYPE OnPopContext(ITfContext* c) override { if(c==activeContext) ScheduleEnd(); return S_OK; }
    HRESULT STDMETHODCALLTYPE OnCompositionTerminated(TfEditCookie,ITfComposition* c) override {
        if(c==composition) { invalid=true; Drop(composition); Drop(activeContext); }
        return S_OK;
    }
    HRESULT Apply(ITfContext* c,const Command& command,TfEditCookie cookie) {
        pending=false;
        if(command.action==5) { if(command.session==activeSession && c==activeContext) EndOwned(cookie); return S_OK; }
        // Recheck at edit execution: an async TSF lock may arrive after timeout/focus change.
        if(pipe==INVALID_HANDLE_VALUE || command.expires<GetTickCount64() || !Foreground(command) || !ContextMatches(c)) { if(pipe!=INVALID_HANDLE_VALUE) WriteAck(pipe,command,3,last.size()); return S_OK; }
        if(command.action==1) {
            if(composition && activeContext!=c) { ScheduleEnd(); WriteAck(pipe,command,3,last.size()); return S_OK; }
            if(composition) EndOwned(cookie);
            activeSession=0; invalid=false; last.clear();
            TF_STATUS status{}; TF_SELECTION selection{}; ULONG fetched=0; BOOL empty=FALSE;
            HRESULT h=c->GetStatus(&status);
            if(FAILED(h) || (status.dwDynamicFlags & TF_SD_READONLY) || Disabled(c) || LegacyEditDenied() ||
               FAILED(c->GetSelection(cookie,TF_DEFAULT_SELECTION,1,&selection,&fetched)) || fetched!=1 || !selection.range) {
                Drop(selection.range); WriteAck(pipe,command,2,0); return S_OK;
            }
            selection.range->IsEmpty(cookie,&empty);
            if(!empty || PrivateScope(c,cookie,selection.range)) { Drop(selection.range); WriteAck(pipe,command,2,0); return S_OK; }
            ITfContextComposition* cc=nullptr;
            h=c->QueryInterface(IID_ITfContextComposition,reinterpret_cast<void**>(&cc));
            if(SUCCEEDED(h)) h=cc->StartComposition(cookie,selection.range,this,&composition);
            Drop(cc); Drop(selection.range);
            if(FAILED(h) || !composition) { WriteAck(pipe,command,4,0); return S_OK; }
            activeSession=command.session; activeContext=c; c->AddRef();
            WriteAck(pipe,command,0,0); return S_OK;
        }
        if(command.session!=activeSession || c!=activeContext || invalid || !composition) { WriteAck(pipe,command,3,last.size()); return S_OK; }
        ITfRange* range=nullptr; TF_SELECTION selection{}; ULONG fetched=0;
        HRESULT h=composition->GetRange(&range); LONG compare=1; BOOL empty=FALSE;
        if(SUCCEEDED(h)) h=c->GetSelection(cookie,TF_DEFAULT_SELECTION,1,&selection,&fetched);
        if(SUCCEEDED(h) && fetched==1 && selection.range) {
            selection.range->IsEmpty(cookie,&empty);
            if(empty) h=selection.range->CompareStart(cookie,range,TF_ANCHOR_END,&compare);
        }
        Drop(selection.range);
        WCHAR existing[Capacity]{}; ULONG read=0;
        if(SUCCEEDED(h)) h=range->GetText(cookie,0,existing,Capacity,&read);
        if(FAILED(h) || !empty || compare!=0 || last!=std::wstring(existing,read)) { Drop(range); invalid=true; EndOwned(cookie); WriteAck(pipe,command,3,last.size()); return S_OK; }
        if(command.action==2 || command.action==3 || command.action==4) {
            DWORD length=command.action==4 ? 0 : command.length;
            h=range->SetText(cookie,0,command.text,length);
            if(SUCCEEDED(h)) {
                last.assign(command.text,length);
                range->Collapse(cookie,TF_ANCHOR_END);
                TF_SELECTION sel{range,{TF_AE_NONE,FALSE}};
                h=c->SetSelection(cookie,1,&sel);
            }
            if(SUCCEEDED(h) && command.action!=2) {
                h=EndOwned(cookie);
            }
        } else h=E_INVALIDARG;
        Drop(range); WriteAck(pipe,command,SUCCEEDED(h)?0:4,last.size()); return S_OK;
    }
};

Edit::Edit(Service* s,ITfContext* c,const Command& cmd):service(s),context(c),command(cmd) { service->AddRef(); context->AddRef(); InterlockedIncrement(&objects); }
Edit::~Edit() { service->Release(); context->Release(); InterlockedDecrement(&objects); }
HRESULT Edit::QueryInterface(REFIID id,void** out) { if(!out) return E_POINTER; *out=nullptr; if(id!=IID_IUnknown && id!=IID_ITfEditSession) return E_NOINTERFACE; *out=static_cast<ITfEditSession*>(this); AddRef(); return S_OK; }
HRESULT Edit::DoEditSession(TfEditCookie cookie) { return service->Apply(context,command,cookie); }

class Factory final : public IClassFactory {
    LONG refs=1;
public:
    Factory() { InterlockedIncrement(&objects); }
    ~Factory() { InterlockedDecrement(&objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out) override { if(!out) return E_POINTER; *out=nullptr; if(id!=IID_IUnknown && id!=IID_IClassFactory) return E_NOINTERFACE; *out=this; AddRef(); return S_OK; }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { ULONG r=InterlockedDecrement(&refs); if(!r) delete this; return r; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,REFIID id,void** out) override { if(outer) return CLASS_E_NOAGGREGATION; Service* s=new(std::nothrow) Service(); if(!s) return E_OUTOFMEMORY; HRESULT h=s->QueryInterface(id,out); s->Release(); return h; }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { InterlockedExchangeAdd(&objects,lock?1:-1); return S_OK; }
};
extern "C" HRESULT WINAPI DllGetClassObject(REFCLSID id,REFIID iid,void** out) { if(id!=ServiceId) return CLASS_E_CLASSNOTAVAILABLE; auto f=new(std::nothrow) Factory(); if(!f) return E_OUTOFMEMORY; HRESULT h=f->QueryInterface(iid,out); f->Release(); return h; }
extern "C" HRESULT WINAPI DllCanUnloadNow() { return objects==0?S_OK:S_FALSE; }
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID) { if(reason==DLL_PROCESS_ATTACH) { module=instance; DisableThreadLibraryCalls(instance); } return TRUE; }

extern "C" __declspec(dllexport) HRESULT WINAPI VoiceRegister(BOOL install) {
    WCHAR guid[40],path[32768]; StringFromGUID2(ServiceId,guid,40); GetModuleFileNameW(module,path,32768);
    std::wstring key=L"Software\\Classes\\CLSID\\"; key+=guid;
    HRESULT h=S_OK; HKEY registry=nullptr;
    if(install) {
        LONG r=RegCreateKeyExW(HKEY_LOCAL_MACHINE,(key+L"\\InprocServer32").c_str(),0,nullptr,0,KEY_WRITE,nullptr,&registry,nullptr);
        if(r!=ERROR_SUCCESS) return HRESULT_FROM_WIN32(r);
        r=RegSetValueExW(registry,nullptr,0,REG_SZ,reinterpret_cast<BYTE*>(path),(wcslen(path)+1)*sizeof(WCHAR));
        if(r==ERROR_SUCCESS) r=RegSetValueExW(registry,L"ThreadingModel",0,REG_SZ,reinterpret_cast<const BYTE*>(L"Apartment"),sizeof(L"Apartment"));
        RegCloseKey(registry); if(r!=ERROR_SUCCESS) return HRESULT_FROM_WIN32(r);
    }
    ITfInputProcessorProfiles* profiles=nullptr; ITfCategoryMgr* categories=nullptr;
    h=CoCreateInstance(CLSID_TF_InputProcessorProfiles,nullptr,CLSCTX_INPROC_SERVER,IID_ITfInputProcessorProfiles,reinterpret_cast<void**>(&profiles));
    if(SUCCEEDED(h)) h=CoCreateInstance(CLSID_TF_CategoryMgr,nullptr,CLSCTX_INPROC_SERVER,IID_ITfCategoryMgr,reinterpret_cast<void**>(&categories));
    if(SUCCEEDED(h) && install) {
        h=profiles->Register(ServiceId);
        LANGID* languages=nullptr; ULONG count=0;
        if(SUCCEEDED(h)) h=profiles->GetLanguageList(&languages,&count);
        for(ULONG i=0;SUCCEEDED(h) && i<count;i++) h=profiles->AddLanguageProfile(ServiceId,languages[i],ProfileId,L"小蟒语音输入",6,path,wcslen(path),0);
        CoTaskMemFree(languages);
        if(SUCCEEDED(h)) h=categories->RegisterCategory(ServiceId,GUID_TFCAT_TIP_SPEECH,ServiceId);
    } else if(SUCCEEDED(h)) {
        categories->UnregisterCategory(ServiceId,GUID_TFCAT_TIP_SPEECH,ServiceId);
        h=profiles->Unregister(ServiceId);
        RegDeleteTreeW(HKEY_LOCAL_MACHINE,key.c_str());
    }
    Drop(categories); Drop(profiles); return h;
}
extern "C" __declspec(dllexport) HRESULT WINAPI VoiceActivate() {
    ITfInputProcessorProfileMgr* p=nullptr;
    HRESULT h=CoCreateInstance(CLSID_TF_InputProcessorProfiles,nullptr,CLSCTX_INPROC_SERVER,IID_ITfInputProcessorProfileMgr,reinterpret_cast<void**>(&p));
    DWORD thread=GetWindowThreadProcessId(GetForegroundWindow(),nullptr); LANGID language=LOWORD(reinterpret_cast<ULONG_PTR>(GetKeyboardLayout(thread)));
    if(SUCCEEDED(h)) h=p->ActivateProfile(TF_PROFILETYPE_INPUTPROCESSOR,language,ServiceId,ProfileId,nullptr,TF_IPPMF_FORSESSION|TF_IPPMF_ENABLEPROFILE);
    Drop(p); return h;
}
extern "C" __declspec(dllexport) HRESULT WINAPI VoiceOpenChannel() {
    if(channel) return S_FALSE;
    channel=new(std::nothrow) Channel(); if(!channel) return E_OUTOFMEMORY;
    brokerStop=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    if(!brokerStop) { DWORD error=GetLastError(); delete channel; channel=nullptr; return HRESULT_FROM_WIN32(error); }
    brokerThread=CreateThread(nullptr,0,AcceptConnections,nullptr,0,nullptr);
    if(!brokerThread) { DWORD error=GetLastError(); CloseHandle(brokerStop); brokerStop=nullptr; delete channel; channel=nullptr; return HRESULT_FROM_WIN32(error); }
    return S_OK;
}
extern "C" __declspec(dllexport) void WINAPI VoiceCloseChannel() {
    if(brokerStop) SetEvent(brokerStop);
    if(brokerThread) { WaitForSingleObject(brokerThread,INFINITE); CloseHandle(brokerThread); brokerThread=nullptr; }
    for(HANDLE connection : connections) { WaitForSingleObject(connection,INFINITE); CloseHandle(connection); } connections.clear();
    if(brokerStop) { CloseHandle(brokerStop); brokerStop=nullptr; }
    AcquireSRWLockExclusive(&channelLock); delete channel; channel=nullptr; ReleaseSRWLockExclusive(&channelLock);
}
extern "C" __declspec(dllexport) HRESULT WINAPI VoiceCommand(DWORD sequence,DWORD action,ULONGLONG session,ULONGLONG window,const WCHAR* text,DWORD length) {
    if(!channel || !text || length>=Capacity || action<1 || action>4) return E_INVALIDARG;
    DWORD pid=FocusProcess(reinterpret_cast<HWND>(window));
    AcquireSRWLockExclusive(&channelLock);
    channel->command={sequence,action,session,pid,length,window,GetTickCount64()+2000,{}};
    memcpy(channel->command.text,text,length*sizeof(WCHAR));
    ReleaseSRWLockExclusive(&channelLock); return S_OK;
}
extern "C" __declspec(dllexport) BOOL WINAPI VoiceAck(DWORD sequence,ULONGLONG session,DWORD* status,DWORD* length,DWORD* pid) {
    if(!channel) return FALSE;
    AcquireSRWLockShared(&channelLock); Ack a=channel->ack; ReleaseSRWLockShared(&channelLock);
    if(a.sequence!=sequence || a.session!=session) return FALSE;
    *status=a.status; *length=a.length; *pid=a.pid; return TRUE;
}
#ifdef VOICE_INPUT_TEST
static ITfThreadMgr* testManager;
static Service* testService;
extern "C" __declspec(dllexport) HRESULT WINAPI VoiceTestStart() {
    HRESULT h=CoCreateInstance(CLSID_TF_ThreadMgr,nullptr,CLSCTX_INPROC_SERVER,IID_ITfThreadMgr,reinterpret_cast<void**>(&testManager));
    TfClientId client=0;
    if(SUCCEEDED(h)) h=testManager->Activate(&client);
    if(SUCCEEDED(h)) { testService=new Service(); h=testService->Activate(testManager,client); }
    return h;
}
extern "C" __declspec(dllexport) void WINAPI VoiceTestStop() {
    if(testService) { testService->Deactivate(); Drop(testService); }
    if(testManager) { testManager->Deactivate(); Drop(testManager); }
}
#endif
