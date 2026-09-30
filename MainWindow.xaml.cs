using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace FirawH4K;

public partial class MainWindow : Window
{
    private const int HotkeyId = 9001;
    private const int DestructiveHotkeyId = 9002;
    private readonly DispatcherTimer gooseTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly OverlayWindow overlay = new();
    private readonly OverlayWindow infiltrator = new();
    private readonly MatrixTrailWindow matrixTrail = new();
    private readonly MatrixTrailWindow redMatrixTrail = new();
    private readonly FootprintWindow footprintTrail = new();
    private readonly RiftEffectWindow riftEffect = new();
    private readonly RiftEffectWindow extractionRift = new();
    private readonly FightEffectWindow fightEffect = new();
    private readonly DestructionWindow destructionEffect = new();
    private readonly InputLockService inputLock;
    private readonly CursorVisibilityService cursorVisibility = new();
    private readonly TelemetryWindow telemetryWindow = new();
    private readonly HackPrankWindow[] prankWindows = [new HackPrankWindow(), new HackPrankWindow()];
    private readonly Forms.NotifyIcon trayIcon;
    private readonly Forms.ContextMenuStrip trayMenu = new();
    private Stream? trayIconStream;
    private HwndSource? hwndSource;
    private bool gooseActive;
    private bool destructiveMode;
    private bool exitRequested;
    private bool hasGooseTarget;
    private DateTime pauseUntil;
    private DateTime greenPullAt;
    private DateTime telemetryAt;
    private DateTime extractionStartedAt;
    private DateTime nextFightAt;
    private DateTime fightEndAt;
    private DateTime fightStartedAt;
    private DateTime nextDamageAt;
    private DateTime nextFractureAt;
    private DateTime nextFightJoltAt;
    private DateTime nextFightDriftAt;
    private DateTime nextDisplayCheck;
    private DateTime nextLayerRaise;
    private DateTime redPullAt;
    private DateTime greenPauseUntil;
    private DateTime redPauseUntil;
    private bool greenSeekingEdge;
    private bool greenSeekingTelemetry;
    private bool greenTransiting;
    private bool redTransiting;
    private bool extractingTelemetry;
    private bool telemetryRevealed;
    private bool fightSeeking;
    private bool fighting;
    private bool redSeekingEdge;
    private System.Drawing.Rectangle greenScreen;
    private System.Drawing.Rectangle fightScreen;
    private System.Drawing.Rectangle redScreen;
    private System.Drawing.Rectangle greenDestinationScreen;
    private System.Drawing.Rectangle redDestinationScreen;
    private double fightX;
    private double fightY;
    private double fightTargetX;
    private double fightTargetY;
    private double fightVelocityX;
    private double fightVelocityY;
    private double fightDuration;
    private DateTime? escHoldStarted;
    private double gooseX;
    private double gooseY;
    private double targetX;
    private double targetY;
    private double travelHeading;
    private double travelSpeed;
    private bool hasTravelHeading;
    private DateTime gooseStartedAt;
    private DateTime lastGooseTick = DateTime.UtcNow;
    private bool infiltratorActive;
    private bool infiltratorEmerging;
    private DateTime infiltratorBirthAt;
    private bool hasInfiltratorTarget;
    private DateTime nextInfiltratorAt;
    private double infiltratorX;
    private double infiltratorY;
    private double infiltratorTargetX;
    private double infiltratorTargetY;
    private double infiltratorHeading;
    private double infiltratorSpeed;
    private readonly Random random = new();
    private string selectedHotkey = "F12";
    private static readonly string MonitorSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Firaw-h4K", "monitors.json");

    public MainWindow()
    {
        InitializeComponent();
        inputLock = new InputLockService(Dispatcher);
        inputLock.EmergencyStop += StopGoose;
        LoadMonitorSelection();
        gooseTimer.Tick += GooseTimer_Tick;
        StateChanged += Window_StateChanged;

        trayIcon = new Forms.NotifyIcon
        {
            Text = "Firaw - h4K",
            Visible = true,
            Icon = LoadTrayIcon()
        };
        trayIcon.DoubleClick += (_, _) => ShowFromTray();
        trayMenu.Items.Add("Abrir Firaw - h4K", null, (_, _) => ShowFromTray());
        trayMenu.Items.Add("Verificar atualizações", null, async (_, _) => await UpdateService.CheckAsync(true, () => gooseActive));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("Sair", null, (_, _) => ExitFromTray());
        trayIcon.ContextMenuStrip = trayMenu;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
        hwndSource.AddHook(WndProc);
        RegisterEmergencyHotkey();
        Dispatcher.BeginInvoke(async () => await UpdateService.CheckAsync(false, () => gooseActive), DispatcherPriority.Background);
#if DEBUG
        if (Environment.GetEnvironmentVariable("FIRAW_H4K_VISUAL_PREVIEW") == "1")
            Dispatcher.BeginInvoke(() => StartGoose(true));
#endif
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!exitRequested)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        StopGoose();
        UnregisterEmergencyHotkey();
        hwndSource?.RemoveHook(WndProc);
        trayIcon.Visible = false;
        trayIcon.Dispose();
        trayMenu.Dispose();
        trayIconStream?.Dispose();
        overlay.Close();
        infiltrator.Close();
        matrixTrail.Close();
        redMatrixTrail.Close();
        footprintTrail.Close();
        riftEffect.Close();
        extractionRift.Close();
        fightEffect.Close();
        destructionEffect.Close();
        inputLock.Dispose();
        cursorVisibility.Dispose();
        telemetryWindow.Close();
        foreach (var prankWindow in prankWindows)
            prankWindow.Close();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            HideToTray();
    }

    private void HotkeyCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (HotkeyCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
        {
            selectedHotkey = tag;
            if (hwndSource != null)
            {
                UnregisterEmergencyHotkey();
                RegisterEmergencyHotkey();
            }
        }
    }

    private void SpeedSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        overlay.SetAnimationSpeed(e.NewValue);
        infiltrator.SetAnimationSpeed(e.NewValue);
    }

    private void MonitorsButton_Click(object sender, RoutedEventArgs e)
    {
        var screens = Forms.Screen.AllScreens.Where(screen => screen.Bounds.Width > 0 && screen.Bounds.Height > 0).ToArray();
        var allowed = MonitorRouting.GetEnabledDevices();
        var popup = new Window
        {
            Title = "Telas do Firaw - h4K",
            Width = 400,
            Height = Math.Min(390, 155 + screens.Length * 45),
            MinWidth = 350,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Topmost = true,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(11, 17, 22)),
            Foreground = System.Windows.Media.Brushes.White
        };
        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(new TextBlock { Text = "Onde os besouros podem andar?", FontSize = 16, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "O Windows lista telas com área de trabalho ativa. Desmarque alguma que permaneça listada mesmo apagada.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 13), Foreground = System.Windows.Media.Brushes.LightGray });
        var checks = new List<(string Device, System.Windows.Controls.CheckBox Check)>();
        foreach (var screen in screens)
        {
            var check = new System.Windows.Controls.CheckBox
            {
                Content = $"{screen.DeviceName.Replace("\\\\.\\", "Tela ")}  ·  {screen.Bounds.Width} × {screen.Bounds.Height}{(screen.Primary ? "  (principal)" : "")}",
                IsChecked = allowed == null || allowed.Contains(screen.DeviceName, StringComparer.OrdinalIgnoreCase),
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 13
            };
            checks.Add((screen.DeviceName, check));
            content.Children.Add(check);
        }
        var save = new System.Windows.Controls.Button { Content = "SALVAR TELAS", Height = 36, Margin = new Thickness(0, 10, 0, 0) };
        save.Click += (_, _) =>
        {
            var selected = checks.Where(item => item.Check.IsChecked == true).Select(item => item.Device).ToArray();
            if (selected.Length == 0)
            {
                System.Windows.MessageBox.Show(popup, "Selecione pelo menos uma tela.", "Firaw - h4K", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            MonitorRouting.SetEnabledDevices(selected);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MonitorSettingsPath)!);
                File.WriteAllText(MonitorSettingsPath, JsonSerializer.Serialize(selected));
            }
            catch { /* Runtime selection still applies if local storage is unavailable. */ }
            RefreshActiveScreens();
            popup.DialogResult = true;
        };
        content.Children.Add(save);
        popup.Content = content;
        popup.ShowDialog();
    }

    private void LoadMonitorSelection()
    {
        try
        {
            if (!File.Exists(MonitorSettingsPath)) return;
            var devices = JsonSerializer.Deserialize<string[]>(File.ReadAllText(MonitorSettingsPath));
            if (devices is { Length: > 0 }) MonitorRouting.SetEnabledDevices(devices);
        }
        catch { /* The default is all active Windows desktop screens. */ }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) => StartGoose(false);

    private void DestructiveButton_Click(object sender, RoutedEventArgs e)
    {
        var consent = new DestructiveConsentWindow { Owner = this };
        if (consent.ShowDialog() == true) StartGoose(true);
    }

    private void StartGoose(bool destructive)
    {
        if (gooseActive)
            return;

        var pointer = Forms.Cursor.Position;
        var availableScreens = MonitorRouting.ActiveDesktopScreens();
        var bounds = availableScreens.FirstOrDefault(screen => screen.Contains(pointer));
        if (bounds == System.Drawing.Rectangle.Empty)
            bounds = availableScreens.FirstOrDefault();
        if (bounds == System.Drawing.Rectangle.Empty)
            bounds = GetMonitorBounds(pointer.X, pointer.Y);
        greenScreen = bounds;
        gooseX = ClampX(pointer.X, bounds, 120);
        gooseY = ClampY(pointer.Y, bounds, 130);
        hasGooseTarget = false;
        hasTravelHeading = false;
        travelSpeed = 0;
        infiltratorActive = false;
        hasInfiltratorTarget = false;
        infiltrator.Hide();
        overlay.SetDefaultSkin();
        gooseStartedAt = DateTime.UtcNow;
        lastGooseTick = gooseStartedAt;
        nextInfiltratorAt = gooseStartedAt.AddSeconds(destructive ? 3.7 : 7.5 + random.NextDouble() * 4.5);
        greenPullAt = gooseStartedAt.AddSeconds(destructive ? 0.9 : 2.5);
        greenPauseUntil = DateTime.MinValue;
        redPauseUntil = DateTime.MinValue;
        greenSeekingEdge = false;
        greenSeekingTelemetry = false;
        infiltratorEmerging = false;
        greenTransiting = false;
        redTransiting = false;
        extractingTelemetry = false;
        telemetryRevealed = false;
        fightSeeking = false;
        fighting = false;
        overlay.SetFighting(false);
        infiltrator.SetFighting(false);
        telemetryAt = DateTime.MaxValue;
        nextFightAt = gooseStartedAt.AddSeconds(destructive ? 9 : 23 + random.NextDouble() * 12);
        nextDisplayCheck = gooseStartedAt;
        nextLayerRaise = gooseStartedAt;
        redSeekingEdge = false;
        pauseUntil = gooseStartedAt.AddMilliseconds(650);
        gooseActive = true;
        destructiveMode = destructive;

        matrixTrail.StartTrail(System.Windows.Media.Color.FromRgb(66, 245, 179));
        footprintTrail.StartTrail();
        overlay.Show();
        overlay.MoveToPoint((int)Math.Round(gooseX), (int)Math.Round(gooseY), travelHeading, 0);
        overlay.PlayActivationAnimation();
        riftEffect.PlayEmergenceAt(gooseX, gooseY, red: false);
        gooseTimer.Start();
        StartMascotAnimation();

        StartButton.IsEnabled = false;
        DestructiveButton.IsEnabled = false;
        ModeBadgeText.Text = destructive ? "MODO DESESPERO" : "MODO SEGURO";
        SubtitleText.Text = destructive ? "Confronto contínuo • marcas na tela" : "Desktop Goose • mascote autônomo";
        SafetyHintText.Text = destructive
            ? "Mouse e teclado bloqueados, ponteiro oculto até Ctrl+Alt+F12. Ctrl+Alt+Del permanece disponível pelo Windows."
            : "Segure Esc por 1 segundo para parar. O modo normal é reversível e não bloqueia teclado, mouse ou o Windows.";
        FooterStopHint.Text = destructive ? "Ctrl+Alt+F12 para parar" : "segure Esc ou use o atalho configurado";
        StatusText.Text = destructive ? "Status: modo desespero ativo — Ctrl+Alt+F12 para parar" : "Status: Desktop Goose ativo — use o atalho configurado para parar";
        if (destructive)
        {
#if DEBUG
            var visualPreview = Environment.GetEnvironmentVariable("FIRAW_H4K_VISUAL_PREVIEW") == "1";
            if (visualPreview)
            {
                if (Environment.GetEnvironmentVariable("FIRAW_H4K_DISABLE_FRACTURE") != "1")
                    destructionEffect.StartEffect();
                return;
            }
#endif
            WindowState = WindowState.Minimized;
            Dispatcher.BeginInvoke(async () =>
            {
                // The visual frame must never freeze the animated desktop.
                await Task.Delay(300);
                if (!gooseActive || !destructiveMode) return;
                destructionEffect.StartEffect();
                if (!inputLock.Start())
                {
                    StatusText.Text = "Status: trava global indisponível; efeito visual ativo";
                    trayIcon.ShowBalloonTip(1800, "Firaw - h4K", "A trava de entrada não pôde ser iniciada. Ctrl+Alt+F12 para parar.", Forms.ToolTipIcon.Warning);
                }
                else if (!cursorVisibility.Start())
                {
                    StatusText.Text = "Status: modo ativo; não foi possível ocultar o ponteiro globalmente";
                }
            });
        }
    }

    private void StopGoose()
    {
        var stoppedDestructiveMode = gooseActive && destructiveMode;
        gooseActive = false;
        escHoldStarted = null;
        gooseTimer.Stop();
        StopMascotAnimation();
        overlay.Hide();
        infiltrator.Hide();
        infiltratorActive = false;
        matrixTrail.StopTrail();
        redMatrixTrail.StopTrail();
        footprintTrail.StopTrail();
        foreach (var prankWindow in prankWindows)
            prankWindow.HidePrank();
        telemetryWindow.HideMonitor();
        extractionRift.Hide();
        fightEffect.StopEffect();
        destructionEffect.StopEffect();
        cursorVisibility.Stop();
        inputLock.Stop();
        overlay.SetPulling(false);
        greenSeekingEdge = false;
        greenSeekingTelemetry = false;
        infiltratorEmerging = false;
        greenTransiting = false;
        redTransiting = false;
        extractingTelemetry = false;
        fightSeeking = false;
        fighting = false;
        overlay.SetFighting(false);
        infiltrator.SetFighting(false);
        redSeekingEdge = false;
        riftEffect.Hide();
        StartButton.IsEnabled = true;
        DestructiveButton.IsEnabled = true;
        destructiveMode = false;
        ModeBadgeText.Text = "MODO SEGURO";
        SubtitleText.Text = "Desktop Goose • mascote autônomo";
        SafetyHintText.Text = "Segure Esc por 1 segundo para parar. O modo normal é reversível e não bloqueia teclado, mouse ou o Windows.";
        FooterStopHint.Text = "segure Esc ou use o atalho configurado";
        StatusText.Text = "Status: parado";
        if (stoppedDestructiveMode && !exitRequested)
            trayIcon.ShowBalloonTip(1200, "Firaw - h4K", "Modo desespero encerrado. Mouse e teclado liberados.", Forms.ToolTipIcon.Info);
    }

    private void GooseTimer_Tick(object? sender, EventArgs e)
    {
        if (!gooseActive)
            return;

        var now = DateTime.UtcNow;
        if (destructiveMode &&
            (GetAsyncKeyState(0x11) & 0x8000) != 0 &&
            (GetAsyncKeyState(0x12) & 0x8000) != 0 &&
            (GetAsyncKeyState(0x7B) & 0x8000) != 0)
        {
            StopGoose();
            return;
        }
        cursorVisibility.KeepAlive();
        var seconds = Math.Clamp((now - lastGooseTick).TotalSeconds, 0.001, 0.20);
        lastGooseTick = now;
        var bounds = greenScreen;

        if ((GetAsyncKeyState(0x1B) & 0x8000) != 0)
        {
            if (!destructiveMode)
            {
                escHoldStarted ??= now;
                if (now - escHoldStarted.Value >= TimeSpan.FromSeconds(1))
                {
                    StopGoose();
                    return;
                }
            }
        }
        else
        {
            escHoldStarted = null;
        }

        if (now >= nextDisplayCheck)
        {
            RefreshActiveScreens();
            nextDisplayCheck = now.AddSeconds(3);
            bounds = greenScreen;
        }

        if (now >= nextLayerRaise)
        {
            // Other normal topmost windows may be raised after the mascots.
            // Keep the two click-through beetles ahead without stealing focus.
            overlay.RaiseAbovePanels();
            infiltrator.RaiseAbovePanels();
            nextLayerRaise = now.AddMilliseconds(240);
        }

        if (extractingTelemetry)
        {
            UpdateTelemetryExtraction(now);
            UpdateInfiltrator(bounds, now, seconds);
            return;
        }

        if (UpdateFight(now, seconds))
            return;

        if (now < greenPauseUntil)
        {
            overlay.MoveToPoint((int)Math.Round(gooseX), (int)Math.Round(gooseY), travelHeading, 0);
            UpdateInfiltrator(bounds, now, seconds);
            return;
        }

        if (!greenSeekingEdge && !prankWindows[0].IsVisible && now >= greenPullAt)
        {
            ChooseEdgeTarget(bounds, gooseX, gooseY, out targetX, out targetY);
            greenSeekingEdge = true;
            hasGooseTarget = true;
            pauseUntil = now;
        }

        if (!greenSeekingEdge && !greenSeekingTelemetry && prankWindows[0].IsVisible && !telemetryWindow.IsVisible && now >= telemetryAt)
        {
            targetX = bounds.Left + bounds.Width * 0.54;
            targetY = bounds.Top + bounds.Height * 0.54;
            greenSeekingTelemetry = true;
            hasGooseTarget = true;
            pauseUntil = now;
        }

        if (!hasGooseTarget)
            ChooseGooseTarget(bounds, now, immediate: false);

        var motionSpeed = 0.0;
        if (now >= pauseUntil)
        {
            var dx = targetX - gooseX;
            var dy = targetY - gooseY;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance < 34)
            {
                if (greenSeekingEdge)
                    PullPanel(0, gooseX, gooseY, bounds, now);
                else if (greenSeekingTelemetry)
                    PullTelemetry(gooseX, gooseY, bounds, now);
                else if (greenTransiting)
                {
                    greenScreen = greenDestinationScreen;
                    greenTransiting = false;
                    ChooseGooseTarget(greenScreen, now, immediate: true);
                }
                else
                    ChooseGooseTarget(bounds, now, immediate: false);
            }
            else
            {
                var desiredHeading = Math.Atan2(dy, dx);
                if (!hasTravelHeading)
                {
                    travelHeading = desiredHeading;
                    hasTravelHeading = true;
                }

                var headingError = NormalizeAngle(desiredHeading - travelHeading);
                // A heading with a limited turn rate produces a natural curve,
                // while the target itself is regenerated randomly over the whole screen.
                var maxTurn = (greenSeekingEdge || greenSeekingTelemetry || greenTransiting ? 4.2 : 2.4 + SpeedSlider.Value * 0.2) * seconds;
                travelHeading = NormalizeAngle(travelHeading + Math.Clamp(headingError, -maxTurn, maxTurn));

                var desiredSpeed = (190 + SpeedSlider.Value * 44) * (greenSeekingEdge && distance < 190 ? 0.72 : 1.0);
                travelSpeed += (desiredSpeed - travelSpeed) * Math.Clamp(seconds * 10, 0, 1);
                motionSpeed = travelSpeed;
                gooseX += Math.Cos(travelHeading) * travelSpeed * seconds;
                gooseY += Math.Sin(travelHeading) * travelSpeed * seconds;

                var safeLeft = bounds.Left + 78;
                var safeRight = bounds.Right - 78;
                var safeTop = bounds.Top + 92;
                var safeBottom = bounds.Bottom - 92;
                if (!greenTransiting && (gooseX <= safeLeft || gooseX >= safeRight || gooseY <= safeTop || gooseY >= safeBottom))
                {
                    gooseX = Math.Clamp(gooseX, safeLeft, safeRight);
                    gooseY = Math.Clamp(gooseY, safeTop, safeBottom);
                    if (greenSeekingEdge)
                        PullPanel(0, gooseX, gooseY, bounds, now);
                    else if (greenSeekingTelemetry)
                        PullTelemetry(gooseX, gooseY, bounds, now);
                    else
                        ChooseGooseTarget(bounds, now, immediate: true);
                }
            }
        }

        overlay.MoveToPoint((int)Math.Round(gooseX), (int)Math.Round(gooseY), travelHeading, motionSpeed);
        matrixTrail.AddTrailPoint(gooseX, gooseY);
        footprintTrail.AddFootprints(gooseX, gooseY, travelHeading, red: false);
        UpdateInfiltrator(bounds, now, seconds);
    }

    private void UpdateInfiltrator(System.Drawing.Rectangle bounds, DateTime now, double seconds)
    {
        if (!infiltratorActive)
        {
            if (now < nextInfiltratorAt)
                return;

            if (!infiltratorEmerging)
            {
                infiltratorEmerging = true;
                infiltratorBirthAt = now.AddMilliseconds(360);
                greenPauseUntil = now.AddMilliseconds(1300);
                riftEffect.PlayShellSplitAt(gooseX, gooseY);
                return;
            }
            if (now < infiltratorBirthAt)
                return;

            infiltratorActive = true;
            infiltratorEmerging = false;
            hasInfiltratorTarget = false;
            infiltratorX = gooseX + 20;
            infiltratorY = gooseY + 18;
            redScreen = greenScreen;
            infiltratorHeading = travelHeading + Math.PI / 2;
            infiltratorSpeed = 0;
            redPullAt = now.AddSeconds(destructiveMode ? 0.9 : 3.0);
            redPauseUntil = now.AddMilliseconds(650);
            redSeekingEdge = false;
            infiltrator.SetInfiltratorSkin();
            redMatrixTrail.StartTrail(System.Windows.Media.Color.FromRgb(255, 66, 82));
            infiltrator.Show();
            infiltrator.MoveToPoint((int)Math.Round(infiltratorX), (int)Math.Round(infiltratorY), infiltratorHeading, 0);
            infiltrator.PlayInfiltratorBirthAnimation();
            return;
        }

        var infiltratorBounds = redScreen;
        if (now < redPauseUntil)
        {
            infiltrator.MoveToPoint((int)Math.Round(infiltratorX), (int)Math.Round(infiltratorY), infiltratorHeading, 0);
            return;
        }

        if (!redSeekingEdge && !prankWindows[1].IsVisible && now >= redPullAt)
        {
            ChooseEdgeTarget(infiltratorBounds, infiltratorX, infiltratorY, out infiltratorTargetX, out infiltratorTargetY);
            hasInfiltratorTarget = true;
            redSeekingEdge = true;
        }
        if (!hasInfiltratorTarget)
            ChooseInfiltratorTarget(infiltratorBounds);

        var dx = infiltratorTargetX - infiltratorX;
        var dy = infiltratorTargetY - infiltratorY;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance < 55)
        {
            if (redSeekingEdge)
            {
                PullPanel(1, infiltratorX, infiltratorY, infiltratorBounds, now);
                return;
            }
            if (redTransiting)
            {
                redScreen = redDestinationScreen;
                infiltratorBounds = redScreen;
                redTransiting = false;
                ChooseInfiltratorTarget(redScreen);
                dx = infiltratorTargetX - infiltratorX;
                dy = infiltratorTargetY - infiltratorY;
            }
            else
            {
                ChooseInfiltratorTarget(infiltratorBounds);
                dx = infiltratorTargetX - infiltratorX;
                dy = infiltratorTargetY - infiltratorY;
            }
        }

        var desiredHeading = Math.Atan2(dy, dx);
        var headingError = NormalizeAngle(desiredHeading - infiltratorHeading);
        var maxTurn = (redSeekingEdge || redTransiting ? 4.4 : 2.8) * seconds;
        infiltratorHeading = NormalizeAngle(infiltratorHeading + Math.Clamp(headingError, -maxTurn, maxTurn));
        var desiredSpeed = (210 + SpeedSlider.Value * 48) * (redSeekingEdge && distance < 190 ? 0.72 : 1.0);
        infiltratorSpeed += (desiredSpeed - infiltratorSpeed) * Math.Clamp(seconds * 10, 0, 1);
        infiltratorX += Math.Cos(infiltratorHeading) * infiltratorSpeed * seconds;
        infiltratorY += Math.Sin(infiltratorHeading) * infiltratorSpeed * seconds;

        var safeLeft = infiltratorBounds.Left + 84;
        var safeRight = infiltratorBounds.Right - 84;
        var safeTop = infiltratorBounds.Top + 98;
        var safeBottom = infiltratorBounds.Bottom - 98;
        if (!redTransiting && (infiltratorX <= safeLeft || infiltratorX >= safeRight || infiltratorY <= safeTop || infiltratorY >= safeBottom))
        {
            infiltratorX = Math.Clamp(infiltratorX, safeLeft, safeRight);
            infiltratorY = Math.Clamp(infiltratorY, safeTop, safeBottom);
            if (redSeekingEdge)
                PullPanel(1, infiltratorX, infiltratorY, infiltratorBounds, now);
            else
                ChooseInfiltratorTarget(infiltratorBounds);
        }

        infiltrator.MoveToPoint((int)Math.Round(infiltratorX), (int)Math.Round(infiltratorY), infiltratorHeading, infiltratorSpeed);
        redMatrixTrail.AddTrailPoint(infiltratorX, infiltratorY);
        footprintTrail.AddFootprints(infiltratorX, infiltratorY, infiltratorHeading, red: true);
    }

    private void ChooseInfiltratorTarget(System.Drawing.Rectangle bounds)
    {
        if (!destructiveMode && prankWindows[1].IsVisible && random.NextDouble() < 0.29 && MonitorRouting.TryChooseNeighbour(bounds, random, out var other, out var gatewayX, out var gatewayY))
        {
            redDestinationScreen = other;
            redTransiting = true;
            infiltratorTargetX = gatewayX;
            infiltratorTargetY = gatewayY;
            hasInfiltratorTarget = true;
            return;
        }
        var margin = 105;
        infiltratorTargetX = bounds.Left + margin + random.NextDouble() * Math.Max(1, bounds.Width - margin * 2);
        infiltratorTargetY = bounds.Top + margin + random.NextDouble() * Math.Max(1, bounds.Height - margin * 2);
        hasInfiltratorTarget = true;
    }

    private void PullPanel(int owner, double actorX, double actorY, System.Drawing.Rectangle bounds, DateTime now)
    {
        if (prankWindows[owner].IsVisible)
            return;
        if (owner == 0)
        {
            greenSeekingEdge = false;
            hasGooseTarget = false;
            greenPauseUntil = now.AddSeconds(0.62);
            travelSpeed = 0;
            telemetryAt = now.AddSeconds(destructiveMode ? 1.3 : 5);
        }
        else
        {
            redSeekingEdge = false;
            hasInfiltratorTarget = false;
            redPauseUntil = now.AddSeconds(0.62);
            infiltratorSpeed = 0;
        }

        riftEffect.PlayAt(bounds, actorX, actorY, red: owner == 1);
        prankWindows[owner].ConfigurePanel(owner, redTheme: owner == 1);
        prankWindows[owner].MoveAway((int)Math.Round(actorX), (int)Math.Round(actorY), true, bounds, owner);
        overlay.RaiseAbovePanels();
        infiltrator.RaiseAbovePanels();
    }

    private void PullTelemetry(double actorX, double actorY, System.Drawing.Rectangle bounds, DateTime now)
    {
        greenSeekingTelemetry = false;
        hasGooseTarget = false;
        greenPauseUntil = now.AddSeconds(3.1);
        travelSpeed = 0;
        extractingTelemetry = true;
        telemetryRevealed = false;
        extractionStartedAt = now;
        overlay.SetPulling(true);
        extractionRift.PlayExtractionAt(actorX, actorY);
        overlay.RaiseAbovePanels();
    }

    private void UpdateTelemetryExtraction(DateTime now)
    {
        var elapsed = (now - extractionStartedAt).TotalSeconds;
        overlay.MoveToPoint(
            (int)Math.Round(gooseX + Math.Sin(elapsed * 15) * 4),
            (int)Math.Round(gooseY + Math.Cos(elapsed * 13) * 3),
            travelHeading + Math.Sin(elapsed * 9) * 0.12,
            0);
        if (!telemetryRevealed && elapsed >= 0.95)
        {
            telemetryRevealed = true;
            var occupied = prankWindows.Where(window => window.IsVisible)
                .Select(window => new Rect(window.Left, window.Top, window.Width, window.Height))
                .ToList();
            if (IsVisible && WindowState != WindowState.Minimized)
                occupied.Add(new Rect(Left, Top, Width, Height));
            telemetryWindow.EmergeAt(gooseX, gooseY, greenScreen, occupied);
            overlay.RaiseAbovePanels();
            infiltrator.RaiseAbovePanels();
        }
        if (elapsed < 3.1) return;
        extractingTelemetry = false;
        overlay.SetPulling(false);
        hasGooseTarget = false;
        greenPauseUntil = DateTime.MinValue;
    }

    private void RefreshActiveScreens()
    {
        var active = MonitorRouting.ActiveDesktopScreens();
        if (active.Length == 0)
        {
            MonitorStatusText.Text = "Nenhuma tela selecionada está ativa; escolha outra tela.";
            return;
        }
        MonitorStatusText.Text = $"Telas ativas no Windows: {active.Length}  •  passeio entre telas adjacentes";
        if (greenTransiting && !active.Contains(greenDestinationScreen))
        {
            greenTransiting = false;
            hasGooseTarget = false;
        }
        if (!active.Contains(greenScreen))
        {
            greenScreen = active.OrderBy(screen => Math.Pow(gooseX - screen.Left - screen.Width / 2.0, 2) + Math.Pow(gooseY - screen.Top - screen.Height / 2.0, 2)).First();
            gooseX = ClampX((int)gooseX, greenScreen, 120);
            gooseY = ClampY((int)gooseY, greenScreen, 130);
            hasGooseTarget = false;
            greenSeekingEdge = greenSeekingTelemetry = greenTransiting = false;
            if (extractingTelemetry)
            {
                extractingTelemetry = false;
                extractionRift.Hide();
                overlay.SetPulling(false);
            }
        }
        if ((fightSeeking || fighting) && !active.Contains(fightScreen))
        {
            fightSeeking = fighting = false;
            overlay.SetFighting(false);
            infiltrator.SetFighting(false);
            fightEffect.StopEffect();
            if (!destructiveMode) destructionEffect.StopEffect();
            nextFightAt = DateTime.UtcNow.AddSeconds(5);
        }
        if (!infiltratorActive) return;
        if (redTransiting && !active.Contains(redDestinationScreen))
        {
            redTransiting = false;
            hasInfiltratorTarget = false;
        }
        if (!active.Contains(redScreen))
        {
            redScreen = active.OrderBy(screen => Math.Pow(infiltratorX - screen.Left - screen.Width / 2.0, 2) + Math.Pow(infiltratorY - screen.Top - screen.Height / 2.0, 2)).First();
            infiltratorX = ClampX((int)infiltratorX, redScreen, 120);
            infiltratorY = ClampY((int)infiltratorY, redScreen, 130);
            hasInfiltratorTarget = false;
            redSeekingEdge = redTransiting = false;
            fightSeeking = fighting = false;
            overlay.SetFighting(false);
            infiltrator.SetFighting(false);
            fightEffect.StopEffect();
            if (!destructiveMode) destructionEffect.StopEffect();
            nextFightAt = DateTime.UtcNow.AddSeconds(5);
        }
    }

    private bool UpdateFight(DateTime now, double seconds)
    {
        if (!fightSeeking && !fighting)
        {
            if (now < nextFightAt || !infiltratorActive || !prankWindows[0].IsVisible || !prankWindows[1].IsVisible || !telemetryWindow.IsVisible || greenSeekingEdge || greenSeekingTelemetry || redSeekingEdge || greenTransiting || redTransiting || now < greenPauseUntil || now < redPauseUntil)
            {
                return false;
            }
            if (greenScreen != redScreen && !MonitorRouting.TryNextHop(redScreen, greenScreen, out _, out _, out _))
            {
                nextFightAt = now.AddSeconds(10);
                return false;
            }
            fightSeeking = true;
            StatusText.Text = "Status: os besouros se aproximam";
            fightScreen = greenScreen;
            var horizontalMargin = Math.Min(220, fightScreen.Width / 4.0);
            var verticalMargin = Math.Min(190, fightScreen.Height / 4.0);
            fightX = Math.Clamp((gooseX + infiltratorX) / 2, fightScreen.Left + horizontalMargin, fightScreen.Right - horizontalMargin);
            fightY = Math.Clamp((gooseY + infiltratorY) / 2, fightScreen.Top + verticalMargin, fightScreen.Bottom - verticalMargin);
            hasGooseTarget = false;
            hasInfiltratorTarget = false;
        }

        if (fightSeeking)
        {
            if (greenScreen != fightScreen)
            {
                if (!MonitorRouting.TryNextHop(greenScreen, fightScreen, out var greenNext, out var greenHopX, out var greenHopY))
                {
                    fightSeeking = false;
                    nextFightAt = now.AddSeconds(2);
                    return false;
                }
                MoveFighter(ref gooseX, ref gooseY, ref travelHeading, ref travelSpeed, greenHopX, greenHopY, seconds);
                if (Math.Abs(gooseX - greenHopX) < 42 && Math.Abs(gooseY - greenHopY) < 42)
                    greenScreen = greenNext;
            }
            else
                MoveFighter(ref gooseX, ref gooseY, ref travelHeading, ref travelSpeed, fightX - 48, fightY, seconds);
            if (redScreen != fightScreen)
            {
                if (!MonitorRouting.TryNextHop(redScreen, fightScreen, out var nextScreen, out var hopX, out var hopY))
                {
                    fightSeeking = false;
                    StatusText.Text = destructiveMode ? "Status: modo desespero ativo — Ctrl+Alt+F12 para parar" : "Status: Desktop Goose ativo — use o atalho configurado para parar";
                    nextFightAt = now.AddSeconds(10);
                    return false;
                }
                MoveFighter(ref infiltratorX, ref infiltratorY, ref infiltratorHeading, ref infiltratorSpeed, hopX, hopY, seconds);
                if (Math.Abs(infiltratorX - hopX) < 42 && Math.Abs(infiltratorY - hopY) < 42)
                    redScreen = nextScreen;
            }
            else
                MoveFighter(ref infiltratorX, ref infiltratorY, ref infiltratorHeading, ref infiltratorSpeed, fightX + 48, fightY, seconds);
            overlay.MoveToPoint((int)gooseX, (int)gooseY, travelHeading, travelSpeed);
            infiltrator.MoveToPoint((int)infiltratorX, (int)infiltratorY, infiltratorHeading, infiltratorSpeed);
            matrixTrail.AddTrailPoint(gooseX, gooseY);
            redMatrixTrail.AddTrailPoint(infiltratorX, infiltratorY);
            footprintTrail.AddFootprints(gooseX, gooseY, travelHeading, red: false);
            footprintTrail.AddFootprints(infiltratorX, infiltratorY, infiltratorHeading, red: true);
            if (greenScreen == fightScreen && redScreen == fightScreen && Math.Abs(gooseX - (fightX - 48)) < 48 && Math.Abs(gooseY - fightY) < 48 && Math.Abs(infiltratorX - (fightX + 48)) < 48 && Math.Abs(infiltratorY - fightY) < 48)
            {
                fightSeeking = false;
                fighting = true;
                StatusText.Text = destructiveMode ? "Status: confronto contínuo" : "Status: confronto";
                fightStartedAt = now;
                fightDuration = destructiveMode ? 2.7 : 5.0;
                fightEndAt = destructiveMode ? DateTime.MaxValue : now.AddSeconds(fightDuration);
                fightVelocityX = fightVelocityY = 0;
                nextFightJoltAt = now;
                nextFightDriftAt = now.AddSeconds(4.5);
                ChooseFightTarget();
                overlay.SetFighting(true);
                infiltrator.SetFighting(true);
                // The neutral blur/displacement is identical in both modes.
                // Only persistent mode adds the frame and lasting damage.
                fightEffect.PlayAt(fightX, fightY, fightScreen, seconds: 2.4);
                if (destructiveMode)
                    destructionEffect.AddImpact(fightX, fightY, fightScreen);
                nextDamageAt = now.AddSeconds(2.6);
                nextFractureAt = now.AddMilliseconds(800);
                overlay.RaiseAbovePanels();
                infiltrator.RaiseAbovePanels();
            }
            return true;
        }

        if (!destructiveMode && now >= fightEndAt)
        {
            fighting = false;
            overlay.SetFighting(false);
            infiltrator.SetFighting(false);
            StatusText.Text = destructiveMode ? "Status: modo desespero ativo — Ctrl+Alt+F12 para parar" : "Status: Desktop Goose ativo — use o atalho configurado para parar";
            fightEffect.StopEffect();
            if (!destructiveMode)
                destructionEffect.StopEffect();
            nextFightAt = destructiveMode ? now : now.AddSeconds(24 + random.NextDouble() * 20);
            hasGooseTarget = false;
            hasInfiltratorTarget = false;
            return true;
        }

        if (now >= nextFightDriftAt || Math.Sqrt(Math.Pow(fightTargetX - fightX, 2) + Math.Pow(fightTargetY - fightY, 2)) < 90)
        {
            ChooseFightTarget();
            nextFightDriftAt = now.AddSeconds(2.2 + random.NextDouble() * 1.5);
        }
        // The fight drifts organically and recoils on contact instead of
        // marching in one formation. The red beetle circles the green one,
        // mirroring the motion used by the site's beetle encounter.
        var dx = fightTargetX - fightX;
        var dy = fightTargetY - fightY;
        var distance = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        var driftSpeed = destructiveMode ? 225.0 : 135.0;
        var desiredVx = dx / distance * driftSpeed;
        var desiredVy = dy / distance * driftSpeed;
        if (now >= nextFightJoltAt)
        {
            fightVelocityX += random.Next(-125, 126);
            fightVelocityY += random.Next(-125, 126);
            nextFightJoltAt = now.AddMilliseconds(random.Next(290, 760));
        }
        fightVelocityX += (desiredVx - fightVelocityX) * Math.Clamp(seconds * 2.3, 0, 1);
        fightVelocityY += (desiredVy - fightVelocityY) * Math.Clamp(seconds * 2.3, 0, 1);
        fightX = Math.Clamp(fightX + fightVelocityX * seconds, fightScreen.Left + 105, fightScreen.Right - 105);
        fightY = Math.Clamp(fightY + fightVelocityY * seconds, fightScreen.Top + 110, fightScreen.Bottom - 110);
        var phase = (now - fightStartedAt).TotalSeconds * 12.0;
        var recoil = Math.Sin(phase * 1.55) * 9;
        gooseX = fightX + Math.Cos(phase * .75) * 10 - Math.Cos(phase) * recoil * .4;
        gooseY = fightY + Math.Sin(phase * .7) * 9 - Math.Sin(phase) * recoil * .4;
        infiltratorX = fightX + Math.Cos(phase) * (48 + recoil);
        infiltratorY = fightY + Math.Sin(phase * 1.18) * (36 + recoil);
        travelHeading = Math.Atan2(infiltratorY - gooseY, infiltratorX - gooseX) + Math.PI / 2;
        infiltratorHeading = Math.Atan2(gooseY - infiltratorY, gooseX - infiltratorX) + Math.PI / 2;
        overlay.MoveToPoint((int)gooseX, (int)gooseY, travelHeading, 0);
        infiltrator.MoveToPoint((int)infiltratorX, (int)infiltratorY, infiltratorHeading, 0);
        if (now >= nextDamageAt)
        {
            fightEffect.PlayAt((gooseX + infiltratorX) / 2, (gooseY + infiltratorY) / 2,
                fightScreen, seconds: 2.4);
            nextDamageAt = now.AddSeconds(2.6);
            overlay.RaiseAbovePanels();
            infiltrator.RaiseAbovePanels();
        }
        if (destructiveMode && now >= nextFractureAt)
        {
            destructionEffect.AddImpact((gooseX + infiltratorX) / 2,
                (gooseY + infiltratorY) / 2, fightScreen);
            nextFractureAt = now.AddMilliseconds(800);
        }
        matrixTrail.AddTrailPoint(gooseX, gooseY);
        redMatrixTrail.AddTrailPoint(infiltratorX, infiltratorY);
        // Footprints belong to walking; during combat only impact damage is left.
        return true;
    }

    private void ChooseFightTarget()
    {
        var marginX = Math.Min(180, fightScreen.Width * .22);
        var marginY = Math.Min(165, fightScreen.Height * .22);
        fightTargetX = fightScreen.Left + marginX + random.NextDouble() * Math.Max(1, fightScreen.Width - marginX * 2);
        fightTargetY = fightScreen.Top + marginY + random.NextDouble() * Math.Max(1, fightScreen.Height - marginY * 2);
    }

    private static void MoveFighter(ref double x, ref double y, ref double heading, ref double speed, double targetX, double targetY, double seconds)
    {
        var dx = targetX - x;
        var dy = targetY - y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 12) { speed = 0; return; }
        var desired = Math.Atan2(dy, dx);
        heading = NormalizeAngle(heading + Math.Clamp(NormalizeAngle(desired - heading), -5.5 * seconds, 5.5 * seconds));
        speed += (Math.Min(460, distance * 2.2) - speed) * Math.Clamp(seconds * 8, 0, 1);
        x += Math.Cos(heading) * speed * seconds;
        y += Math.Sin(heading) * speed * seconds;
    }

    private void ChooseEdgeTarget(System.Drawing.Rectangle bounds, double x, double y, out double edgeX, out double edgeY)
    {
        var corners = new[]
        {
            (X: (double)bounds.Left + 105, Y: (double)bounds.Top + 115),
            (X: (double)bounds.Right - 105, Y: (double)bounds.Top + 115),
            (X: (double)bounds.Left + 105, Y: (double)bounds.Bottom - 115),
            (X: (double)bounds.Right - 105, Y: (double)bounds.Bottom - 115)
        };
        var choices = corners.OrderBy(point => Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2)).Take(2).ToArray();
        var selected = choices[random.Next(choices.Length)];
        edgeX = selected.X;
        edgeY = selected.Y;
    }

    private void ChooseGooseTarget(System.Drawing.Rectangle bounds, DateTime now, bool immediate)
    {
        if (telemetryWindow.IsVisible && random.NextDouble() < 0.29 && MonitorRouting.TryChooseNeighbour(bounds, random, out var other, out var gatewayX, out var gatewayY))
        {
            greenDestinationScreen = other;
            greenTransiting = true;
            targetX = gatewayX;
            targetY = gatewayY;
            hasGooseTarget = true;
            pauseUntil = now;
            return;
        }
        var margin = 90;
        var left = bounds.Left + margin;
        var right = bounds.Right - margin;
        var top = bounds.Top + margin;
        var bottom = bounds.Bottom - margin;
        if (left > right)
            left = right = bounds.Left + (bounds.Width / 2);
        if (top > bottom)
            top = bottom = bounds.Top + (bounds.Height / 2);

        if (MovementMode.SelectedIndex == 2)
        {
            switch (random.Next(4))
            {
                case 0:
                    targetX = left;
                    targetY = top + random.NextDouble() * Math.Max(1, bottom - top);
                    break;
                case 1:
                    targetX = right;
                    targetY = top + random.NextDouble() * Math.Max(1, bottom - top);
                    break;
                case 2:
                    targetX = left + random.NextDouble() * Math.Max(1, right - left);
                    targetY = top;
                    break;
                default:
                    targetX = left + random.NextDouble() * Math.Max(1, right - left);
                    targetY = bottom;
                    break;
            }
        }
        else
        {
            // Passeio livre: o próximo ponto nasce em qualquer região da tela.
            // O besouro não usa uma rota fixa; a curva entre pontos é guiada pela
            // velocidade e pelo limite de giro do corpo.
            var attempts = 0;
            do
            {
                targetX = left + random.NextDouble() * Math.Max(1, right - left);
                targetY = top + random.NextDouble() * Math.Max(1, bottom - top);
                attempts++;
            }
            while (attempts < 12 && Math.Sqrt(Math.Pow(targetX - gooseX, 2) + Math.Pow(targetY - gooseY, 2)) < 240);
        }

        hasGooseTarget = true;
        pauseUntil = now.AddMilliseconds(immediate ? 0 : random.Next(0, 130));
        if (!hasTravelHeading)
            travelHeading = Math.Atan2(targetY - gooseY, targetX - gooseX);
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > Math.PI) angle -= Math.PI * 2;
        while (angle < -Math.PI) angle += Math.PI * 2;
        return angle;
    }

    private static int ClampX(int value, System.Drawing.Rectangle bounds, int margin)
    {
        return Math.Clamp(value, bounds.Left + margin, bounds.Right - margin);
    }

    private static int ClampY(int value, System.Drawing.Rectangle bounds, int margin)
    {
        return Math.Clamp(value, bounds.Top + margin, bounds.Bottom - margin);
    }

    private static System.Drawing.Rectangle GetMonitorBounds(int x, int y)
    {
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point(x, y));
        return screen.Bounds.Width > 0 && screen.Bounds.Height > 0
            ? screen.Bounds
            : Forms.SystemInformation.VirtualScreen;
    }

    private System.Drawing.Icon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/firaw-beetle-v3.ico"));
        if (resource == null)
            return System.Drawing.SystemIcons.Application;

        trayIconStream = resource.Stream;
        return new System.Drawing.Icon(trayIconStream);
    }

    private void HideToTray()
    {
        Hide();
        trayIcon.ShowBalloonTip(1200, "Firaw - h4K", "O Desktop Goose continua ativo na bandeja.", Forms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        exitRequested = true;
        Close();
    }

    private void StartMascotAnimation()
    {
        if (MascotImage.RenderTransform is not ScaleTransform scale)
            return;

        var pulse = new DoubleAnimation(0.82, 0.9, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private void StopMascotAnimation()
    {
        if (MascotImage.RenderTransform is not ScaleTransform scale)
            return;

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = 0.82;
        scale.ScaleY = 0.82;
    }

    private void RegisterEmergencyHotkey()
    {
        if (hwndSource == null)
            return;

        const uint ModAlt = 0x0001;
        const uint ModControl = 0x0002;
        const uint ModShift = 0x0004;
        var modifiers = selectedHotkey == "SHIFT_F12" ? ModControl | ModShift : ModControl | ModAlt;
        var key = selectedHotkey == "F11" ? 0x7A : 0x7B;
        RegisterHotKey(hwndSource.Handle, HotkeyId, modifiers, (uint)key);
        if (selectedHotkey != "F12")
            RegisterHotKey(hwndSource.Handle, DestructiveHotkeyId, ModControl | ModAlt, 0x7B);
    }

    private void UnregisterEmergencyHotkey()
    {
        if (hwndSource != null)
            UnregisterHotKey(hwndSource.Handle, HotkeyId);
        if (hwndSource != null)
            UnregisterHotKey(hwndSource.Handle, DestructiveHotkeyId);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && (wParam.ToInt32() == HotkeyId || wParam.ToInt32() == DestructiveHotkeyId))
        {
            var destructiveShortcut = wParam.ToInt32() == DestructiveHotkeyId ||
                (wParam.ToInt32() == HotkeyId && selectedHotkey == "F12");
            if (!destructiveMode || destructiveShortcut)
                StopGoose();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
