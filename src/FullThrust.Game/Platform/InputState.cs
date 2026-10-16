using Brutal.GlfwApi;
using Brutal.Numerics;

namespace FullThrust.Game.Platform;

public sealed class InputState : IDisposable {

    private const int KeyCount = 512;
    private const int ButtonCount = 8;

    private readonly GlfwWindow _window;

    private readonly bool[] _keys = new bool[KeyCount];
    private readonly bool[] _keysPressed = new bool[KeyCount];

    private readonly bool[] _buttons = new bool[ButtonCount];
    private readonly bool[] _buttonsPressed = new bool[ButtonCount];

    private double2 _lastCursor;
    private bool _hasCursor;

    private double2 _pendingMouseDelta;
    private double _pendingScroll;

    public InputState(GlfwWindow window) {

        _window = window;

        _window.OnKey += HandleKey;
        _window.OnMouseButton += HandleMouseButton;
        _window.OnCursorPos += HandleCursor;
        _window.OnScroll += HandleScroll;

    }

    public bool UiWantsKeyboard { get; set; }

    public bool UiWantsMouse { get; set; }

    public double2 MouseDelta { get; private set; }

    public double Scroll { get; private set; }

    // Call after polling events so per-frame edges and deltas reflect this frame only.
    public void BeginFrame() {

        MouseDelta = _pendingMouseDelta;
        Scroll = UiWantsMouse ? 0 : _pendingScroll;

        _pendingMouseDelta = default;
        _pendingScroll = 0;

    }

    public void EndFrame() {

        Array.Clear(_keysPressed);
        Array.Clear(_buttonsPressed);

    }

    public bool IsDown(GlfwKey key) => !UiWantsKeyboard && IsValid(key) && _keys[(int)key];

    public bool WasPressed(GlfwKey key) => !UiWantsKeyboard && IsValid(key) && _keysPressed[(int)key];

    // System shortcuts (overlay toggle, quit) must work even while ImGui has focus.
    public bool WasPressedRaw(GlfwKey key) => IsValid(key) && _keysPressed[(int)key];

    public bool IsDown(GlfwMouseButton button) => _buttons[(int)button];

    public bool WasPressed(GlfwMouseButton button) => !UiWantsMouse && _buttonsPressed[(int)button];

    public float Axis(GlfwKey negative, GlfwKey positive) => (IsDown(positive) ? 1f : 0f) - (IsDown(negative) ? 1f : 0f);

    public void SetMouseCaptured(bool captured) {

        var mode = captured ? GlfwCursorMode.Disabled : GlfwCursorMode.Normal;

        if (_window.GetInputMode(GlfwInputMode.Cursor) == (int)mode) {

            return;

        }

        _window.SetInputMode(GlfwInputMode.Cursor, (int)mode);

        if (captured && Glfw.RawMotionSupported()) {

            _window.SetInputMode(GlfwInputMode.RawMouseMotion, Glfw.Constants.GLFW_TRUE);

        }

        _hasCursor = false;

    }

    private static bool IsValid(GlfwKey key) => (uint)key < KeyCount;

    private void HandleKey(GlfwWindow window, GlfwKey key, int scancode, GlfwKeyAction action, GlfwModifier mods) {

        if (!IsValid(key)) {

            return;

        }

        if (action == GlfwKeyAction.Press) {

            _keys[(int)key] = true;
            _keysPressed[(int)key] = true;

        } else if (action == GlfwKeyAction.Release) {

            _keys[(int)key] = false;

        }

    }

    private void HandleMouseButton(GlfwWindow window, GlfwMouseButton button, GlfwButtonAction action, GlfwModifier mods) {

        if ((uint)button >= ButtonCount) {

            return;

        }

        var down = action == GlfwButtonAction.Press;

        if (down && UiWantsMouse) {

            return;

        }

        _buttons[(int)button] = down;

        if (down) {

            _buttonsPressed[(int)button] = true;

        }

    }

    private void HandleCursor(GlfwWindow window, double2 position) {

        if (_hasCursor) {

            _pendingMouseDelta += position - _lastCursor;

        }

        _lastCursor = position;
        _hasCursor = true;

    }

    private void HandleScroll(GlfwWindow window, double2 offset) => _pendingScroll += offset.Y;

    public void Dispose() {

        _window.OnKey -= HandleKey;
        _window.OnMouseButton -= HandleMouseButton;
        _window.OnCursorPos -= HandleCursor;
        _window.OnScroll -= HandleScroll;

    }

}
