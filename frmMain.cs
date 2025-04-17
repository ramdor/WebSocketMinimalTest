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
using System.Windows.Forms;
using System.Threading;

namespace WebSocketMinimalTest
{
    public partial class frmMain : Form
    {
        private LGEWebSocketLite _ws;
        private Thread _colorCycleThread;
        private volatile bool _colorCycling;

        public frmMain()
        {
            InitializeComponent();

            updateButtonState(false, false);

            _ws = new LGEWebSocketLite("127.0.0.1", 50001);
            _ws.OnOpened += () => Console.WriteLine("Connected");
            _ws.OnClosed += () =>
            {
                Console.WriteLine("Closed");
                _colorCycling = false;
                if (InvokeRequired)
                    BeginInvoke(new Action(() => updateButtonState(false, false)));
                else
                    updateButtonState(false, false);
            };
            _ws.OnMessage += msg =>
            {
                Console.WriteLine("Message: " + msg);
                if (msg.Trim() == "ready;")
                {
                    if (InvokeRequired)
                        BeginInvoke(new Action(() => updateButtonState(true, _colorCycling)));
                    else
                        updateButtonState(true, _colorCycling);
                }
            };

            this.FormClosing += Form1_FormClosing;
            _ws.Start();
        }

        private void updateButtonState(bool enabled, bool cycling)
        {
            btnStartStopCycle.Enabled = enabled;
            btnStartStopCycle.Text = cycling ? "Stop SPOT colour cycle" : "Start SPOT colour cycle";
            btnStartStopCycle.Refresh();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _colorCycling = false;
            _ws.Stop();

            if (_colorCycleThread != null && _colorCycleThread.IsAlive)
                _colorCycleThread.Join(1000);
        }

        private void btnStartStopCycle_Click(object sender, EventArgs e)
        {
            if (_colorCycling)
            {
                _colorCycling = false;
                updateButtonState(true, false);
                return;
            }

            if (_ws.IsConnected && !_colorCycling)
            {
                _colorCycling = true;
                updateButtonState(true, true);
                _colorCycleThread = new Thread(() =>
                {
                    double hue = 0;
                    while (_colorCycling && _ws.IsConnected)
                    {
                        Color c = FromHsv(hue, 1, 1);
                        int rgb = (c.R << 16) | (c.G << 8) | c.B;
                        _ws.Send("SPOT:MW0LGE,,14040000," + rgb.ToString() + ",TEST,;");
                        hue += 3;
                        if (hue >= 360) hue = 0;
                        Thread.Sleep(100);
                    }
                    if (InvokeRequired)
                        BeginInvoke(new Action(() => updateButtonState(true, false)));
                    else
                        updateButtonState(true, false);
                });
                _colorCycleThread.IsBackground = true;
                _colorCycleThread.Start();
            }
        }

        private Color FromHsv(double h, double s, double v)
        {
            int hi = Convert.ToInt32(Math.Floor(h / 60)) % 6;
            double f = h / 60 - Math.Floor(h / 60);
            v = v * 255;
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
