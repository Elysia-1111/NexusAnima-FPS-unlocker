#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <wchar.h>
#include <string.h>

#define CHANNEL_MAGIC 0x5046414e
#define CHANNEL_VERSION 2
#define FPS_RVA 0x2a1c15c
#ifndef PROCESS_QUERY_LIMITED_INFORMATION
#define PROCESS_QUERY_LIMITED_INFORMATION 0x1000
#endif

typedef struct Shared {
    LONG magic, version, target, command;
    LONG owner_pid, owner_tick, state, error;
    LONG original, current, writes, worker_tick;
    LONG written, restore_result, game_pid, reserved;
} Shared;

static HMODULE own_module;
static volatile LONG started;

static const BYTE signature0[]={0x8b,0x05,0x46,0x06,0x81,0x02,0xc3};
static const BYTE signature1[]={0x89,0x0d,0x36,0x06,0x81,0x02,0xc3};
static const BYTE signature2[]={0x8b,0x05,0xc3,0xb3,0xf9,0x00,0xf3,0x0f,0x2a,0xc8,0x0f,0x57,0xd2,0x0f,0x28,0xc1,0xf3,0x0f,0xc2,0xc2,0x06,0x0f,0x54,0xc8,0xf3,0x0f,0x10,0x15,0x6d,0xc2,0xc4,0x00};
static const BYTE signature3[]={0xe8,0xa7,0xfb,0xff,0xff,0xf3,0x0f,0x5a,0xc0,0xf2,0x44,0x0f,0x10,0x05,0x32,0x56,0xc4,0x00,0xf2,0x44,0x0f,0x5e,0xc0};

static LONG read_atomic(volatile LONG *value) { return InterlockedCompareExchange(value,0,0); }
static int plausible(LONG value) { return value==-1 || (value>=0 && value<=240); }
static int writable(volatile LONG *address)
{
    MEMORY_BASIC_INFORMATION page;
    if(!VirtualQuery((LPCVOID)address,&page,sizeof(page)))return 0;
    DWORD protection=page.Protect & 0xff;
    return page.State==MEM_COMMIT && !(page.Protect&PAGE_GUARD) &&
        (protection==PAGE_READWRITE || protection==PAGE_WRITECOPY) &&
        (uintptr_t)address+sizeof(LONG)<=(uintptr_t)page.BaseAddress+page.RegionSize;
}
static int valid_engine(BYTE *engine)
{
    return engine && !memcmp(engine+0x20bb10,signature0,sizeof(signature0)) &&
        !memcmp(engine+0x20bb20,signature1,sizeof(signature1)) &&
        !memcmp(engine+0x1a80d93,signature2,sizeof(signature2)) &&
        !memcmp(engine+0x1a81114,signature3,sizeof(signature3));
}

// This export is invoked only after LoadLibrary has returned. DllMain does not
// create threads, perform IPC, or hold the loader lock while the worker runs.
__declspec(dllexport) DWORD WINAPI NexusStart(LPVOID unused)
{
    (void)unused;
    if(InterlockedCompareExchange(&started,1,0))return ERROR_ALREADY_EXISTS;
    WCHAR image[MAX_PATH],name[96];
    DWORD length=GetModuleFileNameW(NULL,image,MAX_PATH);
    if(!length || length>=MAX_PATH){ExitThread(ERROR_BAD_EXE_FORMAT);return ERROR_BAD_EXE_FORMAT;}
    WCHAR *base=image,*scan;
    for(scan=image;*scan;scan++)if(*scan==L'\\')base=scan+1;
    if(lstrcmpiW(base,L"NexusAnima.exe")){ExitThread(ERROR_BAD_EXE_FORMAT);return ERROR_BAD_EXE_FORMAT;}
    lstrcpyW(name,L"Local\\NexusAnimaFPS_v2_");
    WCHAR digits[12];int digits_count=0,name_length=lstrlenW(name);DWORD pid=GetCurrentProcessId();
    do{digits[digits_count++]=(WCHAR)(L'0'+pid%10);pid/=10;}while(pid);
    while(digits_count)name[name_length++]=digits[--digits_count];name[name_length]=0;
    HANDLE mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name);
    if(!mapping)return GetLastError();
    Shared *shared=(Shared*)MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Shared));
    if(!shared){DWORD error=GetLastError();CloseHandle(mapping);return error;}
    HANDLE owner=NULL;
    volatile LONG *fps=NULL;
    LONG original=0,target=0,written=0,error=0,restore=3;
    BYTE *engine=NULL;
    if(shared->magic!=CHANNEL_MAGIC || shared->version!=CHANNEL_VERSION || shared->game_pid!=(LONG)GetCurrentProcessId()) {error=1;goto done;}
    InterlockedExchange(&shared->reserved,1);
    DWORD waiting_since=GetTickCount();
    for(;;)
    {
        DWORD now=GetTickCount();
        InterlockedExchange(&shared->worker_tick,(LONG)now);
        if(read_atomic(&shared->command)==2 || (DWORD)(now-(DWORD)read_atomic(&shared->owner_tick))>10000)goto done;
        engine=(BYTE*)GetModuleHandleW(L"UnityPlayer.dll");
        if(engine)break;
        if((DWORD)(now-waiting_since)>90000){error=9;goto done;}
        Sleep(50);
    }
    if(!valid_engine(engine)){error=2;goto done;}
    fps=(volatile LONG*)(engine+FPS_RVA);
    if(!writable(fps) || !plausible(read_atomic(fps))){error=3;goto done;}
    original=read_atomic(fps);
    InterlockedExchange(&shared->original,original);
    InterlockedExchange(&shared->current,original);
    owner=OpenProcess(SYNCHRONIZE|PROCESS_QUERY_LIMITED_INFORMATION,FALSE,(DWORD)shared->owner_pid);
    InterlockedExchange(&shared->worker_tick,(LONG)GetTickCount());
    InterlockedExchange(&shared->state,1);
    for(;;)
    {
        DWORD now=GetTickCount();
        InterlockedExchange(&shared->worker_tick,(LONG)now);
        LONG command=read_atomic(&shared->command);
        if(command==2 || (owner && WaitForSingleObject(owner,0)==WAIT_OBJECT_0) || (DWORD)(now-(DWORD)read_atomic(&shared->owner_tick))>10000)break;
        if(command==0){Sleep(50);continue;}
        if(command!=1){error=4;break;}
        LONG requested=read_atomic(&shared->target);
        if(requested<1 || requested>240){error=5;break;}
        if(target && requested!=target){error=8;break;}
        target=requested;
        if(!writable(fps)){error=6;break;}
        LONG current=read_atomic(fps);
        if(!plausible(current)){error=7;break;}
        if(current!=target)
        {
            // Preserve the latest game-selected value, and do not overwrite a
            // value that the game changes between observation and replacement.
            original=current;
            InterlockedExchange(&shared->original,original);
            if(InterlockedCompareExchange(fps,target,current)==current)
            {
                written=1;InterlockedExchange(&shared->written,1);
                InterlockedIncrement(&shared->writes);
            }
        }
        InterlockedExchange(&shared->current,read_atomic(fps));
        InterlockedExchange(&shared->state,2);
        Sleep(100);
    }
done:
    if(written && fps)
    {
        restore=4;
        if(writable(fps) && valid_engine(engine))
        {
            LONG current=read_atomic(fps);
            if(current==target)
                restore=InterlockedCompareExchange(fps,original,target)==target ? 1 : 2;
            else if(plausible(current))restore=2;
            InterlockedExchange(&shared->current,read_atomic(fps));
        }
    }
    InterlockedExchange(&shared->restore_result,restore);
    InterlockedExchange(&shared->error,error);
    InterlockedExchange(&shared->worker_tick,(LONG)GetTickCount());
    InterlockedExchange(&shared->state,error ? 4 : 3);
    if(owner)CloseHandle(owner);
    UnmapViewOfFile(shared);CloseHandle(mapping);
    // The module stays mapped until normal game exit. This avoids unloading
    // while its scheduling export is still returning on a startup thread.
    ExitThread((DWORD)error);
    return 0;
}

__declspec(dllexport) DWORD WINAPI NexusSchedule(LPVOID unused)
{
    HANDLE thread=CreateThread(NULL,0,NexusStart,NULL,0,NULL);
    if(!thread)return GetLastError();
    CloseHandle(thread);return 0;
}

BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID reserved)
{
    (void)reserved;
    if(reason==DLL_PROCESS_ATTACH){own_module=module;DisableThreadLibraryCalls(module);}
    return TRUE;
}
