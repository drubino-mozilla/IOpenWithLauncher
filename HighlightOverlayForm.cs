using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Svg;

namespace DIExplorer;

/// <summary>
/// Describes a character overlay: the embedded resource name,
/// its native dimensions, and the Y coordinates of the horizontal
/// transparent gap where the button should appear.
/// PNG resources are drawn directly; SVG resources use Svg.NET.
/// </summary>
internal sealed record OverlayCharacter(
    string ResourceName,
    float NativeWidth,
    float NativeHeight,
    float GapTopY,
    float GapBottomY)
{
    public bool IsPng => ResourceName.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    public static readonly OverlayCharacter Kit = new(
        "Kit.svg", 420f, 600f, 226.548f, 407.743f);

    public static readonly OverlayCharacter Clippy = new(
        "Clippy.png", 840f, 540f, 270f, 360f);
}

/// <summary>
/// A borderless, topmost, fully transparent and click-through overlay
/// that draws a character (Kit or Clippy) positioned so the horizontal
/// gap in the SVG aligns with the target button — making the character
/// appear to stand behind the button the user needs to click.
///
/// Uses UpdateLayeredWindow with per-pixel alpha so anti-aliased
/// edges blend cleanly against whatever is behind the window.
/// </summary>
internal sealed class HighlightOverlayForm : Form
{
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly Color ButtonBoxColor = ColorTranslator.FromHtml("#7543E3");

    private readonly OverlayCharacter _character;
    private Bitmap? _processedPng;

    public HighlightOverlayForm(OverlayCharacter character)
    {
        _character = character;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
    }

    public void SetTargetBounds(Rectangle buttonBounds)
    {
        float gapHeight = _character.GapBottomY - _character.GapTopY;
        float gapCenterY = (_character.GapTopY + _character.GapBottomY) / 2f;

        float scale = buttonBounds.Height / gapHeight;

        int charW = Math.Max(1, (int)(_character.NativeWidth * scale));
        int charH = Math.Max(1, (int)(_character.NativeHeight * scale));
        float gapCenterPx = gapCenterY * scale;

        int btnCenterX = buttonBounds.X + buttonBounds.Width / 2;
        int btnCenterY = buttonBounds.Y + buttonBounds.Height / 2;

        int bmpW = Math.Max(charW, buttonBounds.Width + 2);
        int bmpH = charH;

        int overlayX = btnCenterX - bmpW / 2;
        int overlayY = btnCenterY - (int)gapCenterPx;

        int charOffsetX = (bmpW - charW) / 2;

        var btnRelative = new Rectangle(
            buttonBounds.X - overlayX, buttonBounds.Y - overlayY,
            buttonBounds.Width, buttonBounds.Height);

        SetBounds(overlayX, overlayY, bmpW, bmpH);
        ApplyBitmap(bmpW, bmpH, charW, charH, charOffsetX, btnRelative);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    private void ApplyBitmap(int bmpW, int bmpH, int charW, int charH, int charOffsetX, Rectangle btnRelative)
    {
        using var bmp = RenderCharacter(bmpW, bmpH, charW, charH, charOffsetX, btnRelative);
        if (bmp == null)
            return;

        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var hBitmap = bmp.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
        var oldBitmap = SelectObject(memDc, hBitmap);

        var blend = new BLENDFUNCTION
        {
            BlendOp = 0,             // AC_SRC_OVER
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1,         // AC_SRC_ALPHA
        };

        var ptSrc = new POINT(0, 0);
        var ptDst = new POINT(Left, Top);
        var size = new SIZE(bmpW, bmpH);

        UpdateLayeredWindow(Handle, screenDc, ref ptDst, ref size,
            memDc, ref ptSrc, 0, ref blend, ULW_ALPHA);

        SelectObject(memDc, oldBitmap);
        DeleteObject(hBitmap);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

    private Bitmap? RenderCharacter(int bmpW, int bmpH, int charW, int charH, int charOffsetX, Rectangle btnRelative)
    {
        var bmp = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (_character.IsPng)
            {
                DrawPngCharacter(g, charW, charH, charOffsetX);
            }
            else
            {
                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(_character.ResourceName);
                if (stream == null) { bmp.Dispose(); return null; }
                DrawSvgCharacter(g, stream, charW, charH, charOffsetX);
            }

            using var pen = new Pen(ButtonBoxColor, 1f);
            g.DrawRectangle(pen,
                btnRelative.X, btnRelative.Y,
                btnRelative.Width - 1, btnRelative.Height - 1);
        }
        return bmp;
    }

    private void DrawSvgCharacter(Graphics g, Stream stream, int charW, int charH, int charOffsetX)
    {
        var svgDoc = SvgDocument.Open<SvgDocument>(stream);
        svgDoc.Width = new SvgUnit(SvgUnitType.Pixel, charW);
        svgDoc.Height = new SvgUnit(SvgUnitType.Pixel, charH);

        g.TranslateTransform(charOffsetX, 0);
        svgDoc.Draw(g);
        g.ResetTransform();
    }

    private Bitmap GetProcessedPng()
    {
        if (_processedPng != null)
            return _processedPng;

        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(_character.ResourceName)!;
        using var raw = new Bitmap(stream);
        _processedPng = RemoveCheckerboardBackground(raw);
        return _processedPng;
    }

    private void DrawPngCharacter(Graphics g, int charW, int charH, int charOffsetX)
    {
        var src = GetProcessedPng();
        float scaleY = (float)charH / src.Height;

        float gapTopPx = _character.GapTopY * scaleY;
        float gapBottomPx = _character.GapBottomY * scaleY;

        g.DrawImage(src,
            new RectangleF(charOffsetX, 0, charW, gapTopPx),
            new RectangleF(0, 0, src.Width, _character.GapTopY),
            GraphicsUnit.Pixel);

        g.DrawImage(src,
            new RectangleF(charOffsetX, gapBottomPx, charW, charH - gapBottomPx),
            new RectangleF(0, _character.GapBottomY, src.Width, src.Height - _character.GapBottomY),
            GraphicsUnit.Pixel);
    }

    /// <summary>
    /// Detects the checkerboard background pattern by its grid structure
    /// and removes matching pixels everywhere (including enclosed regions).
    /// A pixel is only removed if it matches the expected checkerboard color
    /// for its grid cell AND at least one neighbor one block away also matches,
    /// which prevents false positives on solid white areas (like eye whites).
    /// </summary>
    private static Bitmap RemoveCheckerboardBackground(Bitmap source)
    {
        int w = source.Width, h = source.Height;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(source, 0, 0, w, h);
        }

        var data = bmp.LockBits(
            new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int stride = data.Stride;
        var px = new byte[stride * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);

        byte cr0 = px[2], cg0 = px[1], cb0 = px[0];

        int blockSize = 1;
        for (int x = 1; x < Math.Min(200, w); x++)
        {
            int off = x * 4;
            if (Math.Abs(px[off + 2] - cr0) > 10 ||
                Math.Abs(px[off + 1] - cg0) > 10 ||
                Math.Abs(px[off] - cb0) > 10)
            { blockSize = x; break; }
        }

        int off2 = blockSize * 4;
        byte cr1 = px[off2 + 2], cg1 = px[off2 + 1], cb1 = px[off2];

        const int tol = 8;

        bool MatchesExpected(int x, int y)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return false;
            int off = y * stride + x * 4;
            int parity = ((x / blockSize) + (y / blockSize)) % 2;
            byte er = parity == 0 ? cr0 : cr1;
            byte eg = parity == 0 ? cg0 : cg1;
            byte eb = parity == 0 ? cb0 : cb1;
            return Math.Abs(px[off + 2] - er) <= tol &&
                   Math.Abs(px[off + 1] - eg) <= tol &&
                   Math.Abs(px[off] - eb) <= tol;
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!MatchesExpected(x, y)) continue;

                bool confirmed = MatchesExpected(x + blockSize, y)
                              || MatchesExpected(x - blockSize, y)
                              || MatchesExpected(x, y + blockSize)
                              || MatchesExpected(x, y - blockSize);

                if (confirmed)
                {
                    int off = y * stride + x * 4;
                    px[off] = 0; px[off + 1] = 0; px[off + 2] = 0; px[off + 3] = 0;
                }
            }
        }

        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        return bmp;
    }

    #region UpdateLayeredWindow P/Invoke

    private const int ULW_ALPHA = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT(int x, int y) { public int x = x, y = y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE(int cx, int cy) { public int cx = cx, cy = cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey,
        ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    #endregion
}
