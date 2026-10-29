using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AntigravityIsland.Native;

namespace AntigravityIsland.Services
{
    public class VoiceAgent
    {
        private SpeechSynthesizer _synthesizer;
        private bool _isSpeaking = false;

        public event Action OnSpeakingStarted;
        public event Action OnSpeakingCompleted;
        public bool IsSpeaking { get { return _isSpeaking; } }

        public VoiceAgent()
        {
            InitSynthesizer();
        }

        private void InitSynthesizer()
        {
            try
            {
                _synthesizer = new SpeechSynthesizer();
                _synthesizer.Rate = 1; // Velocidade natural

                // Tenta selecionar voz em portugues (ex: Microsoft Maria Desktop)
                bool voiceFound = false;
                foreach (InstalledVoice voice in _synthesizer.GetInstalledVoices())
                {
                    if (voice.Enabled && voice.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase))
                    {
                        _synthesizer.SelectVoice(voice.VoiceInfo.Name);
                        voiceFound = true;
                        break;
                    }
                }

                if (!voiceFound)
                {
                    // Fallback para ingles ou padrao
                    try { _synthesizer.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult); } catch { }
                }

                _synthesizer.SpeakStarted += (s, e) =>
                {
                    _isSpeaking = true;
                    if (OnSpeakingStarted != null) OnSpeakingStarted();
                };

                _synthesizer.SpeakCompleted += (s, e) =>
                {
                    _isSpeaking = false;
                    if (OnSpeakingCompleted != null) OnSpeakingCompleted();
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Erro ao inicializar TTS: " + ex.Message);
            }
        }

        /// <summary>
        /// Limpa markdown e sintaxe tecnica para que a fala soe natural em audio.
        /// </summary>
        private string CleanTextForSpeech(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // Remove blocos de codigo ``` ... ```
            string clean = Regex.Replace(text, @"```[\s\S]*?```", " (bloco de código omitido na fala) ");
            // Remove codigo inline `...`
            clean = Regex.Replace(clean, @"`([^`]+)`", "$1");
            // Remove markdown links [texto](url)
            clean = Regex.Replace(clean, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            // Remove titulos #, **, *
            clean = Regex.Replace(clean, @"[#*_~>]+", " ");
            // Remove URLs
            clean = Regex.Replace(clean, @"https?://\S+", "link");

            // Limita a leitura a no maximo 600 caracteres para nao ficar falando sem parar
            if (clean.Length > 600)
            {
                clean = clean.Substring(0, 600) + "... O restante está disponível no terminal.";
            }

            return clean.Trim();
        }

        public void SpeakAsync(string rawText)
        {
            if (_synthesizer == null) return;

            string clean = CleanTextForSpeech(rawText);
            if (string.IsNullOrWhiteSpace(clean)) return;

            try
            {
                _synthesizer.SpeakAsyncCancelAll();
                _synthesizer.SpeakAsync(clean);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Erro ao falar: " + ex.Message);
            }
        }

        public void StopSpeaking()
        {
            if (_synthesizer != null)
            {
                try
                {
                    _synthesizer.SpeakAsyncCancelAll();
                    _isSpeaking = false;
                    if (OnSpeakingCompleted != null) OnSpeakingCompleted();
                }
                catch { }
            }
        }

        /// <summary>
        /// Aciona o sistema nativo de Ditado por Voz do Windows (Win + H)
        /// diretamente no campo de texto ativo.
        /// </summary>
        public static void TriggerWindowsVoiceDictation()
        {
            const byte VK_LWIN = 0x5B;
            const byte VK_H = 0x48;
            const uint KEYEVENTF_KEYUP = 0x0002;

            NativeWin32.keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            NativeWin32.keybd_event(VK_H, 0, 0, UIntPtr.Zero);
            NativeWin32.keybd_event(VK_H, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            NativeWin32.keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }
}
