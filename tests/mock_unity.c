#include <windows.h>
#include <string.h>
#include "mock_profile.h"
static BYTE arena[0x2a1e000];
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID reserved)
{
    if(reason==DLL_PROCESS_ATTACH){
        arena[0]=1;*(volatile LONG*)((BYTE*)module+0x2a1c15c)=60;
        memcpy((BYTE*)module+0x20bb10,signature0,sizeof(signature0));
        memcpy((BYTE*)module+0x20bb20,signature1,sizeof(signature1));
        memcpy((BYTE*)module+0x1a80d93,signature2,sizeof(signature2));
        memcpy((BYTE*)module+0x1a81114,signature3,sizeof(signature3));
    }
    return TRUE;
}
