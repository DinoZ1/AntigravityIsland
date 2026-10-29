using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AntigravityIsland.Native;

namespace AntigravityIsland.Services
{
    public class VoiceAgent
    {
        private SpeechSynthesizer _synthesizer;
        private MediaPlayer _mediaPlayer;
        private bool _isSpeaking = false;
        private string _currentVoice = "pt-BR-AntonioNeural"; // Voz neural humana ultra-realista padrão

        public event Action OnSpeakingStarted;
        public event Action OnSpeakingCompleted;
        public bool IsSpeaking { get { return _isSpeaking; } }

        public string CurrentVoice
        {
            get { return _currentVoice; }
            set { if (!string.IsNullOrEmpty(value)) _currentVoice = value; }
        }

        public string VoiceDisplayName
        {
            get
            {
                return _currentVoice.Contains("Antonio") ? "Antonio (Neural)" : "Francisca (Neural)";
            }
        }

        public void ToggleVoice()
        {
            if (_currentVoice.Contains("Antonio"))
            {
                _currentVoice = "pt-BR-FranciscaNeural";
            }
            else
            {
                _currentVoice = "pt-BR-AntonioNeural";
            }
        }

        public VoiceAgent()
        {
            InitSynthesizer();
            InitMediaPlayer();
        }

        private void InitMediaPlayer()
        {
            try
            {
                if (Application.Current != null && Application.Current.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _mediaPlayer = new MediaPlayer();
                        _mediaPlayer.MediaEnded += (s, e) =>
                        {
                            _isSpeaking = false;
                            if (OnSpeakingCompleted != null) OnSpeakingCompleted();
                        };
                        _mediaPlayer.MediaFailed += (s, e) =>
                        {
                            _isSpeaking = false;
                            if (OnSpeakingCompleted != null) OnSpeakingCompleted();
                        };
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Erro MediaPlayer: " + ex.Message);
            }
        }

        private void InitSynthesizer()
        {
            try
            {
                _synthesizer = new SpeechSynthesizer();
                _synthesizer.Rate = 1;

                foreach (InstalledVoice voice in _synthesizer.GetInstalledVoices())
                {
                    if (voice.Enabled && voice.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase))
                    {
                        _synthesizer.SelectVoice(voice.VoiceInfo.Name);
                        break;
                    }
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
            catch { }
        }

        /// <summary>
        /// Limpa markdown e sintaxe tecnica para que a fala soe natural em audio.
        /// </summary>
        private string CleanTextForSpeech(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // Remove blocos de codigo ``` ... ```
            string clean = Regex.Replace(text, @"```[\s\S]*?```", " ");
            // Remove codigo inline `...`
            clean = Regex.Replace(clean, @"`([^`]+)`", "$1");
            // Remove markdown links [texto](url)
            clean = Regex.Replace(clean, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            // Remove titulos #, **, *
            clean = Regex.Replace(clean, @"[#*_~>]+", " ");
            // Remove URLs
            clean = Regex.Replace(clean, @"https?://\S+", "");

            // Limita a 500 caracteres para respostas de voz concisas e dinamicas
            if (clean.Length > 500)
            {
                clean = clean.Substring(0, 500) + "...";
            }

            return clean.Trim();
        }

        public async void SpeakAsync(string rawText)
        {
            string clean = CleanTextForSpeech(rawText);
            if (string.IsNullOrWhiteSpace(clean)) return;

            StopSpeaking();

            bool playedWithNeural = false;
            try
            {
                string tempDir = Path.GetTempPath();
                string txtIn = Path.Combine(tempDir, "agy_tts_in.txt");
                string mp3Out = Path.Combine(tempDir, "agy_tts_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".mp3");

                File.WriteAllText(txtIn, clean, Encoding.UTF8);

                await Task.Run(() =>
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = string.Format("/c npx -y @andresaya/edge-tts synthesize -f \"{0}\" -v \"{1}\" -o \"{2}\"",
                            txtIn, _currentVoice, mp3Out),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using (Process proc = Process.Start(psi))
                    {
                        proc.WaitForExit(10000);
                    }
                });

                if (File.Exists(mp3Out) && new FileInfo(mp3Out).Length > 1000)
                {
                    if (Application.Current != null && Application.Current.Dispatcher != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (_mediaPlayer == null) InitMediaPlayer();
                            if (_mediaPlayer != null)
                            {
                                _mediaPlayer.Open(new Uri(mp3Out));
                                _mediaPlayer.Play();
                                _isSpeaking = true;
                                if (OnSpeakingStarted != null) OnSpeakingStarted();
                                playedWithNeural = true;
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Erro Neural TTS: " + ex.Message);
            }

            // Fallback para o sintetizador local do Windows se offline
            if (!playedWithNeural && _synthesizer != null)
            {
                try
                {
                    _synthesizer.SpeakAsyncCancelAll();
                    _synthesizer.SpeakAsync(clean);
                }
                catch { }
            }
        }

        public void StopSpeaking()
        {
            try
            {
                if (Application.Current != null && Application.Current.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_mediaPlayer != null)
                        {
                            _mediaPlayer.Stop();
                            _mediaPlayer.Close();
                        }
                    });
                }
                if (_synthesizer != null)
                {
                    _synthesizer.SpeakAsyncCancelAll();
                }
            }
            catch { }
            finally
            {
                _isSpeaking = false;
                if (OnSpeakingCompleted != null) OnSpeakingCompleted();
            }
        }

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
