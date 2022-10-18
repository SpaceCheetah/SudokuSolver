using System.Collections.ObjectModel;
using System.Text;

namespace SudokuSolver;

public class SudokuSolver {
    private SudokuState State;
    public SudokuSolver(SudokuState state) {
        State = state;
    }

    public record StepResult(string Log, Dictionary<(int row, int col), Color> Cells);

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

    private List<Rule> GetRules() => new List<Rule>() {
        SingleCandidate,
        HSingle,
        NPair,
        HPair
    };

    private StepResult SingleCandidate() {
        for(int r = 0; r < 9; r++) {
            for(int c = 0; c < 9; c++) {
                SudokuState.Cell cell = State.Cells[r, c];
                if (cell.Value == 0 && cell.Marks.Count == 1) {
                    cell.Value = cell.Marks.First();
                    return new StepResult($"Single Candidate: Cell R{r + 1}C{c + 1} had only one possibility",
                        new Dictionary<(int row, int col), Color>() {
                            [(r, c)] = Colors.Green
                        });
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
                        return new StepResult($"Hidden Single: Cell R{pos.row + 1}C{pos.col + 1} was the only possibility for {mark} in {type.ToString()} {group + 1}",
                        new Dictionary<(int row, int col), Color>() {
                            [(pos.row, pos.col)] = Colors.Green
                        });
                    }
                }
            }
        }
        return null;
    }

    private StepResult NPair() {
        foreach(var type in Types) {
            for(int group = 0; group < 9; group++) {
                var dict = new Dictionary<(int mark1, int mark2), (int row, int col)>();
                for(int i = 0; i < 9; i++) {
                    (int row, int col) = GetPos(type, group, i);
                    SudokuState.Cell cell = State.Cells[row, col];
                    if (cell.Marks.Count != 2) continue;
                    (int mark1, int mark2) marks = (cell.Marks.First(), cell.Marks.Last());
                    if(dict.ContainsKey(marks)) {
                        (int row, int col) pos = dict[marks]; 
                        List<(int, int)> removed = RemoveMarks(type, group, new HashSet<int>() { marks.mark1, marks.mark2 },
                            new HashSet<(int, int)>() { pos, (row, col) });
                        if (removed.Count == 0) continue;
                        var cellColors = new Dictionary<(int row, int col), Color>() {
                            [(row, col)] = Colors.Blue,
                            [(pos.row, pos.col)] = Colors.Blue
                        };
                        foreach(var p in removed) {
                            cellColors[p] = Colors.Green;
                        }
                        return new StepResult($"Naked Pair: Cells R{pos.row + 1}C{pos.col + 1} and R{row + 1}C{col + 1} formed pair in {type.ToString()} {group + 1}, " +
                            $"affecting cells {ListCells(removed)}", cellColors);
                    } else {
                        dict[marks] = (row, col);
                    }
                }
            }
        }
        return null;
    }

    private StepResult HPair() {
        foreach (var type in Types) {
            for (int group = 0; group < 9; group++) {
                //Looking for two marks that only occur in the same 2 cells in a group
                //map from mark to the list of containing cells
                var dict = new Dictionary<int, List<(int row, int col)>>();
                for(int i = 1; i < 9; i++) {
                    dict[i] = new List<(int, int)>();
                }
                for (int i = 0; i < 9; i++) {
                    (int row, int col) = GetPos(type, group, i);
                    SudokuState.Cell cell = State.Cells[row, col];
                    if(cell.Value != 0) {
                        dict.Remove(cell.Value);
                        continue;
                    }
                    foreach(int mark in cell.Marks) {
                        if (!dict.ContainsKey(mark)) continue;
                        if (dict[mark].Count == 2) dict.Remove(mark);
                        else dict[mark].Add((row, col));
                    }
                }
                //At this point, should be left with only lists of exactly 2 cells (0 would be invalid, 1 would be taken care of by HSingle). Verify anyway though, since HSingle might not have been run.
                var kvps = dict.ToList();
                for(int i = 0; i < dict.Count; i++) {
                    for(int j = i + 1; j < dict.Count; j++) {
                        if (!kvps[i].Value.SequenceEqual(kvps[j].Value) || kvps[i].Value.Count != 2) continue;
                        var pos1 = kvps[i].Value[0];
                        var pos2 = kvps[i].Value[1];
                        var cell1 = State.Cells[pos1.row, pos1.col];
                        var cell2 = State.Cells[pos2.row, pos2.col];
                        if (cell1.Marks.Count == 2 && cell2.Marks.Count == 2) continue; //nothing to remove
                        var marks = new HashSet<int>() {kvps[i].Key, kvps[j].Key};
                        cell1.Marks = marks;
                        cell2.Marks = marks;
                        return new StepResult($"Hidden pair: Cells R{pos1.row + 1}C{pos1.col + 1} and R{pos2.row + 1}C{pos2.col + 1} were the only candidates for {kvps[i].Key} and " +
                            $"{kvps[j].Key} in {type.ToString()} {group + 1}",
                            new Dictionary<(int row, int col), Color>() {
                                [(pos1.row, pos1.col)] = Colors.Green,
                                [(pos2.row, pos2.col)] = Colors.Green
                            });
                    }
                }
            }
        }
        return null;
    }

    private string ListCells(List<(int row, int col)> cells) {
        var sb = new StringBuilder("[");
        foreach(var cell in cells) {
            sb.Append($"R{cell.row + 1}C{cell.col + 1}, ");
        }
        sb.Remove(sb.Length - 2, 2);
        sb.Append("]");
        return sb.ToString();
    }

    private List<(int,int)> RemoveMarks(GroupType type, int group, HashSet<int> marks, HashSet<(int row, int col)> except) {
        var removed = new List<(int, int)>();
        for(int i = 0; i < 9; i++) {
            (int row, int col) = GetPos(type, group, i);
            if (except is not null && except.Contains((row, col))) continue;
            SudokuState.Cell cell = State.Cells[row, col];
            if (cell.Value != 0) continue;
            int marksCount = cell.Marks.Count;
            cell.Marks.ExceptWith(marks);
            if (marksCount != cell.Marks.Count) {
                removed.Add((row, col));
            }
        }
        return removed;
    }

    public void Verify() {
        //Verify each cell has options
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
                if (State.Cells[r, c].Marks.Count == 0)
                    throw new InvalidStateException($"Cell R{r + 1}C{c + 1} has no possible values", new (int, int)[] { (r, c) });
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
                var neighbors = Contents(GroupType.Column, c);
                neighbors.UnionWith(rowContents);
                neighbors.UnionWith(boxContents[r / 3 * 3 + c / 3]);
                State.Cells[r, c].Marks.ExceptWith(neighbors);
            }
        }
    }

    private enum GroupType {
        Row, Column, Box
    }

    private static readonly GroupType[] Types = { GroupType.Row, GroupType.Column, GroupType.Box };

    private (int row, int col) GetPos(GroupType type, int group, int i) => type switch {
        GroupType.Row => (group, i),
        GroupType.Column => (i, group),
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