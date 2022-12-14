using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver;
public class SudokuState {
    public class Cell {
        public int Value = 0;
        public HashSet<int> Marks = new HashSet<int>(new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
    }
    public Cell[,] Cells;
    
    public SudokuState(Cell[,] cells) {
        if (cells.GetLength(0) != 9 || cells.GetLength(1) != 9) throw new ArgumentException($"{nameof(cells)} must have size [9,9]");
        Cells = cells;
    }

    public SudokuState Clone() {
        var cellsCopy = new Cell[9, 9];
        for(int r = 0; r < 9; r++) {
            for(int c = 0; c < 9; c++) {
                cellsCopy[r, c] = new Cell() { Value = Cells[r, c].Value, Marks = new HashSet<int>(Cells[r, c].Marks) };
            }
        }
        return new SudokuState(cellsCopy);
    }
}
