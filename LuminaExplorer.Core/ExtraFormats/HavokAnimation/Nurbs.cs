using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace LuminaExplorer.Core.ExtraFormats.HavokAnimation;

public class Nurbs {
    private readonly int _elementCount;
    private readonly ImmutableList<float[]> _controlPoints;
    private readonly ImmutableList<byte> _knots;
    private readonly int _degree;

    public Nurbs(int elementCount, IEnumerable<float[]> controlPoints, IEnumerable<byte> knots, int degree)
    {
        this._elementCount = elementCount;
        this._controlPoints = controlPoints.ToImmutableList();
        this._knots = knots.ToImmutableList();
        this._degree = degree;
    }

    public float[] this[float t] {
        get {
            var span = this._FindSpan(t);
            var basis = this._BsplineBasis(span, t);

            var value = new float[this._elementCount];
            for (var i = 0; i <= this._degree; i++) {
                for (var j = 0; j < this._elementCount; j++)
                    value[j] += this._controlPoints[span - i][j] * basis[i];
            }

            return value;
        }
    }

    /*
     * bsplineBasis and findSpan are based on the implementations of
     * https://github.com/PredatorCZ/HavokLib
     */

    private float[] _BsplineBasis(int span, float t)
    {
        var res = Enumerable.Range(0, this._degree + 1).Select(_ => 0f).ToArray();

        res[0] = 1f;

        for (var i = 0; i < this._degree; ++i) {
            for (var j = i; j >= 0; --j) {
                var a = (t - this._knots[span - j]) / (this._knots[span + i + 1 - j] - this._knots[span - j]);
                var tmp = res[j] * a;
                res[j + 1] += res[j] - tmp;
                res[j] = tmp;
            }
        }

        return res;
    }

    private int _FindSpan(float t)
    {
        if (t >= this._knots[this._controlPoints.Count])
            return this._controlPoints.Count - 1;
        if (t < 0)
            return 0;

        var low = this._degree;
        var high = this._controlPoints.Count;
        var mid = (low + high) / 2;

        while (t < this._knots[mid] || t >= this._knots[mid + 1]) {
            if (t < this._knots[mid]) {
                high = mid;
            } else {
                low = mid;
            }

            mid = (low + high) / 2;
        }

        return mid;
    }
}
