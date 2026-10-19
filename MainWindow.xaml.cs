using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Companion;

public partial class MainWindow : Window
{
    // The tiny state machine. Later milestones add Celebrating, Alert, Sleeping, etc.
    private enum State { Idle, Walking, Sitting }

    // ---- Tuning knobs (all in device-independent pixels) -------------------
    private const double WalkSpeed = 130;       // pixels per second
    private const double FollowDistance = 170; // he stops this far from the cursor
    private const double Slack = 30;           // extra distance before he starts walking again
    private const double SitAfterSeconds = 4;  // cursor still this long -> he sits
    // ------------------------------------------------------------------------

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private double _animTime;

    private State _state = State.Idle;
    private int _facing = 1;                   // 1 = facing right, -1 = facing left
    private bool _following = true;
    private bool _dragging;

    private Point _lastCursor;
    private double _lastCursorMoveTime;

    public MainWindow()
    {
        InitializeComponent();

        // Start near the bottom-right of the primary screen's work area.
        Left = SystemParameters.WorkArea.Right - Width - 60;
        Top = SystemParameters.WorkArea.Bottom - Height - 20;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.HideFromAltTab(hwnd);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _lastTime = _clock.Elapsed.TotalSeconds;
        _lastCursor = GetCursorDip();
        _lastCursorMoveTime = _lastTime;

        // Fires once per rendered frame (~60 fps): our game loop.
        CompositionTarget.Rendering += OnRendering;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
    }

    // ---- Game loop ---------------------------------------------------------

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _lastTime, 0.1); // clamp so a hiccup can't teleport him
        _lastTime = now;
        _animTime += dt;

        if (_dragging) return;

        Point cursor = GetCursorDip();
        if ((cursor - _lastCursor).Length > 4)
        {
            _lastCursor = cursor;
            _lastCursorMoveTime = now;
        }

        Point me = new Point(Left + Width / 2, Top + Height / 2);
        Vector toCursor = cursor - me;
        double dist = toCursor.Length;
        bool cursorStill = now - _lastCursorMoveTime > SitAfterSeconds;

        UpdateState(dist, cursorStill);

        if (_state == State.Walking && dist > 0.001)
        {
            Vector dir = toCursor / dist;
            double step = Math.Min(WalkSpeed * dt, dist - FollowDistance);
            if (step > 0)
            {
                MoveTo(me + dir * step);
            }
            if (Math.Abs(dir.X) > 0.2)
            {
                _facing = dir.X > 0 ? 1 : -1;
            }
        }

        ApplyPose();
    }

    private void UpdateState(double distToCursor, bool cursorStill)
    {
        if (!_following)
        {
            _state = State.Idle;
            return;
        }

        bool tooFar = distToCursor > FollowDistance + Slack;

        switch (_state)
        {
            case State.Walking:
                if (distToCursor <= FollowDistance) _state = State.Idle;
                break;

            case State.Idle:
                if (tooFar) _state = State.Walking;
                else if (cursorStill) _state = State.Sitting;
                break;

            case State.Sitting:
                if (tooFar) _state = State.Walking;
                else if (!cursorStill) _state = State.Idle;
                break;
        }
    }

    // Placeholder "animation": bobbing, arm swing and squash on the shape-based character.
    // With real sprites this becomes "pick the frame for the current state".
    private void ApplyPose()
    {
        double bob = 0, squash = 1, swing = 0;

        switch (_state)
        {
            case State.Walking:
                bob = -Math.Abs(Math.Sin(_animTime * 10)) * 6;
                swing = Math.Sin(_animTime * 10) * 25;
                break;
            case State.Idle:
                squash = 1 + Math.Sin(_animTime * 2) * 0.015; // gentle breathing
                break;
            case State.Sitting:
                squash = 0.82;
                break;
        }

        Pose.ScaleX = _facing;
        Pose.ScaleY = squash;
        Shift.Y = bob;
        LeftArmRot.Angle = swing;
        RightArmRot.Angle = -swing;
    }

    // ---- Helpers -----------------------------------------------------------

    private void MoveTo(Point center)
    {
        double minX = SystemParameters.VirtualScreenLeft;
        double minY = SystemParameters.VirtualScreenTop;
        double maxX = minX + SystemParameters.VirtualScreenWidth - Width;
        double maxY = minY + SystemParameters.VirtualScreenHeight - Height;

        Left = Math.Clamp(center.X - Width / 2, minX, maxX);
        Top = Math.Clamp(center.Y - Height / 2, minY, maxY);
    }

    // Win32 gives the cursor in physical pixels; WPF works in device-independent units.
    private Point GetCursorDip()
    {
        Native.GetCursorPos(out var p);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            return source.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
        }
        return new Point(p.X, p.Y);
    }

    // ---- Input -------------------------------------------------------------

    // Left-drag moves him around by hand.
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        try
        {
            DragMove(); // blocks until the mouse button is released
        }
        catch (InvalidOperationException)
        {
            // Button was already released; nothing to drag.
        }
        _dragging = false;
        _state = State.Idle;
        _lastCursorMoveTime = _clock.Elapsed.TotalSeconds;
    }

    private void FollowItem_Click(object sender, RoutedEventArgs e)
    {
        _following = FollowItem.IsChecked;
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
