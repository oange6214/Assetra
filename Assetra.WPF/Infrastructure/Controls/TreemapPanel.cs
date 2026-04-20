using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Infrastructure.Controls;

/// <summary>
/// Custom WPF panel that lays out children as proportional tiles using a binary-split treemap.
///
/// Algorithm: recursive binary split, alternating vertical/horizontal divides.
/// The first split is always left-right so the largest item anchors the left side of the panel.
/// Items should be supplied in descending weight order (the VM's Rebuild() already does this).
///
/// <para>
/// <b>ScalePower</b> (default 0.65) applies a perceptual correction before layout:
/// <c>displayArea ∝ weight ^ ScalePower</c>.
/// Pure linear (1.0) makes a 70 % tile dominate and leaves tiny tiles unreadable.
/// 0.65 keeps the visual hierarchy intact (biggest is still clearly biggest) while giving
/// small tiles enough room to show their label.  The actual % is still shown as text.
/// </para>
///
/// Each child must set the attached <see cref="WeightProperty"/> via ItemContainerStyle on the
/// parent ItemsControl — NOT on the DataTemplate root — because ItemsControl wraps each item in a
/// ContentPresenter and ArrangeOverride reads Weight from that direct child.
/// </summary>
public sealed class TreemapPanel : Panel
{
    // ── Attached property: Weight ────────────────────────────────────────────

    public static readonly DependencyProperty WeightProperty =
        DependencyProperty.RegisterAttached(
            "Weight",
            typeof(double),
            typeof(TreemapPanel),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetWeight(DependencyObject obj) => (double)obj.GetValue(WeightProperty);
    public static void SetWeight(DependencyObject obj, double value) => obj.SetValue(WeightProperty, value);

    // ── ScalePower property ──────────────────────────────────────────────────

    /// <summary>
    /// Exponent applied to each normalised weight before layout.
    /// 1.0 = exact proportions; 0.5 = square-root (maximum small-tile boost).
    /// Default 0.65 gives the best balance for typical portfolio distributions.
    /// </summary>
    public static readonly DependencyProperty ScalePowerProperty =
        DependencyProperty.Register(
            nameof(ScalePower),
            typeof(double),
            typeof(TreemapPanel),
            new FrameworkPropertyMetadata(0.65, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double ScalePower
    {
        get => (double)GetValue(ScalePowerProperty);
        set => SetValue(ScalePowerProperty, value);
    }

    // ── Gap property ─────────────────────────────────────────────────────────

    /// <summary>
    /// Uniform gap (in device-independent pixels) between all tiles and at panel edges.
    /// Handled entirely by the panel — do NOT also set Margin on the DataTemplate Border.
    /// Default 2 px gives a clean 2 px gutter everywhere.
    /// </summary>
    public static readonly DependencyProperty GapProperty =
        DependencyProperty.Register(
            nameof(Gap),
            typeof(double),
            typeof(TreemapPanel),
            new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    // ── Panel overrides ──────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(
            double.IsInfinity(availableSize.Width)  ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

        foreach (UIElement child in Children)
            child.Measure(size);

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0 || finalSize.Width <= 0 || finalSize.Height <= 0)
            return finalSize;

        var weights = Children.Cast<UIElement>()
            .Select(c => Math.Max(GetWeight(c), 0d))
            .ToList();

        var rects = Layout(weights, finalSize.Width, finalSize.Height);

        // Apply uniform gap by snapping each tile's EDGES (not its size) to whole pixels.
        //
        // Why snap edges, not sizes?
        //   Snapping Width/Height independently causes drift: tile N's right edge ≠ tile N+1's
        //   left edge, producing hairline gaps or overlaps at HiDPI scaling.
        //   Snapping the left/top edges and deriving Width/Height from those coordinates
        //   guarantees adjacent tiles share the exact same pixel boundary.
        double halfGap = Gap / 2d;
        for (int i = 0; i < Children.Count; i++)
        {
            var r = rects[i];

            // Round each edge (not each dimension) to a whole pixel.
            // Using AwayFromZero so .5 values always round the same direction —
            // default "banker's rounding" can push two adjacent edges in opposite
            // directions, widening one gap to 3 px while shrinking the next to 1 px.
            double x1 = Math.Round(r.X           + halfGap, MidpointRounding.AwayFromZero);
            double y1 = Math.Round(r.Y           + halfGap, MidpointRounding.AwayFromZero);
            double x2 = Math.Round(r.X + r.Width - halfGap, MidpointRounding.AwayFromZero);
            double y2 = Math.Round(r.Y + r.Height- halfGap, MidpointRounding.AwayFromZero);

            Children[i].Arrange(new Rect(x1, y1, Math.Max(0d, x2 - x1), Math.Max(0d, y2 - y1)));
        }

        return finalSize;
    }

    // ── Layout algorithm ─────────────────────────────────────────────────────

    private List<Rect> Layout(List<double> weights, double W, double H)
    {
        var totalW = weights.Sum();
        if (totalW <= 0)
            return weights.Select(_ => new Rect()).ToList();

        // ── Perceptual scaling ──────────────────────────────────────────────
        double exp        = Math.Clamp(ScalePower, 0.1, 1.0);
        var    scaled     = weights.Select(w => Math.Pow(Math.Max(w / totalW, 0d), exp)).ToList();
        var    scaledTotal = scaled.Sum();
        var    areas      = (scaledTotal > 0
            ? scaled.Select(s => s / scaledTotal * W * H)
            : weights.Select(w => w / totalW * W * H)).ToList();

        var rects = new Rect[areas.Count];
        ColumnarLayout(areas, rects, new Rect(0, 0, W, H), 0, areas.Count);
        return [.. rects];
    }

    /// <summary>
    /// Hybrid layout that prevents the panel-level gap misalignment while still producing
    /// a rich 2-D treemap inside the right section.
    ///
    /// <para><b>Panel-level structure (one fixed vertical boundary):</b>
    /// <list type="number">
    ///   <item>The dominant item(s) fill the <b>left column</b> as stacked rows.</item>
    ///   <item>All remaining items go into the <b>right column</b>, whose left edge is
    ///         always at the same X — so the main divider never "drifts".</item>
    /// </list>
    /// </para>
    ///
    /// <para><b>Right column (rich layout):</b>
    /// <see cref="AlternatingLayout"/> applies a recursive binary split that alternates
    /// horizontal ↔ vertical at each level, producing a 2-D layout with tiles of varied
    /// aspect ratios — exactly like the reference Snowball-style treemap.
    /// Because this sub-layout is <em>entirely contained</em> within the right column
    /// rectangle, its internal boundaries never interfere with the panel-level divider.
    /// </para>
    /// </summary>
    private static void ColumnarLayout(
        IReadOnlyList<double> areas, Rect[] rects,
        Rect container, int start, int end)
    {
        int count = end - start;
        if (count <= 0) return;
        if (count == 1) { rects[start] = container; return; }

        double total = 0;
        for (int i = start; i < end; i++) total += areas[i];
        if (total <= 0) { for (int i = start; i < end; i++) rects[i] = new Rect(); return; }

        // ── Find the column split point ─────────────────────────────────────
        // If the first item ≥ 45 % of display area it gets its own column (dominant tile).
        // Threshold is 45 % rather than 50 % because perceptual scaling compresses the
        // dominant item: a 69 % real holding becomes ~49 % display area, which would
        // otherwise spill a second item into the left column and break the clean look.
        int    colSplit = start + 1;
        if (areas[start] / total < 0.45)
        {
            double half    = total / 2;
            double running = 0;
            for (int i = start; i < end - 1; i++)
            {
                running  += areas[i];
                colSplit  = i + 1;
                if (running >= half) break;
            }
        }

        double leftSum = 0;
        for (int i = start; i < colSplit; i++) leftSum += areas[i];
        double w1 = Math.Max(1d, container.Width * (leftSum / total));

        var leftCol  = new Rect(container.X,      container.Y, w1,                                 container.Height);
        var rightCol = new Rect(container.X + w1, container.Y, Math.Max(0d, container.Width - w1), container.Height);

        // ── Left column: stacked rows (dominant item, usually just one) ─────
        StackRows(areas, rects, leftCol, start, colSplit, leftSum);

        // ── Right column: rich 2-D alternating-split layout ─────────────────
        // Start direction chosen by aspect ratio: a tall column starts with a horizontal
        // split (rows first) so tiles stay wide; a wide column starts vertically.
        bool startH = rightCol.Height >= rightCol.Width;
        AlternatingLayout(areas, rects, rightCol, colSplit, end, startH);
    }

    /// <summary>
    /// Recursive binary split that alternates horizontal ↔ vertical at each level.
    /// Splits items into two ~equal-area halves, assigns each half a sub-rectangle,
    /// then recurses — producing a 2-D mosaic with naturally varied tile shapes.
    ///
    /// <para>
    /// Because every call operates entirely <em>within</em> its own <paramref name="container"/>
    /// rectangle, internal sub-boundaries never escape that rectangle.  The caller
    /// (<see cref="ColumnarLayout"/>) guarantees the container itself has a fixed position,
    /// so no panel-level gap drift is possible.
    /// </para>
    /// </summary>
    private static void AlternatingLayout(
        IReadOnlyList<double> areas, Rect[] rects,
        Rect container, int start, int end, bool splitH)
    {
        int count = end - start;
        if (count <= 0) return;
        if (count == 1) { rects[start] = container; return; }

        double total = 0;
        for (int i = start; i < end; i++) total += areas[i];
        if (total <= 0) { for (int i = start; i < end; i++) rects[i] = new Rect(); return; }

        // Find the split point closest to 50 % of total area.
        int    split   = start + 1;
        double half    = total / 2;
        double running = 0;
        for (int i = start; i < end - 1; i++)
        {
            running += areas[i];
            split    = i + 1;
            if (running >= half) break;
        }

        double firstSum = 0;
        for (int i = start; i < split; i++) firstSum += areas[i];
        double ratio = firstSum / total;

        Rect first, second;
        if (splitH)   // horizontal: top | bottom
        {
            double h1 = Math.Max(1d, container.Height * ratio);
            first  = new Rect(container.X, container.Y,      container.Width, h1);
            second = new Rect(container.X, container.Y + h1, container.Width, Math.Max(0d, container.Height - h1));
        }
        else          // vertical: left | right
        {
            double w1 = Math.Max(1d, container.Width * ratio);
            first  = new Rect(container.X,      container.Y, w1,                                 container.Height);
            second = new Rect(container.X + w1, container.Y, Math.Max(0d, container.Width - w1), container.Height);
        }

        AlternatingLayout(areas, rects, first,  start, split, !splitH);
        AlternatingLayout(areas, rects, second, split, end,   !splitH);
    }

    /// <summary>
    /// Place <c>areas[start..end)</c> as stacked horizontal rows inside <paramref name="container"/>.
    /// Every row spans the full column width, so all items share the same left and right X.
    /// Row heights are proportional to item area.
    /// </summary>
    private static void StackRows(
        IReadOnlyList<double> areas, Rect[] rects,
        Rect container, int start, int end, double groupTotal)
    {
        double y = container.Y;
        for (int i = start; i < end; i++)
        {
            double h = groupTotal > 0 ? areas[i] / groupTotal * container.Height : 0d;
            rects[i] = new Rect(container.X, y, container.Width, h);
            y += h;
        }
    }
}
