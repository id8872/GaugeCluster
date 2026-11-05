GaugeCluster
A NinjaTrader 8 indicator that draws a small instrument cluster directly on the price panel: an ADX speedometer, an RSI slider, an ATR thermometer, and the day's price levels — the four things a scalper otherwise has to read out of a text HUD or a stack of sub-panels.
It is a display-only companion to QuickCoverScalper — it never trades, never places or modifies orders, and shares no state with any strategy. Built by Muse Spark (Meta).
!GaugeCluster on an MES chart
Why three different shapes
Each gauge is drawn in the shape its number actually means:
ADX is a speedometer (0–80 scale). Trend strength is a "how fast are we going" quantity — one direction, more is more. Zones: 0–20 grey, 20–25 amber, 25–50 green, 50–80 red, with a needle, a digital readout, and a regime label (NO TREND / DEVELOPING / STRONG / VERY STRONG). Uses ADX(14).
RSI is a horizontal slider (0–100). RSI's information lives at both ends — oversold below 30, overbought above 70, neutral between — so it gets a two-ended bar with both extreme zones coloured. The marker and the readout change colour inside either extreme. Uses RSI(7, 3), the same parameters QuickCoverScalper runs, so the gauge and the strategy's HUD always agree.
ATR is a vertical thermometer (0–60 ticks). Volatility is a magnitude that fills up toward a limit, so the mercury rises — green below 22t, amber 22–38t, red above — toward a hard line drawn at 44 ticks: the ES/MES profile's maximum-ATR entry gate in QuickCoverScalper. When ATR crosses that line, entries are vetoed by the strategy, and the gauge prints LOCKED — a silent gate made visible. Uses ATR(14).
Levels
GaugeCluster reads the same daily levels XML that QuickCoverScalper (and the XML_PriceMarker indicator) reads — <Level value="7730.25"/> elements, prices only — and:
draws each level as a line across the panel with a price tag,
shows a readout of the nearest level above and below the last close, with the distance in ticks (▲ 7600.00 +435t / ▼ 7490.00 -5t),
reloads automatically when the file's write time changes (the file is regenerated before the open), and ignores files older than yesterday, mirroring the strategy's staleness rule.
Why a file and not the strategy's settings? NinjaTrader keeps strategies and indicators sealed off from each other — an indicator cannot read a running strategy's parameters. The levels don't live in the strategy anyway; they live in the file, so the indicator reads the file itself, through its own Levels File Path setting.
Position and scaling
Position anchor: TopLeft, TopRight, MiddleLeft (default), MiddleRight, BottomLeft, BottomRight — the cluster moves as one unit.
Offset X / Offset Y: pixel margin or nudge from the anchor.
Auto-scaling: the layout was designed on a ~1900×950 panel and scales proportionally with the actual chart panel (clamped 0.40×–1.25×). Resize the chart and the cluster follows.
Size %: a manual multiplier (40–200, default 100) on top of the automatic scaling.
Settings
Setting
Default
Notes
Levels File Path
G:\My Drive\ES_price_levels.xml
The author's path — point it at your own levels file
Position
MiddleLeft
Cluster anchor on the price panel
Offset X / Offset Y
16 / 16
Pixels from the anchor
Size %
100
Manual size multiplier over the auto-scaling
Setup
NinjaTrader 8 · any chart and instrument · save GaugeCluster.cs into bin\Custom\Indicators in your NinjaTrader 8 folder, press F5 in the NinjaScript Editor, then add GaugeCluster from the chart's Indicators dialog and set the levels path if you use one.
The 44-tick ceiling is the ES/MES value. For the NQ/MNQ profile (ceiling 80t), change the AtrCeilingTicks constant near the top of the file. Indicator periods are set where the series are created in OnStateChange (ADX(14), RSI(7, 3), ATR(14)).
Limitations
Renders on the price panel only; values refresh on bar close, like the strategy it accompanies.
Level price tags can overlap tags from other level-drawing indicators (e.g. XML_PriceMarker) if both are on the same chart — the lines are at identical prices by design.
One ceiling constant per file: a chart running both MES and NQ panels needs the constant set per panel's instrument in separate copies if the LOCKED line matters on both.
Disclaimer
A charting aid, published for education and discussion. It displays indicator values and file data; it is not a trading system and not financial advice.
