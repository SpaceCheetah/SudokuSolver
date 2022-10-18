using System.Collections.ObjectModel;

namespace SudokuSolver;

public class SudokuSolver {
    private SudokuState State;
    public SudokuSolver(SudokuState state) {
        State = state;
    }

    public record StepResult(string Log, ReadOnlyDictionary<(int row, int col), Color> Cells);

    public StepResult Step() {
        StepResult result = null;
        try {
            foreach (var rule in GetRules()) {
                var ruleResult = rule();
                if (ruleResult is not null) {
                    result = ruleResult;
                    break;
                }
            }
            if (result is null) {
                return null;
            }
            FillMarks();
            Verify();
            return result;
        } catch(InvalidStateException e) {
            if(result is not null) {
                e.Log = result.Log;
            }
            throw;
        }
    }

    private delegate StepResult Rule();

    private List<Rule> GetRules() {
        var rules = new List<Rule>();
        rules.Add(SingleCandidate);
        rules.Add(HSingle);
        return rules;
    }

    private StepResult SingleCandidate() {
        for(int r = 0; r < 9; r++) {
            for(int c = 0; c < 9; c++) {
                SudokuState.Cell cell = State.Cells[r, c];
                if (cell.Value == 0 && cell.Marks.Count == 1) {
                    cell.Value = cell.Marks.First();
                    return new StepResult($"Single Candidate: Cell R{r + 1}C{c + 1} had only one possibility", new ReadOnlyDictionary<(int row, int col), Color>(
                        new Dictionary<(int row, int col), Color>() {
                            [(r, c)] = Colors.Green
                        }));
                }
            }
        }
        return null;
    }

    private StepResult HSingle() {
        for(int group = 0; group < 9; group++) {
            var dict = new Dictionary<(GroupType, int mark), (int count, (int row, int col) pos)>();
            for (int i = 0; i < 9; i++) {
                foreach(var type in Types) {
                    (int row, int col) = GetPos(type, group, i);
                    if (State.Cells[row, col].Value == 0) {
                        foreach (var mark in State.Cells[row, col].Marks) {
                            (int count, (int row, int col) pos) value = (0, (0, 0));
                            dict.TryGetValue((type, mark), out value);
                            value.count++;
                            value.pos = (row, col);
                            dict[(type, mark)] = value;
                        }
                    }
                    else {
                        dict[(type, State.Cells[row, col].Value)] = (2, (0, 0));
                    }
                }
            }
            for (int mark = 1; mark < 10; mark++) {
                foreach(var type in Types) {
                    var (count, pos) = dict[(type, mark)];
                    if(count == 1) {
                        State.Cells[pos.row, pos.col].Value = mark;
                        return new StepResult($"Hidden Single: Cell R{pos.row + 1}C{pos.col + 1} was the only possibility for {mark} in {type.ToString()} {group + 1}", new ReadOnlyDictionary<(int row, int col), Color>(
                        new Dictionary<(int row, int col), Color>() {
                            [(pos.row, pos.col)] = Colors.Green
                        }));
                    }
                }
            }
        }
        return null;
    }

    public void Verify() {
        //Verify no repeated numbers in a group
        foreach (GroupType type in Types) {
            for (int group = 0; group < 9; group++) {
                _ = Contents(type, group);
                //Verify that each group has at least one mark for each value
                var set = new HashSet<int>();
                for (int i = 0; i < 9; i++) {
                    (int row, int col) = GetPos(type, group, i);
                    var cell = State.Cells[row, col];
                    if (cell.Value == 0) {
                        foreach (var mark in cell.Marks) {
                            set.Add(mark);
                        }
                    }
                    else set.Add(cell.Value);
                }
                for(int mark = 1; mark < 10; mark++) {
                    if(!set.Contains(mark)) {
                        var cellsInGroup = new List<(int, int)>();
                        for(int i = 0; i < 9; i++) {
                            cellsInGroup.Add(GetPos(type, group, i));
                        }
                        throw new InvalidStateException($"{type.ToString()} {group + 1} has no options for {mark}", cellsInGroup);
                    }
                }
            }
        }
        //Verify each cell has options
        for(int r = 0; r < 9; r++)
            for(int c = 0; c < 9; c++)
                if (State.Cells[r, c].Marks.Count == 0)
                    throw new InvalidStateException($"Cell R{r + 1}C{c + 1} has no possible values", new (int, int)[] { (r, c) });
    }

    public void FillMarks() {
        var boxContents = new HashSet<int>[9];
        for (int i = 0; i < 9; i++) {
            boxContents[i] = Contents(GroupType.Box, i);
        }
        for (int r = 0; r < 9; r++) {
            var rowContents = Contents(GroupType.Row, r);
            for (int c = 0; c < 9; c++) {
                if (State.Cells[r, c].Value != 0) continue;
                var neighbors = Contents(GroupType.Col, c);
                neighbors.UnionWith(rowContents);
                neighbors.UnionWith(boxContents[r / 3 * 3 + c / 3]);
                State.Cells[r, c].Marks.ExceptWith(neighbors);
            }
        }
    }

    private enum GroupType {
        Row, Col, Box
    }

    private static readonly GroupType[] Types = { GroupType.Row, GroupType.Col, GroupType.Box };

    private (int row, int col) GetPos(GroupType type, int group, int i) => type switch {
        GroupType.Row => (group, i),
        GroupType.Col => (i, group),
        GroupType.Box => (group / 3 * 3 + i / 3, group % 3 * 3 + i % 3),
        _ => throw new ArgumentException()
    };

    private HashSet<int> Contents(GroupType type, int group) {
        var dict = new Dictionary<int, (int row, int col)>();
        for (int i = 0; i < 9; i++) {
            (int row, int col) = GetPos(type, group, i);
            int v = State.Cells[row, col].Value;
            if (v == 0) {
                continue;
            }
            try {
                dict.Add(v, (row, col));
            } catch (ArgumentException) {
                (int row2, int col2) = dict[v];
                throw new InvalidStateException($"{type.ToString()} {group + 1} contains two {v}s: R{row2 + 1}C{col2 + 1}, R{row + 1}C{col + 1}",
                    new (int, int)[] { (row, col), (row2, col2) });
            }
        }
        return dict.Keys.ToHashSet();
    }
}