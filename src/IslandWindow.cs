using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using AntigravityIsland.Model;
using AntigravityIsland.Native;
using AntigravityIsland.Services;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace AntigravityIsland.UI
{
    public class IslandWindow : Window
    {
        // Dimensoes do modo Compacto (Notch/Pill)
        private const double PILL_WIDTH = 340;
        private const double PILL_HEIGHT = 44;
        private const double PILL_RADIUS = 22;

        // Dimensoes do modo Expandido (Dashboard Multi-Card NotchNook)
        private const double DASHBOARD_WIDTH = 880;
        private const double DASHBOARD_HEIGHT = 185;
        private const double DASHBOARD_RADIUS = 24;

        private Border _islandBorder;
        private Grid _pillLayout;
        private Grid _dashboardLayout;

        // Componentes do Agent Card (Esquerda)
        private Ellipse _agentStatusLamp;
        private TextBlock _agentTitle;
        private TextBlock _agentStatusText;
        private TextBlock _terminalCodeLine1;
        private TextBlock _terminalCodeLine2;
        private TextBox _voiceInputBox;
        private StackPanel _permissionButtonsPanel;
        private StackPanel _soundwavePanel;
        private Button _btnStopVoice;
        private Ellipse _roboLeftEye;
        private Ellipse _roboRightEye;
        private Ellipse _roboAntennaBulb;

        // Componentes dos Widgets (Direita)
        private TextBlock _clockTimeText;
        private TextBlock _clockDateText;
        private TextBlock _focusTimerText;
        private TextBlock _focusSubText;
        private TextBlock _spotifyTrackText;
        private TextBlock _spotifyStatusText;
        private TextBlock _pillStatusText;
        private TextBlock _voiceNameSubText;
        private Ellipse _pillLamp;

        // Timers internos
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _focusTimer;
        private DispatcherTimer _autoCollapseTimer;
        private DispatcherTimer _expandedWatchdogTimer;
        private DispatcherTimer _voiceSilenceTimer;
        private DispatcherTimer _eyeBlinkTimer;
        private DispatcherTimer _soundwaveTimer;

        private int _focusRemainingSeconds = 25 * 60; // 25:00
        private bool _focusRunning = false;
        private bool _isExpanded = false;
        private bool _isVoiceActive = false;
        private DateTime _lastHotkeyTime = DateTime.MinValue;
        private string _pendingScreenCapture = null;

        private Action<bool, string> _currentPermissionCallback;
        private ToolPermissionRequest _currentRequest;
        private AgentStatus _currentStatus = AgentStatus.Idle;

        private VoiceAgent _voiceAgent;
        private Random _rnd = new Random();

        public IslandWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.Manual;

            Width = DASHBOARD_WIDTH + 80;
            Height = DASHBOARD_HEIGHT + 70;

            _voiceAgent = new VoiceAgent();
            _voiceAgent.OnSpeakingStarted += VoiceAgent_SpeakingStarted;
            _voiceAgent.OnSpeakingCompleted += VoiceAgent_SpeakingCompleted;

            InitVisualComponents();
            InitTimers();

            Loaded += IslandWindow_Loaded;
            KeyDown += IslandWindow_KeyDown;
            Deactivated += (s, e) =>
            {
                if (_isExpanded && !_isVoiceActive)
                {
                    _autoCollapseTimer.Stop();
                    _expandedWatchdogTimer.Stop();
                    CollapseToPillClean();
                }
            };
        }

        private void IslandWindow_Loaded(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeWin32.MakeNonActivating(hwnd);

            // Posiciona centralizado horizontalmente no topo da tela principal
            double screenW = SystemParameters.PrimaryScreenWidth;
            Left = (screenW - Width) / 2;
            Top = 4;

            SetState(AgentStatus.Idle, "Waiting for Antigravity...");
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeWin32.MakeNonActivating(hwnd);

            // Registra Hotkeys Globais (funcionam em tela cheia durante jogos e qualquer aplicativo):
            // 1001: Ctrl + Shift + Espaço (Voz rápida / In-Game Assistant)
            NativeWin32.RegisterHotKey(hwnd, 1001, NativeWin32.MOD_CONTROL | NativeWin32.MOD_SHIFT | NativeWin32.MOD_NOREPEAT, NativeWin32.VK_SPACE);

            // 1002: Ctrl + Shift + G (Game Assistant: captura tela do jogo + microfone)
            NativeWin32.RegisterHotKey(hwnd, 1002, NativeWin32.MOD_CONTROL | NativeWin32.MOD_SHIFT | NativeWin32.MOD_NOREPEAT, NativeWin32.VK_G);

            // 1003: Ctrl + Alt + V (Voz com uma mão)
            NativeWin32.RegisterHotKey(hwnd, 1003, NativeWin32.MOD_CONTROL | NativeWin32.MOD_ALT | NativeWin32.MOD_NOREPEAT, NativeWin32.VK_V);

            HwndSource source = HwndSource.FromHwnd(hwnd);
            if (source != null)
            {
                source.AddHook(HwndHook);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                NativeWin32.UnregisterHotKey(hwnd, 1001);
                NativeWin32.UnregisterHotKey(hwnd, 1002);
                NativeWin32.UnregisterHotKey(hwnd, 1003);
            }
            catch { }
            base.OnClosed(e);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeWin32.WM_HOTKEY)
            {
                // Debounce de 450ms para evitar duplo disparo se a tecla for mantida
                if ((DateTime.Now - _lastHotkeyTime).TotalMilliseconds < 450)
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                _lastHotkeyTime = DateTime.Now;

                int hotkeyId = wParam.ToInt32();
                if (hotkeyId == 1001 || hotkeyId == 1003)
                {
                    handled = true;
                    OnGameVoiceHotkeyTriggered(false);
                }
                else if (hotkeyId == 1002)
                {
                    handled = true;
                    OnGameVoiceHotkeyTriggered(true); // com captura de tela do jogo
                }
            }
            return IntPtr.Zero;
        }

        private void OnGameVoiceHotkeyTriggered(bool captureScreen)
        {
            Dispatcher.Invoke(() =>
            {
                // Se o robô estiver falando enquanto você joga, apertar o atalho corta a fala na hora (Mute/Barge-in)
                if (_voiceAgent != null && _voiceAgent.IsSpeaking)
                {
                    _voiceAgent.StopSpeaking();
                    _isVoiceActive = false;
                    AnimateToPill();
                    return;
                }

                // Se a caixa de voz já estiver aberta e o usuário apertar novamente após falar, cancela
                if (_isVoiceActive && _voiceInputBox.Visibility == Visibility.Visible && string.IsNullOrWhiteSpace(_voiceInputBox.Text))
                {
                    _voiceSilenceTimer.Stop();
                    _isVoiceActive = false;
                    _voiceInputBox.Visibility = Visibility.Collapsed;
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    NativeWin32.MakeNonActivating(hwnd);
                    AnimateToPill();
                    return;
                }

                // Marca a sessão de voz como ativa para impedir qualquer fechamento automático
                _isVoiceActive = true;
                _autoCollapseTimer.Stop();
                _expandedWatchdogTimer.Stop();

                AnimateToDashboard();

                if (captureScreen)
                {
                    try
                    {
                        _pendingScreenCapture = ScreenCapture.Capture(false);
                        _terminalCodeLine1.Text = "$ agy (🎮 Game Assistant)";
                        _terminalCodeLine2.Text = "Tela capturada! Fale sua dúvida sobre o jogo...";
                    }
                    catch
                    {
                        _pendingScreenCapture = null;
                        _terminalCodeLine1.Text = "$ agy (🎮 Game Assistant)";
                        _terminalCodeLine2.Text = "Fale sua dúvida sobre o jogo...";
                    }
                }
                else
                {
                    _pendingScreenCapture = null;
                    _terminalCodeLine1.Text = "$ agy (🎮 In-Game Voice)";
                    _terminalCodeLine2.Text = "Ouvindo você... (fale agora)";
                }

                StartVoiceInputMode();
            });
        }

        private void InitTimers()
        {
            // 1. Relogio em tempo real
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => UpdateClock();
            _clockTimer.Start();
            UpdateClock();

            // 2. Focus Timer (Pomodoro)
            _focusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _focusTimer.Tick += (s, e) =>
            {
                if (_focusRunning && _focusRemainingSeconds > 0)
                {
                    _focusRemainingSeconds--;
                    int m = _focusRemainingSeconds / 60;
                    int sec = _focusRemainingSeconds % 60;
                    _focusTimerText.Text = string.Format("{0:00}:{1:00}", m, sec);
                    if (_focusRemainingSeconds == 0)
                    {
                        _focusRunning = false;
                        _focusSubText.Text = "Concluido!";
                    }
                }
            };

            // 3. Auto-collapse da ilha apos o mouse sair
            _autoCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _autoCollapseTimer.Tick += (s, e) =>
            {
                _autoCollapseTimer.Stop();
                if (_isExpanded && !_isVoiceActive && !_islandBorder.IsMouseOver)
                {
                    CollapseToPillClean();
                }
            };

            // 4. Watchdog continuo enquanto expandido para garantir retracao imediata
            _expandedWatchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _expandedWatchdogTimer.Tick += (s, e) =>
            {
                if (_isExpanded && !_isVoiceActive && !_islandBorder.IsMouseOver)
                {
                    _expandedWatchdogTimer.Stop();
                    CollapseToPillClean();
                }
            };

            // 5. Temporizador de silêncio para auto-envio da fala
            _voiceSilenceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
            _voiceSilenceTimer.Tick += (s, e) =>
            {
                _voiceSilenceTimer.Stop();
                if (_voiceInputBox.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(_voiceInputBox.Text))
                {
                    SubmitVoiceCommand(_voiceInputBox.Text);
                }
            };

            // 4. Piscar natural dos olhos do Robo
            _eyeBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            _eyeBlinkTimer.Tick += (s, e) =>
            {
                if (_roboLeftEye != null && _roboRightEye != null && !_voiceAgent.IsSpeaking)
                {
                    DoubleAnimation blink = new DoubleAnimation(0.1, new Duration(TimeSpan.FromMilliseconds(100)))
                    {
                        AutoReverse = true
                    };
                    _roboLeftEye.BeginAnimation(HeightProperty, blink);
                    _roboRightEye.BeginAnimation(HeightProperty, blink);
                }
            };
            _eyeBlinkTimer.Start();

            // 5. Animacao da onda sonora durante a fala
            _soundwaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _soundwaveTimer.Tick += (s, e) =>
            {
                if (_voiceAgent.IsSpeaking && _soundwavePanel != null)
                {
                    foreach (UIElement child in _soundwavePanel.Children)
                    {
                        Rectangle bar = child as Rectangle;
                        if (bar != null)
                        {
                            bar.Height = _rnd.Next(4, 16);
                        }
                    }
                }
            };
            _soundwaveTimer.Start();
        }

        private void UpdateClock()
        {
            DateTime now = DateTime.Now;
            _clockTimeText.Text = now.ToString("HH:mm");
            CultureInfo pt = new CultureInfo("pt-BR");
            string day = pt.DateTimeFormat.GetAbbreviatedDayName(now.DayOfWeek);
            string month = pt.DateTimeFormat.GetAbbreviatedMonthName(now.Month);
            _clockDateText.Text = string.Format("{0} {1} {2}", char.ToUpper(day[0]) + day.Substring(1), now.Day, char.ToUpper(month[0]) + month.Substring(1));
        }

        private void InitVisualComponents()
        {
            Grid rootGrid = new Grid { Background = null };

            _islandBorder = new Border
            {
                Width = PILL_WIDTH,
                Height = PILL_HEIGHT,
                CornerRadius = new CornerRadius(PILL_RADIUS),
                Background = new SolidColorBrush(Color.FromArgb(248, 16, 17, 21)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                BorderThickness = new Thickness(1.2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                ClipToBounds = true,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromArgb(230, 0, 0, 0),
                    BlurRadius = 28,
                    ShadowDepth = 8,
                    Opacity = 0.65
                }
            };

            _islandBorder.MouseEnter += (s, e) =>
            {
                _autoCollapseTimer.Stop();
                if (!_isExpanded) AnimateToDashboard();
            };

            _islandBorder.MouseLeave += (s, e) =>
            {
                if (!_isVoiceActive)
                {
                    _autoCollapseTimer.Stop();
                    _autoCollapseTimer.Interval = TimeSpan.FromMilliseconds(120);
                    _autoCollapseTimer.Start();
                }
            };

            _islandBorder.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    double screenW = SystemParameters.PrimaryScreenWidth;
                    Left = (screenW - Width) / 2;
                    Top = 4;
                }
                else if (e.LeftButton == MouseButtonState.Pressed && !_isExpanded)
                {
                    DragMove();
                }
            };

            BuildPillLayout();
            BuildDashboardLayout();

            Grid container = new Grid();
            container.Children.Add(_pillLayout);
            container.Children.Add(_dashboardLayout);
            _islandBorder.Child = container;

            rootGrid.Children.Add(_islandBorder);
            Content = rootGrid;
        }

        // =========================================================================
        // MODO 1: PILULA COMPACTA (Notch Rest State)
        // =========================================================================
        private void BuildPillLayout()
        {
            _pillLayout = new Grid
            {
                Margin = new Thickness(14, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _pillLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _pillLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _pillLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel leftGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Ellipse miniLamp = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                Margin = new Thickness(0, 0, 8, 0)
            };
            _pillLamp = miniLamp;
            TextBlock pillTitle = new TextBlock
            {
                Text = "Antigravity",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            leftGroup.Children.Add(miniLamp);
            leftGroup.Children.Add(pillTitle);
            Grid.SetColumn(leftGroup, 0);

            _pillStatusText = new TextBlock
            {
                Text = "Idle",
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                FontSize = 12,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_pillStatusText, 1);

            StackPanel rightGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button btnVoice = CreateTinyButton("\uD83C\uDF99", "Voz", (s, e) =>
            {
                AnimateToDashboard();
                StartVoiceInputMode();
            });
            Button btnShot = CreateTinyButton("\uD83D\uDCF7", "Print", (s, e) => CaptureScreenContext());
            Button btnClose = CreateTinyButton("\u2715", "Fechar", (s, e) => Close());

            rightGroup.Children.Add(btnVoice);
            rightGroup.Children.Add(btnShot);
            rightGroup.Children.Add(btnClose);
            Grid.SetColumn(rightGroup, 2);

            _pillLayout.Children.Add(leftGroup);
            _pillLayout.Children.Add(_pillStatusText);
            _pillLayout.Children.Add(rightGroup);
        }

        // =========================================================================
        // MODO 2: DASHBOARD MULTI-CARD (com Agente de Voz e Widgets)
        // =========================================================================
        private void BuildDashboardLayout()
        {
            _dashboardLayout = new Grid
            {
                Margin = new Thickness(12),
                Visibility = Visibility.Collapsed,
                Opacity = 0
            };

            _dashboardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(435, GridUnitType.Pixel) });
            _dashboardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200, GridUnitType.Pixel) });
            _dashboardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200, GridUnitType.Pixel) });

            // ----------------------------------------------------
            // COLUNA 0: MAIN AGENT CARD (Robo + Console + Voz)
            // ----------------------------------------------------
            Border agentCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 22, 23, 29)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 10, 0)
            };

            Grid agentGrid = new Grid();
            agentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            agentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            FrameworkElement roboAvatar = BuildRoboAvatar();
            Grid.SetColumn(roboAvatar, 0);

            Grid agentContent = new Grid { Margin = new Thickness(14, 0, 0, 0) };
            agentContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            agentContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            agentContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Linha 0: Titulo com icone e botao de Microfone
            Grid headGrid = new Grid();
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel headLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Rectangle iconBox = new Rectangle { Width = 11, Height = 11, Fill = new SolidColorBrush(Color.FromRgb(160, 170, 185)), Margin = new Thickness(0, 0, 6, 0), RadiusX = 2, RadiusY = 2 };
            _agentTitle = new TextBlock { Text = "Antigravity CLI", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13.5 };
            headLeft.Children.Add(iconBox);
            headLeft.Children.Add(_agentTitle);
            Grid.SetColumn(headLeft, 0);

            // Botao de Voz no Topo do Card
            StackPanel voiceControls = new StackPanel { Orientation = Orientation.Horizontal };
            Button btnVoiceTrigger = new Button
            {
                Content = "\uD83C\uDF99 Falar",
                Background = new SolidColorBrush(Color.FromArgb(50, 59, 130, 246)),
                Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 0),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 59, 130, 246)),
                Cursor = Cursors.Hand
            };
            btnVoiceTrigger.Click += (s, e) => StartVoiceInputMode();

            _btnStopVoice = new Button
            {
                Content = "\u25A0 Parar",
                Background = new SolidColorBrush(Color.FromArgb(50, 239, 68, 68)),
                Foreground = new SolidColorBrush(Color.FromRgb(252, 165, 165)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed
            };
            _btnStopVoice.Click += (s, e) => _voiceAgent.StopSpeaking();

            voiceControls.Children.Add(btnVoiceTrigger);
            voiceControls.Children.Add(_btnStopVoice);
            Grid.SetColumn(voiceControls, 1);

            headGrid.Children.Add(headLeft);
            headGrid.Children.Add(voiceControls);
            Grid.SetRow(headGrid, 0);

            // Linha 1: Status com lampada e soundwave animado
            Grid statusRow = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel statusPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _agentStatusLamp = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)), Margin = new Thickness(0, 0, 6, 0) };
            _agentStatusText = new TextBlock { Text = "Waiting for Antigravity...", Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), FontSize = 11 };
            statusPanel.Children.Add(_agentStatusLamp);
            statusPanel.Children.Add(_agentStatusText);
            Grid.SetColumn(statusPanel, 0);

            // Soundwave de audio (5 barrinhas animadas)
            _soundwavePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            for (int i = 0; i < 5; i++)
            {
                _soundwavePanel.Children.Add(new Rectangle
                {
                    Width = 2.5,
                    Height = 6,
                    Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                    Margin = new Thickness(1.5, 0, 1.5, 0),
                    RadiusX = 1,
                    RadiusY = 1
                });
            }
            Grid.SetColumn(_soundwavePanel, 1);

            statusRow.Children.Add(statusPanel);
            statusRow.Children.Add(_soundwavePanel);
            Grid.SetRow(statusRow, 1);

            // Linha 2: Caixa do Terminal / Entrada de Voz
            Border terminalBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 12, 13, 17)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 6, 10, 6)
            };

            Grid terminalInner = new Grid();
            terminalInner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            terminalInner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            terminalInner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            terminalInner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _terminalCodeLine1 = new TextBlock
            {
                Text = "$ agy",
                Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetRow(_terminalCodeLine1, 0);

            _terminalCodeLine2 = new TextBlock
            {
                Text = "Antigravity pronto. Clique em Falar para interagir por voz",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 10.5,
                Margin = new Thickness(0, 1, 0, 4),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(_terminalCodeLine2, 1);

            // Campo de Input de Voz (Permite falar via ditado Win+H ou digitar)
            _voiceInputBox = new TextBox
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 20, 22, 28)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 59, 130, 246)),
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                FontSize = 11,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 2, 0, 4),
                Visibility = Visibility.Collapsed
            };
            _voiceInputBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    _voiceSilenceTimer.Stop();
                    SubmitVoiceCommand(_voiceInputBox.Text);
                }
                else if (e.Key == Key.Escape)
                {
                    _voiceSilenceTimer.Stop();
                    _isVoiceActive = false;
                    _voiceInputBox.Visibility = Visibility.Collapsed;
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    NativeWin32.MakeNonActivating(hwnd);
                    AnimateToPill();
                }
            };
            _voiceInputBox.TextChanged += (s, e) =>
            {
                if (_voiceInputBox.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(_voiceInputBox.Text))
                {
                    // Reinicia o temporizador de silêncio a cada nova palavra ditada
                    _voiceSilenceTimer.Stop();
                    _voiceSilenceTimer.Interval = TimeSpan.FromMilliseconds(1600);
                    _voiceSilenceTimer.Start();
                }
            };
            Grid.SetRow(_voiceInputBox, 2);

            // Botoes Permitir / Negar
            _permissionButtonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed
            };

            Button btnAllow = new Button
            {
                Content = "\u2713 Allow",
                Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Padding = new Thickness(10, 2, 10, 2),
                Margin = new Thickness(0, 0, 6, 0),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btnAllow.Click += (s, e) => AnswerPermission(true, "");

            Button btnDeny = new Button
            {
                Content = "\u2715 Deny",
                Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 10.5,
                Padding = new Thickness(10, 2, 10, 2),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btnDeny.Click += (s, e) => AnswerPermission(false, "Negado pelo usuario na Dynamic Island.");

            _permissionButtonsPanel.Children.Add(btnAllow);
            _permissionButtonsPanel.Children.Add(btnDeny);
            Grid.SetRow(_permissionButtonsPanel, 3);

            terminalInner.Children.Add(_terminalCodeLine1);
            terminalInner.Children.Add(_terminalCodeLine2);
            terminalInner.Children.Add(_voiceInputBox);
            terminalInner.Children.Add(_permissionButtonsPanel);
            terminalBox.Child = terminalInner;
            Grid.SetRow(terminalBox, 2);

            agentContent.Children.Add(headGrid);
            agentContent.Children.Add(statusRow);
            agentContent.Children.Add(terminalBox);
            Grid.SetColumn(agentContent, 1);

            agentGrid.Children.Add(roboAvatar);
            agentGrid.Children.Add(agentContent);
            agentCard.Child = agentGrid;
            Grid.SetColumn(agentCard, 0);

            // ----------------------------------------------------
            // COLUNA 1: WIDGETS (SPOTIFY + RELOGIO)
            // ----------------------------------------------------
            Grid col1Grid = new Grid { Margin = new Thickness(0, 0, 10, 0) };
            col1Grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            col1Grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Widget Spotify
            Border spotifyCard = CreateTileCard();
            Grid spotifyContent = new Grid();
            spotifyContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            spotifyContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border spotifyIcon = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(30, 215, 96)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock { Text = "\u266B", FontSize = 20, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Grid.SetColumn(spotifyIcon, 0);

            StackPanel spotifyTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _spotifyTrackText = new TextBlock { Text = "Spotify", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13 };
            _spotifyStatusText = new TextBlock { Text = "Music", Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), FontSize = 11 };
            spotifyTexts.Children.Add(_spotifyTrackText);
            spotifyTexts.Children.Add(_spotifyStatusText);
            Grid.SetColumn(spotifyTexts, 1);

            spotifyContent.Children.Add(spotifyIcon);
            spotifyContent.Children.Add(spotifyTexts);
            spotifyCard.Child = spotifyContent;
            spotifyCard.MouseLeftButtonDown += (s, e) =>
            {
                try { Process.Start("spotify:"); } catch { Process.Start("https://open.spotify.com"); }
            };
            Grid.SetRow(spotifyCard, 0);

            // Widget Relogio & Data
            Border clockCard = CreateTileCard();
            StackPanel clockContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            _clockTimeText = new TextBlock { Text = "14:36", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 24, LineHeight = 24 };
            _clockDateText = new TextBlock { Text = "Mon 12 Oct", Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
            Rectangle timeBar = new Rectangle { Width = 32, Height = 2, Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), RadiusX = 1, RadiusY = 1 };
            clockContent.Children.Add(_clockTimeText);
            clockContent.Children.Add(_clockDateText);
            clockContent.Children.Add(timeBar);
            clockCard.Child = clockContent;
            Grid.SetRow(clockCard, 1);

            col1Grid.Children.Add(spotifyCard);
            col1Grid.Children.Add(clockCard);
            Grid.SetColumn(col1Grid, 1);

            // ----------------------------------------------------
            // COLUNA 2: WIDGETS (FOCUS + SETTINGS / TOOLS)
            // ----------------------------------------------------
            Grid col2Grid = new Grid();
            col2Grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            col2Grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Widget Focus Timer
            Border focusCard = CreateTileCard();
            Grid focusContent = new Grid();
            focusContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            focusContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border focusCircle = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                BorderThickness = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock { Text = "\u23F1", FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Grid.SetColumn(focusCircle, 0);

            StackPanel focusTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _focusTimerText = new TextBlock { Text = "25:00", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13.5 };
            _focusSubText = new TextBlock { Text = "Focus", Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), FontSize = 11 };
            focusTexts.Children.Add(_focusTimerText);
            focusTexts.Children.Add(_focusSubText);
            Grid.SetColumn(focusTexts, 1);

            focusContent.Children.Add(focusCircle);
            focusContent.Children.Add(focusTexts);
            focusCard.Child = focusContent;
            focusCard.MouseLeftButtonDown += (s, e) =>
            {
                _focusRunning = !_focusRunning;
                _focusSubText.Text = _focusRunning ? "Em foco..." : "Pausado";
                if (_focusRunning) _focusTimer.Start(); else _focusTimer.Stop();
            };
            Grid.SetRow(focusCard, 0);

            // Widget Settings / Tools
            Border settingsCard = CreateTileCard();
            Grid settingsContent = new Grid();
            settingsContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            settingsContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border settingsIcon = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(168, 85, 247)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock { Text = "\uD83C\uDF99", FontSize = 18, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Grid.SetColumn(settingsIcon, 0);

            StackPanel settingsTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock settingsTitle = new TextBlock { Text = "Voz Neural", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13 };
            _voiceNameSubText = new TextBlock { Text = _voiceAgent.VoiceDisplayName, Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), FontSize = 11 };
            settingsTexts.Children.Add(settingsTitle);
            settingsTexts.Children.Add(_voiceNameSubText);
            Grid.SetColumn(settingsTexts, 1);

            settingsContent.Children.Add(settingsIcon);
            settingsContent.Children.Add(settingsTexts);
            settingsCard.Child = settingsContent;
            settingsCard.MouseLeftButtonDown += (s, e) =>
            {
                _voiceAgent.ToggleVoice();
                _voiceNameSubText.Text = _voiceAgent.VoiceDisplayName;
                _voiceAgent.SpeakAsync("Voz alterada para " + _voiceAgent.VoiceDisplayName);
            };
            Grid.SetRow(settingsCard, 1);

            col2Grid.Children.Add(focusCard);
            col2Grid.Children.Add(settingsCard);
            Grid.SetColumn(col2Grid, 2);

            _dashboardLayout.Children.Add(agentCard);
            _dashboardLayout.Children.Add(col1Grid);
            _dashboardLayout.Children.Add(col2Grid);
        }

        private Border CreateTileCard()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 22, 23, 29)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
        }

        // =========================================================================
        // MASCOTE ROBO VETORIAL
        // =========================================================================
        private FrameworkElement BuildRoboAvatar()
        {
            Canvas canvas = new Canvas
            {
                Width = 72,
                Height = 84,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };

            // Clique no robo ativa o modo de voz
            canvas.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                StartVoiceInputMode();
            };

            Line antenna = new Line
            {
                X1 = 36, Y1 = 20,
                X2 = 36, Y2 = 6,
                Stroke = new SolidColorBrush(Color.FromRgb(210, 218, 225)),
                StrokeThickness = 2.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(antenna);

            _roboAntennaBulb = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                Effect = new DropShadowEffect { Color = Color.FromRgb(16, 185, 129), BlurRadius = 8, ShadowDepth = 0 }
            };
            Canvas.SetLeft(_roboAntennaBulb, 32.5);
            Canvas.SetTop(_roboAntennaBulb, 2);
            canvas.Children.Add(_roboAntennaBulb);

            Border head = new Border
            {
                Width = 68,
                Height = 54,
                CornerRadius = new CornerRadius(20),
                Background = new LinearGradientBrush(Color.FromRgb(255, 255, 255), Color.FromRgb(220, 228, 235), 90),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = Color.FromArgb(100, 0, 0, 0), BlurRadius = 10, ShadowDepth = 3 }
            };
            Canvas.SetLeft(head, 2);
            Canvas.SetTop(head, 18);
            canvas.Children.Add(head);

            Border visor = new Border
            {
                Width = 48,
                Height = 30,
                CornerRadius = new CornerRadius(12),
                Background = new LinearGradientBrush(Color.FromRgb(28, 32, 40), Color.FromRgb(10, 12, 16), 90)
            };
            Canvas.SetLeft(visor, 12);
            Canvas.SetTop(visor, 30);
            canvas.Children.Add(visor);

            _roboLeftEye = new Ellipse
            {
                Width = 7,
                Height = 12,
                Fill = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
                Effect = new DropShadowEffect { Color = Color.FromRgb(96, 165, 250), BlurRadius = 8, ShadowDepth = 0 }
            };
            Canvas.SetLeft(_roboLeftEye, 21);
            Canvas.SetTop(_roboLeftEye, 39);
            canvas.Children.Add(_roboLeftEye);

            _roboRightEye = new Ellipse
            {
                Width = 7,
                Height = 12,
                Fill = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
                Effect = new DropShadowEffect { Color = Color.FromRgb(96, 165, 250), BlurRadius = 8, ShadowDepth = 0 }
            };
            Canvas.SetLeft(_roboRightEye, 44);
            Canvas.SetTop(_roboRightEye, 39);
            canvas.Children.Add(_roboRightEye);

            return canvas;
        }

        private Button CreateTinyButton(string text, string tooltip, RoutedEventHandler onClick)
        {
            Button btn = new Button
            {
                Content = text,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(2, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
            btn.Click += onClick;
            return btn;
        }

        // =========================================================================
        // AGENTE DE VOZ (SPEECH & ANTIGRAVITY CLI)
        // =========================================================================
        private void StartVoiceInputMode()
        {
            _isVoiceActive = true;
            _autoCollapseTimer.Stop();
            _expandedWatchdogTimer.Stop();

            if (_voiceAgent != null && _voiceAgent.IsSpeaking)
            {
                _voiceAgent.StopSpeaking();
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            // 1. Remove temporariamente o estilo WS_EX_NOACTIVATE para a janela receber foco real
            int exStyle = NativeWin32.GetWindowLong(hwnd, NativeWin32.GWL_EXSTYLE);
            exStyle &= ~NativeWin32.WS_EX_NOACTIVATE;
            NativeWin32.SetWindowLong(hwnd, NativeWin32.GWL_EXSTYLE, exStyle);

            // 2. Traz a janela para primeiro plano com Alt-bypass
            NativeWin32.ActivateWindowWithAltBypass(hwnd);

            _voiceInputBox.Visibility = Visibility.Visible;
            _voiceInputBox.Text = "";
            _voiceInputBox.Focus();
            Keyboard.Focus(_voiceInputBox);

            _terminalCodeLine1.Text = "$ agy (escutando voz...)";
            _terminalCodeLine2.Text = "Fale agora... (o envio será automático quando você terminar)";

            _agentStatusText.Text = "\uD83C\uDF99 Ouvindo você...";
            _agentStatusLamp.Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246));

            // 3. Dispara o ditado nativo (Win + H) garantindo foco no campo de texto da ilha
            DispatcherTimer startTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            startTimer.Tick += (s, e) =>
            {
                startTimer.Stop();
                _voiceInputBox.Focus();
                Keyboard.Focus(_voiceInputBox);
                VoiceAgent.TriggerWindowsVoiceDictation();
            };
            startTimer.Start();
        }

        private async void SubmitVoiceCommand(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return;

            _voiceSilenceTimer.Stop();
            _voiceInputBox.Visibility = Visibility.Collapsed;

            // Restaura o modo no-activate para não roubar foco de outras janelas
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeWin32.MakeNonActivating(hwnd);

            // Se houver captura pendente disparada pelo atalho de jogo (Ctrl+Shift+G)
            if (!string.IsNullOrEmpty(_pendingScreenCapture))
            {
                prompt += string.Format(" (Contexto visual do jogo capturado em: {0})", _pendingScreenCapture);
                _pendingScreenCapture = null;
            }
            else
            {
                // Se o usuário mencionar a tela, captura contexto visual automaticamente
                string lowerPrompt = prompt.ToLower();
                if (lowerPrompt.Contains("tela") || lowerPrompt.Contains("print") || lowerPrompt.Contains("screenshot") || lowerPrompt.Contains("veja") || lowerPrompt.Contains("olha"))
                {
                    try
                    {
                        string screenPath = ScreenCapture.Capture(false);
                        prompt += string.Format(" (Contexto visual da tela capturado em: {0})", screenPath);
                    }
                    catch { }
                }
            }

            SetState(AgentStatus.Thinking, "Gemini pensando...");
            _terminalCodeLine1.Text = string.Format("$ agy -p \"{0}\"", prompt);
            _terminalCodeLine2.Text = "Consultando seu Gemini Pro no Antigravity CLI...";

            string answer = await AntigravityCli.RunPromptAsync(prompt, true);

            SetState(AgentStatus.Idle, "Gemini respondeu");
            _terminalCodeLine2.Text = answer;

            // Robo fala a resposta em voz alta com sintetizador natural!
            _voiceAgent.SpeakAsync(answer);
        }

        private void VoiceAgent_SpeakingStarted()
        {
            Dispatcher.Invoke(() =>
            {
                _btnStopVoice.Visibility = Visibility.Visible;
                _soundwavePanel.Visibility = Visibility.Visible;
                _agentStatusText.Text = "\uD83D\uDD0A Robo falando...";

                if (_roboAntennaBulb != null)
                {
                    _roboAntennaBulb.Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                    _roboAntennaBulb.Effect = new DropShadowEffect { Color = Color.FromRgb(59, 130, 246), BlurRadius = 14, ShadowDepth = 0 };
                }
            });
        }

        private void VoiceAgent_SpeakingCompleted()
        {
            Dispatcher.Invoke(() =>
            {
                _btnStopVoice.Visibility = Visibility.Collapsed;
                _soundwavePanel.Visibility = Visibility.Collapsed;
                _agentStatusText.Text = "Waiting for Antigravity...";

                if (_roboAntennaBulb != null)
                {
                    _roboAntennaBulb.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    _roboAntennaBulb.Effect = new DropShadowEffect { Color = Color.FromRgb(16, 185, 129), BlurRadius = 8, ShadowDepth = 0 };
                }

                // Se o usuário estiver jogando (cursor fora da ilha), retrai suavemente para não cobrir o jogo
                if (!_islandBorder.IsMouseOver)
                {
                    _autoCollapseTimer.Stop();
                    _autoCollapseTimer.Interval = TimeSpan.FromMilliseconds(1000);
                    _autoCollapseTimer.Start();
                }
            });
        }

        // =========================================================================
        // ESTADOS E CONTROLE DO AGENTE
        // =========================================================================
        public void SetState(AgentStatus status, string message)
        {
            Dispatcher.Invoke(() =>
            {
                _currentStatus = status;
                _agentStatusText.Text = message;

                Color dotColor;
                switch (status)
                {
                    case AgentStatus.Idle:
                        dotColor = Color.FromRgb(16, 185, 129);
                        _permissionButtonsPanel.Visibility = Visibility.Collapsed;
                        break;

                    case AgentStatus.Thinking:
                        dotColor = Color.FromRgb(59, 130, 246);
                        _permissionButtonsPanel.Visibility = Visibility.Collapsed;
                        break;

                    case AgentStatus.WaitingPermission:
                        dotColor = Color.FromRgb(245, 158, 11);
                        AnimateToDashboard();
                        break;

                    case AgentStatus.ExecutingTool:
                        dotColor = Color.FromRgb(168, 85, 247);
                        _permissionButtonsPanel.Visibility = Visibility.Collapsed;
                        break;

                    default:
                        dotColor = Color.FromRgb(16, 185, 129);
                        break;
                }

                _agentStatusLamp.Fill = new SolidColorBrush(dotColor);
                if (_pillLamp != null)
                {
                    _pillLamp.Fill = new SolidColorBrush(dotColor);
                }
                if (_pillStatusText != null)
                {
                    _pillStatusText.Text = status == AgentStatus.Idle ? "Idle" :
                                           status == AgentStatus.Thinking ? "Pensando..." :
                                           status == AgentStatus.WaitingPermission ? "Permissao" :
                                           status == AgentStatus.ExecutingTool ? "Executando..." : message;
                }
                if (_roboAntennaBulb != null && !_voiceAgent.IsSpeaking)
                {
                    _roboAntennaBulb.Fill = new SolidColorBrush(dotColor);
                    _roboAntennaBulb.Effect = new DropShadowEffect { Color = dotColor, BlurRadius = 8, ShadowDepth = 0 };
                }
            });
        }

        public void ShowPermissionPrompt(ToolPermissionRequest req, Action<bool, string> callback)
        {
            Dispatcher.Invoke(() =>
            {
                _currentRequest = req;
                _currentPermissionCallback = callback;

                _terminalCodeLine1.Text = string.Format("$ {0}", req.ToolName);
                _terminalCodeLine2.Text = req.ToolArgs ?? "Deseja permitir a execucao?";
                _permissionButtonsPanel.Visibility = Visibility.Visible;
                _voiceInputBox.Visibility = Visibility.Collapsed;

                SetState(AgentStatus.WaitingPermission, string.Format("Permissao: {0}", req.ToolName));
            });
        }

        private void AnswerPermission(bool allow, string reason)
        {
            if (_currentPermissionCallback != null)
            {
                var cb = _currentPermissionCallback;
                _currentPermissionCallback = null;
                cb(allow, reason);
            }

            _permissionButtonsPanel.Visibility = Visibility.Collapsed;
            _autoCollapseTimer.Stop();
            _autoCollapseTimer.Interval = TimeSpan.FromMilliseconds(120);
            _autoCollapseTimer.Start();
        }

        private void CollapseToPillClean()
        {
            if (_isVoiceActive) return;
            if (_voiceInputBox != null)
            {
                _voiceInputBox.Visibility = Visibility.Collapsed;
            }
            Keyboard.ClearFocus();
            AnimateToPill();
        }

        private void AnimateToDashboard()
        {
            if (_isExpanded) return;
            _isExpanded = true;
            if (!_isVoiceActive) _expandedWatchdogTimer.Start();

            Duration duration = new Duration(TimeSpan.FromMilliseconds(240));
            CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            DoubleAnimation widthAnim = new DoubleAnimation(DASHBOARD_WIDTH, duration) { EasingFunction = ease };
            DoubleAnimation heightAnim = new DoubleAnimation(DASHBOARD_HEIGHT, duration) { EasingFunction = ease };

            _islandBorder.BeginAnimation(WidthProperty, widthAnim);
            _islandBorder.BeginAnimation(HeightProperty, heightAnim);
            _islandBorder.CornerRadius = new CornerRadius(DASHBOARD_RADIUS);

            _pillLayout.Visibility = Visibility.Collapsed;
            _dashboardLayout.Visibility = Visibility.Visible;

            DoubleAnimation fadeIn = new DoubleAnimation(1.0, new Duration(TimeSpan.FromMilliseconds(160)));
            _dashboardLayout.BeginAnimation(OpacityProperty, fadeIn);
        }

        private void AnimateToPill()
        {
            if (!_isExpanded) return;
            _isExpanded = false;
            _expandedWatchdogTimer.Stop();

            Duration duration = new Duration(TimeSpan.FromMilliseconds(200));
            CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            DoubleAnimation widthAnim = new DoubleAnimation(PILL_WIDTH, duration) { EasingFunction = ease };
            DoubleAnimation heightAnim = new DoubleAnimation(PILL_HEIGHT, duration) { EasingFunction = ease };

            _islandBorder.BeginAnimation(WidthProperty, widthAnim);
            _islandBorder.BeginAnimation(HeightProperty, heightAnim);
            _islandBorder.CornerRadius = new CornerRadius(PILL_RADIUS);

            _dashboardLayout.Visibility = Visibility.Collapsed;
            _dashboardLayout.Opacity = 0;
            _pillLayout.Visibility = Visibility.Visible;
            _pillLayout.Opacity = 1;
        }

        private void CaptureScreenContext()
        {
            try
            {
                Opacity = 0;
                System.Threading.Thread.Sleep(50);

                string filePath = ScreenCapture.Capture(false);
                Opacity = 1;

                Clipboard.SetText(filePath);
                _terminalCodeLine2.Text = "\uD83D\uDCF7 Print copiado para o Clipboard!";
            }
            catch (Exception ex)
            {
                Opacity = 1;
                _terminalCodeLine2.Text = "Erro print: " + ex.Message;
            }
        }

        private void FocusTerminal()
        {
            if (_currentRequest != null && _currentRequest.Pid > 0)
            {
                try
                {
                    Process proc = Process.GetProcessById(_currentRequest.Pid);
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        NativeWin32.ActivateWindowWithAltBypass(proc.MainWindowHandle);
                        return;
                    }
                }
                catch { }
            }

            Process[] procs = Process.GetProcessesByName("WindowsTerminal");
            if (procs.Length == 0) procs = Process.GetProcessesByName("wt");
            if (procs.Length == 0) procs = Process.GetProcessesByName("Code");

            if (procs.Length > 0 && procs[0].MainWindowHandle != IntPtr.Zero)
            {
                NativeWin32.ActivateWindowWithAltBypass(procs[0].MainWindowHandle);
            }
        }

        private void IslandWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (_currentStatus == AgentStatus.WaitingPermission)
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    AnswerPermission(true, "");
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    AnswerPermission(false, "Cancelado via tecla Escape.");
                }
            }
        }
    }
}
