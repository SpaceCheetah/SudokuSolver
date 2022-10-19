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
        HPair,
        NTriple,
        HTriple,
        NQuad,
        LockedCandidates
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

    private StepResult HSingle() => DoForEachGroup((type, group) => {
        var dict = new Dictionary<int, (int row, int col)>();
        for (int mark = 1; mark < 10; mark++) {
            dict[mark] = (-1, -1);
        }
        for (int i = 0; i < 9; i++) {
            (int row, int col) = GetPos(type, group, i);
            var cell = State.Cells[row, col];
            if (cell.Value == 0) {
                foreach (int mark in cell.Marks) {
                    if (!dict.ContainsKey(mark)) continue;
                    if (dict[mark] == (-1, -1)) {
                        dict[mark] = (row, col);
                    }
                    else {
                        dict.Remove(mark);
                    }
                }
            }
            else {
                dict.Remove(cell.Value);
            }
        }
        if (dict.Count == 0) return null;
        var kvp = dict.First();
        State.Cells[kvp.Value.row, kvp.Value.col].Value = kvp.Key;
        return new StepResult($"Hidden Single: Cell R{kvp.Value.row + 1}C{kvp.Value.col + 1} was the only possibility for {kvp.Key} in {type.ToString()} {group + 1}",
            new Dictionary<(int row, int col), Color>() {
                [(kvp.Value.row, kvp.Value.col)] = Colors.Green
            });
    });

    private StepResult NPair() => DoForEachGroup((type, group) => {
        var dict = new Dictionary<(int mark1, int mark2), (int row, int col)>();
        for (int i = 0; i < 9; i++) {
            (int row, int col) = GetPos(type, group, i);
            SudokuState.Cell cell = State.Cells[row, col];
            if (cell.Marks.Count != 2 || cell.Value != 0) continue;
            (int mark1, int mark2) marks = (cell.Marks.First(), cell.Marks.Last());
            if (dict.ContainsKey(marks)) {
                (int row, int col) pos = dict[marks];
                List<(int, int)> removed = RemoveMarks(type, group, new HashSet<int>() { marks.mark1, marks.mark2 },
                    new HashSet<(int, int)>() { pos, (row, col) });
                if (removed.Count == 0) continue;
                var cellColors = new Dictionary<(int row, int col), Color>() {
                    [(row, col)] = Colors.Blue,
                    [(pos.row, pos.col)] = Colors.Blue
                };
                foreach (var p in removed) {
                    cellColors[p] = Colors.Green;
                }
                return new StepResult($"Naked Pair: Cells R{pos.row + 1}C{pos.col + 1} and R{row + 1}C{col + 1} formed pair [{marks.mark1},{marks.mark2}] in {type.ToString()} {group + 1}, " +
                    $"affecting cells {ListCells(removed)}", cellColors);
            }
            else {
                dict[marks] = (row, col);
            }
        }
        return null;
    });

    private StepResult HPair() => DoForEachGroup((type, group) => {
        //Looking for two marks that only occur in the same 2 cells in a group
        //map from mark to the list of containing cells
        var dict = new Dictionary<int, List<(int row, int col)>>();
        for (int i = 1; i < 9; i++) {
            dict[i] = new List<(int, int)>();
        }
        for (int i = 0; i < 9; i++) {
            (int row, int col) = GetPos(type, group, i);
            SudokuState.Cell cell = State.Cells[row, col];
            if (cell.Value != 0) {
                dict.Remove(cell.Value);
                continue;
            }
            foreach (int mark in cell.Marks) {
                if (!dict.ContainsKey(mark)) continue;
                if (dict[mark].Count == 2) dict.Remove(mark);
                else dict[mark].Add((row, col));
            }
        }
        //At this point, should be left with only lists of exactly 2 cells (0 would be invalid, 1 would be taken care of by HSingle). Verify anyway though, since HSingle might not have been run.
        var kvps = dict.ToList();
        for (int i = 0; i < dict.Count; i++) {
            for (int j = i + 1; j < dict.Count; j++) {
                if (!kvps[i].Value.SequenceEqual(kvps[j].Value) || kvps[i].Value.Count != 2) continue;
                var pos1 = kvps[i].Value[0];
                var pos2 = kvps[i].Value[1];
                var cell1 = State.Cells[pos1.row, pos1.col];
                var cell2 = State.Cells[pos2.row, pos2.col];
                if (cell1.Marks.Count == 2 && cell2.Marks.Count == 2) continue; //nothing to remove
                var marks = new HashSet<int>() { kvps[i].Key, kvps[j].Key };
                int markCount1 = cell1.Marks.Count;
                int markCount2 = cell2.Marks.Count;
                cell1.Marks = marks;
                cell2.Marks = new HashSet<int>(marks); //Copy to avoid tying the two cells together
                return new StepResult($"Hidden pair: Cells R{pos1.row + 1}C{pos1.col + 1} and R{pos2.row + 1}C{pos2.col + 1} were the only candidates for {kvps[i].Key} and " +
                    $"{kvps[j].Key} in {type.ToString()} {group + 1}",
                    new Dictionary<(int row, int col), Color>() {
                        [(pos1.row, pos1.col)] = (markCount1 == cell1.Marks.Count) ? Colors.Blue : Colors.Green,
                        [(pos2.row, pos2.col)] = (markCount2 == cell2.Marks.Count) ? Colors.Blue : Colors.Green
                    });
            }
        }
        return null;
    });

    private StepResult NTriple() => DoForEachGroup((type, group) => {
        //Here's where it starts getting tricky
        //The concept is similar to NPair, except with tripple, it doesn't have to be an exact match
        //For instance, 1,2, 2,3, and 1,3 would form a tripple
        //Fortunately there are few enough cells in a group that a brute force method works fine
        var cells = new List<((int row, int col) pos, HashSet<int> marks)>();
        for (int i = 0; i < 9; i++) {
            (int row, int col) = GetPos(type, group, i);
            var cell = State.Cells[row, col];
            if (cell.Value != 0 || cell.Marks.Count > 3) continue;
            cells.Add(((row, col), cell.Marks));
        }
        for (int i = 0; i < cells.Count; i++) {
            for (int j = i + 1; j < cells.Count; j++) {
                for (int k = j + 1; k < cells.Count; k++) {
                    var set = new HashSet<int>(cells[i].marks);
                    set.UnionWith(cells[j].marks);
                    set.UnionWith(cells[k].marks);
                    if (set.Count != 3) continue;
                    var tripple = new HashSet<(int row, int col)>() { cells[i].pos, cells[j].pos, cells[k].pos };
                    var removed = RemoveMarks(type, group, set, tripple);
                    if (removed.Count == 0) continue;
                    var dict = new Dictionary<(int row, int pos), Color>();
                    foreach (var pos in tripple) {
                        dict[pos] = Colors.Blue;
                    }
                    foreach (var pos in removed) {
                        dict[pos] = Colors.Green;
                    }
                    return new StepResult($"Naked triple: Cells {ListCells(tripple)} formed triple {ListMarks(set)} in {type.ToString()} {group + 1}, affecting cells {ListCells(removed)}", dict);
                }
            }
        }
        return null;
    });

    private StepResult HTriple() => DoForEachGroup((type, group) => {
        var dict = new Dictionary<int, HashSet<(int row, int col)>>();
        for (int i = 1; i < 9; i++) {
            dict[i] = new HashSet<(int, int)>();
        }
        for (int i = 0; i < 9; i++) {
            (int row, int col) = GetPos(type, group, i);
            SudokuState.Cell cell = State.Cells[row, col];
            if (cell.Value != 0) {
                dict.Remove(cell.Value);
                continue;
            }
            foreach (int mark in cell.Marks) {
                if (!dict.ContainsKey(mark)) continue;
                if (dict[mark].Count == 3) dict.Remove(mark);
                else dict[mark].Add((row, col));
            }
        }
        var kvps = dict.ToList();
        for (int i = 0; i < dict.Count; i++) {
            for (int j = i + 1; j < dict.Count; j++) {
                for (int k = j + 1; k < dict.Count; k++) {
                    var set = new HashSet<(int row, int col)>(kvps[i].Value);
                    set.UnionWith(kvps[j].Value);
                    set.UnionWith(kvps[k].Value);
                    if (set.Count != 3) continue;
                    var marks = new HashSet<int>() { kvps[i].Key, kvps[j].Key, kvps[k].Key };
                    var changed = new List<(int row, int col)>();
                    foreach (var pos in set) {
                        var cell = State.Cells[pos.row, pos.col];
                        int markCount = cell.Marks.Count;
                        cell.Marks.IntersectWith(marks);
                        if (cell.Marks.Count != markCount) {
                            changed.Add(pos);
                        }
                    }
                    if (changed.Count == 0) continue;
                    var colors = new Dictionary<(int row, int col), Color>();
                    foreach (var pos in set) {
                        colors[pos] = Colors.Blue;
                    }
                    foreach (var pos in changed) {
                        colors[pos] = Colors.Green;
                    }
                    return new StepResult($"Hidden triple: Cells {ListCells(set)} formed triple {ListMarks(marks)} in {type.ToString()} {group + 1}, " +
                        $"affecting cells {ListCells(changed)}", colors);
                }
            }
        }
        return null;
    });

    private StepResult NQuad() => DoForEachGroup((type, group) => {
        //Bunch of repeated code from NTriple, but hard to factor out
        var cells = new List<((int row, int col) pos, HashSet<int> marks)>();
        for (int i = 0; i < 9; i++) {
            (int row, int col) = GetPos(type, group, i);
            var cell = State.Cells[row, col];
            if (cell.Value != 0 || cell.Marks.Count > 4) continue;
            cells.Add(((row, col), cell.Marks));
        }
        for (int i = 0; i < cells.Count; i++) {
            for (int j = i + 1; j < cells.Count; j++) {
                for (int k = j + 1; k < cells.Count; k++) {
                    for(int l = k + 1; l < cells.Count; l++) {
                        var set = new HashSet<int>(cells[i].marks);
                        set.UnionWith(cells[j].marks);
                        set.UnionWith(cells[k].marks);
                        set.UnionWith(cells[l].marks);
                        if (set.Count != 4) continue;
                        var quad = new HashSet<(int row, int col)>() { cells[i].pos, cells[j].pos, cells[k].pos, cells[l].pos };
                        var removed = RemoveMarks(type, group, set, quad);
                        if (removed.Count == 0) continue;
                        var dict = new Dictionary<(int row, int pos), Color>();
                        foreach (var pos in quad) {
                            dict[pos] = Colors.Blue;
                        }
                        foreach (var pos in removed) {
                            dict[pos] = Colors.Green;
                        }
                        return new StepResult($"Naked quad: Cells {ListCells(quad)} formed quad {ListMarks(set)} in {type.ToString()} {group + 1}, affecting cells {ListCells(removed)}", dict);
                    }
                }
            }
        }
        return null;
    });

    private StepResult LockedCandidates() => DoForEachGroup((type, group) => {
        var dict = new Dictionary<int, HashSet<(int row, int col)>>();
        for(int mark = 1; mark < 10; mark++) {
            dict[mark] = new HashSet<(int, int)>();
        }
        for(int i = 0; i < 9; i++) {
            (int row, int col) = GetPos(type, group, i);
            var cell = State.Cells[row, col];
            if(cell.Value == 0) {
                foreach (int mark in cell.Marks) {
                    if (!dict.ContainsKey(mark)) continue;
                    dict[mark].Add((row, col));
                }
            } else {
                dict.Remove(cell.Value);
            }
        }
        foreach(var kvp in dict) {
            //check if all the candidates are in the same group (excepting the one it's currently iterating through)
            var groups = GetGroups(kvp.Value.First().row, kvp.Value.First().col);
            groups.Remove((type, group));
            foreach(var pos in kvp.Value.Skip(1)) {
                groups.IntersectWith(GetGroups(pos.row, pos.col));
            }
            if (groups.Count == 0) continue;
            var removed = RemoveMarks(groups.First().type, groups.First().group, new HashSet<int>() { kvp.Key }, kvp.Value);
            if (removed.Count == 0) continue;
            var colors = new Dictionary<(int row, int col), Color>();
            foreach(var cell in kvp.Value) {
                colors[cell] = Colors.Blue;
            }
            foreach(var cell in removed) {
                colors[cell] = Colors.Green;
            }
            return new StepResult($"Locked candidates: All candidates for {kvp.Key} in {type.ToString()} {group + 1} are in {groups.First().type.ToString()} {groups.First().group + 1}, " +
                $"affecting cells {ListCells(removed)}", colors);
        }
        return null;
    });

    private StepResult DoForEachGroup(Func<GroupType,int,StepResult> function) {
        StepResult result = null;
        foreach(var type in Types) {
            for(int group = 0; group < 9; group++) {
                result = function(type, group);
                if (result is not null) return result;
            }
        }
        return null;
    }

    private string ListCells(IEnumerable<(int row, int col)> cells) {
        var sb = new StringBuilder("[");
        foreach(var cell in cells) {
            sb.Append($"R{cell.row + 1}C{cell.col + 1}, ");
        }
        sb.Remove(sb.Length - 2, 2);
        sb.Append("]");
        return sb.ToString();
    }

    private string ListMarks(IEnumerable<int> marks) {
        var sb = new StringBuilder("[");
        foreach (int mark in marks) {
            sb.Append($"{mark}, ");
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
        DoForEachGroup((type, group) => {
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
            for (int mark = 1; mark < 10; mark++) {
                if (!set.Contains(mark)) {
                    var cellsInGroup = new List<(int, int)>();
                    for (int i = 0; i < 9; i++) {
                        cellsInGroup.Add(GetPos(type, group, i));
                    }
                    throw new InvalidStateException($"{type.ToString()} {group + 1} has no options for {mark}", cellsInGroup);
                }
            }
            return null;
        });
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

    private HashSet<(GroupType type, int group)> GetGroups(int row, int col) => new() {
            (GroupType.Row, row),
            (GroupType.Column, col),
            (GroupType.Box, row / 3 * 3 + col / 3)
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