using System.Net;
using System.Text;

namespace Server.Library.Utils
{
    internal abstract class HttpService
    {
        protected string Host;
        private HttpListener _listener;
        private bool _isActive = true;
        private DateTime _lastErrorLogTime = DateTime.MinValue; // 20261116 异常日志节流

        protected HttpService()
        {
        }

        public void Listen(object obj)
        {
            CancellationToken token = (CancellationToken)obj;

            // 20261119 审计修复：IsSupported 与 new HttpListener() 原在 try 外，后台线程抛异常会终止进程；
            // 统一纳入 try，失败时记日志并退出监听线程
            string[] prefixes = { Host };

            try
            {
                if (!HttpListener.IsSupported)
                {
                    MessageQueue.Instance.Enqueue("HttpListener 不受当前操作系统支持（需 Windows XP SP2 / Server 2003 及以上），HTTP 服务未启动");
                    return;
                }

                _listener = new HttpListener();
                foreach (var s in prefixes)
                {
                    _listener.Prefixes.Add(s);
                }
                _listener.Start();
                MessageQueue.Instance.Enqueue(GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.HttpServiceStarted));
            }
            catch (Exception err)
            {
                MessageQueue.Instance.Enqueue(GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.HttpServiceStartFailed) + err);
                return;
            }

            while (_isActive && !token.IsCancellationRequested)
            {
                try
                {
                    var context = _listener.GetContext();
                    var request = context.Request;
                    // 20261119 审计修复：原记录 RawUrl 含查询串（newaccount 的 psd/question/answer 明文）→只记路径
                    Console.WriteLine("{0} {1} HTTP/1.1", request.HttpMethod, request.Url.AbsolutePath);
                    Console.WriteLine("User-Agent: {0}", request.UserAgent);
                    Console.WriteLine("Accept-Encoding: {0}", request.Headers["Accept-Encoding"]);
                    Console.WriteLine("Connection: {0}", request.KeepAlive ? "Keep-Alive" : "close");
                    Console.WriteLine("Host: {0}", request.UserHostName);
                    var response = context.Response;
                    // 20261119 审计修复P1：原 RemoteEndPoint==null 时整体跳过校验=零认证 fail-open；改 fail-closed——
                    // 取不到对端 IP 或不在白名单一律拒绝
                    var clientIp = context.Request.RemoteEndPoint?.Address.ToString();
                    if (clientIp == null || clientIp != Settings.HTTPTrustedIPAddress)
                    {
                        WriteResponse(response, "notrusted:" + (clientIp ?? "unknown"));
                        continue;
                    }

                    if (request.HttpMethod == "GET")
                    {
                        OnGetRequest(request, response);
                    }
                    else
                    {
                        OnPostRequest(request, response);
                    }
                }
                // 20261116 审计修复：空 catch 吞掉所有请求处理异常无法感知故障；节流日志（每分钟最多 1 条）防日志刷屏
                catch (Exception ex)
                {
                    if ((DateTime.Now - _lastErrorLogTime).TotalMinutes >= 1)
                    {
                        _lastErrorLogTime = DateTime.Now;
                        MessageQueue.Instance.Enqueue("HttpService 请求处理异常(节流1分钟): " + ex.Message);
                    }
                }
            }
        }

        public void Stop()
        {
            _isActive = false;
            try
            {
                if (_listener != null)
                {
                    if (_listener.IsListening) _listener.Stop();
                    _listener.Close();
                }
            }
            catch { }
        }

        public abstract void OnGetRequest(HttpListenerRequest request, HttpListenerResponse response);
        public abstract void OnPostRequest(HttpListenerRequest request, HttpListenerResponse response);

        public void WriteResponse(HttpListenerResponse response, string responseString)
        {
            try
            {
                response.ContentLength64 = Encoding.UTF8.GetByteCount(responseString);
                response.ContentType = "text/html; charset=UTF-8";
            }
            finally
            {
                var output = response.OutputStream;
                var writer = new StreamWriter(output);
                writer.Write(responseString);
                writer.Close();
            }
        }
    }
}
