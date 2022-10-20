using System.Text;

namespace SudokuSolver;

public class SudokuSolver {
    private SudokuState State;
    private bool IsRecursive;
    public SudokuSolver(SudokuState state, bool isRecursive = false) {
        State = state;
        IsRecursive = isRecursive;
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
            if(result is null && !IsRecursive) {
                result = Guess();
            }
            if (result is null) {
                return null;
            }
            FillMarks();
            Verify();
            return result;
        } catch (InvalidStateException e) {
            if (result is not null) {
                e.PreviousStep = result;
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
        LockedCandidates,
        NTriple,
        HTriple,
        NQuad,
        HQuad,
        XWing,
        Swordfish,
        () => XYZWing(false),
        () => XYZWing(true)
    };

    private StepResult SingleCandidate() {
        for (int r = 0; r < 9; r++) {
            for (int c = 0; c < 9; c++) {
                SudokuState.Cell cell = State.Cells[r, c];
                if (cell.Value == 0 && cell.Marks.Count == 1) {
                    cell.Value = cell.Marks.First();
                    return new StepResult($"Single Candidate: Cell R{r + 1}C{c + 1} could only be {cell.Marks.First()}",
                        new Dictionary<(int row, int col), Color>() {
                            [(r, c)] = Colors.Green
                        });
                }
            }
        }
        return null;
    }

    private StepResult HSingle() => DoForEachGroup((type, group) => {
        var dict = GetMarkDict(type, group, 1);
        if (dict.Count == 0) return null;
        var kvp = dict.First();
        State.Cells[kvp.Value.First().row, kvp.Value.First().col].Value = kvp.Key;
        return new StepResult($"Hidden Single: Cell R{kvp.Value.First().row + 1}C{kvp.Value.First().col + 1} was the only possibility for {kvp.Key} in {type.ToString()} {group + 1}",
            new Dictionary<(int row, int col), Color>() {
                [(kvp.Value.First().row, kvp.Value.First().col)] = Colors.Green
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
        var dict = GetMarkDict(type, group, 2);
        //At this point, should be left with only lists of exactly 2 cells (0 would be invalid, 1 would be taken care of by HSingle). Verify anyway though, since HSingle might not have been run.
        var kvps = dict.ToList();
        for (int i = 0; i < dict.Count; i++) {
            for (int j = i + 1; j < dict.Count; j++) {
                if (!kvps[i].Value.SequenceEqual(kvps[j].Value) || kvps[i].Value.Count != 2) continue;
                var pos1 = kvps[i].Value.First();
                var pos2 = kvps[i].Value.Skip(1).First();
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

    private StepResult LockedCandidates() => DoForEachGroup((type, group) => {
        foreach (var kvp in GetMarkDict(type, group, 9)) {
            //check if all the candidates are in the same group (excepting the one it's currently iterating through)
            var groups = GetGroups(kvp.Value.First().row, kvp.Value.First().col);
            groups.Remove((type, group));
            foreach (var pos in kvp.Value.Skip(1)) {
                groups.IntersectWith(GetGroups(pos.row, pos.col));
            }
            if (groups.Count == 0) continue;
            var removed = RemoveMarks(groups.First().type, groups.First().group, kvp.Key, kvp.Value);
            if (removed.Count == 0) continue;
            var colors = new Dictionary<(int row, int col), Color>();
            foreach (var cell in kvp.Value) {
                colors[cell] = Colors.Blue;
            }
            foreach (var cell in removed) {
                colors[cell] = Colors.Green;
            }
            return new StepResult($"Locked candidates: All candidates for {kvp.Key} in {type.ToString()} {group + 1} are in {groups.First().type.ToString()} {groups.First().group + 1}, " +
                $"affecting cells {ListCells(removed)}", colors);
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
        var dict = GetMarkDict(type, group, 3);
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
                    for (int l = k + 1; l < cells.Count; l++) {
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

    private StepResult HQuad() => DoForEachGroup((type, group) => {
        //Mostly copied from HTriple; could probably factor much of it out
        var dict = GetMarkDict(type, group, 4);
        var kvps = dict.ToList();
        for (int i = 0; i < dict.Count; i++) {
            for (int j = i + 1; j < dict.Count; j++) {
                for (int k = j + 1; k < dict.Count; k++) {
                    for (int l = k + 1; l < dict.Count; l++) {
                        var set = new HashSet<(int row, int col)>(kvps[i].Value);
                        set.UnionWith(kvps[j].Value);
                        set.UnionWith(kvps[k].Value);
                        set.UnionWith(kvps[l].Value);
                        if (set.Count != 4) continue;
                        var marks = new HashSet<int>() { kvps[i].Key, kvps[j].Key, kvps[k].Key, kvps[l].Key };
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
                        return new StepResult($"Hidden quad: Cells {ListCells(set)} formed quad {ListMarks(marks)} in {type.ToString()} {group + 1}, " +
                            $"affecting cells {ListCells(changed)}", colors);
                    }
                }
            }
        }
        return null;
    });

    private StepResult XWing() {
        foreach (var type in new List<GroupType>() { GroupType.Row, GroupType.Column }) {
            var allMarkPairs = new List<Dictionary<int, HashSet<(int row, int col)>>>();
            for (int group = 0; group < 9; group++) {
                var dict = GetMarkDict(type, group, 2);
                foreach (var otherDict in allMarkPairs) {
                    for (int mark = 1; mark < 10; mark++) {
                        if (!(dict.ContainsKey(mark) && otherDict.ContainsKey(mark))) continue;
                        List<(int row, int col)> removed;
                        if (type == GroupType.Row) {
                            if (dict[mark].First().col != otherDict[mark].First().col || dict[mark].Last().col != otherDict[mark].Last().col) continue;
                            removed = RemoveMarks(GroupType.Column, dict[mark].First().col, mark,
                                new HashSet<(int, int)>() { dict[mark].First(), otherDict[mark].First() });
                            removed.AddRange(RemoveMarks(GroupType.Column, dict[mark].Last().col, mark,
                                    new HashSet<(int, int)>() { dict[mark].Last(), otherDict[mark].Last() }));
                        } else {
                            if (dict[mark].First().row != otherDict[mark].First().row || dict[mark].Last().row != otherDict[mark].Last().row) continue;
                            removed = RemoveMarks(GroupType.Row, dict[mark].First().row, mark,
                                new HashSet<(int, int)>() { dict[mark].First(), otherDict[mark].First() });
                            removed.AddRange(RemoveMarks(GroupType.Row, dict[mark].Last().row, mark,
                                    new HashSet<(int, int)>() { dict[mark].Last(), otherDict[mark].Last() }));
                        }
                        if (removed.Count == 0) continue;
                        var xWingCells = new List<(int row, int col)>() { dict[mark].First(), dict[mark].Last(), otherDict[mark].First(), otherDict[mark].Last() };
                        var colors = new Dictionary<(int row, int col), Color>();
                        foreach (var cell in xWingCells) {
                            colors[cell] = Colors.Blue;
                        }
                        foreach (var cell in removed) {
                            colors[cell] = Colors.Green;
                        }
                        return new StepResult($"X-Wing: Cells {ListCells(xWingCells)} prevent any other {mark}s in their " +
                            $"{(type == GroupType.Row ? "Column" : "Row")}, affecting cells {ListCells(removed)}", colors);
                    }
                }
                allMarkPairs.Add(dict);
            }
        }
        return null;
    }

    private StepResult Swordfish() {
        foreach (var type in new List<GroupType>() { GroupType.Row, GroupType.Column }) {
            var allMarkPairs = new List<Dictionary<int, HashSet<(int row, int col)>>>();
            for (int group = 0; group < 9; group++) {
                allMarkPairs.Add(GetMarkDict(type, group, 3));
            }
            for (int i = 0; i < allMarkPairs.Count; i++) {
                for (int j = i + 1; j < allMarkPairs.Count; j++) {
                    for (int k = j + 1; k < allMarkPairs.Count; k++) {
                        for (int mark = 1; mark < 10; mark++) {
                            if (!(allMarkPairs[i].ContainsKey(mark) && allMarkPairs[j].ContainsKey(mark) && allMarkPairs[k].ContainsKey(mark))) continue;
                            var dictIGroups = new HashSet<int>();
                            var exceptSet = new HashSet<(int row, int col)>();
                            foreach (var pos in allMarkPairs[i][mark]) {
                                dictIGroups.Add(type == GroupType.Row ? pos.col : pos.row);
                                exceptSet.Add(pos);
                            }
                            var dictJGroups = new HashSet<int>();
                            foreach (var pos in allMarkPairs[j][mark]) {
                                dictJGroups.Add(type == GroupType.Row ? pos.col : pos.row);
                                exceptSet.Add(pos);
                            }
                            var dictKGroups = new HashSet<int>();
                            foreach (var pos in allMarkPairs[k][mark]) {
                                dictKGroups.Add(type == GroupType.Row ? pos.col : pos.row);
                                exceptSet.Add(pos);
                            }
                            dictIGroups.UnionWith(dictJGroups);
                            dictIGroups.UnionWith(dictKGroups);
                            if (dictIGroups.Count != 3) continue;
                            var removed = RemoveMarks(type == GroupType.Row ? GroupType.Column : GroupType.Row, dictIGroups.First(), mark, exceptSet);
                            removed.AddRange(RemoveMarks(type == GroupType.Row ? GroupType.Column : GroupType.Row, dictIGroups.Skip(1).First(), mark, exceptSet));
                            removed.AddRange(RemoveMarks(type == GroupType.Row ? GroupType.Column : GroupType.Row, dictIGroups.Last(), mark, exceptSet));
                            if (removed.Count == 0) continue;
                            var colors = new Dictionary<(int row, int col), Color>();
                            foreach (var pos in exceptSet) {
                                colors[pos] = Colors.Blue;
                            }
                            foreach (var pos in removed) {
                                colors[pos] = Colors.Green;
                            }
                            return new StepResult($"Swordfish: Cells {ListCells(exceptSet)} for candidate {mark}, affecting cells {ListCells(removed)}", colors);
                        }
                    }
                }
            }
        }
        return null;
    }

    private StepResult XYZWing(bool doZ) {
        //This function is pretty bad; probably can be optimized, maybe with a dictionary? Still, not too big a deal.
        for (int rowA = 0; rowA < 9; rowA++) {
            for (int colA = 0; colA < 9; colA++) {
                var cellA = State.Cells[rowA, colA];
                if (cellA.Marks.Count != 2 || cellA.Value != 0) continue;
                var groupsA = GetGroups(rowA, colA);
                foreach ((GroupType typeA, int groupA) in groupsA) {
                    for (int i = 0; i < 9; i++) {
                        (int rowB, int colB) = GetPos(typeA, groupA, i);
                        if ((rowB, colB) == (rowA, colA)) continue;
                        var cellB = State.Cells[rowB, colB];
                        if (cellB.Marks.Count != (doZ ? 3 : 2) || cellB.Value != 0) continue;
                        var intersect = cellB.Marks.Intersect(cellA.Marks);
                        if (intersect.Count() != (doZ ? 2 : 1)) continue;
                        int x, y, z;
                        if (doZ) {
                            x = cellA.Marks.First();
                            y = cellB.Marks.Except(intersect).First();
                            z = cellA.Marks.Last();
                        }
                        else {
                            x = intersect.First();
                            y = cellB.Marks.Except(intersect).First();
                            z = cellA.Marks.Except(intersect).First();
                        }
                        var groupsB = GetGroups(rowB, colB);
                        groupsB.ExceptWith(groupsA);
                        foreach ((GroupType typeB, int groupB) in groupsB) {
                            for (int j = 0; j < 9; j++) {
                                (int rowC, int colC) = GetPos(typeB, groupB, j);
                                if ((rowC, colC) == (rowB, colB)) continue;
                                var cellC = State.Cells[rowC, colC];
                                if (cellC.Marks.Count != 2 || cellC.Value != 0) continue;
                                if (!(cellC.Marks.Contains(y) && cellC.Marks.Contains(z))) continue;
                                var groupsC = GetGroups(rowC, colC);
                                if (groupsC.Intersect(groupsA).Count() != 0) continue;
                                //If it got to this point, it is an XY-Wing, but very likely to not remove any marks
                                //Affected cells will be in both one of groupsA and one of groupsC
                                //If doZ, then it must be in one of groupsB as well
                                var affected = new List<(int row, int col)>();
                                foreach ((GroupType type, int group) in groupsA) {
                                    for (int k = 0; k < 9; k++) {
                                        (int row, int col) = GetPos(type, group, k);
                                        if ((row, col) == (rowB, colB)) continue;
                                        var groups = GetGroups(row, col);
                                        if (groups.Intersect(groupsC).Count() == 0) continue;
                                        if (doZ && groups.Intersect(groupsB).Count() == 0) continue;
                                        var cell = State.Cells[row, col];
                                        if (!(cell.Marks.Contains(z) && cell.Value == 0)) continue;
                                        cell.Marks.Remove(z);
                                        affected.Add((row, col));
                                    }
                                }
                                if (affected.Count == 0) continue;
                                var xyWingCells = new List<(int row, int col)>() {
                                    (rowA, colA), (rowB, colB), (rowC, colC)
                                };
                                var colors = new Dictionary<(int row, int col), Color>();
                                foreach (var pos in xyWingCells) {
                                    colors[pos] = Colors.Blue;
                                }
                                foreach (var pos in affected) {
                                    colors[pos] = Colors.Green;
                                }
                                string name = doZ ? "XYZ-Wing" : "XY-Wing";
                                return new StepResult($"{name}: Cells {ListCells(xyWingCells)} form {name} ({x}, {y}) for {z}, affecting {ListCells(affected)}", colors);
                            }
                        }
                    }
                }
            }
        }
        return null;
    }

    private StepResult Guess() {
        (StepResult result, SudokuState state) bestResult = (null, null);
        //Find the shortest (probably easiest to understand for a human) result
        void setResult((StepResult result, SudokuState state) arg) {
            if(bestResult.result is null || arg.result.Log.Length < bestResult.result.Log.Length) {
                bestResult = arg;
            }
        };
        foreach((var path1, var path2) in GetPathPairs()) {
            (List<string> logs, HashSet<(int row, int col)> involvedCells, bool fail, SudokuState state) path1Results = RunPath(path1.row, path1.col, path1.mark);
            if (path1Results.fail) {
                setResult(FailPathResult(path1.row, path1.col, path1.mark, path1Results.logs, path1Results.involvedCells));
                continue;
            }
            (List<string> logs, HashSet<(int row, int col)> involvedCells, bool fail, SudokuState state) path2Results = RunPath(path2.row, path2.col, path2.mark);
            if(path2Results.fail) {
                setResult(FailPathResult(path2.row, path2.col, path2.mark, path2Results.logs, path2Results.involvedCells));
                continue;
            }

            //only cells affected in both paths will be affected at this level
            var involvedCells = path1Results.involvedCells.Intersect(path2Results.involvedCells);
            var changedCells = new HashSet<(int row, int col)>();
            var state = State.Clone();
            foreach((int row, int col) in involvedCells) {
                var cell1 = path1Results.state.Cells[row, col];
                var cell2 = path2Results.state.Cells[row, col];
                var cellOriginal = state.Cells[row, col];
                if(cellOriginal.Value != 0) {
                    throw new Exception("This shouldn't happen");
                }
                var cellMarks = (cell1.Value == 0) ?  new HashSet<int>(cell1.Marks) : new HashSet<int>() { cell1.Value };
                cellMarks.UnionWith((cell2.Value == 0) ? cell2.Marks : new HashSet<int>() { cell2.Value });
                if(cellOriginal.Marks.Count < cellMarks.Count) {
                    cellOriginal.Marks = cellMarks;
                    changedCells.Add((row, col));
                }
            }
            if (changedCells.Count == 0) continue;

            var sb = new StringBuilder();
            if(path1.row == path2.row && path1.col == path2.col) {
                sb.AppendLine($"R{path1.row + 1}C{path1.col + 1} must be one of [{path1.mark}, {path2.mark}");
            } else {
                sb.AppendLine($"Either R{path1.row + 1}C{path1.col + 1} or R{path2.row + 1}C{path2.col + 1} must be {path1.mark}");
            }
            sb.AppendLine($"If R{path1.row + 1}C{path1.col + 1} is {path1.mark}:");
            foreach (string log in path1Results.logs) {
                sb.AppendLine("\t" + log);
            }
            sb.AppendLine($"If R{path2.row + 1}C{path2.col + 1} is {path2.mark}:");
            foreach (string log in path2Results.logs) {
                sb.AppendLine("\t" + log);
            }
            sb.Append($"Both paths changed cells {ListCells(changedCells)}");
            var colors = new Dictionary<(int row, int col), Color>();
            foreach(var pos in path1Results.involvedCells) {
                colors[pos] = Colors.OrangeRed;
            }
            foreach (var pos in path2Results.involvedCells) {
                colors[pos] = Colors.Blue;
            }
            foreach (var pos in involvedCells) {
                colors[pos] = Colors.Purple;
            }
            foreach (var pos in changedCells) {
                colors[pos] = Colors.Green;
            }
            setResult((new StepResult(sb.ToString(), colors), state));
        }
        if (bestResult.result is null) return null;
        State.Cells = bestResult.state.Cells;
        return bestResult.result;
    }

    private (StepResult, SudokuState) FailPathResult(int row, int col, int mark, List<string> logs, HashSet<(int row, int col)> involvedCells) {
        var state = State.Clone();
        state.Cells[row, col].Marks.Remove(mark);
        var sb = new StringBuilder();
        sb.AppendLine($"If R{row + 1}C{col + 1} is {mark}:");
        foreach(string log in logs) {
            sb.AppendLine("\t" + log);
        }
        sb.Append($"R{row + 1}C{col + 1} cannot be {mark}");
        var colors = new Dictionary<(int row, int col), Color>();
        foreach((int, int) pos in involvedCells) {
            colors[pos] = Colors.Blue;
        }
        colors[(row, col)] = Colors.Green;
        return (new StepResult(sb.ToString(), colors), state);
    }

    private (List<string> logs, HashSet<(int row, int col)> involvedCells, bool fail, SudokuState endState) RunPath(int row, int col, int mark) {
        var stateCopy = State.Clone();
        stateCopy.Cells[row, col].Value = mark;
        var logs = new List<string>();
        var involvedCells = new HashSet<(int row, int col)>();
        involvedCells.Add((row, col));
        var solver = new SudokuSolver(stateCopy, true);
        while(true) {
            try {
                var result = solver.Step();
                if (result is null) break;
                logs.Add(result.Log);
                foreach((var pos, var color) in result.Cells) {
                    involvedCells.Add(pos);
                }
            } catch(InvalidStateException e) {
                if(e.PreviousStep is not null) {
                    logs.Add(e.PreviousStep.Log);
                    foreach ((var pos, var color) in e.PreviousStep.Cells) {
                        involvedCells.Add(pos);
                    }
                }
                logs.Add("Invalid state: " + e.Reason);
                foreach(var pos in e.InvolvedCells) {
                    involvedCells.Add(pos);
                }
                return (logs, involvedCells, true, null);
            }
        }
        return (logs, involvedCells, false, solver.State);
    }

    private List<((int row, int col, int mark) path1, (int row, int col, int mark) path2)> GetPathPairs() {
        //every path should only be in one pair, since any other pairs it is in would happen anyway
        var pathPairs = new List<((int row, int col, int mark) path1, (int row, int col, int mark) path2)>();
        var paths = new HashSet<(int row, int col, int mark)>();
        for(int r = 0; r < 9; r++) {
            for(int c = 0; c < 9; c++) {
                var cell = State.Cells[r, c];
                if (cell.Marks.Count != 2 || cell.Value != 0) continue;
                (int, int, int) path1 = (r, c, cell.Marks.First());
                (int, int, int) path2 = (r, c, cell.Marks.Last());
                //don't need to check paths, as it will have a different position then any previous
                paths.Add(path1);
                paths.Add(path2);
                pathPairs.Add((path1, path2));
            }
        }
        DoForEachGroup((type, group) => {
            var markDict = GetMarkDict(type, group, 2);
            for(int mark = 1; mark < 10; mark++) {
                if (!markDict.ContainsKey(mark)) continue;
                var set = markDict[mark];
                (int, int, int) path1 = (set.First().row, set.First().col, mark);
                (int, int, int) path2 = (set.Last().row, set.Last().col, mark);
                if (paths.Contains(path1) || paths.Contains(path2)) continue;
                pathPairs.Add((path1, path2));
            }
            return null;
        });
        return pathPairs;
    }

    private Dictionary<int, HashSet<(int row, int col)>> GetMarkDict(GroupType type, int group, int maxMarks) {
        var dict = new Dictionary<int, HashSet<(int row, int col)>>();
        for (int i = 1; i < 10; i++) {
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
                if (dict[mark].Count == maxMarks) dict.Remove(mark);
                else dict[mark].Add((row, col));
            }
        }
        return dict;
    }

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

    private List<(int, int)> RemoveMarks(GroupType type, int group, int mark, HashSet<(int row, int col)> except) => RemoveMarks(type, group, new HashSet<int>() { mark }, except);
    
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