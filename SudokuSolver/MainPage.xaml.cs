namespace SudokuSolver;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

public partial class MainPage : ContentPage {
    private (SudokuCell cell, (int row, int col) pos) Selected;
    private SudokuCell[,] Cells = new SudokuCell[9, 9];
    private SudokuState State;
    private static readonly Color BColor = Application.Current.RequestedTheme == AppTheme.Light ? Colors.White : Colors.Black;
    private static readonly Color FColor = Application.Current.RequestedTheme == AppTheme.Light ? Colors.Black : Colors.White;
    private static readonly Color SColor = Application.Current.RequestedTheme == AppTheme.Light ? Colors.LightSkyBlue : Colors.DarkBlue;
    private Thread ProcessingThread;
    private int SelectedLog = -1;

    public static readonly BindableProperty ProcessingProperty = BindableProperty.Create(nameof(Processing), typeof(bool), typeof(MainPage), false);
    public bool Processing { get => (bool)GetValue(ProcessingProperty); private set => SetValue(ProcessingProperty, value); }

    public class LogEntry : INotifyPropertyChanged {
        public string Entry { get; set; }
        public SudokuState State;
        public Dictionary<(int row, int col), Color> Colors;
        public bool Valid;
        public int Id;
        private Color _color;
        public Color Color { get => _color; set {
                _color = value;
                OnColorChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnColorChanged() {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
        }
    }

    //public record LogEntry(string Entry, SudokuState State, Dictionary<(int row, int col), Color> Colors, bool Valid, int Id);

    public ObservableCollection<LogEntry> Log { get; } = new ObservableCollection<LogEntry>();

    public void AddLogEntry(string entry, SudokuState state, Dictionary<(int row, int col), Color> colors, bool valid) =>
        Log.Insert(0, new LogEntry() { Entry = entry, State = state.Clone(), Colors = colors, Valid = valid, Id = Log.Count, Color = BColor});

    public MainPage() {
        InitializeComponent();
        Container.BackgroundColor = BColor;
        DefineGrid();
        AddCells();
        AddWalls();
        if(Application.Current is App app) {
            app.Page = this;
        }
    }

    public void DisplayState(Dictionary<(int row, int col), Color> colors) {
        for(int r = 0; r < 9; r++) {
            for(int c = 0; c < 9; c++) {
                Cells[r, c].Value = State.Cells[r, c].Value;
                if (Cells[r, c].Value == 0) {
                    Cells[r, c].SetMarks(State.Cells[r, c].Marks);
                }
            }
        }
        foreach((var pos, var color) in colors) {
            Cells[pos.row, pos.col].BackgroundColor = color;
        }
    }

    public void OnStartClicked(object sender, EventArgs _) {
        if (Processing) return;
        if (SelectedLog != -1) {
            for (int i = Log.Count - 1; i > SelectedLog; i--) {
                Log.RemoveAt(0);
            }
            SelectedLog = -1;
        }
        if (LogCollection.SelectedItem is LogEntry entry) {
            entry.Color = BColor;
        }
        Selected.cell = null;
        ClearColors();
        SudokuSolver solver;
        if (State is null) {
            var cellsData = new SudokuState.Cell[9, 9];
            for (int r = 0; r < 9; r++) {
                for (int c = 0; c < 9; c++) {
                    cellsData[r, c] = new SudokuState.Cell() { Value = Cells[r, c].Value };
                }
            }
            State = new SudokuState(cellsData);
            solver = new SudokuSolver(State);
            try {
                solver.FillMarks();
                solver.Verify();
                Start.Text = "Continue";
                DisplayState(new Dictionary<(int row, int col), Color>());
            } catch(InvalidStateException e) {
                var colors = new Dictionary<(int row, int col), Color>();
                foreach (var cell in e.InvolvedCells) {
                    colors[cell] = Colors.Red;
                }
                if (e.PreviousStep is not null && e.PreviousStep.Log.Length != 0) AddLogEntry(e.PreviousStep.Log, State, colors, false);
                AddLogEntry("Invalid state: " + e.Reason, State, colors, false);
                DisplayState(colors);
                State = null;
            }
            return;
        }
        Processing = true;
        ProcessingThread = new Thread(() => {
            solver = new SudokuSolver(State);
            try {
                SudokuSolver.StepResult result = solver.Step();
                MainThread.BeginInvokeOnMainThread(() => {
                    if (result is null) {
                        AddLogEntry("Failed to make any progress", State, new Dictionary<(int row, int col), Color>(), true);
                        Processing = false;
                        ProcessingThread.Join();
                        return;
                    }
                    DisplayState(result.Cells);
                    AddLogEntry(result.Log, State, result.Cells, true);
                    Processing = false;
                    ProcessingThread.Join();
                });
            }
            catch (InvalidStateException e) {
                MainThread.BeginInvokeOnMainThread(() => {
                    var colors = new Dictionary<(int row, int col), Color>();
                    foreach (var cell in e.InvolvedCells) {
                        colors[cell] = Colors.Red;
                    }
                    if (e.PreviousStep is not null && e.PreviousStep.Log.Length != 0) AddLogEntry(e.PreviousStep.Log, State, colors, false);
                    AddLogEntry("Invalid state: " + e.Reason, State, colors, false);
                    DisplayState(colors);
                    State = null;
                    Start.Text = "Start";
                    Processing = false;
                    ProcessingThread.Join();
                });
            }
        });
        ProcessingThread.Start();
    }

    private void LogCollectionChanged(object sender, SelectionChangedEventArgs e) {
        Selected.cell = null;
        var entry = e.CurrentSelection[0] as LogEntry;
        if (e.PreviousSelection.Count != 0 && e.PreviousSelection[0] is LogEntry previous) {
            previous.Color = BColor;
        }
        entry.Color = SColor;
        State = entry.State.Clone();
        ClearColors();
        DisplayState(entry.Colors);
        if(entry.Valid) {
            Start.Text = "Continue";
        } else {
            State = null;
            Start.Text = "Start";
        }
        SelectedLog = entry.Id;
        if(entry.Id == Log.Count - 1) {
            SelectedLog = -1;
        }
    }

    public void NumberPressed(int num) {
        if(Selected.cell is not null && !Processing) {
            Selected.cell.Value = num;
            State = null;
            foreach(var cell in Cells) {
                cell.ResetMarks();
            }
            Start.Text = "Start";
            if (SelectedLog != -1) {
                for(int i = Log.Count - 1; i > SelectedLog; i--) {
                    Log.RemoveAt(0);
                }
                SelectedLog = -1;
            }
            if (LogCollection.SelectedItem is LogEntry entry) {
                entry.Color = BColor;
            }
        }
    }

    public enum Arrow {
        Up, Right, Down, Left
    }

    public void ArrowPressed(Arrow arrow) {
        if (Selected.cell is null) return;
        (int row, int col) = Selected.pos;
        switch(arrow) {
            case Arrow.Up:
                row--;
                break;
            case Arrow.Right:
                col++;
                if(col == 9) {
                    col = 0;
                    row++;
                }
                break;
            case Arrow.Down:
                row++;
                break;
            case Arrow.Left:
                col--;
                if(col == -1) {
                    col = 8;
                    row--;
                }
                break;
        }
        if (row is < 0 or >= 9) {
            return;
        }
        Selected.pos = (row, col);
        Selected.cell.BackgroundColor = BColor;
        Selected.cell = Cells[Selected.pos.row, Selected.pos.col];
        Selected.cell.BackgroundColor = SColor;
    }

    public void OnClick(object sender, EventArgs args) {
        if (Processing) {
            return;
        }
        bool cleared = false;
        if (Selected.cell is not null) {
            Selected.cell = null;
            ClearColors();
            cleared = true;
        }
        if (sender is SudokuCell s) {
            if (!cleared) ClearColors();
            Selected.cell = s;
            Selected.cell.BackgroundColor = SColor;
            for(int r = 0; r < 9; r++) {
                for(int c = 0; c < 9; c++) {
                    if(Selected.cell == Cells[r,c]) {
                        Selected.pos = (r, c);
                        break;
                    }
                }
            }
        }
    }

    private void ClearColors() {
        foreach(var cell in Cells) {
            cell.BackgroundColor = BColor;
        }
    }

    private void DefineGrid() {
        var rowLarge = new RowDefinition(10);
        var rowSmall = new RowDefinition(3);
        var rowCell = new RowDefinition(GridLength.Star);
        var colLarge = new ColumnDefinition(10);
        var colSmall = new ColumnDefinition(3);
        var colCell = new ColumnDefinition(GridLength.Star);
        var rows = new RowDefinitionCollection();
        var cols = new ColumnDefinitionCollection();
        for (int i = 0; i < 3; i++) {
            rows.Add(rowLarge);
            rows.Add(rowCell);
            cols.Add(colLarge);
            cols.Add(colCell);
            for (int j = 0; j < 2; j++) {
                rows.Add(rowSmall);
                rows.Add(rowCell);
                cols.Add(colSmall);
                cols.Add(colCell);
            }
        }
        rows.Add(rowLarge);
        cols.Add(colLarge);
        Board.RowDefinitions = rows;
        Board.ColumnDefinitions = cols;
    }

    private void AddCells() {
        var tapGestureRecognizer = new TapGestureRecognizer();
        tapGestureRecognizer.Tapped += OnClick;
        for (int r = 0; r < 9; r++) {
            for (int c = 0; c < 9; c++) {
                var cell = new SudokuCell();
                cell.BackgroundColor = BColor; //At least on Windows, this fixes click detection
                //cell.Value = c + 1;
                cell.GestureRecognizers.Add(tapGestureRecognizer);
                Board.Add(cell, c * 2 + 1, r * 2 + 1);
                Cells[r, c] = cell;
            }
        }
    }

    private void AddWalls() {
        for (int i = 0; i <= 18; i += 2) {
            var bvRow = new BoxView { Color = FColor };
            var bvCol = new BoxView { Color = FColor };
            bvRow.SetValue(Grid.ColumnSpanProperty, 19);
            bvCol.SetValue(Grid.RowSpanProperty, 19);
            Board.Add(bvRow, 0, i);
            Board.Add(bvCol, i, 0);
        }
    }
}

