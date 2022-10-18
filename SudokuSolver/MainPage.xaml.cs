namespace SudokuSolver;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

public partial class MainPage : ContentPage {
    private SudokuCell Selected;
    private SudokuCell[,] Cells = new SudokuCell[9,9];
    private SudokuState State;
    private static readonly Color BColor = Application.Current.RequestedTheme == AppTheme.Light ? Colors.White : Colors.Black;
    private static readonly Color FColor = Application.Current.RequestedTheme == AppTheme.Light ? Colors.Black : Colors.White;
    private static readonly Color SColor = Application.Current.RequestedTheme == AppTheme.Light ? Colors.LightSkyBlue : Colors.DarkBlue;
    private Thread ProcessingThread;

    public static readonly BindableProperty ProcessingProperty = BindableProperty.Create(nameof(Processing), typeof(bool), typeof(MainPage), false);
    public bool Processing { get => (bool)GetValue(ProcessingProperty); private set => SetValue(ProcessingProperty, value); }

    public record LogEntry(string Entry); //Have to have a class to bind to

    public ObservableCollection<LogEntry> Log { get; } = new ObservableCollection<LogEntry>();

    public void AddLogEntry(string entry) => Log.Insert(0, new LogEntry(entry));

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

    public void DisplayState() {
        for(int r = 0; r < 9; r++) {
            for(int c = 0; c < 9; c++) {
                Cells[r, c].Value = State.Cells[r, c].Value;
                if (Cells[r, c].Value == 0) {
                    Cells[r, c].SetMarks(State.Cells[r, c].Marks);
                }
            }
        }
    }

    public void OnStartClicked(object sender, EventArgs _) {
        if (Processing) return;
        Selected = null;
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
                DisplayState();
            } catch(InvalidStateException e) {
                AddLogEntry(e.Log);
                AddLogEntry("Invalid state: " + e.Reason);
                DisplayState();
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
                        AddLogEntry("Failed to make any progress");
                        Processing = false;
                        ProcessingThread.Join();
                        return;
                    }
                    DisplayState();
                    AddLogEntry(result.Log);
                    foreach (var kvp in result.Cells) {
                        //might need to change the colors later to account for dark mode
                        Cells[kvp.Key.row, kvp.Key.col].BackgroundColor = kvp.Value;
                    }
                    Processing = false;
                    ProcessingThread.Join();
                });
            }
            catch (InvalidStateException e) {
                MainThread.BeginInvokeOnMainThread(() => {
                    AddLogEntry(e.Log);
                    AddLogEntry("Invalid state: " + e.Reason);
                    DisplayState();
                    State = null;
                    Start.Text = "Start";
                    Processing = false;
                    ProcessingThread.Join();
                });
            }
        });
        ProcessingThread.Start();
    }

    public void NumberPressed(int num) {
        if(Selected is not null && !Processing) {
            Selected.Value = num;
            State = null;
            foreach(var cell in Cells) {
                cell.ResetMarks();
            }
            Start.Text = "Start";
        }
    }

    public void OnClick(object sender, EventArgs args) {
        if (Processing) {
            return;
        }
        Selected = null;
        ClearColors();
        if (sender is SudokuCell s) {
            Selected = s;
            Selected.BackgroundColor = SColor;
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

