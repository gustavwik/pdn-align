// Name: Align
// Submenu: Align
// Author: Gustav W. Fevang
// Title: Align Content
// Version: 1.0
// Desc: Moves the visible content of the current layer to a chosen position in the canvas or selection
// Keywords: align|center|centre|middle|position|move|layer
// URL: https://github.com/gustavwik/pdn-align
#region UICode
ListBoxControl HorizAlign = 1; // Horizontal|Leave as is|Center|Left|Right
ListBoxControl VertAlign = 1; // Vertical|Leave as is|Middle|Top|Bottom
IntSliderControl AlphaCutoff = 0; // [0,254] Ignore pixels fainter than
CheckboxControl AlignToCanvas = false; // Ignore selection, align to whole canvas
#endregion

/* ------------------------------------------------------------------
   How this works, in three steps:

   1. Find the content. Scan the layer for the smallest rectangle that
      contains every pixel that isn't (near) transparent. That box is
      what we're actually aligning — not the layer, which is always the
      full canvas size.

   2. Work out how far to move it. Compare the content box to the frame
      we're aligning inside, and calculate an x/y offset.

   3. Draw it shifted. For every output pixel, read the source pixel
      that offset says should land there. Anything with no source pixel
      behind it becomes transparent.

   Paint.NET calls OnRender once per tile, on several threads at once.
   Step 1 must only happen once, so the result is cached behind a lock.
------------------------------------------------------------------ */

private readonly object scanLock = new object();
private int cachedKey = int.MinValue;
private bool cachedFound;
private int cachedMinX, cachedMinY, cachedMaxX, cachedMaxY;

protected override void OnRender(IBitmapEffectOutput output)
{
    using IEffectInputBitmap<ColorBgra32> sourceBitmap = Environment.GetSourceBitmapBgra32();
    using IBitmapLock<ColorBgra32> sourceLock = sourceBitmap.Lock(new RectInt32(0, 0, sourceBitmap.Size));
    RegionPtr<ColorBgra32> sourceRegion = sourceLock.AsRegionPtr();

    RectInt32 outputBounds = output.Bounds;
    using IBitmapLock<ColorBgra32> outputLock = output.LockBgra32();
    var outputRegion = outputLock.AsRegionPtr().OffsetView(-outputBounds.Location);

    int canvasWidth = Environment.Document.Size.Width;
    int canvasHeight = Environment.Document.Size.Height;

    // The frame is the box we align inside. With no selection active,
    // RenderBounds is already the whole canvas, so this does the right
    // thing by default and the checkbox is only needed to deliberately
    // ignore a selection that does exist.
    var selection = Environment.Selection.RenderBounds;
    int frameLeft   = AlignToCanvas ? 0 : selection.Left;
    int frameTop    = AlignToCanvas ? 0 : selection.Top;
    int frameRight  = AlignToCanvas ? canvasWidth : selection.Right;
    int frameBottom = AlignToCanvas ? canvasHeight : selection.Bottom;

    ScanContent(sourceRegion, frameLeft, frameTop, frameRight, frameBottom, AlphaCutoff);

    int offsetX = 0;
    int offsetY = 0;

    if (cachedFound)
    {
        int contentWidth  = cachedMaxX - cachedMinX + 1;
        int contentHeight = cachedMaxY - cachedMinY + 1;

        // Each case works out where the content's left/top edge should
        // end up, then subtracts where it currently is.
        switch (HorizAlign)
        {
            case 1: offsetX = frameLeft + (frameRight - frameLeft - contentWidth) / 2 - cachedMinX; break;
            case 2: offsetX = frameLeft - cachedMinX; break;
            case 3: offsetX = frameRight - contentWidth - cachedMinX; break;
        }

        switch (VertAlign)
        {
            case 1: offsetY = frameTop + (frameBottom - frameTop - contentHeight) / 2 - cachedMinY; break;
            case 2: offsetY = frameTop - cachedMinY; break;
            case 3: offsetY = frameBottom - contentHeight - cachedMinY; break;
        }
    }

    ColorBgra32 blank = default; // all zero channels, including alpha

    for (int y = outputBounds.Top; y < outputBounds.Bottom; ++y)
    {
        if (IsCancelRequested) return;

        for (int x = outputBounds.Left; x < outputBounds.Right; ++x)
        {
            int sx = x - offsetX;
            int sy = y - offsetY;

            bool insideSource = sx >= 0 && sx < canvasWidth && sy >= 0 && sy < canvasHeight;
            outputRegion[x, y] = insideSource ? sourceRegion[sx, sy] : blank;
        }
    }
}

/* Finds the bounding box of everything worth keeping.

   The alpha cutoff matters more than it looks. Anti-aliased edges and
   soft brushes leave a halo of pixels at alpha 1 or 2 that are
   invisible on screen but still count as content, which drags the
   bounding box outward and throws the centering off by a few pixels.
   Raising the cutoff ignores them. */
private void ScanContent(RegionPtr<ColorBgra32> src, int left, int top, int right, int bottom, int cutoff)
{
    // The box only changes if the cutoff or the frame changes, so cache
    // on those. Without this, every tile would rescan the whole layer.
    int key = cutoff * 31 + left * 7 + top * 13 + right * 17 + bottom * 19;

    lock (scanLock)
    {
        if (cachedKey == key) return;

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;

        for (int y = top; y < bottom; ++y)
        {
            for (int x = left; x < right; ++x)
            {
                if (src[x, y].A <= cutoff) continue;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        cachedFound = maxX >= minX;
        cachedMinX = minX;
        cachedMinY = minY;
        cachedMaxX = maxX;
        cachedMaxY = maxY;
        cachedKey = key;
    }
}
