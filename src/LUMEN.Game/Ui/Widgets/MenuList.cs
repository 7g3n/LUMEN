using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Ui.Widgets;

public sealed record MenuItem(string Label, bool Enabled = true, string? Hint = null);

/// <summary>
/// A vertical keyboard/mouse menu. <see cref="Update"/> returns the activated index, or
/// -1. Disabled items are skipped by keyboard navigation and ignored on click.
/// </summary>
public sealed class MenuList
{
    private readonly List<MenuItem> _items;

    public MenuList(IEnumerable<MenuItem> items)
    {
        _items = items.ToList();
        Selected = FirstEnabled(0, 1);
    }

    public MenuList(params string[] labels) : this(labels.Select(l => new MenuItem(l))) { }

    public int Selected { get; set; }

    public float RowHeight { get; set; } = 44f;

    public IReadOnlyList<MenuItem> Items => _items;

    /// <summary>Returns the index the player activated this frame, or -1.</summary>
    public int Update(InputFrame input, Rectangle area)
    {
        if (_items.Count == 0)
        {
            return -1;
        }

        if (input.Pressed(Keys.Down) || input.Pressed(Keys.S))
        {
            Selected = FirstEnabled(Selected + 1, 1);
        }
        else if (input.Pressed(Keys.Up) || input.Pressed(Keys.W))
        {
            Selected = FirstEnabled(Selected - 1, -1);
        }

        int hovered = HitTest(input.MousePosition, area);
        if (hovered >= 0 && input.MouseMoved && _items[hovered].Enabled)
        {
            Selected = hovered;
        }

        if ((input.Pressed(Keys.Enter) || input.Pressed(Keys.Space))
            && _items[Selected].Enabled)
        {
            return Selected;
        }

        if (input.MouseClicked && hovered >= 0 && _items[hovered].Enabled)
        {
            Selected = hovered;
            return hovered;
        }

        return -1;
    }

    public void Draw(UiRenderer ui, Rectangle area, TextAlign align = TextAlign.Left)
    {
        var font = ui.Display(Theme.DisplayM);
        int y = area.Y;

        for (int i = 0; i < _items.Count; i++)
        {
            MenuItem item = _items[i];
            var row = new Rectangle(area.X, y, area.Width, (int)RowHeight);
            bool selected = i == Selected;

            Color color = !item.Enabled ? Theme.TextFaint
                : selected ? Theme.AccentBright
                : Theme.TextMuted;

            var textArea = align == TextAlign.Left
                ? new Rectangle(row.X + (selected ? 6 : 0), row.Y, row.Width, row.Height)
                : row;
            ui.Text(font, item.Label, textArea, color, align);

            if (selected && item.Enabled)
            {
                Vector2 size = ui.Measure(font, item.Label);
                if (align == TextAlign.Left)
                {
                    ui.FillRect(new Rectangle(area.X - 18, row.Y + 8, 4, (int)RowHeight - 16), Theme.Accent);
                }
                else
                {
                    int underlineW = (int)size.X + 20;
                    int cx = align == TextAlign.Center ? row.Center.X : row.Right - underlineW / 2;
                    ui.FillRect(new Rectangle(cx - underlineW / 2, row.Bottom - 6, underlineW, 2), Theme.Accent);
                }
            }

            if (item.Hint is { Length: > 0 } hint)
            {
                ui.Text(ui.Mono(Theme.Label), hint,
                    new Rectangle(row.X, row.Y, row.Width, row.Height), Theme.TextFaint, TextAlign.Right);
            }

            y += (int)RowHeight;
        }
    }

    private int HitTest(Point mouse, Rectangle area)
    {
        if (!area.Contains(mouse))
        {
            return -1;
        }

        int index = (int)((mouse.Y - area.Y) / RowHeight);
        return index >= 0 && index < _items.Count ? index : -1;
    }

    private int FirstEnabled(int start, int direction)
    {
        int count = _items.Count;
        for (int step = 0; step < count; step++)
        {
            int i = ((start + direction * step) % count + count) % count;
            if (_items[i].Enabled)
            {
                return i;
            }
        }

        return Math.Clamp(start, 0, count - 1);
    }
}
