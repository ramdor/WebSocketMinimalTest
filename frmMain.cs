/* 
 * This file is part of WebSocketMinimalTest.
 * Copyright (C) 2025 Richard Samphire / Blitter8 Ltd.
 *
 * WebSocketMinimalTest is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * WebSocketMinimalTest is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of 
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the 
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with WebSocketMinimalTest. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace WebSocketMinimalTest
{
    public partial class frmMain : Form
    {
        private const int COLOR_STEP_DEGREES = 3;
        private const int CYCLE_INTERVAL_MS = 100;

        private readonly LGEWebSocketLite _ws;
        private readonly ManualResetEvent _cycleStopEvent = new ManualResetEvent(true);

        private Thread _colorCycleThread;
        private volatile bool _connected;

        public frmMain()
        {
            InitializeComponent();

            updateButtonState(false, false);

            _ws = new LGEWebSocketLite("127.0.0.1", 50001);

            _ws.OnOpened += onOpened;
            _ws.OnClosed += onClosed;
            _ws.OnMessage += onMessage;

            FormClosing += onFormClosing;

            _ws.Start();
        }

        private void onOpened()
        {
            Console.WriteLine("Connected");
            _connected = true;
        }

        private void onClosed()
        {
            Console.WriteLine("Closed");

            _connected = false;
            _cycleStopEvent.Set();

            if (InvokeRequired)
                BeginInvoke(new Action(() => updateButtonState(false, false)));
            else
                updateButtonState(false, false);
        }

        private void onMessage(string msg)
        {
            Console.WriteLine("Message: " + msg);

            if (!_connected) return;
            if (!_ws.IsConnected) return;

            if (msg.Trim() == "ready;")
            {
                if (InvokeRequired)
                    BeginInvoke(new Action(() => updateButtonState(true, !_cycleStopEvent.WaitOne(0))));
                else
                    updateButtonState(true, !_cycleStopEvent.WaitOne(0));
            }
        }

        private void updateButtonState(bool enabled, bool cycling)
        {
            btnStartStopCycle.Enabled = enabled;
            btnStartStopCycle.Text = cycling ? "Stop SPOT colour cycle"
                                             : "Start SPOT colour cycle";
            btnStartStopCycle.Refresh();
        }

        private void onFormClosing(object sender, FormClosingEventArgs e)
        {
            _cycleStopEvent.Set();
            _ws.Stop();

            if (_colorCycleThread != null && _colorCycleThread.IsAlive)
                _colorCycleThread.Join(1000);

            _cycleStopEvent.Dispose();
        }

        private void btnStartStopCycle_Click(object sender, EventArgs e)
        {
            if (!_ws.IsConnected || !_connected) return;

            bool cycling = !_cycleStopEvent.WaitOne(0);

            if (cycling)
            {
                _cycleStopEvent.Set();
                updateButtonState(true, false);
                return;
            }

            _cycleStopEvent.Reset();
            updateButtonState(true, true);

            _colorCycleThread = new Thread(colorCycleLoop)
            {
                IsBackground = true,
                Name = "Colour‑Cycle"
            };
            _colorCycleThread.Start();
        }

        private void colorCycleLoop()
        {
            double hue = 0;

            while (!_cycleStopEvent.WaitOne(0) && _ws.IsConnected)
            {
                Color c = fromHsv(hue, 1, 1);
                int rgb = (c.R << 16) | (c.G << 8) | c.B;

                _ws.Send("SPOT:MW0LGE,,14040000," + rgb + ",TEST,;");

                hue += COLOR_STEP_DEGREES;
                if (hue >= 360) hue = 0;

                if (_cycleStopEvent.WaitOne(CYCLE_INTERVAL_MS)) break;
            }

            bool reconnectPossible = _connected && _ws.IsConnected;
            if (InvokeRequired)
                BeginInvoke(new Action(() => updateButtonState(reconnectPossible, false)));
            else
                updateButtonState(reconnectPossible, false);
        }

        /* ---------- colour helper ---------- */

        private static Color fromHsv(double h, double s, double v)
        {
            int hi = Convert.ToInt32(Math.Floor(h / 60)) % 6;
            double f = h / 60 - Math.Floor(h / 60);

            v *= 255;
            int vi = Convert.ToInt32(v);
            int p = Convert.ToInt32(v * (1 - s));
            int q = Convert.ToInt32(v * (1 - f * s));
            int t = Convert.ToInt32(v * (1 - (1 - f) * s));

            switch (hi)
            {
                case 0: return Color.FromArgb(vi, t, p);
                case 1: return Color.FromArgb(q, vi, p);
                case 2: return Color.FromArgb(p, vi, t);
                case 3: return Color.FromArgb(p, q, vi);
                case 4: return Color.FromArgb(t, p, vi);
                default: return Color.FromArgb(vi, p, q);
            }
        }
    }
}
