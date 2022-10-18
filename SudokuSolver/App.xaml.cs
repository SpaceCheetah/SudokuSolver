using Microsoft.Maui;

namespace SudokuSolver;

#if WINDOWS || MACCATALYST
using SharpHook;
using SharpHook.Native;
#endif

public partial class App : Application {
    public MainPage Page;

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
            KeyCode kc = args.Data.KeyCode;
            if(kc is >= KeyCode.Vc1 and <= KeyCode.Vc9
                || kc == KeyCode.VcBackspace && Page is not null) {
                MainThread.BeginInvokeOnMainThread(() => Page.NumberPressed(kc == KeyCode.VcBackspace ? 0 : kc - KeyCode.Vc1 + 1));
            }
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
