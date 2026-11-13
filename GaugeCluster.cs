// ============================================================================
// GaugeCluster.cs v1.9 - An all-in-one HUD instrument cluster for NinjaTrader 8:
//   * ADX(14)  - Semicircular speedometer (0-80 scale; zones 0-20 grey,
//     20-25 amber, 25-50 green, 50-80 red) with two customizable radial
//     threshold ticks (default 28 and 32), dynamic regime labels with
//     optional +DI/-DI directional arrow & tint, and high-contrast
//     zone-matched needle/hub/readout tints.
//   * DI Tug-of-War Bar (Optional, default ON) - Horizontal +DI vs -DI
//     dominance split bar and numeric readout right beneath the ADX dial.
//   * RSI(7,3) - Horizontal two-ended slider bar (oversold < 30 in cyan,
//     neutral 30-70, overbought > 70 in coral-orange; periods configurable)
//     with bright contrast marker and readout tints.
//   * EMA Bias Badge (Optional, default OFF) - Compact BULL / BEAR readout
//     comparing Close against a configurable EMA (default 21).
//   * ATR(14)  - Vertical thermometer measured in ticks (0-60 scale) with
//     customizable floor and ceiling threshold lines (default 5t and 44t),
//     zone-matched mercury and high-contrast tick readout, and LOCKED LOW /
//     LOCKED HIGH status alerts.
//   * Optional Audio/Visual Alerts (v1.9, default OFF) - Threshold crossing
//     alerts for ADX Momentum entry, +DI/-DI directional flips, ATR Lock
//     state changes, and RSI extremes with a clickable mute/unmute bell icon.
//
// Features:
//   * Auto-scales proportionally to chart panel size (clamped 0.40x-1.25x)
//     plus a manual Size % multiplier (40% - 300%).
//   * Rounded-corner opaque background card with adjustable opacity (0-100%)
//     that dynamically adjusts height based on active optional modules.
//   * Drag-and-drop repositioning anywhere on the price panel; Offset X/Y
//     update automatically relative to the selected corner/edge anchor.
//   * Cached DirectWrite factory and TextFormats for zero-allocation text
//     rendering on chart refreshes.
//
// Display-only indicator (no plots, no order interaction). Updates OnBarClose.
//
// Installation:
//   1. Copy GaugeCluster.cs into Documents\NinjaTrader 8\bin\Custom\Indicators\
//   2. Open the NinjaScript Editor in NinjaTrader 8 and press F5 to compile.
//   3. Right-click any chart -> Indicators (Ctrl+I) -> add GaugeCluster.
// ============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;

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

        private ADX adxSeries;
        private DM dmSeries;
        private RSI rsiSeries;
        private ATR atrSeries;
        private EMA emaSeries;

        private double adxValue;
        private double diPlusValue;
        private double diMinusValue;
        private double rsiValue;
        private double atrTicks;
        private double emaValue;
        private double lastClose;

        private bool hasAdx;
        private bool hasDm;
        private bool hasRsi;
        private bool hasAtr;
        private bool hasEma;

        // Previous-bar state tracking for rising-edge alert triggers.
        private bool prevStateInitialized;
        private bool prevAdxMomentum;
        private int prevDiDirection;   // 1 = +DI > -DI, -1 = -DI > +DI, 0 = tie
        private int prevAtrLockState;  // -1 = LOCKED LOW, 0 = TRADABLE, 1 = LOCKED HIGH
        private int prevRsiZone;       // -1 = OVERSOLD, 0 = NEUTRAL, 1 = OVERBOUGHT
        private DateTime lastAlertUtc = DateTime.MinValue;
        private bool alertsMuted;

        private float renderScale = 1f;

        // Cached DirectWrite text resources.
        private SharpDX.DirectWrite.Factory writeFactory;
        private Dictionary<float, SharpDX.DirectWrite.TextFormat> formatCache = new Dictionary<float, SharpDX.DirectWrite.TextFormat>();
        private float formatCacheScale = -1f;

        // Bounding box, bell hit-box & drag-and-drop state.
        private RectangleF clusterBounds = RectangleF.Empty;
        private RectangleF bellBounds = RectangleF.Empty;
        private bool isDragging;
        private System.Windows.Point dragStartMouse;
        private int dragStartOffsetX;
        private int dragStartOffsetY;
        private bool mouseHooked;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Compact HUD instrument cluster displaying an ADX speedometer, directional DI bar, RSI slider, optional EMA bias, ATR tick thermometer, and threshold alerts.";
                Name = "GaugeCluster";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;

                // Indicator parameters
                AdxPeriod = 14;
                AdxLowThreshold = 28.0;
                AdxHighThreshold = 32.0;
                RsiPeriod = 7;
                RsiSmooth = 3;
                AtrPeriod = 14;
                AtrFloorTicks = 5.0;
                AtrCeilingTicks = 44.0;
                EmaPeriod = 21;

                // Directional feature toggles
                ShowAdxDirection = true;
                ShowDiBar = true;
                ShowEmaBias = false;

                // Alert settings
                EnableAlerts = false;
                AlertOnAdxMomentum = true;
                AlertOnDiCross = true;
                AlertOnAtrLock = true;
                AlertOnRsiExtreme = false;
                AlertCooldownSeconds = 60;
                AlertSoundFile = "Alert1.wav";

                // Layout & appearance
                Position = GaugeClusterPosition.MiddleLeft;
                OffsetX = 16;
                OffsetY = 16;
                SizePercent = 100;
                BackgroundOpacity = 92;
            }
            else if (State == State.DataLoaded)
            {
                adxSeries = ADX(AdxPeriod);
                dmSeries = DM(AdxPeriod);
                rsiSeries = RSI(RsiPeriod, RsiSmooth);
                atrSeries = ATR(AtrPeriod);
                emaSeries = EMA(EmaPeriod);
            }
            else if (State == State.Historical)
            {
                HookMouseEvents();
            }
            else if (State == State.Terminated)
            {
                UnhookMouseEvents();

                foreach (KeyValuePair<float, SharpDX.DirectWrite.TextFormat> kv in formatCache)
                    kv.Value.Dispose();
                formatCache.Clear();
                if (writeFactory != null)
                {
                    writeFactory.Dispose();
                    writeFactory = null;
                }
            }
        }

        // ---- drag-and-drop & bell click hooks -------------------------------

        private void HookMouseEvents()
        {
            if (mouseHooked || ChartControl == null)
                return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                if (mouseHooked || ChartControl == null)
                    return;
                ChartControl.PreviewMouseLeftButtonDown += OnChartMouseDown;
                ChartControl.PreviewMouseMove += OnChartMouseMove;
                ChartControl.PreviewMouseLeftButtonUp += OnChartMouseUp;
                mouseHooked = true;
            });
        }

        private void UnhookMouseEvents()
        {
            if (!mouseHooked || ChartControl == null)
                return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                if (ChartControl == null)
                    return;
                ChartControl.PreviewMouseLeftButtonDown -= OnChartMouseDown;
                ChartControl.PreviewMouseMove -= OnChartMouseMove;
                ChartControl.PreviewMouseLeftButtonUp -= OnChartMouseUp;
                mouseHooked = false;
            });
        }

        private void OnChartMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ChartControl == null || clusterBounds.IsEmpty)
                return;

            System.Windows.Point pt = e.GetPosition(ChartControl);
            float px = (float)pt.X;
            float py = (float)pt.Y;

            // Clicking the bell icon toggles mute without initiating a drag.
            if (EnableAlerts && !bellBounds.IsEmpty && bellBounds.Contains(px, py))
            {
                alertsMuted = !alertsMuted;
                ChartControl.InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (clusterBounds.Contains(px, py))
            {
                isDragging = true;
                dragStartMouse = pt;
                dragStartOffsetX = OffsetX;
                dragStartOffsetY = OffsetY;
                ChartControl.CaptureMouse();
                e.Handled = true;
            }
        }

        private void OnChartMouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragging || ChartControl == null)
                return;

            System.Windows.Point pt = e.GetPosition(ChartControl);
            int dx = (int)Math.Round(pt.X - dragStartMouse.X);
            int dy = (int)Math.Round(pt.Y - dragStartMouse.Y);

            bool rightAnchored = (Position == GaugeClusterPosition.TopRight
                               || Position == GaugeClusterPosition.MiddleRight
                               || Position == GaugeClusterPosition.BottomRight);

            bool bottomAnchored = (Position == GaugeClusterPosition.BottomLeft
                                || Position == GaugeClusterPosition.BottomRight);

            int newOffsetX = rightAnchored ? (dragStartOffsetX - dx) : (dragStartOffsetX + dx);
            int newOffsetY = bottomAnchored ? (dragStartOffsetY - dy) : (dragStartOffsetY + dy);

            OffsetX = Math.Max(0, Math.Min(3000, newOffsetX));
            OffsetY = Math.Max(0, Math.Min(3000, newOffsetY));

            ChartControl.InvalidateVisual();
            e.Handled = true;
        }

        private void OnChartMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDragging)
                return;

            isDragging = false;
            if (ChartControl != null && ChartControl.IsMouseCaptured)
                ChartControl.ReleaseMouseCapture();
            e.Handled = true;
        }

        // ---- bar updates & alert evaluation ---------------------------------

        protected override void OnBarUpdate()
        {
            if (adxSeries != null) { adxValue = adxSeries[0]; hasAdx = true; }
            if (dmSeries != null)
            {
                diPlusValue = dmSeries.DiPlus[0];
                diMinusValue = dmSeries.DiMinus[0];
                hasDm = true;
            }
            if (rsiSeries != null) { rsiValue = rsiSeries[0]; hasRsi = true; }
            if (atrSeries != null && TickSize > 0) { atrTicks = atrSeries[0] / TickSize; hasAtr = true; }
            if (emaSeries != null)
            {
                emaValue = emaSeries[0];
                lastClose = Close[0];
                hasEma = true;
            }

            if (!hasAdx || !hasDm || !hasRsi || !hasAtr)
                return;

            double momThresh = AdxLowThreshold > 0 ? AdxLowThreshold : 25.0;
            bool curAdxMomentum = adxValue >= momThresh;
            int curDiDir = diPlusValue > diMinusValue ? 1 : (diMinusValue > diPlusValue ? -1 : 0);

            int curAtrLock = 0;
            if (AtrFloorTicks > 0 && atrTicks < AtrFloorTicks) curAtrLock = -1;
            else if (AtrCeilingTicks > 0 && atrTicks > AtrCeilingTicks) curAtrLock = 1;

            int curRsiZone = 0;
            if (rsiValue < 30.0) curRsiZone = -1;
            else if (rsiValue > 70.0) curRsiZone = 1;

            // Seed baseline states on historical bars so loading a chart doesn't burst alerts.
            if (!prevStateInitialized || State == State.Historical)
            {
                prevAdxMomentum = curAdxMomentum;
                prevDiDirection = curDiDir;
                prevAtrLockState = curAtrLock;
                prevRsiZone = curRsiZone;
                prevStateInitialized = true;
                return;
            }

            if (EnableAlerts && !alertsMuted)
            {
                List<string> triggers = new List<string>();

                // 1. ADX crossed into Momentum threshold
                if (AlertOnAdxMomentum && curAdxMomentum && !prevAdxMomentum)
                {
                    string dirTag = curDiDir > 0 ? "BULL (+DI)" : (curDiDir < 0 ? "BEAR (-DI)" : "NEUTRAL");
                    triggers.Add("ADX Momentum (" + adxValue.ToString("F0") + " " + dirTag + ")");
                }

                // 2. +DI / -DI directional flip while market is trending (ADX >= 20)
                if (AlertOnDiCross && curDiDir != 0 && curDiDir != prevDiDirection && adxValue >= 20.0)
                {
                    string crossMsg = curDiDir > 0
                        ? "DI Bullish Cross (+DI " + diPlusValue.ToString("F0") + " > -DI " + diMinusValue.ToString("F0") + ")"
                        : "DI Bearish Cross (-DI " + diMinusValue.ToString("F0") + " > +DI " + diPlusValue.ToString("F0") + ")";
                    triggers.Add(crossMsg);
                }

                // 3. ATR Lock state transition
                if (AlertOnAtrLock && curAtrLock != prevAtrLockState)
                {
                    if (curAtrLock == -1)
                        triggers.Add("ATR LOCKED LOW (" + atrTicks.ToString("F0") + "t < " + AtrFloorTicks.ToString("F0") + "t)");
                    else if (curAtrLock == 1)
                        triggers.Add("ATR LOCKED HIGH (" + atrTicks.ToString("F0") + "t > " + AtrCeilingTicks.ToString("F0") + "t)");
                    else
                        triggers.Add("ATR UNLOCKED (" + atrTicks.ToString("F0") + "t tradable)");
                }

                // 4. RSI entered Oversold (< 30) or Overbought (> 70)
                if (AlertOnRsiExtreme && curRsiZone != 0 && curRsiZone != prevRsiZone)
                {
                    triggers.Add(curRsiZone < 0
                        ? "RSI OVERSOLD (" + rsiValue.ToString("F0") + ")"
                        : "RSI OVERBOUGHT (" + rsiValue.ToString("F0") + ")");
                }

                if (triggers.Count > 0)
                {
                    DateTime nowUtc = DateTime.UtcNow;
                    if ((nowUtc - lastAlertUtc).TotalSeconds >= AlertCooldownSeconds)
                    {
                        lastAlertUtc = nowUtc;
                        string instName = Instrument != null ? Instrument.MasterInstrument.Name : "Chart";
                        string alertText = "GaugeCluster [" + instName + "]: " + string.Join(" | ", triggers);
                        string sound = string.IsNullOrWhiteSpace(AlertSoundFile) ? "Alert1.wav" : AlertSoundFile.Trim();

                        Alert("GaugeClusterAlert_" + CurrentBar, Priority.Medium, alertText,
                            NinjaTrader.Core.Globals.InstallDir + @"\sounds\" + sound,
                            AlertCooldownSeconds, System.Windows.Media.Brushes.Transparent, System.Windows.Media.Brushes.Transparent);
                    }
                }
            }

            prevAdxMomentum = curAdxMomentum;
            prevDiDirection = curDiDir;
            prevAtrLockState = curAtrLock;
            prevRsiZone = curRsiZone;
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
            float scaledW = Math.Max(width, width * renderScale);
            SharpDX.DirectWrite.TextFormat format;
            if (!formatCache.TryGetValue(fontSize, out format))
            {
                format = new SharpDX.DirectWrite.TextFormat(writeFactory, "Arial", fontSize);
                format.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
                format.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near;
                format.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
                formatCache[fontSize] = format;
            }
            RectangleF rect = new RectangleF(centerX - scaledW / 2f, topY, scaledW, fontSize + 8f);
            RenderTarget.DrawText(text, format, rect, brush);
        }

        private void DrawBellIcon(float centerX, float topY, float s, bool muted, bool recentFlash, SolidColorBrush activeBrush, SolidColorBrush flashBrush, SolidColorBrush mutedBrush)
        {
            SolidColorBrush brush = muted ? mutedBrush : (recentFlash ? flashBrush : activeBrush);
            float stroke = Math.Max(1.1f, 1.4f * s);

            // Bell dome & flared skirt
            float domeR = 4.2f * s;
            float domeCy = topY + 5.2f * s;
            float skirtY = topY + 10.5f * s;
            float skirtHalfW = 6.2f * s;

            // Upper dome arc (left to right)
            int segs = 10;
            Vector2 prev = new Vector2(centerX - domeR, domeCy);
            for (int i = 1; i <= segs; i++)
            {
                double a = Math.PI * (1.0 - (double)i / segs);
                Vector2 p = new Vector2(centerX + (float)(domeR * Math.Cos(a)), domeCy - (float)(domeR * Math.Sin(a)));
                RenderTarget.DrawLine(prev, p, brush, stroke);
                prev = p;
            }

            // Sides flaring out to skirt + base rim
            Vector2 leftDome = new Vector2(centerX - domeR, domeCy);
            Vector2 rightDome = new Vector2(centerX + domeR, domeCy);
            Vector2 leftSkirt = new Vector2(centerX - skirtHalfW, skirtY);
            Vector2 rightSkirt = new Vector2(centerX + skirtHalfW, skirtY);

            RenderTarget.DrawLine(leftDome, leftSkirt, brush, stroke);
            RenderTarget.DrawLine(rightDome, rightSkirt, brush, stroke);
            RenderTarget.DrawLine(leftSkirt, rightSkirt, brush, stroke);

            // Top loop & bottom clapper
            RenderTarget.DrawLine(new Vector2(centerX, topY + 0.2f * s), new Vector2(centerX, domeCy - domeR), brush, stroke);
            float clapperR = Math.Max(1.2f, 1.6f * s);
            RenderTarget.FillEllipse(new Ellipse(new Vector2(centerX, skirtY + 2.0f * s), clapperR, clapperR), brush);

            // Diagonal slash when muted
            if (muted)
            {
                RenderTarget.DrawLine(
                    new Vector2(centerX - 7.5f * s, topY + 13.5f * s),
                    new Vector2(centerX + 7.5f * s, topY - 0.5f * s),
                    mutedBrush, Math.Max(1.2f, 1.6f * s));
            }
        }

        // ---- render -----------------------------------------------------------

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (ChartPanel == null || RenderTarget == null)
                return;

            // Auto-scale: designed on a ~1900x950 panel; scales proportionally
            // with the actual panel, then applies the Size % multiplier (40% - 300%).
            float s = Math.Min(ChartPanel.W / 1900f, ChartPanel.H / 950f);
            s = Math.Max(0.40f, Math.Min(1.25f, s));
            s = s * SizePercent / 100f;
            renderScale = s;

            if (formatCacheScale != renderScale)
            {
                foreach (KeyValuePair<float, SharpDX.DirectWrite.TextFormat> kv in formatCache)
                    kv.Value.Dispose();
                formatCache.Clear();
                formatCacheScale = renderScale;
            }
            if (writeFactory == null)
                writeFactory = new SharpDX.DirectWrite.Factory();

            // Dynamic height expansion for optional directional rows.
            float diExtraH = ShowDiBar ? (26f * s) : 0f;
            float emaExtraH = ShowEmaBias ? (24f * s) : 0f;

            float padX = 16f * s;
            float padTop = 16f * s;
            float padBottom = 14f * s;
            float innerW = 208f * s;
            float innerH = 194f * s + diExtraH + emaExtraH;

            float clusterW = innerW + padX * 2f;
            float clusterH = innerH + padTop + padBottom;

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

            float cornerRadius = 12f * s;
            RectangleF bgRect = new RectangleF(ox, oy, clusterW, clusterH);
            clusterBounds = bgRect;

            float rightEdge = ox + padX + innerW;
            float topY = oy + padTop;

            byte bgAlpha = (byte)Math.Round(255.0 * Math.Max(0, Math.Min(100, BackgroundOpacity)) / 100.0);

            using (SolidColorBrush bgBrush = new SolidColorBrush(RenderTarget, new Color((byte)20, (byte)22, (byte)26, bgAlpha)))
            using (SolidColorBrush bgBorderBrush = new SolidColorBrush(RenderTarget, isDragging ? new Color((byte)110, (byte)175, (byte)235, (byte)240) : new Color((byte)75, (byte)80, (byte)90, bgAlpha)))
            // Zone fill brushes (deeper/muted so needles & markers pop against them)
            using (SolidColorBrush greyZoneBrush = new SolidColorBrush(RenderTarget, new Color(100, 100, 100, 255)))
            using (SolidColorBrush amberZoneBrush = new SolidColorBrush(RenderTarget, new Color(185, 150, 45, 255)))
            using (SolidColorBrush greenZoneBrush = new SolidColorBrush(RenderTarget, new Color(52, 145, 78, 255)))
            using (SolidColorBrush redZoneBrush = new SolidColorBrush(RenderTarget, new Color(185, 58, 40, 255)))
            using (SolidColorBrush rsiOversoldZoneBrush = new SolidColorBrush(RenderTarget, new Color(42, 145, 165, 255)))
            using (SolidColorBrush rsiOverboughtZoneBrush = new SolidColorBrush(RenderTarget, new Color(180, 75, 35, 255)))
            // Needle / marker / readout highlight brushes (brighter tints of each zone)
            using (SolidColorBrush greyHiBrush = new SolidColorBrush(RenderTarget, new Color(185, 185, 185, 255)))
            using (SolidColorBrush amberHiBrush = new SolidColorBrush(RenderTarget, new Color(250, 215, 90, 255)))
            using (SolidColorBrush greenHiBrush = new SolidColorBrush(RenderTarget, new Color(95, 235, 135, 255)))
            using (SolidColorBrush redHiBrush = new SolidColorBrush(RenderTarget, new Color(255, 115, 95, 255)))
            using (SolidColorBrush rsiOversoldHiBrush = new SolidColorBrush(RenderTarget, new Color(95, 225, 245, 255)))
            using (SolidColorBrush rsiOverboughtHiBrush = new SolidColorBrush(RenderTarget, new Color(255, 130, 80, 255)))
            // Structural UI brushes
            using (SolidColorBrush rimBrush = new SolidColorBrush(RenderTarget, new Color(170, 170, 170, 255)))
            using (SolidColorBrush lineWhiteBrush = new SolidColorBrush(RenderTarget, new Color(245, 245, 245, 255)))
            using (SolidColorBrush textBrush = new SolidColorBrush(RenderTarget, new Color(230, 230, 230, 255)))
            using (SolidColorBrush dimBrush = new SolidColorBrush(RenderTarget, new Color(150, 150, 150, 255)))
            using (SolidColorBrush trackBrush = new SolidColorBrush(RenderTarget, new Color(62, 62, 62, 255)))
            {
                // ---------- Rounded-corner opaque background card ----------
                RoundedRectangle roundedBg = new RoundedRectangle
                {
                    Rect = bgRect,
                    RadiusX = cornerRadius,
                    RadiusY = cornerRadius
                };
                RenderTarget.FillRoundedRectangle(roundedBg, bgBrush);
                RenderTarget.DrawRoundedRectangle(roundedBg, bgBorderBrush, isDragging ? Math.Max(1.5f, 2f * s) : 1f);

                // ---------- Optional Alert Bell Icon (top-left of card) ----------
                if (EnableAlerts)
                {
                    float bellW = 22f * s;
                    float bellH = 22f * s;
                    bellBounds = new RectangleF(ox + 8f * s, oy + 7f * s, bellW, bellH);
                    bool recentFlash = (DateTime.UtcNow - lastAlertUtc).TotalSeconds < 4.0;
                    DrawBellIcon(bellBounds.X + bellW / 2f, bellBounds.Y + 4f * s, s, alertsMuted, recentFlash, amberHiBrush, greenHiBrush, dimBrush);
                }
                else
                {
                    bellBounds = RectangleF.Empty;
                }

                // ---------- ATR thermometer (rightmost column, stretches with card height) ----------
                float atrW = 18f * s;
                float atrX = rightEdge - atrW;
                float atrTop = topY + 16f * s;
                float atrH = 168f * s + diExtraH + emaExtraH;
                float atrBottom = atrTop + atrH;
                float atrCx = atrX + atrW / 2f;

                DrawCenteredText("ATR", atrCx, topY - 4f * s, 52f, 11f, dimBrush);
                RenderTarget.FillRectangle(new RectangleF(atrX, atrTop, atrW, atrH), trackBrush);
                RenderTarget.DrawRectangle(new RectangleF(atrX, atrTop, atrW, atrH), rimBrush, 1f);

                SolidColorBrush atrFillBrush = greyZoneBrush;
                SolidColorBrush atrHiBrush = greyHiBrush;
                if (hasAtr)
                {
                    double frac = Math.Max(0.0, Math.Min(1.0, atrTicks / AtrScaleMax));
                    float fillH = (float)(atrH * frac);
                    if (atrTicks < AtrFloorTicks)
                    {
                        atrFillBrush = greyZoneBrush;
                        atrHiBrush = greyHiBrush;
                    }
                    else if (atrTicks < 22.0)
                    {
                        atrFillBrush = greenZoneBrush;
                        atrHiBrush = greenHiBrush;
                    }
                    else if (atrTicks < 38.0)
                    {
                        atrFillBrush = amberZoneBrush;
                        atrHiBrush = amberHiBrush;
                    }
                    else
                    {
                        atrFillBrush = redZoneBrush;
                        atrHiBrush = redHiBrush;
                    }

                    if (fillH > 0f)
                        RenderTarget.FillRectangle(new RectangleF(atrX + 1f, atrBottom - fillH, atrW - 2f, fillH), atrFillBrush);
                }

                // Upper ATR ceiling line + label.
                if (AtrCeilingTicks > 0 && AtrCeilingTicks <= AtrScaleMax)
                {
                    float ceilY = atrBottom - (float)(atrH * (AtrCeilingTicks / AtrScaleMax));
                    RenderTarget.DrawLine(new Vector2(atrX - 4f, ceilY), new Vector2(atrX + atrW + 4f, ceilY), lineWhiteBrush, Math.Max(1f, 1.5f * s));
                    DrawCenteredText(AtrCeilingTicks.ToString("F0"), atrCx, ceilY - 16f * s, 44f, 9f, dimBrush);
                }

                // Lower ATR floor line + label.
                if (AtrFloorTicks > 0 && AtrFloorTicks <= AtrScaleMax)
                {
                    float floorY = atrBottom - (float)(atrH * (AtrFloorTicks / AtrScaleMax));
                    RenderTarget.DrawLine(new Vector2(atrX - 4f, floorY), new Vector2(atrX + atrW + 4f, floorY), lineWhiteBrush, Math.Max(1f, 1.5f * s));
                    DrawCenteredText(AtrFloorTicks.ToString("F0"), atrCx, floorY + 3f * s, 44f, 9f, dimBrush);
                }

                if (hasAtr)
                {
                    DrawCenteredText(atrTicks.ToString("F0") + "t", atrCx, atrBottom + 4f * s, 56f, 12f, atrHiBrush);
                    if (atrTicks > AtrCeilingTicks)
                        DrawCenteredText("LOCKED HIGH", atrCx, atrBottom + 20f * s, 84f, 10f, redHiBrush);
                    else if (atrTicks < AtrFloorTicks)
                        DrawCenteredText("LOCKED LOW", atrCx, atrBottom + 20f * s, 84f, 10f, redHiBrush);
                }

                // ---------- ADX speedometer (left of the thermometer) ----------
                float radius = 74f * s;
                float cx = atrX - 24f * s - radius;
                float cy = topY + radius + 28f * s;

                if (hasAdx)
                {
                    double v = Math.Max(0.0, Math.Min(AdxScaleMax, adxValue));
                    DrawArcZone(greyZoneBrush, cx, cy, radius, 0.0, 20.0, 9f);
                    DrawArcZone(amberZoneBrush, cx, cy, radius, 20.0, 25.0, 9f);
                    DrawArcZone(greenZoneBrush, cx, cy, radius, 25.0, 50.0, 9f);
                    DrawArcZone(redZoneBrush, cx, cy, radius, 50.0, AdxScaleMax, 9f);
                    DrawArcZone(rimBrush, cx, cy, radius + 6f, 0.0, AdxScaleMax, 1.5f);

                    Vector2 left = DialPoint(cx, cy, radius, 0.0);
                    Vector2 right = DialPoint(cx, cy, radius, 1.0);
                    RenderTarget.DrawLine(left, right, dimBrush, 1f);

                    double[] ticks = new double[] { 0.0, 20.0, 40.0, 60.0, 80.0 };
                    for (int i = 0; i < ticks.Length; i++)
                    {
                        Vector2 tp = DialPoint(cx, cy, radius + 17f * s, ticks[i] / AdxScaleMax);
                        DrawCenteredText(ticks[i].ToString("F0"), tp.X, tp.Y - 7f * s, 38f, 10f, dimBrush);
                    }

                    // Customizable ADX threshold markers on the outer arc.
                    if (AdxLowThreshold > 0 && AdxLowThreshold <= AdxScaleMax)
                    {
                        Vector2 tickA = DialPoint(cx, cy, radius + 3f, AdxLowThreshold / AdxScaleMax);
                        Vector2 tickB = DialPoint(cx, cy, radius + 9f, AdxLowThreshold / AdxScaleMax);
                        RenderTarget.DrawLine(tickA, tickB, greenHiBrush, Math.Max(1f, 2f * s));
                        Vector2 mp = DialPoint(cx, cy, radius + 13f, AdxLowThreshold / AdxScaleMax);
                        DrawCenteredText(AdxLowThreshold.ToString("F0"), mp.X, mp.Y - 7f * s, 34f, 9f, greenHiBrush);
                    }

                    if (AdxHighThreshold > 0 && AdxHighThreshold <= AdxScaleMax)
                    {
                        Vector2 tickA = DialPoint(cx, cy, radius + 3f, AdxHighThreshold / AdxScaleMax);
                        Vector2 tickB = DialPoint(cx, cy, radius + 9f, AdxHighThreshold / AdxScaleMax);
                        RenderTarget.DrawLine(tickA, tickB, redHiBrush, Math.Max(1f, 2f * s));
                        Vector2 mp = DialPoint(cx, cy, radius + 13f, AdxHighThreshold / AdxScaleMax);
                        DrawCenteredText(AdxHighThreshold.ToString("F0"), mp.X, mp.Y - 7f * s, 34f, 9f, redHiBrush);
                    }

                    // Needle, hub, and value use a brighter shade than the zone arc so they stand out.
                    SolidColorBrush adxNeedleBrush;
                    if (adxValue < 20.0) adxNeedleBrush = greyHiBrush;
                    else if (adxValue < 25.0) adxNeedleBrush = amberHiBrush;
                    else if (adxValue < 50.0) adxNeedleBrush = greenHiBrush;
                    else adxNeedleBrush = redHiBrush;

                    Vector2 tip = DialPoint(cx, cy, radius - 13f * s, v / AdxScaleMax);
                    RenderTarget.DrawLine(new Vector2(cx, cy), tip, adxNeedleBrush, Math.Max(1.2f, 3f * s));
                    float hub = Math.Max(2.5f, 5f * s);
                    RenderTarget.FillEllipse(new Ellipse(new Vector2(cx, cy), hub, hub), adxNeedleBrush);

                    string regime;
                    if (adxValue < 20.0) regime = "RANGE / CHOP";
                    else if (adxValue < AdxLowThreshold) regime = "EMERGING TREND";
                    else if (adxValue < AdxHighThreshold) regime = "MOMENTUM";
                    else if (adxValue < 50.0) regime = "STRONG TREND";
                    else regime = "EXTREME TREND";

                    SolidColorBrush regimeBrush = textBrush;
                    if (ShowAdxDirection && hasDm && adxValue >= 20.0)
                    {
                        if (diPlusValue > diMinusValue)
                        {
                            regime = "^ " + regime;
                            regimeBrush = greenHiBrush;
                        }
                        else if (diMinusValue > diPlusValue)
                        {
                            regime = "v " + regime;
                            regimeBrush = redHiBrush;
                        }
                    }

                    DrawCenteredText("ADX", cx, cy - 44f * s, 90f, 11f, dimBrush);
                    DrawCenteredText(adxValue.ToString("F0"), cx, cy - 30f * s, 90f, 20f, adxNeedleBrush);
                    DrawCenteredText(regime, cx, cy + 8f * s, 185f, 11f, regimeBrush);
                }

                float leftColW = radius * 2f;
                float leftColX = cx - radius;
                float nextY = cy + 32f * s;

                // ---------- Optional Module 2: +DI / -DI Tug-of-War Bar ----------
                if (ShowDiBar)
                {
                    float diBarH = Math.Max(4f, 6f * s);
                    if (hasDm)
                    {
                        double sumDi = diPlusValue + diMinusValue;
                        float plusFrac = sumDi > 0.0001 ? (float)(diPlusValue / sumDi) : 0.5f;
                        float plusW = leftColW * plusFrac;

                        RenderTarget.FillRectangle(new RectangleF(leftColX, nextY, plusW, diBarH), greenZoneBrush);
                        RenderTarget.FillRectangle(new RectangleF(leftColX + plusW, nextY, leftColW - plusW, diBarH), redZoneBrush);
                        RenderTarget.DrawRectangle(new RectangleF(leftColX, nextY, leftColW, diBarH), rimBrush, 0.8f);

                        // Center 50/50 divider tick
                        float midDiX = leftColX + leftColW / 2f;
                        RenderTarget.DrawLine(new Vector2(midDiX, nextY - 2f), new Vector2(midDiX, nextY + diBarH + 2f), lineWhiteBrush, 1f);

                        SolidColorBrush diTextBrush = diPlusValue >= diMinusValue ? greenHiBrush : redHiBrush;
                        string diLabel = "+DI " + diPlusValue.ToString("F0") + "  |  -DI " + diMinusValue.ToString("F0");
                        DrawCenteredText(diLabel, cx, nextY + diBarH + 2f * s, 175f, 9.5f, diTextBrush);
                    }
                    nextY += diExtraH + 6f * s;
                }
                else
                {
                    nextY = cy + 48f * s;
                }

                // ---------- RSI slider ----------
                float rsiW = leftColW;
                float rsiX = leftColX;
                float rsiY = nextY;
                float rsiH = Math.Max(6f, 13f * s);

                if (hasRsi)
                {
                    float z1 = rsiW * 0.30f;
                    float z2 = rsiW * 0.40f;
                    RenderTarget.FillRectangle(new RectangleF(rsiX, rsiY, z1, rsiH), rsiOversoldZoneBrush);
                    RenderTarget.FillRectangle(new RectangleF(rsiX + z1, rsiY, z2, rsiH), trackBrush);
                    RenderTarget.FillRectangle(new RectangleF(rsiX + z1 + z2, rsiY, rsiW - z1 - z2, rsiH), rsiOverboughtZoneBrush);
                    RenderTarget.DrawRectangle(new RectangleF(rsiX, rsiY, rsiW, rsiH), rimBrush, 1f);

                    // Centre tick at 50.
                    float midX = rsiX + rsiW / 2f;
                    RenderTarget.DrawLine(new Vector2(midX, rsiY - 3f), new Vector2(midX, rsiY + rsiH + 3f), dimBrush, 1f);

                    string rsiLabel;
                    SolidColorBrush rsiMarkerBrush;
                    if (rsiValue < 30.0)
                    {
                        rsiLabel = "OVERSOLD";
                        rsiMarkerBrush = rsiOversoldHiBrush;
                    }
                    else if (rsiValue > 70.0)
                    {
                        rsiLabel = "OVERBOUGHT";
                        rsiMarkerBrush = rsiOverboughtHiBrush;
                    }
                    else
                    {
                        rsiLabel = "NEUTRAL";
                        rsiMarkerBrush = textBrush;
                    }

                    // Value marker (brighter shade than the bar zone so it pops clearly).
                    double rv = Math.Max(0.0, Math.Min(100.0, rsiValue));
                    float mx = rsiX + (float)(rsiW * rv / 100.0);
                    RenderTarget.DrawLine(new Vector2(mx, rsiY - 4f * s), new Vector2(mx, rsiY + rsiH + 4f * s), rsiMarkerBrush, Math.Max(1.2f, 3f * s));

                    DrawCenteredText("RSI " + rsiValue.ToString("F0") + " - " + rsiLabel, cx, rsiY + rsiH + 6f * s, 210f, 11f, rsiMarkerBrush);
                    DrawCenteredText("0", rsiX + 6f * s, rsiY + rsiH + 20f * s, 32f, 9f, dimBrush);
                    DrawCenteredText("50", midX, rsiY + rsiH + 20f * s, 32f, 9f, dimBrush);
                    DrawCenteredText("100", rsiX + rsiW - 10f * s, rsiY + rsiH + 20f * s, 42f, 9f, dimBrush);
                }

                // ---------- Optional Module 3: EMA Trend Bias Pill ----------
                if (ShowEmaBias && hasEma)
                {
                    float emaY = rsiY + rsiH + 36f * s;
                    bool isBull = lastClose >= emaValue;
                    string emaText = "EMA(" + EmaPeriod + "): " + (isBull ? "^ BULL" : "v BEAR");
                    SolidColorBrush emaBrush = isBull ? greenHiBrush : redHiBrush;
                    DrawCenteredText(emaText, cx, emaY, 175f, 10.5f, emaBrush);
                }
            }
        }

        // ---- Indicator Parameters -------------------------------------------

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "ADX Period", Description = "Lookback period for ADX and +DI/-DI.", Order = 1, GroupName = "Parameters")]
        public int AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 80)]
        [Display(Name = "ADX Low Marker", Description = "Lower ADX threshold tick on the dial (0 to hide).", Order = 2, GroupName = "Parameters")]
        public double AdxLowThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0, 80)]
        [Display(Name = "ADX High Marker", Description = "Upper ADX threshold tick on the dial (0 to hide).", Order = 3, GroupName = "Parameters")]
        public double AdxHighThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "RSI Period", Description = "Lookback period for RSI.", Order = 4, GroupName = "Parameters")]
        public int RsiPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "RSI Smooth", Description = "Smoothing period for RSI.", Order = 5, GroupName = "Parameters")]
        public int RsiSmooth { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "ATR Period", Description = "Lookback period for ATR.", Order = 6, GroupName = "Parameters")]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "ATR Floor (Ticks)", Description = "Minimum volatility threshold in ticks (0 to hide).", Order = 7, GroupName = "Parameters")]
        public double AtrFloorTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "ATR Ceiling (Ticks)", Description = "Maximum volatility threshold in ticks (0 to hide).", Order = 8, GroupName = "Parameters")]
        public double AtrCeilingTicks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Description = "EMA lookback period used when Show EMA Bias is enabled.", Order = 9, GroupName = "Parameters")]
        public int EmaPeriod { get; set; }

        // ---- Direction Toggles ----------------------------------------------

        [NinjaScriptProperty]
        [Display(Name = "Show ADX Direction", Description = "Prefix ADX regime label with ^ / v and green/red tint based on +DI vs -DI.", Order = 1, GroupName = "Direction")]
        public bool ShowAdxDirection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show DI Tug-of-War", Description = "Display a +DI vs -DI dominance split bar and readout beneath the ADX dial.", Order = 2, GroupName = "Direction")]
        public bool ShowDiBar { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show EMA Bias", Description = "Display a BULL / BEAR readout comparing Close against the configured EMA.", Order = 3, GroupName = "Direction")]
        public bool ShowEmaBias { get; set; }

        // ---- Alert Settings -------------------------------------------------

        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Description = "Master toggle for threshold alerts and the interactive bell icon on the card.", Order = 1, GroupName = "Alerts")]
        public bool EnableAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert on ADX Momentum", Description = "Alert when ADX crosses above the ADX Low Marker threshold.", Order = 2, GroupName = "Alerts")]
        public bool AlertOnAdxMomentum { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert on DI Crossover", Description = "Alert when +DI and -DI flip dominance while ADX >= 20.", Order = 3, GroupName = "Alerts")]
        public bool AlertOnDiCross { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert on ATR Lock Change", Description = "Alert when ATR enters or exits LOCKED LOW / LOCKED HIGH states.", Order = 4, GroupName = "Alerts")]
        public bool AlertOnAtrLock { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert on RSI Extreme", Description = "Alert when RSI crosses into OVERSOLD (< 30) or OVERBOUGHT (> 70).", Order = 5, GroupName = "Alerts")]
        public bool AlertOnRsiExtreme { get; set; }

        [NinjaScriptProperty]
        [Range(5, 3600)]
        [Display(Name = "Alert Cooldown (sec)", Description = "Minimum seconds between consecutive audio/log alerts.", Order = 6, GroupName = "Alerts")]
        public int AlertCooldownSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert Sound File", Description = "WAV filename in NinjaTrader 8\\sounds\\ (e.g., Alert1.wav, Alert2.wav).", Order = 7, GroupName = "Alerts")]
        public string AlertSoundFile { get; set; }

        // ---- Layout & Appearance --------------------------------------------

        [NinjaScriptProperty]
        [Display(Name = "Position", Description = "Anchor corner or edge for the gauge cluster on the price panel.", Order = 1, GroupName = "Layout")]
        public GaugeClusterPosition Position { get; set; }

        [NinjaScriptProperty]
        [Range(0, 3000)]
        [Display(Name = "Offset X (px)", Description = "Horizontal offset in pixels from the anchor (updates automatically when dragged).", Order = 2, GroupName = "Layout")]
        public int OffsetX { get; set; }

        [NinjaScriptProperty]
        [Range(0, 3000)]
        [Display(Name = "Offset Y (px)", Description = "Vertical offset in pixels from the anchor (updates automatically when dragged).", Order = 3, GroupName = "Layout")]
        public int OffsetY { get; set; }

        [NinjaScriptProperty]
        [Range(40, 300)]
        [Display(Name = "Size %", Description = "Manual size multiplier on top of automatic chart-size scaling (40% - 300%).", Order = 4, GroupName = "Layout")]
        public int SizePercent { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Background Opacity %", Description = "Opacity of the rounded card behind the gauges (0 = transparent, 100 = solid).", Order = 5, GroupName = "Layout")]
        public int BackgroundOpacity { get; set; }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private GaugeCluster[] cacheGaugeCluster;
        public GaugeCluster GaugeCluster(int adxPeriod, double adxLowThreshold, double adxHighThreshold, int rsiPeriod, int rsiSmooth, int atrPeriod, double atrFloorTicks, double atrCeilingTicks, int emaPeriod, bool showAdxDirection, bool showDiBar, bool showEmaBias, bool enableAlerts, bool alertOnAdxMomentum, bool alertOnDiCross, bool alertOnAtrLock, bool alertOnRsiExtreme, int alertCooldownSeconds, string alertSoundFile, GaugeClusterPosition position, int offsetX, int offsetY, int sizePercent, int backgroundOpacity)
        {
            return GaugeCluster(Input, adxPeriod, adxLowThreshold, adxHighThreshold, rsiPeriod, rsiSmooth, atrPeriod, atrFloorTicks, atrCeilingTicks, emaPeriod, showAdxDirection, showDiBar, showEmaBias, enableAlerts, alertOnAdxMomentum, alertOnDiCross, alertOnAtrLock, alertOnRsiExtreme, alertCooldownSeconds, alertSoundFile, position, offsetX, offsetY, sizePercent, backgroundOpacity);
        }

        public GaugeCluster GaugeCluster(ISeries<double> input, int adxPeriod, double adxLowThreshold, double adxHighThreshold, int rsiPeriod, int rsiSmooth, int atrPeriod, double atrFloorTicks, double atrCeilingTicks, int emaPeriod, bool showAdxDirection, bool showDiBar, bool showEmaBias, bool enableAlerts, bool alertOnAdxMomentum, bool alertOnDiCross, bool alertOnAtrLock, bool alertOnRsiExtreme, int alertCooldownSeconds, string alertSoundFile, GaugeClusterPosition position, int offsetX, int offsetY, int sizePercent, int backgroundOpacity)
        {
            if (cacheGaugeCluster != null)
                for (int idx = 0; idx < cacheGaugeCluster.Length; idx++)
                    if (cacheGaugeCluster[idx] != null && cacheGaugeCluster[idx].AdxPeriod == adxPeriod && cacheGaugeCluster[idx].AdxLowThreshold == adxLowThreshold && cacheGaugeCluster[idx].AdxHighThreshold == adxHighThreshold && cacheGaugeCluster[idx].RsiPeriod == rsiPeriod && cacheGaugeCluster[idx].RsiSmooth == rsiSmooth && cacheGaugeCluster[idx].AtrPeriod == atrPeriod && cacheGaugeCluster[idx].AtrFloorTicks == atrFloorTicks && cacheGaugeCluster[idx].AtrCeilingTicks == atrCeilingTicks && cacheGaugeCluster[idx].EmaPeriod == emaPeriod && cacheGaugeCluster[idx].ShowAdxDirection == showAdxDirection && cacheGaugeCluster[idx].ShowDiBar == showDiBar && cacheGaugeCluster[idx].ShowEmaBias == showEmaBias && cacheGaugeCluster[idx].EnableAlerts == enableAlerts && cacheGaugeCluster[idx].AlertOnAdxMomentum == alertOnAdxMomentum && cacheGaugeCluster[idx].AlertOnDiCross == alertOnDiCross && cacheGaugeCluster[idx].AlertOnAtrLock == alertOnAtrLock && cacheGaugeCluster[idx].AlertOnRsiExtreme == alertOnRsiExtreme && cacheGaugeCluster[idx].AlertCooldownSeconds == alertCooldownSeconds && cacheGaugeCluster[idx].AlertSoundFile == alertSoundFile && cacheGaugeCluster[idx].Position == position && cacheGaugeCluster[idx].OffsetX == offsetX && cacheGaugeCluster[idx].OffsetY == offsetY && cacheGaugeCluster[idx].SizePercent == sizePercent && cacheGaugeCluster[idx].BackgroundOpacity == backgroundOpacity && cacheGaugeCluster[idx].EqualsInput(input))
                        return cacheGaugeCluster[idx];
            return CacheIndicator<GaugeCluster>(new GaugeCluster(){ AdxPeriod = adxPeriod, AdxLowThreshold = adxLowThreshold, AdxHighThreshold = adxHighThreshold, RsiPeriod = rsiPeriod, RsiSmooth = rsiSmooth, AtrPeriod = atrPeriod, AtrFloorTicks = atrFloorTicks, AtrCeilingTicks = atrCeilingTicks, EmaPeriod = emaPeriod, ShowAdxDirection = showAdxDirection, ShowDiBar = showDiBar, ShowEmaBias = showEmaBias, EnableAlerts = enableAlerts, AlertOnAdxMomentum = alertOnAdxMomentum, AlertOnDiCross = alertOnDiCross, AlertOnAtrLock = alertOnAtrLock, AlertOnRsiExtreme = alertOnRsiExtreme, AlertCooldownSeconds = alertCooldownSeconds, AlertSoundFile = alertSoundFile, Position = position, OffsetX = offsetX, OffsetY = offsetY, SizePercent = sizePercent, BackgroundOpacity = backgroundOpacity }, input, ref cacheGaugeCluster);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.GaugeCluster GaugeCluster(int adxPeriod, double adxLowThreshold, double adxHighThreshold, int rsiPeriod, int rsiSmooth, int atrPeriod, double atrFloorTicks, double atrCeilingTicks, int emaPeriod, bool showAdxDirection, bool showDiBar, bool showEmaBias, bool enableAlerts, bool alertOnAdxMomentum, bool alertOnDiCross, bool alertOnAtrLock, bool alertOnRsiExtreme, int alertCooldownSeconds, string alertSoundFile, GaugeClusterPosition position, int offsetX, int offsetY, int sizePercent, int backgroundOpacity)
        {
            return indicator.GaugeCluster(Input, adxPeriod, adxLowThreshold, adxHighThreshold, rsiPeriod, rsiSmooth, atrPeriod, atrFloorTicks, atrCeilingTicks, emaPeriod, showAdxDirection, showDiBar, showEmaBias, enableAlerts, alertOnAdxMomentum, alertOnDiCross, alertOnAtrLock, alertOnRsiExtreme, alertCooldownSeconds, alertSoundFile, position, offsetX, offsetY, sizePercent, backgroundOpacity);
        }

        public Indicators.GaugeCluster GaugeCluster(ISeries<double> input , int adxPeriod, double adxLowThreshold, double adxHighThreshold, int rsiPeriod, int rsiSmooth, int atrPeriod, double atrFloorTicks, double atrCeilingTicks, int emaPeriod, bool showAdxDirection, bool showDiBar, bool showEmaBias, bool enableAlerts, bool alertOnAdxMomentum, bool alertOnDiCross, bool alertOnAtrLock, bool alertOnRsiExtreme, int alertCooldownSeconds, string alertSoundFile, GaugeClusterPosition position, int offsetX, int offsetY, int sizePercent, int backgroundOpacity)
        {
            return indicator.GaugeCluster(input, adxPeriod, adxLowThreshold, adxHighThreshold, rsiPeriod, rsiSmooth, atrPeriod, atrFloorTicks, atrCeilingTicks, emaPeriod, showAdxDirection, showDiBar, showEmaBias, enableAlerts, alertOnAdxMomentum, alertOnDiCross, alertOnAtrLock, alertOnRsiExtreme, alertCooldownSeconds, alertSoundFile, position, offsetX, offsetY, sizePercent, backgroundOpacity);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.GaugeCluster GaugeCluster(int adxPeriod, double adxLowThreshold, double adxHighThreshold, int rsiPeriod, int rsiSmooth, int atrPeriod, double atrFloorTicks, double atrCeilingTicks, int emaPeriod, bool showAdxDirection, bool showDiBar, bool showEmaBias, bool enableAlerts, bool alertOnAdxMomentum, bool alertOnDiCross, bool alertOnAtrLock, bool alertOnRsiExtreme, int alertCooldownSeconds, string alertSoundFile, GaugeClusterPosition position, int offsetX, int offsetY, int sizePercent, int backgroundOpacity)
        {
            return indicator.GaugeCluster(Input, adxPeriod, adxLowThreshold, adxHighThreshold, rsiPeriod, rsiSmooth, atrPeriod, atrFloorTicks, atrCeilingTicks, emaPeriod, showAdxDirection, showDiBar, showEmaBias, enableAlerts, alertOnAdxMomentum, alertOnDiCross, alertOnAtrLock, alertOnRsiExtreme, alertCooldownSeconds, alertSoundFile, position, offsetX, offsetY, sizePercent, backgroundOpacity);
        }

        public Indicators.GaugeCluster GaugeCluster(ISeries<double> input , int adxPeriod, double adxLowThreshold, double adxHighThreshold, int rsiPeriod, int rsiSmooth, int atrPeriod, double atrFloorTicks, double atrCeilingTicks, int emaPeriod, bool showAdxDirection, bool showDiBar, bool showEmaBias, bool enableAlerts, bool alertOnAdxMomentum, bool alertOnDiCross, bool alertOnAtrLock, bool alertOnRsiExtreme, int alertCooldownSeconds, string alertSoundFile, GaugeClusterPosition position, int offsetX, int offsetY, int sizePercent, int backgroundOpacity)
        {
            return indicator.GaugeCluster(input, adxPeriod, adxLowThreshold, adxHighThreshold, rsiPeriod, rsiSmooth, atrPeriod, atrFloorTicks, atrCeilingTicks, emaPeriod, showAdxDirection, showDiBar, showEmaBias, enableAlerts, alertOnAdxMomentum, alertOnDiCross, alertOnAtrLock, alertOnRsiExtreme, alertCooldownSeconds, alertSoundFile, position, offsetX, offsetY, sizePercent, backgroundOpacity);
        }
    }
}

#endregion
