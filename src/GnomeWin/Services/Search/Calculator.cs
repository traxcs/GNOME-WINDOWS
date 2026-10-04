using System.Globalization;

namespace GnomeWin.Services.Search;

public static class Calculator
{
    public static bool LooksLikeExpression(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        bool hasDigit = false, hasOp = false;
        foreach (char c in s)
        {
            if (char.IsDigit(c)) hasDigit = true;
            else if ("+-*/%^×÷".Contains(c)) hasOp = true;
            else if (!"()., =".Contains(c)) return false;
        }
        return hasDigit && hasOp;
    }

    public static bool TryEvaluate(string expression, out double result)
    {
        result = 0;
        try
        {
            var p = new Parser(expression.Replace('×', '*').Replace('÷', '/').Replace(',', '.').TrimEnd('=', ' '));
            result = p.ParseExpression();
            p.SkipSpaces();
            return p.AtEnd && !double.IsNaN(result) && !double.IsInfinity(result);
        }
        catch { return false; }
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;
        public Parser(string s) => _s = s;
        public bool AtEnd => _i >= _s.Length;

        public void SkipSpaces() { while (_i < _s.Length && _s[_i] == ' ') _i++; }

        private bool Eat(char c)
        {
            SkipSpaces();
            if (_i < _s.Length && _s[_i] == c) { _i++; return true; }
            return false;
        }

        public double ParseExpression()
        {
            double v = ParseTerm();
            while (true)
            {
                if (Eat('+')) v += ParseTerm();
                else if (Eat('-')) v -= ParseTerm();
                else return v;
            }
        }

        private double ParseTerm()
        {
            double v = ParseFactor();
            while (true)
            {
                if (Eat('*')) v *= ParseFactor();
                else if (Eat('/')) v /= ParseFactor();
                else if (Eat('%')) v %= ParseFactor();
                else return v;
            }
        }

        private double ParseFactor()
        {
            double b = ParseUnary();
            if (Eat('^')) return Math.Pow(b, ParseFactor());
            return b;
        }

        private double ParseUnary()
        {
            if (Eat('-')) return -ParseUnary();
            if (Eat('+')) return ParseUnary();
            if (Eat('('))
            {
                double v = ParseExpression();
                if (!Eat(')')) throw new FormatException("missing )");
                return v;
            }
            SkipSpaces();
            int start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
            if (start == _i) throw new FormatException("number expected");
            return double.Parse(_s.AsSpan(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
