using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver;
public class SquareGrid : Grid {
    protected override Size MeasureOverride(double widthConstraint, double heightConstraint) {
        double minDimension = Math.Min(widthConstraint, heightConstraint);
        if (Height != minDimension || Width != minDimension) {
            HeightRequest = minDimension;
            WidthRequest = minDimension;
        }
        return base.MeasureOverride(widthConstraint, heightConstraint);
    }
}
