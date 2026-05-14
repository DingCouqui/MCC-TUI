using Terminal.Gui;

namespace MccTui;

sealed class ColoredOutputView : View
{
    private static readonly Dictionary<char, Color> _colorMap = new()
    {
        ['0'] = Color.Black,       ['1'] = Color.Blue,
        ['2'] = Color.Green,        ['3'] = Color.BrightCyan,
        ['4'] = Color.Red,          ['5'] = Color.Magenta,
        ['6'] = Color.BrightYellow, ['7'] = Color.Gray,
        ['8'] = Color.DarkGray,     ['9'] = Color.BrightBlue,
        ['a'] = Color.BrightGreen,  ['b'] = Color.BrightCyan,
        ['c'] = Color.BrightRed,    ['d'] = Color.BrightMagenta,
        ['e'] = Color.BrightYellow, ['f'] = Color.White,
        ['r'] = Color.White,
    };

    private IList<string>? _lines;
    private int _scrollOffset;
    private int _drawCounter;

    private (int Line, int Col)? _selStart;
    private (int Line, int Col)? _selEnd;
    private bool _isDragging;
    private bool _didDrag;

    public IList<string>? Lines
    {
        get => _lines;
        set
        {
            var count = value?.Count ?? 0;
            DebugLogger.Log($"OutputView.Lines set: count={count} dragging={_isDragging}");
            _lines = value;
            if (!_isDragging)
                _scrollOffset = 0;
            if (!_isDragging)
                ClearSelection();
            SetNeedsDisplay();
        }
    }

    public bool IsAtEnd => _scrollOffset == 0;

    public void MoveEnd()
    {
        DebugLogger.Log($"OutputView.MoveEnd: scrollOffset was {_scrollOffset}");
        _scrollOffset = 0;
        SetNeedsDisplay();
    }

    public override void OnDrawContent(System.Drawing.Rectangle viewport)
    {
        _drawCounter++;
        if (_drawCounter % 20 == 1)
        {
            DebugLogger.Log($"OutputView.OnDrawContent: #{_drawCounter} total={_lines?.Count ?? 0} scroll={_scrollOffset} view={viewport.Height} selStart=({_selStart?.Line},{_selStart?.Col}) selEnd=({_selEnd?.Line},{_selEnd?.Col}) dragging={_isDragging}");
        }

        var driver = Application.Driver!;
        int totalLines = _lines?.Count ?? 0;
        int visibleRows = viewport.Height;

        int startLine;
        if (totalLines <= visibleRows)
            startLine = 0;
        else
            startLine = Math.Max(0, totalLines - visibleRows - _scrollOffset);

        var (selMin, selMax) = NormalizedSelection();

        for (int row = 0; row < visibleRows; row++)
        {
            Move(0, row);
            int lineIndex = startLine + row;

            if (lineIndex >= totalLines || _lines == null)
            {
                driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                continue;
            }

            int selStartCol = -1;
            int selEndCol = -1;
            if (selMin != null && selMax != null)
            {
                if (lineIndex > selMin.Value.Line && lineIndex < selMax.Value.Line)
                {
                    selStartCol = 0;
                    selEndCol = int.MaxValue;
                }
                else if (lineIndex == selMin.Value.Line && lineIndex == selMax.Value.Line)
                {
                    selStartCol = Math.Min(selMin.Value.Col, selMax.Value.Col);
                    selEndCol = Math.Max(selMin.Value.Col, selMax.Value.Col);
                }
                else if (lineIndex == selMin.Value.Line)
                {
                    selStartCol = selMin.Value.Col;
                    selEndCol = int.MaxValue;
                }
                else if (lineIndex == selMax.Value.Line)
                {
                    selStartCol = 0;
                    selEndCol = selMax.Value.Col;
                }
            }

            RenderLine(driver, _lines[lineIndex], selStartCol, selEndCol);
        }
    }

    protected override bool OnMouseEvent(MouseEvent mouseEvent)
    {
        DebugLogger.Log($"OutputView.OnMouseEvent: flags={mouseEvent.Flags} pos=({mouseEvent.Position.X},{mouseEvent.Position.Y}) lines={_lines?.Count ?? 0} dragging={_isDragging}");

        if (_lines == null || _lines.Count == 0)
        {
            DebugLogger.Log($"OutputView.OnMouseEvent: no lines, returning false");
            return false;
        }

        int totalLines = _lines.Count;
        int visibleRows = Frame.Height;
        int startLine = totalLines <= visibleRows
            ? 0
            : Math.Max(0, totalLines - visibleRows - _scrollOffset);

        int lineIndex = startLine + mouseEvent.Position.Y;
        if (lineIndex < 0 || lineIndex >= totalLines)
        {
            DebugLogger.Log($"OutputView.OnMouseEvent: lineIndex={lineIndex} out of range [0,{totalLines}), returning false");
            return false;
        }

        int visibleCol = GetVisibleColumn(_lines[lineIndex], mouseEvent.Position.X);

        if ((mouseEvent.Flags & MouseFlags.Button1Pressed) != 0 && !_isDragging)
        {
            DebugLogger.Log($"OutputView.OnMouseEvent: Button1Pressed => selStart=({lineIndex},{visibleCol})");
            _selStart = (lineIndex, visibleCol);
            _selEnd = null;
            _isDragging = true;
            _didDrag = false;
            SetNeedsDisplay();
            return true;
        }

        if ((mouseEvent.Flags & MouseFlags.ReportMousePosition) != 0 && _isDragging)
        {
            DebugLogger.Log($"OutputView.OnMouseEvent: DragMove => selEnd=({lineIndex},{visibleCol})");
            _selEnd = (lineIndex, visibleCol);
            _didDrag = true;
            SetNeedsDisplay();
            return true;
        }

        if ((mouseEvent.Flags & MouseFlags.Button1Released) != 0 && _isDragging)
        {
            DebugLogger.Log($"OutputView.OnMouseEvent: Button1Released => selEnd=({lineIndex},{visibleCol}) dragging=false");
            _selEnd = (lineIndex, visibleCol);
            _isDragging = false;
            SetNeedsDisplay();
            return true;
        }

        if ((mouseEvent.Flags & MouseFlags.Button1Clicked) != 0)
        {
            if (!_didDrag)
            {
                DebugLogger.Log($"OutputView.OnMouseEvent: Button1Clicked no drag => clear selection");
                ClearSelection();
                SetNeedsDisplay();
            }
            else
            {
                DebugLogger.Log($"OutputView.OnMouseEvent: Button1Clicked after drag, keeping selection");
            }
            return true;
        }

        DebugLogger.Log($"OutputView.OnMouseEvent: unhandled flags={mouseEvent.Flags}, returning false");
        return false;
    }

    public string GetSelectedText()
    {
        if (_lines == null || _lines.Count == 0)
            return "";

        var (selMin, selMax) = NormalizedSelection();
        if (selMin == null || selMax == null)
        {
            DebugLogger.Log($"OutputView.GetSelectedText: no selection");
            return "";
        }

        DebugLogger.Log($"OutputView.GetSelectedText: selMin=({selMin.Value.Line},{selMin.Value.Col}) selMax=({selMax.Value.Line},{selMax.Value.Col})");

        var sb = new System.Text.StringBuilder();

        for (int i = selMin.Value.Line; i <= selMax.Value.Line && i < _lines.Count; i++)
        {
            if (sb.Length > 0)
                sb.Append('\n');

            string line = _lines[i];
            int startCol = (i == selMin.Value.Line) ? selMin.Value.Col : 0;
            int endCol = (i == selMax.Value.Line) ? selMax.Value.Col : int.MaxValue;

            if (startCol > endCol)
                (startCol, endCol) = (endCol, startCol);

            string visibleText = GetVisibleText(line, startCol, endCol);
            sb.Append(visibleText);
        }

        return sb.ToString();
    }

    public void ClearSelection()
    {
        DebugLogger.Log($"OutputView.ClearSelection: was selStart=({_selStart?.Line},{_selStart?.Col}) selEnd=({_selEnd?.Line},{_selEnd?.Col}) dragging={_isDragging}");
        _selStart = null;
        _selEnd = null;
        _isDragging = false;
        _didDrag = false;
    }

    public override bool OnKeyDown(Key keyEvent)
    {
        DebugLogger.Log($"OutputView.OnKeyDown: key={keyEvent} selExists={_selStart != null} dragging={_isDragging}");

        if (keyEvent == Key.C.WithCtrl)
        {
            var text = GetSelectedText();
            if (text.Length > 0)
            {
                DebugLogger.Log($"OutputView.OnKeyDown: Ctrl+C copy {text.Length} chars");
                Clipboard.TrySetClipboardData(text);
            }
            _isDragging = false;
            return true;
        }

        return false;
    }

    private ((int Line, int Col)?, (int Line, int Col)?) NormalizedSelection()
    {
        if (_selStart == null) return (null, null);
        if (_selEnd == null) return (_selStart, _selStart);

        var a = _selStart.Value;
        var b = _selEnd.Value;

        if (a.Line < b.Line || (a.Line == b.Line && a.Col <= b.Col))
            return (a, b);
        return (b, a);
    }

    private static void RenderLine(ConsoleDriver driver, string line, int selStartCol, int selEndCol)
    {
        Color currentFg = Color.White;
        int pos = 0;
        int visibleCol = 0;

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
                int segVisStart = visibleCol;
                int segVisEnd = visibleCol + segLen;

                int drawStart = Math.Max(segVisStart, selStartCol);
                int drawEnd = Math.Min(segVisEnd, selEndCol);

                if (drawStart < drawEnd)
                {
                    int preLen = drawStart - segVisStart;
                    if (preLen > 0)
                    {
                        driver.SetAttribute(new Terminal.Gui.Attribute(currentFg, Color.Black));
                        driver.AddStr(line.Substring(pos, preLen));
                    }

                    driver.SetAttribute(new Terminal.Gui.Attribute(Color.Black, currentFg));
                    driver.AddStr(line.Substring(pos + preLen, drawEnd - drawStart));

                    int postStart = drawEnd - segVisStart;
                    int postLen = segLen - postStart;
                    if (postLen > 0)
                    {
                        driver.SetAttribute(new Terminal.Gui.Attribute(currentFg, Color.Black));
                        driver.AddStr(line.Substring(pos + postStart, postLen));
                    }
                }
                else
                {
                    driver.SetAttribute(new Terminal.Gui.Attribute(currentFg, Color.Black));
                    driver.AddStr(line.Substring(pos, segLen));
                }

                visibleCol = segVisEnd;
            }

            pos = segEnd;
        }
    }

    private static int GetVisibleColumn(string line, int targetX)
    {
        int visibleCol = 0;
        int pos = 0;

        while (pos < line.Length && visibleCol < targetX)
        {
            if (pos + 1 < line.Length && line[pos] == '§')
            {
                pos += 2;
                continue;
            }

            pos++;
            visibleCol++;
        }

        return visibleCol;
    }

    private static string GetVisibleText(string line, int startCol, int endCol)
    {
        var sb = new System.Text.StringBuilder();
        int visibleCol = 0;
        int pos = 0;

        while (pos < line.Length)
        {
            if (pos + 1 < line.Length && line[pos] == '§')
            {
                pos += 2;
                continue;
            }

            if (visibleCol >= startCol && visibleCol < endCol)
                sb.Append(line[pos]);

            pos++;
            visibleCol++;
        }

        return sb.ToString();
    }
}
