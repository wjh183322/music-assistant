using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MusicAssistant {
 public static class Recycle {
  public static void Send(string path) {
   if(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).DriveType!=DriveType.Fixed)throw new IOException("该位置未验证回收站支持；原文件保留");
   Exception error=null;var thread=new Thread(()=>{try{SendSta(path);}catch(Exception ex){error=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)throw error;
  }
  static void SendSta(string path) {
   IShellItem item=null;IFileOperation operation=null;
   try {Guid iid=typeof(IShellItem).GUID;Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out item));
    operation=(IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09")));
    // RECYCLEONDELETE explicitly requests recycling rather than permanent deletion (Windows 8+).
    operation.SetOperationFlags(0x00080000|0x20000000|0x00100000|0x00000400|0x00000004|0x00000010);
    operation.DeleteItem(item,IntPtr.Zero);operation.PerformOperations();bool aborted;operation.GetAnyOperationsAborted(out aborted);
    if(aborted||File.Exists(path))throw new IOException("回收操作未完成；未执行永久删除");
   } finally {if(item!=null)Marshal.FinalReleaseComObject(item);if(operation!=null)Marshal.FinalReleaseComObject(operation);}
  }
  [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)] static extern int SHCreateItemFromParsingName(string path,IntPtr bind,ref Guid iid,out IShellItem item);
  [ComImport,Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IShellItem {
   void BindToHandler(IntPtr pbc,ref Guid bhid,ref Guid riid,out IntPtr ppv);void GetParent(out IShellItem parent);void GetDisplayName(uint name,out IntPtr result);void GetAttributes(uint mask,out uint attributes);void Compare(IShellItem other,uint hint,out int order);
  }
  [ComImport,Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IFileOperation {
   void Advise(IntPtr sink,out uint cookie);void Unadvise(uint cookie);void SetOperationFlags(uint flags);void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)]string message);void SetProgressDialog(IntPtr dialog);void SetProperties(IntPtr props);void SetOwnerWindow(IntPtr owner);
   void ApplyPropertiesToItem(IShellItem item);void ApplyPropertiesToItems(IntPtr items);
   void RenameItem(IShellItem item,[MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr sink);void RenameItems(IntPtr items,[MarshalAs(UnmanagedType.LPWStr)]string name);
   void MoveItem(IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr sink);void MoveItems(IntPtr items,IShellItem destination);
   void CopyItem(IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr sink);void CopyItems(IntPtr items,IShellItem destination);
   void DeleteItem(IShellItem item,IntPtr sink);void DeleteItems(IntPtr items);
   void NewItem(IShellItem destination,uint attributes,[MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string template,IntPtr sink);
   void PerformOperations();void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)]out bool aborted);
  }
 }
}
