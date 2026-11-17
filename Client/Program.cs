using System.Diagnostics;
using System.Threading; //20261117 审计修复P2：补丁器忙等改 Thread.Sleep(50) 需要
using Launcher;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Client.Resolution;

namespace Client
{
    internal static class Program
    {
        public static CMain Form;
        public static AMain PForm;

        public static bool Restart;
        public static bool Launch;

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                foreach (var arg in args)
                {
                    if (arg.ToLower() == "-tc") Settings.UseTestConfig = true;
                }
            }

            #if DEBUG
                Settings.UseTestConfig = true;
            #endif

            try
            {
                if (UpdatePatcher()) return;

                if (RuntimePolicyHelper.LegacyV2RuntimeEnabledSuccessfully == true) { }

                Packet.IsServer = false;
                Settings.Load();

                // 20261116 低分辨率全屏拉伸：捕获物理屏幕尺寸（捕获失败回退逻辑分辨率，避免零尺寸）
                try
                {
                    var bounds = Screen.PrimaryScreen.Bounds;
                    Settings.NativeWidth = bounds.Width > 0 ? bounds.Width : Settings.ScreenWidth;
                    Settings.NativeHeight = bounds.Height > 0 ? bounds.Height : Settings.ScreenHeight;
                }
                catch
                {
                    Settings.NativeWidth = Settings.ScreenWidth;
                    Settings.NativeHeight = Settings.ScreenHeight;
                }

                // 20261117 审计修复：本客户端为固定逻辑分辨率 D3D 渲染（后缓冲恒 ScreenWidth×ScreenHeight），
                // 依赖 Windows 位图缩放对齐鼠标；.NET 8 WinForms 默认 SystemAware，缩放≠100% 时
                // PointToClient 逻辑坐标与物理后缓冲不一致致 UI 鼠标点不上/错位。DpiUnaware 让进程按虚拟
                // 96 DPI 运行、由 Windows 对整窗位图缩放，ClientSize 与 D3D 后缓冲恢复 1:1，鼠标命中对齐。
                // 必须在 EnableVisualStyles 及任何窗体创建之前调用，否则抛 InvalidOperationException。
                Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                CheckResolutionSetting();

                Launch = false;
                if (Settings.P_Patcher)
                    Application.Run(PForm = new AMain());
                else
                    Launch = true;

                if (Launch)
                    Application.Run(Form = new CMain());

                Settings.Save();

                if (Restart)
                {
                    Application.Restart();
                }
            }
            catch (Exception ex)
            {
                CMain.SaveError(ex.ToString());
            }
        }

        private static bool UpdatePatcher()
        {
            try
            {
                const string fromName = @".\AutoPatcher.gz", toName = @".\AutoPatcher.exe";
                if (!File.Exists(fromName)) return false;

                Process[] processes = Process.GetProcessesByName("AutoPatcher");

                if (processes.Length > 0)
                {
                    string patcherPath = Application.StartupPath + @"\AutoPatcher.exe";

                    // 20261117 审计修复P2：进程可能已退出，MainModule/FileName 抛异常不应中断补丁更新；逐个 try 忽略
                    for (int i = 0; i < processes.Length; i++)
                    {
                        try
                        {
                            if (processes[i].MainModule.FileName == patcherPath)
                                processes[i].Kill();
                        }
                        catch { } //进程已退出或访问被拒绝，忽略
                    }

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    bool wait = true;
                    processes = Process.GetProcessesByName("AutoPatcher");

                    while (wait)
                    {
                        wait = false;
                        try
                        {
                            for (int i = 0; i < processes.Length; i++)
                                if (processes[i].MainModule.FileName == patcherPath)
                                {
                                    wait = true;
                                }
                        }
                        catch { wait = true; } //访问异常时保守继续等待

                        // 20261117 审计修复P2：忙等改 Thread.Sleep(50)，避免 3 秒等待期间 CPU 空转
                        if (stopwatch.ElapsedMilliseconds <= 3000) { Thread.Sleep(50); continue; }
                        MessageBox.Show(GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.FailedCloseAutoPatcherUpdate));
                        return true;
                    }
                }

                if (File.Exists(toName)) File.Delete(toName);
                File.Move(fromName, toName);
                Process.Start(toName, "Auto");

                return true;
            }
            catch (Exception ex)
            {
                CMain.SaveError(ex.ToString());
                
                throw;
            }
        }

        public static class RuntimePolicyHelper
        {
            public static bool LegacyV2RuntimeEnabledSuccessfully { get; private set; }

            static RuntimePolicyHelper()
            {
                //ICLRRuntimeInfo clrRuntimeInfo =
                //    (ICLRRuntimeInfo)RuntimeEnvironment.GetRuntimeInterfaceAsObject(
                //        Guid.Empty,
                //        typeof(ICLRRuntimeInfo).GUID);

                //try
                //{
                //    clrRuntimeInfo.BindAsLegacyV2Runtime();
                //    LegacyV2RuntimeEnabledSuccessfully = true;
                //}
                //catch (COMException)
                //{
                //    // This occurs with an HRESULT meaning 
                //    // "A different runtime was already bound to the legacy CLR version 2 activation policy."
                //    LegacyV2RuntimeEnabledSuccessfully = false;
                //}
            }

            [ComImport]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            [Guid("BD39D1D2-BA2F-486A-89B0-B4B0CB466891")]
            private interface ICLRRuntimeInfo
            {
                void xGetVersionString();
                void xGetRuntimeDirectory();
                void xIsLoaded();
                void xIsLoadable();
                void xLoadErrorString();
                void xLoadLibrary();
                void xGetProcAddress();
                void xGetInterface();
                void xSetDefaultStartupFlags();
                void xGetDefaultStartupFlags();

                [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
                void BindAsLegacyV2Runtime();
            }
        }

        public static void CheckResolutionSetting()
        {
            var parsedOK = DisplayResolutions.GetDisplayResolutions();
            if (!parsedOK)
            {
                MessageBox.Show(GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CouldNotGetDisplayResolutions), GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.GetDisplayResolutionIssue), MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(0);
            }

            if (!DisplayResolutions.IsSupported(Settings.Resolution))
            {
                MessageBox.Show(GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.ClientNotSupportSettingResolution), Settings.Resolution),
                    GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.InvalidClientResolution),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);

                Settings.Resolution = (int)eSupportedResolution.w1024h768;
                Settings.Save();
            }
        }

    }
}