using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AntigravityIsland.Services
{
    public static class AntigravityCli
    {
        private static string _agyPath;

        public static string ResolveAgyPath()
        {
            if (!string.IsNullOrEmpty(_agyPath) && File.Exists(_agyPath))
                return _agyPath;

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string standardPath = Path.Combine(userProfile, @"AppData\Local\agy\bin\agy.exe");

            if (File.Exists(standardPath))
            {
                _agyPath = standardPath;
                return _agyPath;
            }

            // Tenta via PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string part in pathEnv.Split(';'))
            {
                string candidate = Path.Combine(part.Trim(), "agy.exe");
                if (File.Exists(candidate))
                {
                    _agyPath = candidate;
                    return _agyPath;
                }
            }

            _agyPath = "agy.exe";
            return _agyPath;
        }

        /// <summary>
        /// Executa um prompt no Antigravity CLI em modo non-interactive (--print / -p)
        /// e retorna a resposta gerada.
        /// </summary>
        public static async Task<string> RunPromptAsync(string prompt, bool continueConversation = true)
        {
            return await Task.Run(() =>
            {
                string agyExe = ResolveAgyPath();

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = agyExe,
                    Arguments = string.Format("{0} --model gemini-3.8-flash-low -p \"{1}\"", continueConversation ? "-c" : "", prompt.Replace("\"", "\\\"")),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                try
                {
                    using (Process proc = Process.Start(psi))
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        string error = proc.StandardError.ReadToEnd();
                        proc.WaitForExit(90000); // Timeout de 90 segundos

                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            return output.Trim();
                        }
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            return "Erro: " + error.Trim();
                        }
                        return "(Nenhuma resposta retornada pelo Antigravity CLI)";
                    }
                }
                catch (Exception ex)
                {
                    return "Falha ao executar o Antigravity CLI: " + ex.Message;
                }
            });
        }
    }
}
