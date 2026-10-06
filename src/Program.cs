using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MusicAssistant {
 static class Program {
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
  [STAThread] static void Main(string[] args) {
   SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try {
    bool uiCheck=args.Length>0&&args[0]=="--ui-check";
    bool preview=args.Length>0&&(args[0]=="--preview"||uiCheck),check=args.Length>0&&args[0]=="--startup-check",startupPreview=args.Length>0&&args[0]=="--startup-preview";
    string previewRoot=preview?Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"preview-state-"+Path.GetFileNameWithoutExtension(args[1])):null;
    string instance=preview?previewRoot:Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    using(var mutex=new Mutex(false,"Local\\MusicAssistant_"+Files.HashText(instance).Substring(0,16))) {
     if(!mutex.WaitOne(0)){if(check){Environment.ExitCode=2;return;}MessageBox.Show("音乐助手已经运行。","音乐助手");return;}
     try {DataLocation location=preview?new DataLocation{Root=previewRoot,Notice="界面预览数据"}:DataDirectory.ResolveDefault();string root=location.Root;var store=new Store(root);if(check){Files.AtomicText(Path.GetFullPath(args[1]),Json.Write(new{Status="passed",DataRoot=root,HistoryCount=store.History.Count,IsFallback=location.IsFallback,Notice=location.Notice}));return;}using(var form=new MainForm(store,preview)) {
      form.SetDataLocationNotice(location.Notice);
      if(preview||startupPreview){form.Opacity=0;form.ShowInTaskbar=false;form.Show();if(args.Length>2)form.ShowPreviewPage(args[2]);if(args.Length>3&&args[3]=="small")form.Size=new Size(1060,740);Application.DoEvents();if(uiCheck){var checks=form.RunUiChecks();Files.AtomicText(Path.GetFullPath(args[1]),Json.Write(new{Status="passed",Count=checks.Count,Checks=checks}));}else using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,form.Width,form.Height));image.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);}form.SkipTaskSaveOnClose=true;form.Close();}
      else Application.Run(form);
     }}finally{mutex.ReleaseMutex();}
    }
   } catch(Exception ex) {if(args.Length>1&&(args[0]=="--preview"||args[0]=="--startup-check"||args[0]=="--startup-preview"||args[0]=="--ui-check")){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}else MessageBox.Show("无法启动："+ex.Message,"音乐助手",MessageBoxButtons.OK,MessageBoxIcon.Error);}
  }
 }
}
