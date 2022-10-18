using System.Collections.ObjectModel;

namespace SudokuSolver;

public partial class SudokuCell : ContentView {
    private static readonly BindableProperty[] _pencilMarkProperties = new BindableProperty[9];
    public static ReadOnlyCollection<BindableProperty> PencilMarkProperties => new(_pencilMarkProperties);

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(nameof(Value), typeof(int), typeof(SudokuCell), 0);
    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public bool GetMark(int i) => (bool)GetValue(_pencilMarkProperties[i - 1]);
    public void SetMark(int i, bool b) => SetValue(_pencilMarkProperties[i - 1], b);

    public void SetMarks(HashSet<int> s) {
        for(int i = 1; i < 10; i++) {
            SetMark(i, s.Contains(i));
        }
    }

    public void ResetMarks() {
        for (int i = 1; i < 10; i++) {
            SetMark(i, false);
        }
    }

    public bool P1 { get => GetMark(1); set => SetMark(1, value); }
    public bool P2 { get => GetMark(2); set => SetMark(2, value); }
    public bool P3 { get => GetMark(3); set => SetMark(3, value); }
    public bool P4 { get => GetMark(4); set => SetMark(4, value); }
    public bool P5 { get => GetMark(5); set => SetMark(5, value); }
    public bool P6 { get => GetMark(6); set => SetMark(6, value); }
    public bool P7 { get => GetMark(7); set => SetMark(7, value); }
    public bool P8 { get => GetMark(8); set => SetMark(8, value); }
    public bool P9 { get => GetMark(9); set => SetMark(9, value); }

    static SudokuCell() {
        for(int i = 0; i < 9; i++) {
            _pencilMarkProperties[i] = BindableProperty.Create($"P{i + 1}", typeof(bool), typeof(SudokuCell), false);
        }
    }

    public SudokuCell() {
        InitializeComponent();
    }
}