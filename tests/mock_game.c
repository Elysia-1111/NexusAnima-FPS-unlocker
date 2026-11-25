#include <windows.h>
int WINAPI WinMain(HINSTANCE instance,HINSTANCE previous,LPSTR command,int show)
{
    if(!LoadLibraryW(L"UnityPlayer.dll"))return 1;
    Sleep(30000);
    return 0;
}
