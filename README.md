# GaugeCluster for NinjaTrader 8

A compact, display-only HUD instrument cluster for the NinjaTrader 8 price panel. Replaces cluttered sub-panels with a single draggable, auto-scaling cockpit card showing trend strength, directional dominance, momentum extremes, tick volatility, and optional threshold alerts at a glance.

## Gauges & Modules Included

* **ADX Speedometer (`0–80`) + Directional Vector:**
  * Color-coded semicircular arc: **Grey** (`0–20` Range/Chop), **Amber** (`20–25` Emerging), **Green** (`25–50` Trend), and **Red** (`50–80` Extreme).
  * Includes two customizable radial threshold markers (default `28` and `32`) and dynamic regime labels (`RANGE / CHOP`, `EMERGING TREND`, `MOMENTUM`, `STRONG TREND`, `EXTREME TREND`).
  * **ADX Direction (`Show ADX Direction`, default ON):** Prefixes the active regime label (`ADX >= 20`) with `^` (green when `+DI > -DI`) or `v` (red when `-DI > +DI`).
* **`+DI / -DI` Tug-of-War Bar (`Show DI Tug-of-War`, default ON):**
  * Horizontal buyer/seller dominance split bar with a `50/50` center tick and live `+DI` vs. `-DI` readout beneath the ADX dial.
* **RSI Slider (`0–100`):**
  * Two-ended horizontal bar highlighting **Oversold** (`< 30` in Cyan), **Neutral** (`30–70`), and **Overbought** (`> 70` in Coral-Orange) with a center `50` tick and high-contrast value marker.
* **EMA Trend Bias (`Show EMA Bias`, default OFF):**
  * Optional `^ BULL` / `v BEAR` readout comparing the last close against a configurable EMA (default `EMA(21)`).
* **ATR Tick Thermometer (`0–60t`):**
  * Vertical volatility column measured directly in instrument ticks (`ATR / TickSize`).
  * Customizable **Floor** (default `5t`) and **Ceiling** (default `44t`) threshold lines with `LOCKED LOW` and `LOCKED HIGH` warnings when volatility sits outside your tradable window.
* **Threshold Audio/Log Alerts (`Enable Alerts`, default OFF):**
  * Rising-edge alerts for **ADX Momentum** (`ADX >= ADX Low Marker`), **`+DI / -DI` Crossovers** (when `ADX >= 20`), **ATR Lock State Changes** (`LOCKED LOW`, `LOCKED HIGH`, or `UNLOCKED`), and **RSI Extremes** (`< 30` or `> 70`).
  * Draws a clickable **Bell Icon** in the top-left corner of the card when enabled so you can mute or unmute audio alerts on the fly without opening indicator properties.

## Key Features

* **Drag-and-Drop Positioning:** Click and drag anywhere on the rounded card to reposition it across the price panel. `Offset X` and `Offset Y` update automatically relative to your chosen anchor (`TopLeft`, `TopRight`, `MiddleLeft`, `MiddleRight`, `BottomLeft`, `BottomRight`).
* **Auto-Scaling + Manual Zoom:** Scales proportionally with your chart panel size (`0.40x–1.25x`) plus a manual **Size %** multiplier from `40%` to `300%`, with `NoWrap` DirectWrite text layouts so labels never split across lines at small scales.
* **Adjustable Opaque Card:** Rounded-corner dark background card with configurable **Background Opacity %** (`0%` transparent to `100%` solid) that dynamically resizes to fit active optional rows.
* **Lightweight Direct2D / DirectWrite Rendering:** Caches its DirectWrite factory and `TextFormat` instances for zero-allocation text rendering on chart refreshes. Purely display-only (`Calculate.OnBarClose`, no plots, no order interaction).

## Installation

### Option A: Import NinjaScript Add-On (Recommended)
1. Download `GaugeCluster.zip` from the [Releases](../../releases) page.
2. In NinjaTrader 8 Control Center, go to **Tools -> Import -> NinjaScript Add-On...** and select `GaugeCluster.zip`.
3. Open any chart, press **Ctrl+I** (Indicators), select **GaugeCluster**, and click **OK**.

### Option B: Manual `.cs` Install
1. Copy `GaugeCluster.cs` into `Documents\NinjaTrader 8\bin\Custom\Indicators\`.
2. Open the NinjaScript Editor in NinjaTrader 8 and press **F5** to compile.
3. Add **GaugeCluster** to your chart from the **Indicators** (`Ctrl+I`) window.

## Parameters

| Group | Setting | Default | Description |
| :--- | :--- | :--- | :--- |
| **Parameters** | `ADX Period` | `14` | Lookback period for ADX and `+DI / -DI`. |
| **Parameters** | `ADX Low Marker` | `28` | Lower radial threshold tick on the ADX dial (`0` to hide). |
| **Parameters** | `ADX High Marker` | `32` | Upper radial threshold tick on the ADX dial (`0` to hide). |
| **Parameters** | `RSI Period` | `7` | Lookback period for RSI. |
| **Parameters** | `RSI Smooth` | `3` | Smoothing period for RSI. |
| **Parameters** | `ATR Period` | `14` | Lookback period for ATR. |
| **Parameters** | `ATR Floor (Ticks)` | `5` | Minimum tick volatility line (`0` to hide). |
| **Parameters** | `ATR Ceiling (Ticks)` | `44` | Maximum tick volatility line (`0` to hide). |
| **Parameters** | `EMA Period` | `21` | EMA lookback used when `Show EMA Bias` is enabled. |
| **Direction** | `Show ADX Direction` | `true` | Prefix ADX regime label with `^` / `v` and green/red tint based on `+DI` vs. `-DI`. |
| **Direction** | `Show DI Tug-of-War` | `true` | Show `+DI` vs. `-DI` split bar and readout beneath the ADX dial. |
| **Direction** | `Show EMA Bias` | `false` | Show `^ BULL` / `v BEAR` readout comparing `Close` against the EMA. |
| **Alerts** | `Enable Alerts` | `false` | Master toggle for threshold alerts and the interactive card bell icon. |
| **Alerts** | `Alert on ADX Momentum` | `true` | Alert when ADX crosses above `ADX Low Marker`. |
| **Alerts** | `Alert on DI Crossover` | `true` | Alert when `+DI` and `-DI` flip dominance while `ADX >= 20`. |
| **Alerts** | `Alert on ATR Lock Change` | `true` | Alert when ATR enters or exits `LOCKED LOW` / `LOCKED HIGH`. |
| **Alerts** | `Alert on RSI Extreme` | `false` | Alert when RSI crosses into `< 30` or `> 70`. |
| **Alerts** | `Alert Cooldown (sec)` | `60` | Minimum seconds between consecutive alerts. |
| **Alerts** | `Alert Sound File` | `Alert1.wav` | WAV filename inside `NinjaTrader 8\sounds\`. |
| **Layout** | `Position` | `MiddleLeft` | Anchor corner or edge on the price panel. |
| **Layout** | `Offset X (px)` | `16` | Horizontal pixel offset from anchor (auto-updates on drag). |
| **Layout** | `Offset Y (px)` | `16` | Vertical pixel offset from anchor (auto-updates on drag). |
| **Layout** | `Size %` | `100` | Manual size multiplier (`40%` to `300%`). |
| **Layout** | `Background Opacity %` | `92` | Card background opacity (`0%` to `100%`). |

## License

MIT License — free to use, modify, and share.
