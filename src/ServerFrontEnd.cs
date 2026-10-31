// Servidor de arquivos estaticos (frontend) que roda como servico do Windows.
// Uso:
//   ServerFrontEnd.exe            -> executa como servico (chamado pelo Windows)
//   ServerFrontEnd.exe --console  -> executa no terminal, para testes
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace ServerFrontEnd
{
    public class FrontEndService : ServiceBase
    {
        public const string Name = "ServerFrontEnd";

        private HttpListener listener;
        private Thread loopThread;
        private volatile bool running;
        private string root;
        private bool spaFallback;
        private static readonly object logLock = new object();

        private static readonly Dictionary<string, string> MimeTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" }, { ".htm", "text/html; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" }, { ".js", "application/javascript; charset=utf-8" },
            { ".mjs", "application/javascript; charset=utf-8" }, { ".json", "application/json; charset=utf-8" },
            { ".map", "application/json; charset=utf-8" }, { ".txt", "text/plain; charset=utf-8" },
            { ".xml", "application/xml" }, { ".svg", "image/svg+xml" }, { ".png", "image/png" },
            { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" }, { ".gif", "image/gif" },
            { ".webp", "image/webp" }, { ".ico", "image/x-icon" }, { ".avif", "image/avif" },
            { ".woff", "font/woff" }, { ".woff2", "font/woff2" }, { ".ttf", "font/ttf" },
            { ".otf", "font/otf" }, { ".mp4", "video/mp4" }, { ".webm", "video/webm" },
            { ".mp3", "audio/mpeg" }, { ".wasm", "application/wasm" }, { ".pdf", "application/pdf" },
            { ".webmanifest", "application/manifest+json" }
        };

        public FrontEndService()
        {
            ServiceName = Name;
            CanStop = true;
            CanShutdown = true;
        }

        protected override void OnStart(string[] args) { StartServer(); }
        protected override void OnStop() { StopServer(); }
        protected override void OnShutdown() { StopServer(); }

        private static string Setting(string key, string fallback)
        {
            string v = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }

        public void StartServer()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string port = Setting("Port", "8080");
            string rootSetting = Setting("Root", @"..\www");
            root = Path.GetFullPath(Path.IsPathRooted(rootSetting) ? rootSetting : Path.Combine(baseDir, rootSetting))
                       .TrimEnd('\\');
            spaFallback = Setting("SpaFallback", "true").Equals("true", StringComparison.OrdinalIgnoreCase);

            if (!Directory.Exists(root)) Directory.CreateDirectory(root);

            listener = new HttpListener();
            listener.Prefixes.Add("http://+:" + port + "/");
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                // Sem permissao de administrador (modo console): escuta apenas em localhost.
                if (ex.ErrorCode != 5) throw;
                listener = new HttpListener();
                listener.Prefixes.Add("http://localhost:" + port + "/");
                listener.Start();
                Log("Sem permissao para escutar em todas as interfaces; usando apenas localhost.");
            }

            running = true;
            loopThread = new Thread(AcceptLoop);
            loopThread.IsBackground = true;
            loopThread.Start();
            Log("Servidor iniciado na porta " + port + ", servindo " + root);
        }

        public void StopServer()
        {
            if (!running) return;
            running = false;
            try { listener.Stop(); listener.Close(); } catch { }
            Log("Servidor parado.");
        }

        private void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    HttpListenerContext ctx = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(Handle, ctx);
                }
                catch (Exception ex)
                {
                    if (running) Log("Erro ao aceitar conexao: " + ex.Message);
                }
            }
        }

        private void Handle(object state)
        {
            HttpListenerContext ctx = (HttpListenerContext)state;
            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse res = ctx.Response;
            try
            {
                if (req.HttpMethod != "GET" && req.HttpMethod != "HEAD")
                {
                    SendText(res, 405, "Metodo nao permitido");
                    return;
                }

                string relative = Uri.UnescapeDataString(req.Url.AbsolutePath).TrimStart('/').Replace('/', '\\');
                string fullPath = Path.GetFullPath(Path.Combine(root, relative));

                // Bloqueia acesso fora da pasta raiz (ex.: /../../Windows).
                if (!fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                    !fullPath.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    SendText(res, 403, "Acesso negado");
                    return;
                }

                if (Directory.Exists(fullPath)) fullPath = Path.Combine(fullPath, "index.html");

                if (!File.Exists(fullPath))
                {
                    // Rotas de SPA (React, Vue, Angular...) sem extensao caem no index.html.
                    string spaIndex = Path.Combine(root, "index.html");
                    if (spaFallback && Path.GetExtension(relative) == "" && File.Exists(spaIndex))
                        fullPath = spaIndex;
                    else
                    {
                        SendText(res, 404, "Nao encontrado");
                        return;
                    }
                }

                string ext = Path.GetExtension(fullPath);
                string mime;
                if (!MimeTypes.TryGetValue(ext, out mime)) mime = "application/octet-stream";
                res.ContentType = mime;
                res.AddHeader("Cache-Control", ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
                    ? "no-cache" : "public, max-age=3600");
                res.AddHeader("X-Content-Type-Options", "nosniff");

                using (FileStream fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    res.ContentLength64 = fs.Length;
                    if (req.HttpMethod == "GET") fs.CopyTo(res.OutputStream);
                }
                res.StatusCode = 200;
            }
            catch (Exception ex)
            {
                Log("Erro em " + req.Url + ": " + ex.Message);
                try { SendText(res, 500, "Erro interno"); } catch { }
            }
            finally
            {
                try { res.Close(); } catch { }
            }
        }

        private static void SendText(HttpListenerResponse res, int status, string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text);
            res.StatusCode = status;
            res.ContentType = "text/plain; charset=utf-8";
            res.ContentLength64 = body.Length;
            res.OutputStream.Write(body, 0, body.Length);
        }

        public static void Log(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
            if (Environment.UserInteractive) Console.WriteLine(line);
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\logs");
                Directory.CreateDirectory(dir);
                lock (logLock) File.AppendAllText(Path.Combine(dir, "server.log"), line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--console")
            {
                FrontEndService svc = new FrontEndService();
                svc.StartServer();
                Console.WriteLine("Pressione Enter para parar...");
                Console.ReadLine();
                svc.StopServer();
            }
            else
            {
                ServiceBase.Run(new FrontEndService());
            }
        }
    }
}
