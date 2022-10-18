using System.Collections.ObjectModel;

namespace SudokuSolver;
public class InvalidStateException : Exception {
    public string Log;
    public readonly string Reason;
    public readonly ReadOnlyCollection<(int row, int col)> InvolvedCells;
    public InvalidStateException(string reason, IList<(int row, int col)> cells) : base(reason) {
        Reason = reason;
        InvolvedCells = new ReadOnlyCollection<(int, int)>(cells);
    }
}
