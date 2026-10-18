using System.Runtime.InteropServices;

namespace Avalonia.React;

// Minimal ICoreWebView2_3 declaration: placeholders preserve the native vtable slots, only the
// SetVirtualHostNameToFolderMapping method is called.
[ComImport]
[Guid("A0D6DF20-3B92-416D-AA0C-437A9C727857")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICoreWebView2_3
{
    void Reserved01();
    void Reserved02();
    void Reserved03();
    void Reserved04();
    void Reserved05();
    void Reserved06();
    void Reserved07();
    void Reserved08();
    void Reserved09();
    void Reserved10();
    void Reserved11();
    void Reserved12();
    void Reserved13();
    void Reserved14();
    void Reserved15();
    void Reserved16();
    void Reserved17();
    void Reserved18();
    void Reserved19();
    void Reserved20();
    void Reserved21();
    void Reserved22();
    void Reserved23();
    void Reserved24();
    void Reserved25();
    void Reserved26();
    void Reserved27();
    void Reserved28();
    void Reserved29();
    void Reserved30();
    void Reserved31();
    void Reserved32();
    void Reserved33();
    void Reserved34();
    void Reserved35();
    void Reserved36();
    void Reserved37();
    void Reserved38();
    void Reserved39();
    void Reserved40();
    void Reserved41();
    void Reserved42();
    void Reserved43();
    void Reserved44();
    void Reserved45();
    void Reserved46();
    void Reserved47();
    void Reserved48();
    void Reserved49();
    void Reserved50();
    void Reserved51();
    void Reserved52();
    void Reserved53();
    void Reserved54();
    void Reserved55();
    void Reserved56();
    void Reserved57();
    void Reserved58();
    void Reserved59();
    void Reserved60();
    void Reserved61();
    void Reserved62();
    void Reserved63();
    void Reserved64();
    void Reserved65();
    void Reserved66();
    void Reserved67();
    void Reserved68();

    [PreserveSig]
    int SetVirtualHostNameToFolderMapping(
        [MarshalAs(UnmanagedType.LPWStr)] string hostName,
        [MarshalAs(UnmanagedType.LPWStr)] string folderPath,
        int accessKind);
}
