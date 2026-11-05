// ============================================================================
// GaugeCluster.cs - a small instrument cluster for the chart's price panel:
//   * ADX(14)  as a semicircular SPEEDOMETER (zones 0-20 grey, 20-25 amber,
//     25-50 green, 50-80 red; scale 0-80) - trend strength is a "how fast
//     are we going" number, so a speedo is its natural shape.
//   * RSI(7,3) as a horizontal SLIDER bar (same parameters QuickCover
//     uses, so the gauge and the HUD always agree) - RSI's meaning
//     lives at BOTH ends (oversold < 30, overbought > 70, neutral
//     between), so a two-ended bar reads honestly where a speedo
//     would not.
//   * ATR(14)  as a vertical THERMOMETER in ticks, scale 0-60, with the
//     ES/MES strategy profile's 44t entry ceiling drawn as a line across
//     the column; the mercury turns red near the top and the gauge prints
//     LOCKED when ATR is over the ceiling (entries vetoed).
//
//   * LEVELS from the daily levels XML (the same file QuickCoverScalper
//     reads - set the path once below; it reloads automatically when the
//     file is regenerated): drawn as labelled lines across the panel,
//     plus a nearest-above / nearest-below readout with tick distances
//     under the RSI slider.
//
// The cluster auto-scales with the chart panel (designed on a
// ~1900x950 panel, scale clamped 0.40-1.25x) and has a manual Size %
// multiplier in the Position settings.
//
// Display-only indicator (built for Jason, Nov 5, 2026). No plots, no
// trading interaction. Values update on bar close. Replaces ADXSpeedo.cs.
//
// Install: save into C:\NinjaTrader 8\bin\Custom\Indicators\Jason\,
// press F5 in the NinjaScript Editor, then add "GaugeCluster" from the
// chart's Indicators dialog. (If you saved ADXSpeedo.cs earlier, delete
// it - this file supersedes it.)
// ============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;

// Anchor for the cluster on the price panel (global namespace, per the
// NT editor parser's rule for property enums). Set in the indicator's
// Position settings together with the X/Y pixel offsets.
public enum GaugeClusterPosition
{
    TopLeft,
    TopRight,
    MiddleLeft,
    MiddleRight,
    BottomLeft,
    BottomRight
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GaugeCluster : Indicator
    {
        private const double AdxScaleMax = 80.0;
        private const double AtrScaleMax = 60.0;
        private const double AtrCeilingTicks = 44.0; // ES/MES profile ceiling in QuickCoverScalper

        private ADX adxSeries;
        private RSI rsiSeries;
        private ATR atrSeries;
        private double adxValue;
        private double rsiValue;
        private double atrTicks;
        private bool hasAdx;
        private bool hasRsi;
        private bool hasAtr;

        private float renderScale = 1f;

        private List<double> fileLevels = new List<double>();
        private DateTime loadedStamp = DateTime.MinValue;
        private bool levelsLoaded;
        private double lastPrice;
        private bool hasPrice;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "ADX speedometer + RSI slider + ATR thermometer for the price panel. Display only.";
                Name = "GaugeCluster";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;
                LevelsFilePath = @"G:\My Drive\ES_price_levels.xml";
                Position = GaugeClusterPosition.MiddleLeft;
                OffsetX = 16;
                OffsetY = 16;
                SizePercent = 100;
            }
            else if (State == State.DataLoaded)
            {
                adxSeries = ADX(14);
                rsiSeries = RSI(7, 3);
                atrSeries = ATR(14);
            }
        }

        protected override void OnBarUpdate()
        {
            if (adxSeries != null) { adxValue = adxSeries[0]; hasAdx = true; }
            if (rsiSeries != null) { rsiValue = rsiSeries[0]; hasRsi = true; }
            if (atrSeries != null && TickSize > 0) { atrTicks = atrSeries[0] / TickSize; hasAtr = true; }
            lastPrice = Close[0];
            hasPrice = true;
            MaybeReloadLevels();
        }

        // Mirrors QuickCoverScalper's level loading: bare prices from the
        // "value" attribute of each <Level> element, stale files (older
        // than yesterday) ignored, reloads when the file's write time
        // changes so the morning regeneration flows through on its own.
        private void MaybeReloadLevels()
        {
            try
            {
                if (string.IsNullOrEmpty(LevelsFilePath) || !File.Exists(LevelsFilePath))
                {
                    if (levelsLoaded) { fileLevels.Clear(); levelsLoaded = false; }
                    return;
                }
                DateTime stamp = File.GetLastWriteTime(LevelsFilePath);
                if (levelsLoaded && stamp == loadedStamp)
                    return;
                if (stamp.Date < DateTime.Today.AddDays(-1))
                {
                    if (levelsLoaded) { fileLevels.Clear(); levelsLoaded = false; }
                    return;
                }
                XDocument doc = XDocument.Load(LevelsFilePath);
                List<double> fresh = new List<double>();
                foreach (XElement el in doc.Descendants("Level"))
                {
                    XAttribute v = el.Attribute("value");
                    double parsed;
                    if (v != null && double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                        fresh.Add(parsed);
                }
                fresh.Sort();
                fileLevels = fresh;
                levelsLoaded = fileLevels.Count > 0;
                loadedStamp = stamp;
                if (levelsLoaded)
                    Print("[GaugeCluster] LEVELS: " + fileLevels.Count + " levels from file written " + stamp.ToString("yyyy-MM-dd HH:mm") + ".");
            }
            catch (Exception)
            {
                // A half-written file during regeneration just retries next bar.
                levelsLoaded = fileLevels.Count > 0;
            }
        }

        // ---- shared helpers -------------------------------------------------

        private Vector2 DialPoint(float cx, float cy, float r, double fraction)
        {
            double ang = Math.PI * (1.0 - fraction);
            return new Vector2(cx + (float)(r * Math.Cos(ang)), cy - (float)(r * Math.Sin(ang)));
        }

        private void DrawArcZone(SolidColorBrush brush, float cx, float cy, float r, double fromVal, double toVal, float width)
        {
            width = Math.Max(1f, width * renderScale);
            int steps = 24;
            Vector2 prev = DialPoint(cx, cy, r, fromVal / AdxScaleMax);
            for (int i = 1; i <= steps; i++)
            {
                double v = fromVal + (toVal - fromVal) * i / steps;
                Vector2 p = DialPoint(cx, cy, r, v / AdxScaleMax);
                RenderTarget.DrawLine(prev, p, brush, width);
                prev = p;
            }
        }

        private void DrawCenteredText(string text, float centerX, float topY, float width, float fontSize, SolidColorBrush brush)
        {
            fontSize = Math.Max(7f, fontSize * renderScale);
            using (SharpDX.DirectWrite.Factory factory = new SharpDX.DirectWrite.Factory())
            using (SharpDX.DirectWrite.TextFormat format = new SharpDX.DirectWrite.TextFormat(factory, "Arial", fontSize))
            {
                format.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
                format.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near;
                RectangleF rect = new RectangleF(centerX - width / 2f, topY, width, fontSize + 8f);
                RenderTarget.DrawText(text, format, rect, brush);
            }
        }

        // ---- render -----------------------------------------------------------

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (ChartPanel == null || RenderTarget == null)
                return;

            // Auto-scale: the layout was designed on a ~1900x950 panel;
            // shrink/grow proportionally with the actual panel, then apply
            // the user's Size % multiplier.
            float s = Math.Min(ChartPanel.W / 1900f, ChartPanel.H / 950f);
            s = Math.Max(0.40f, Math.Min(1.25f, s));
            s = s * SizePercent / 100f;
            renderScale = s;

            // Cluster origin from the anchor + offsets; the internal layout
            // below is drawn relative to (rightEdge, topY), scaled by s.
            float clusterW = 262f * s;
            float clusterH = 235f * s;
            float ox = ChartPanel.X + OffsetX;
            float oy = ChartPanel.Y + OffsetY;
            switch (Position)
            {
                case GaugeClusterPosition.TopRight:
                    ox = ChartPanel.X + ChartPanel.W - clusterW - OffsetX;
                    oy = ChartPanel.Y + OffsetY;
                    break;
                case GaugeClusterPosition.MiddleLeft:
                    ox = ChartPanel.X + OffsetX;
                    oy = ChartPanel.Y + (ChartPanel.H - clusterH) / 2f + OffsetY;
                    break;
                case GaugeClusterPosition.MiddleRight:
                    ox = ChartPanel.X + ChartPanel.W - clusterW - OffsetX;
                    oy = ChartPanel.Y + (ChartPanel.H - clusterH) / 2f + OffsetY;
                    break;
                case GaugeClusterPosition.BottomLeft:
                    ox = ChartPanel.X + OffsetX;
                    oy = ChartPanel.Y + ChartPanel.H - clusterH - OffsetY;
                    break;
                case GaugeClusterPosition.BottomRight:
                    ox = ChartPanel.X + ChartPanel.W - clusterW - OffsetX;
                    oy = ChartPanel.Y + ChartPanel.H - clusterH - OffsetY;
                    break;
                default: // TopLeft
                    ox = ChartPanel.X + OffsetX;
                    oy = ChartPanel.Y + OffsetY;
                    break;
            }
            float rightEdge = ox + 242f * s;
            float topY = oy;

            using (SolidColorBrush greyBrush = new SolidColorBrush(RenderTarget, new Color(115, 115, 115, 255)))
            using (SolidColorBrush amberBrush = new SolidColorBrush(RenderTarget, new Color(205, 170, 60, 255)))
            using (SolidColorBrush greenBrush = new SolidColorBrush(RenderTarget, new Color(64, 165, 92, 255)))
            using (SolidColorBrush redBrush = new SolidColorBrush(RenderTarget, new Color(205, 72, 50, 255)))
            using (SolidColorBrush hotBrush = new SolidColorBrush(RenderTarget, new Color(200, 90, 40, 255)))
            using (SolidColorBrush rimBrush = new SolidColorBrush(RenderTarget, new Color(170, 170, 170, 255)))
            using (SolidColorBrush needleBrush = new SolidColorBrush(RenderTarget, new Color(245, 245, 245, 255)))
            using (SolidColorBrush textBrush = new SolidColorBrush(RenderTarget, new Color(230, 230, 230, 255)))
            using (SolidColorBrush dimBrush = new SolidColorBrush(RenderTarget, new Color(150, 150, 150, 255)))
            using (SolidColorBrush trackBrush = new SolidColorBrush(RenderTarget, new Color(62, 62, 62, 255)))
            using (SolidColorBrush levelBrush = new SolidColorBrush(RenderTarget, new Color(140, 165, 190, 170)))
            {
                // ---------- Level lines across the panel ----------
                if (levelsLoaded && chartScale != null)
                {
                    for (int i = 0; i < fileLevels.Count; i++)
                    {
                        double lvl = fileLevels[i];
                        if (lvl < chartScale.MinValue || lvl > chartScale.MaxValue)
                            continue;
                        float ly = (float)chartScale.GetYByValue(lvl);
                        RenderTarget.DrawLine(new Vector2(ChartPanel.X, ly), new Vector2(ChartPanel.X + ChartPanel.W, ly), levelBrush, 1f);
                        DrawCenteredText(lvl.ToString("F2"), ChartPanel.X + ChartPanel.W - 34f, ly - 14f, 64f, 9f, levelBrush);
                    }
                }

                // ---------- ATR thermometer (rightmost column) ----------
                float atrW = 18f * s;
                float atrX = rightEdge - atrW;
                float atrTop = topY + 16f * s;
                float atrH = 168f * s;
                float atrBottom = atrTop + atrH;
                float atrCx = atrX + atrW / 2f;

                DrawCenteredText("ATR", atrCx, topY - 4f, 44f, 11f, dimBrush);
                RenderTarget.FillRectangle(new RectangleF(atrX, atrTop, atrW, atrH), trackBrush);
                RenderTarget.DrawRectangle(new RectangleF(atrX, atrTop, atrW, atrH), rimBrush, 1f);

                if (hasAtr)
                {
                    double frac = Math.Max(0.0, Math.Min(1.0, atrTicks / AtrScaleMax));
                    float fillH = (float)(atrH * frac);
                    SolidColorBrush fillBrush = atrTicks < 22.0 ? greenBrush : (atrTicks < 38.0 ? amberBrush : redBrush);
                    if (fillH > 0f)
                        RenderTarget.FillRectangle(new RectangleF(atrX + 1f, atrBottom - fillH, atrW - 2f, fillH), fillBrush);
                }

                // The 44t ceiling line + label.
                float ceilY = atrBottom - (float)(atrH * (AtrCeilingTicks / AtrScaleMax));
                RenderTarget.DrawLine(new Vector2(atrX - 4f, ceilY), new Vector2(atrX + atrW + 4f, ceilY), needleBrush, Math.Max(1f, 1.5f * s));
                DrawCenteredText("44", atrCx, ceilY - 16f * s, 40f, 9f, dimBrush);

                if (hasAtr)
                {
                    DrawCenteredText(atrTicks.ToString("F0") + "t", atrCx, atrBottom + 4f * s, 48f, 12f, textBrush);
                    if (atrTicks > AtrCeilingTicks)
                        DrawCenteredText("LOCKED", atrCx, atrBottom + 20f * s, 56f, 10f, redBrush);
                }

                // ---------- ADX speedometer (left of the thermometer) ----------
                float radius = 74f * s;
                float cx = atrX - 24f * s - radius;
                float cy = topY + radius + 28f * s;

                if (hasAdx)
                {
                    double v = Math.Max(0.0, Math.Min(AdxScaleMax, adxValue));
                    DrawArcZone(greyBrush, cx, cy, radius, 0.0, 20.0, 9f);
                    DrawArcZone(amberBrush, cx, cy, radius, 20.0, 25.0, 9f);
                    DrawArcZone(greenBrush, cx, cy, radius, 25.0, 50.0, 9f);
                    DrawArcZone(redBrush, cx, cy, radius, 50.0, AdxScaleMax, 9f);
                    DrawArcZone(rimBrush, cx, cy, radius + 6f, 0.0, AdxScaleMax, 1.5f);

                    Vector2 left = DialPoint(cx, cy, radius, 0.0);
                    Vector2 right = DialPoint(cx, cy, radius, 1.0);
                    RenderTarget.DrawLine(left, right, dimBrush, 1f);

                    double[] ticks = new double[] { 0.0, 20.0, 40.0, 60.0, 80.0 };
                    for (int i = 0; i < ticks.Length; i++)
                    {
                        Vector2 tp = DialPoint(cx, cy, radius + 17f * s, ticks[i] / AdxScaleMax);
                        DrawCenteredText(ticks[i].ToString("F0"), tp.X, tp.Y - 7f, 34f, 10f, dimBrush);
                    }

                    Vector2 tip = DialPoint(cx, cy, radius - 13f * s, v / AdxScaleMax);
                    RenderTarget.DrawLine(new Vector2(cx, cy), tip, needleBrush, Math.Max(1.2f, 3f * s));
                    float hub = Math.Max(2.5f, 5f * s);
                    RenderTarget.FillEllipse(new Ellipse(new Vector2(cx, cy), hub, hub), needleBrush);

                    string regime;
                    if (adxValue < 20.0) regime = "NO TREND";
                    else if (adxValue < 25.0) regime = "DEVELOPING";
                    else if (adxValue < 50.0) regime = "STRONG";
                    else regime = "VERY STRONG";

                    DrawCenteredText("ADX", cx, cy - 34f * s, 90f, 11f, dimBrush);
                    DrawCenteredText(adxValue.ToString("F0"), cx, cy - 20f * s, 90f, 20f, textBrush);
                    DrawCenteredText(regime, cx, cy + 8f * s, 130f, 11f, textBrush);
                }

                // ---------- RSI slider (under the speedometer) ----------
                float rsiW = radius * 2f;
                float rsiX = cx - radius;
                float rsiY = cy + 48f * s;
                float rsiH = Math.Max(6f, 13f * s);

                if (hasRsi)
                {
                    float z1 = rsiW * 0.30f;
                    float z2 = rsiW * 0.40f;
                    RenderTarget.FillRectangle(new RectangleF(rsiX, rsiY, z1, rsiH), hotBrush);
                    RenderTarget.FillRectangle(new RectangleF(rsiX + z1, rsiY, z2, rsiH), trackBrush);
                    RenderTarget.FillRectangle(new RectangleF(rsiX + z1 + z2, rsiY, rsiW - z1 - z2, rsiH), hotBrush);
                    RenderTarget.DrawRectangle(new RectangleF(rsiX, rsiY, rsiW, rsiH), rimBrush, 1f);

                    // Centre tick at 50.
                    float midX = rsiX + rsiW / 2f;
                    RenderTarget.DrawLine(new Vector2(midX, rsiY - 3f), new Vector2(midX, rsiY + rsiH + 3f), dimBrush, 1f);

                    // Regime first: the marker and the readout take the hot
                    // zone colour inside either extreme, white when neutral.
                    string rsiLabel;
                    if (rsiValue < 30.0) rsiLabel = "OVERSOLD";
                    else if (rsiValue > 70.0) rsiLabel = "OVERBOUGHT";
                    else rsiLabel = "NEUTRAL";
                    SolidColorBrush rsiBrush = (rsiValue < 30.0 || rsiValue > 70.0) ? hotBrush : textBrush;

                    // Value marker.
                    double rv = Math.Max(0.0, Math.Min(100.0, rsiValue));
                    float mx = rsiX + (float)(rsiW * rv / 100.0);
                    RenderTarget.DrawLine(new Vector2(mx, rsiY - 4f * s), new Vector2(mx, rsiY + rsiH + 4f * s), rsiBrush, Math.Max(1.2f, 3f * s));

                    DrawCenteredText("RSI " + rsiValue.ToString("F0") + " - " + rsiLabel, cx, rsiY + rsiH + 6f * s, 170f, 11f, rsiBrush);
                    DrawCenteredText("0", rsiX + 6f, rsiY + rsiH + 20f * s, 24f, 9f, dimBrush);
                    DrawCenteredText("50", midX, rsiY + rsiH + 20f * s, 24f, 9f, dimBrush);
                    DrawCenteredText("100", rsiX + rsiW - 10f, rsiY + rsiH + 20f * s, 30f, 9f, dimBrush);
                }

                // ---------- Levels readout (nearest above / below) ----------
                float lvlY = rsiY + rsiH + 42f * s;
                if (levelsLoaded && hasPrice && TickSize > 0)
                {
                    double above = 0; double below = 0;
                    bool hasAbove = false; bool hasBelow = false;
                    for (int i = 0; i < fileLevels.Count; i++)
                    {
                        if (fileLevels[i] > lastPrice && (!hasAbove || fileLevels[i] < above)) { above = fileLevels[i]; hasAbove = true; }
                        if (fileLevels[i] < lastPrice && (!hasBelow || fileLevels[i] > below)) { below = fileLevels[i]; hasBelow = true; }
                    }
                    string line1 = hasAbove
                        ? "▲ " + above.ToString("F2") + "  +" + Math.Round((above - lastPrice) / TickSize).ToString("F0") + "t"
                        : "▲ —";
                    string line2 = hasBelow
                        ? "▼ " + below.ToString("F2") + "  -" + Math.Round((lastPrice - below) / TickSize).ToString("F0") + "t"
                        : "▼ —";
                    DrawCenteredText(line1, cx, lvlY, 180f, 11f, textBrush);
                    DrawCenteredText(line2, cx, lvlY + 16f * s, 180f, 11f, textBrush);
                }
                else
                {
                    DrawCenteredText("LEVELS: none", cx, lvlY, 180f, 10f, dimBrush);
                }
            }
        }

        [NinjaScriptProperty]
        [Display(Name = "Levels File Path", Description = "Full path to the daily levels XML - the same file QuickCoverScalper reads.", Order = 1, GroupName = "Levels")]
        public string LevelsFilePath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Position", Description = "Anchor corner/edge for the gauge cluster on the price panel.", Order = 1, GroupName = "Position")]
        public GaugeClusterPosition Position { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Offset X (px)", Description = "Horizontal margin/nudge in pixels from the anchor.", Order = 2, GroupName = "Position")]
        public int OffsetX { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Offset Y (px)", Description = "Vertical margin/nudge in pixels from the anchor.", Order = 3, GroupName = "Position")]
        public int OffsetY { get; set; }

        [NinjaScriptProperty]
        [Range(40, 200)]
        [Display(Name = "Size %", Description = "Manual size multiplier on top of the automatic chart-size scaling. 100 = automatic size.", Order = 4, GroupName = "Position")]
        public int SizePercent { get; set; }
    }
}
