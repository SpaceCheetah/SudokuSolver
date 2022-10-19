using Microsoft.Maui;

using MP = SudokuSolver.MainPage;

namespace SudokuSolver;

#if WINDOWS || MACCATALYST
using SharpHook;
using SharpHook.Native;
#endif

public partial class App : Application {
    public MP Page;

    public App() {
        InitializeComponent();
        MainPage = new AppShell();
        //Application.Current.UserAppTheme = AppTheme.Light;
    }

    protected override Window CreateWindow(IActivationState activationState) {
        Window window = base.CreateWindow(activationState);
        window.Activated += (s, e) => AddHookEvents();
        window.Deactivated += (s, e) => RemoveHookEvents();
        return window;
    }

#if WINDOWS || MACCATALYST
    TaskPoolGlobalHook hook;
    private void AddHookEvents() {
        hook = new TaskPoolGlobalHook();
        hook.KeyPressed += (s, args) => {
            if (Page is null) return;
            KeyCode kc = args.Data.KeyCode;
            if(kc is >= KeyCode.Vc1 and <= KeyCode.Vc9
                || kc == KeyCode.VcBackspace) {
                MainThread.BeginInvokeOnMainThread(() => Page.NumberPressed(kc == KeyCode.VcBackspace ? 0 : kc - KeyCode.Vc1 + 1));
                return;
            }
            MP.Arrow arrow;
            switch(kc) {
                case KeyCode.VcNumPadUp:
                case KeyCode.VcUp:
                    arrow = MP.Arrow.Up;
                    break;
                case KeyCode.VcNumPadRight:
                case KeyCode.VcRight:
                    arrow = MP.Arrow.Right;
                    break;
                case KeyCode.VcNumPadDown:
                case KeyCode.VcDown:
                    arrow = MP.Arrow.Down;
                    break;
                case KeyCode.VcNumPadLeft:
                case KeyCode.VcLeft:
                    arrow = MP.Arrow.Left;
                    break;
                default:
                    return;
            }
            MainThread.BeginInvokeOnMainThread(() => Page.ArrowPressed(arrow));
        };
        hook.RunAsync();
    }
    private void RemoveHookEvents() {
        hook.Dispose();
        hook = null;
    }
#else
    private void AddHookEvents() {}
    private void RemoveHookEvents() {}
#endif
}
