using System;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // 无头自检：--smoke <服务端目录> → 加载 MirDB 并打印统计，验证数据链路
            if (args.Length >= 2 && args[0] == "--smoke")
            {
                Environment.CurrentDirectory = args[1];
                // 引擎 LoadDB 在缺库时会写一份空库，自检必须先确认目标库存在
                if (!File.Exists(Server.MirEnvir.Envir.DatabasePath))
                {
                    Console.WriteLine("SMOKE FAIL: 未找到 Server.MirDB");
                    return 1;
                }
                var envir = Server.MirEnvir.Envir.Edit;
                bool ok = envir.LoadDB();
                Console.WriteLine(ok
                    ? $"SMOKE OK: maps={envir.MapInfoList.Count} npcs={envir.NPCInfoList.Count} items={envir.ItemInfoList.Count} monsters={envir.MonsterInfoList.Count}"
                    : "SMOKE FAIL: LoadDB returned false");
                return ok ? 0 : 1;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new LauncherForm());
            return 0;
        }
    }
}
