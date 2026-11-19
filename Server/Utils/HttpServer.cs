using System.Net;
using Server.MirEnvir;
using S = ServerPackets;

namespace Server.Library.Utils
{
    class HttpServer : HttpService
    {
        Thread _thread;
        CancellationTokenSource tokenSource = new();

        public HttpServer()
        {
            Host = Settings.HTTPIPAddress;
        }

        public void Start()
        {
            // 20261119 审计修复：后台监听线程，避免关服时线程阻止进程退出
            _thread = new Thread(Listen) { IsBackground = true };
            _thread.Start(tokenSource.Token);
        }

        public new void Stop()
        {
            base.Stop();
            
            tokenSource.Cancel();
            // 20261119 审计修复：原 Sleep(1000) 不等待线程真正退出→Stop 返回后监听线程可能仍处理请求；改 Join 等待
            try { _thread?.Join(2000); } catch { }
            tokenSource.Dispose();

        }


        public override void OnGetRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            var url = request.Url.PathAndQuery;
            if (url.Contains("?"))
            {
                url = url.Substring(0, url.IndexOf("?", StringComparison.Ordinal));
            }
            // 20261119 审计修复：ToLower 原在 if 分支内，无查询串的 URL 不转小写→大小写归一化不一致；移到分支外
            url = url.ToLowerInvariant();
            try
            {
                switch (url)
                {
                    case "/":
                        WriteResponse(response, GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.GameName));
                        break;
                    case "/newaccount":
                        var id = request.QueryString["id"];
                        var psd = request.QueryString["psd"];
                        var email = request.QueryString["email"];
                        var name = request.QueryString["name"];
                        var question = request.QueryString["question"];
                        var answer = request.QueryString["answer"];
                        var ip = request.QueryString["ip"];
                        var p = new ClientPackets.NewAccount();
                        p.AccountID = id;
                        p.Password = psd;
                        p.EMailAddress = email;
                        p.UserName = name;
                        p.SecretQuestion = question;
                        p.SecretAnswer = answer;
                        var result = Envir.Main.HTTPNewAccount(p, ip);
                        WriteResponse(response, result.ToString());
                        break;                               
                    case "/addnamelist":
                        id = request.QueryString["id"];
                        var fileName = request.QueryString["fileName"];
                        AddNameList(id, fileName);
                        WriteResponse(response, "true");
                        break;              
                    case "/broadcast":
                        var msg = request.QueryString["msg"];
                        // 20261119 审计修复：缺 msg 参数时 msg 为 null，原 msg.Length 抛 NRE 回显堆栈；补空判定
                        if (string.IsNullOrEmpty(msg) || msg.Trim().Length < 5)
                        {
                            WriteResponse(response, "short");
                            return;
                        }
                        Envir.Main.Broadcast(new S.Chat
                        {
                            Message = msg.Trim(),
                            Type = ChatType.Shout2
                        });
                        WriteResponse(response, "true");
                        break;
                    default:
                        WriteResponse(response, "error");
                        break;
                }
            }
            catch (Exception error)
            {
                // 20261119 审计修复：异常详情不回显给客户端（信息泄露），固定文案响应，详情写日志
                MessageQueue.Instance.Enqueue("HttpServer 请求处理异常: " + error);
                WriteResponse(response, "request error");
            }
        }      

        void AddNameList(string playerName, string fileName)
        {
            // 20261119 审计修复P0：fileName 原直接 Path.Combine 查询参数，绝对路径会丢弃 base、..\ 可逃逸，
            // 导致任意文件创建/追加写。取纯文件名 + 规范路径校验必须落在 NameListPath 根目录内 + id 白名单。
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(playerName))
                return;

            // 仅允许纯文件名（去掉任何目录成分）
            fileName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(fileName))
                return;

            var root = Path.GetFullPath(Settings.NameListPath);
            var fullPath = Path.GetFullPath(Path.Combine(root, fileName));
            // 必须位于 root 内（防止 ..\ 逃逸与绝对路径）
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return;

            // id（玩家名）白名单：禁止路径分隔符与控制字符，限长
            if (playerName.Length > 30 || playerName.IndexOfAny(new[] { '/', '\\', '\r', '\n' }) >= 0)
                return;

            Directory.CreateDirectory(root);
            if (!File.Exists(fullPath))
                File.Create(fullPath).Close();

            if (File.ReadAllLines(fullPath).All(t => playerName != t))
            {
                using (var line = File.AppendText(fullPath))
                {
                    line.WriteLine(playerName);
                }
            }
        }    

        public override void OnPostRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            // 20261119 审计修复：原只 Console.WriteLine 不关闭 response→客户端连接悬挂；写空响应关闭输出流
            Console.WriteLine("POST request: {0}", request.Url);
            WriteResponse(response, string.Empty);
        }
    }

}
