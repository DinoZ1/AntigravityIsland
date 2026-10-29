using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using AntigravityIsland.Model;

namespace AntigravityIsland.Services
{
    public class IpcServer
    {
        private HttpListener _listener;
        private Thread _listenerThread;
        private bool _isRunning;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly Dictionary<string, HttpListenerContext> _pendingContexts = new Dictionary<string, HttpListenerContext>();
        private readonly object _lock = new object();

        public event Action<ToolPermissionRequest, Action<bool, string>> OnPermissionRequested;
        public event Action<AgentStatus, string> OnStatusChanged;
        public event Action<string> OnLog;

        public int Port { get; private set; }

        public IpcServer(int port = 47823)
        {
            Port = port;
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", Port));
                _listener.Start();
                _isRunning = true;

                _listenerThread = new Thread(ListenLoop);
                _listenerThread.IsBackground = true;
                _listenerThread.Start();

                Log(string.Format("Servidor IPC rodando em http://127.0.0.1:{0}/", Port));
            }
            catch (Exception ex)
            {
                Log("Falha ao iniciar servidor IPC: " + ex.Message);
            }
        }

        public void Stop()
        {
            _isRunning = false;
            try
            {
                if (_listener != null && _listener.IsListening)
                {
                    _listener.Stop();
                    _listener.Close();
                }
            }
            catch { }
        }

        private void ListenLoop()
        {
            while (_isRunning && _listener != null && _listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(ProcessRequest, context);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log("Erro no loop do listener: " + ex.Message);
                }
            }
        }

        private void ProcessRequest(object state)
        {
            HttpListenerContext context = (HttpListenerContext)state;
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            // CORS headers para permitir requests locais
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-TwoCode-Token, X-Antigravity-Token");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            string rawUrl = request.RawUrl ?? "/";

            try
            {
                if (request.HttpMethod == "POST" && (rawUrl.StartsWith("/api/hook") || rawUrl.StartsWith("/twocode-hook")))
                {
                    HandleHookRequest(context);
                    return;
                }
                else if (request.HttpMethod == "GET" && rawUrl.StartsWith("/api/status"))
                {
                    SendJsonResponse(response, new Dictionary<string, object>
                    {
                        { "status", "online" },
                        { "island", "Antigravity Dynamic Island" },
                        { "version", "1.0.0" }
                    });
                    return;
                }
                else if (request.HttpMethod == "GET" && rawUrl.StartsWith("/api/latest-screenshot"))
                {
                    SendJsonResponse(response, new Dictionary<string, object>
                    {
                        { "path", ScreenCapture.LatestScreenshotPath ?? "" },
                        { "hasImage", !string.IsNullOrEmpty(ScreenCapture.LatestScreenshotPath) }
                    });
                    return;
                }
                else
                {
                    response.StatusCode = 404;
                    response.Close();
                }
            }
            catch (Exception ex)
            {
                Log("Erro ao processar requisicao: " + ex.Message);
                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch { }
            }
        }

        private void HandleHookRequest(HttpListenerContext context)
        {
            string body;
            using (StreamReader reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            Dictionary<string, object> data = null;
            try
            {
                data = _serializer.Deserialize<Dictionary<string, object>>(body);
            }
            catch
            {
                data = new Dictionary<string, object>();
            }

            string eventType = "";
            if (data.ContainsKey("eventType"))
                eventType = Convert.ToString(data["eventType"]);
            else if (data.ContainsKey("hookEventName"))
                eventType = Convert.ToString(data["hookEventName"]);

            Log(string.Format("Hook recebido: {0}", eventType));

            if (eventType == "PreToolUse" || eventType == "PermissionRequest")
            {
                ToolPermissionRequest req = new ToolPermissionRequest();
                req.ConversationId = data.ContainsKey("conversationId") ? Convert.ToString(data["conversationId"]) : "";
                req.StepIdx = data.ContainsKey("stepIdx") ? Convert.ToInt32(data["stepIdx"]) : 0;
                req.Pid = data.ContainsKey("pid") ? Convert.ToInt32(data["pid"]) : 0;

                if (data.ContainsKey("toolCall"))
                {
                    Dictionary<string, object> toolCall = data["toolCall"] as Dictionary<string, object>;
                    if (toolCall != null)
                    {
                        req.ToolName = toolCall.ContainsKey("name") ? Convert.ToString(toolCall["name"]) : "unknown_tool";
                        if (toolCall.ContainsKey("args"))
                        {
                            object argsObj = toolCall["args"];
                            req.ToolArgs = _serializer.Serialize(argsObj);
                        }
                    }
                }
                else
                {
                    req.ToolName = data.ContainsKey("tool") ? Convert.ToString(data["tool"]) : "command";
                    req.ToolArgs = data.ContainsKey("command") ? Convert.ToString(data["command"]) : "";
                }

                lock (_lock)
                {
                    _pendingContexts[req.RequestId] = context;
                }

                if (OnStatusChanged != null)
                {
                    OnStatusChanged(AgentStatus.WaitingPermission, string.Format("Confirmar: {0}", req.ToolName));
                }

                if (OnPermissionRequested != null)
                {
                    OnPermissionRequested(req, (allow, reason) =>
                    {
                        CompletePermission(req.RequestId, allow, reason);
                    });
                }
                // O request HTTP permanece suspenso ate que CompletePermission seja chamado
            }
            else if (eventType == "PreInvocation")
            {
                if (OnStatusChanged != null)
                    OnStatusChanged(AgentStatus.Thinking, "Antigravity pensando...");

                SendJsonResponse(context.Response, new Dictionary<string, object>
                {
                    { "injectSteps", new object[0] }
                });
            }
            else if (eventType == "PostToolUse")
            {
                if (OnStatusChanged != null)
                    OnStatusChanged(AgentStatus.ExecutingTool, "Comando executado");

                SendJsonResponse(context.Response, new Dictionary<string, object>());
            }
            else if (eventType == "Stop")
            {
                if (OnStatusChanged != null)
                    OnStatusChanged(AgentStatus.Finished, "Turno finalizado");

                SendJsonResponse(context.Response, new Dictionary<string, object>());
            }
            else
            {
                // Evento generico
                SendJsonResponse(context.Response, new Dictionary<string, object> { { "ok", true } });
            }
        }

        public void CompletePermission(string requestId, bool allow, string reason)
        {
            HttpListenerContext context = null;
            lock (_lock)
            {
                if (_pendingContexts.ContainsKey(requestId))
                {
                    context = _pendingContexts[requestId];
                    _pendingContexts.Remove(requestId);
                }
            }

            if (context == null) return;

            try
            {
                Dictionary<string, object> resp = new Dictionary<string, object>();
                resp["decision"] = allow ? "allow" : "deny";
                if (!allow && !string.IsNullOrEmpty(reason))
                {
                    resp["reason"] = reason;
                }

                // Compatibilidade adicional com o Claude Code / TwoCode schema:
                Dictionary<string, object> decisionObj = new Dictionary<string, object>();
                decisionObj["behavior"] = allow ? "allow" : "deny";
                if (!allow) decisionObj["message"] = reason ?? "Negado pelo usuario.";
                resp["hookSpecificOutput"] = new Dictionary<string, object>
                {
                    { "hookEventName", "PermissionRequest" },
                    { "decision", decisionObj }
                };

                SendJsonResponse(context.Response, resp);

                if (OnStatusChanged != null)
                {
                    OnStatusChanged(allow ? AgentStatus.ExecutingTool : AgentStatus.Idle,
                        allow ? "Permitido. Executando..." : "Operacao negada");
                }
            }
            catch (Exception ex)
            {
                Log("Erro ao responder permissao: " + ex.Message);
            }
        }

        private void SendJsonResponse(HttpListenerResponse response, object payload)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(_serializer.Serialize(payload));
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.StatusCode = 200;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Close();
        }

        private void Log(string msg)
        {
            if (OnLog != null) OnLog(msg);
        }
    }
}
