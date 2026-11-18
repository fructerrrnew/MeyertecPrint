namespace MeyertecPrint;

/// <summary>
/// Undo/Redo для макета: снапшоты (глубина 50 — как в эталоне).
/// Перед операцией — Push (текущий макет), затем мутация.
/// Undo/Redo подменяют текущий макет на сохранённый.
/// </summary>
public sealed class UndoRedo
{
    public const int MaxDepth = 50;

    private readonly Stack<LabelLayout> _undo = new();
    private readonly Stack<LabelLayout> _redo = new();

    /// <summary>Сохраняет текущий макет как точку отката (до мутации).</summary>
    public void Push(LabelLayout current)
    {
        _undo.Push(current.Clone());
        TrimUndo();
        _redo.Clear();
    }

    /// <summary>Обрезает глубину undo-стекки до MaxDepth (самые старые снапшоты выбрасываются).</summary>
    private void TrimUndo()
    {
        if (_undo.Count <= MaxDepth) return;
        // Stack не поддерживает удаление из низа — пересобираем
        var items = _undo.ToArray();
        _undo.Clear();
        foreach (var item in items.Take(MaxDepth).Reverse())
            _undo.Push(item);
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Откат: текущий макет уходит в redo, возвращается предыдущий.
    /// Возвращает false, если откатывать нечего.
    /// </summary>
    public bool Undo(ref LabelLayout current)
    {
        if (_undo.Count == 0) return false;
        _redo.Push(current.Clone());
        current = _undo.Pop();
        return true;
    }

    /// <summary>Повтор: текущий в undo (история двунаправленная), возвращается следующий.</summary>
    public bool Redo(ref LabelLayout current)
    {
        if (_redo.Count == 0) return false;
        _undo.Push(current.Clone());
        TrimUndo();
        current = _redo.Pop();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

/// <summary>Глубокое клонирование макета (для снапшотов и копий контента ячеек).</summary>
public static class LayoutClone
{
    public static LabelLayout Clone(this LabelLayout source)
    {
        var copy = new LabelLayout
        {
            Geometry = source.Geometry.Clone(),
            MediaWidthUm = source.MediaWidthUm,
            MediaHeightUm = source.MediaHeightUm,
            Rows = source.Rows.Select(r => r.Clone()).ToList(),
            Objects = (source.Objects ?? new()).Select(o => o.Clone()).ToList(),
        };
        return copy;
    }

    public static LabelGeometry Clone(this LabelGeometry source)
        => new()
        {
            LabelsInRow = source.LabelsInRow,
            WidthUm = source.WidthUm,
            HeightUm = source.HeightUm,
            TopUm = source.TopUm,
            LeftUm = source.LeftUm,
            RightUm = source.RightUm,
            GapBetweenUm = source.GapBetweenUm,
            GapBottomUm = source.GapBottomUm,
        };

    public static LabelRow Clone(this LabelRow source)
        => new()
        {
            HeightUm = source.HeightUm,
            Cells = source.Cells.Select(c => c.Clone()).ToList(),
        };

    public static LabelCell Clone(this LabelCell source)
        => new()
        {
            WidthUm = source.WidthUm,
            Content = source.Content.Clone(),
        };
}
