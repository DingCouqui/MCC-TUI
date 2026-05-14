using Terminal.Gui;

namespace MccTui;

sealed class ColoredOutputView : View
{
    private static readonly Dictionary<char, Color> _colorMap = new()
    {
        ['0'] = Color.Black,
        ['1'] = Color.Blue,
        ['2'] = Color.Green,
        ['3'] = Color.BrightCyan,
        ['4'] = Color.Red,
        ['5'] = Color.Magenta,
        ['6'] = Color.BrightYellow,
        ['7'] = Color.Gray,
        ['8'] = Color.DarkGray,
        ['9'] = Color.BrightBlue,
        ['a'] = Color.BrightGreen,
        ['b'] = Color.BrightCyan,
        ['c'] = Color.BrightRed,
        ['d'] = Color.BrightMagenta,
        ['e'] = Color.BrightYellow,
        ['f'] = Color.White,
        ['r'] = Color.White,
    };

    private IList<string>? _lines;
    private int _scrollOffset;

    public IList<string>? Lines
    {
        get => _lines;
        set { _lines = value; _scrollOffset = 0; SetNeedsDisplay(); }
    }

    public bool IsAtEnd => _scrollOffset == 0;

    public void MoveEnd()
    {
        _scrollOffset = 0;
        SetNeedsDisplay();
    }

    public override void OnDrawContent(System.Drawing.Rectangle viewport)
    {
        var driver = Application.Driver!;
        int totalLines = _lines?.Count ?? 0;
        int visibleRows = viewport.Height;

        int startLine;
        if (totalLines <= visibleRows)
            startLine = 0;
        else
            startLine = Math.Max(0, totalLines - visibleRows - _scrollOffset);

        for (int row = 0; row < visibleRows; row++)
        {
            Move(0, row);
            int lineIndex = startLine + row;

            if (lineIndex >= totalLines || _lines == null)
            {
                driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                continue;
            }

            RenderLine(driver, _lines[lineIndex]);
        }
    }

    private static void RenderLine(ConsoleDriver driver, string line)
    {
        Color currentFg = Color.White;
        int pos = 0;

        while (pos < line.Length)
        {
            if (pos + 1 < line.Length && line[pos] == '§' && _colorMap.TryGetValue(line[pos + 1], out var color))
            {
                currentFg = color;
                pos += 2;
                continue;
            }

            int next = line.IndexOf('§', pos);
            int segEnd = next == -1 ? line.Length : next;
            int segLen = segEnd - pos;

            if (segLen > 0)
            {
                driver.SetAttribute(new Terminal.Gui.Attribute(currentFg, Color.Black));
                driver.AddStr(line.Substring(pos, segLen));
            }

            pos = segEnd;
        }
    }
}
