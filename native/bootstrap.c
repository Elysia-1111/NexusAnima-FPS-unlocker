// Position-independent startup helper. All function addresses and strings are
// supplied in the context; the compiled text must have no relocations/imports.
typedef unsigned short U16;
typedef unsigned long U32;
typedef long Status;
typedef struct NativeString { U16 length, maximum; void *buffer; } NativeString;
typedef Status (*LoadDll)(void *,void *,NativeString *,void **);
typedef Status (*GetExport)(void *,NativeString *,U32,void **);
typedef U32 (*Schedule)(void *);
typedef struct Context {
    LoadDll load;
    GetExport resolve;
    void *module;
    Schedule schedule;
    NativeString path;
    NativeString symbol;
    Status status;
} Context;

U32 NexusBootstrap(Context *context)
{
    context->status=context->load(0,0,&context->path,&context->module);
    if(context->status<0)return (U32)context->status;
    context->status=context->resolve(context->module,&context->symbol,0,(void**)&context->schedule);
    if(context->status<0)return (U32)context->status;
    context->status=(Status)context->schedule(0);
    return (U32)context->status;
}
