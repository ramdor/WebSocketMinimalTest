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
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.IO;

namespace WebSocketMinimalTest
{
    public class LGEWebSocketLite
    {
        private string _ip;
        private int _port;
        private TcpClient _client;
        private Stream _stream;
        private Thread _read_thread;
        private Thread _reconnect_thread;
        private volatile bool _running;
        private volatile bool _reconnecting;
        private object _lock = new object();

        public event Action OnOpened;
        public event Action OnClosed;
        public event Action<string> OnMessage;

        public bool IsConnected => _client != null && _client.Connected && _running;

        public LGEWebSocketLite(string ip, int port)
        {
            _ip = ip;
            _port = port;
        }

        public void Start()
        {
            if (_reconnect_thread != null && _reconnect_thread.IsAlive) return;
            Console.WriteLine("Starting WebSocket reconnect thread...");
            _reconnecting = true;
            _reconnect_thread = new Thread(reconnect_loop);
            _reconnect_thread.IsBackground = true;
            _reconnect_thread.Start();
        }

        public void Stop()
        {
            Console.WriteLine("Stopping WebSocket...");
            _reconnecting = false;
            close();
        }

        public void Send(string text)
        {
            try
            {
                byte[] msg = Encoding.UTF8.GetBytes(text);
                int len = msg.Length;
                int header_len = 2;
                if (len >= 126 && len <= 65535) header_len += 2;
                else if (len > 65535) header_len += 8;
                byte[] mask = new byte[4]; new RNGCryptoServiceProvider().GetBytes(mask);
                header_len += 4;
                byte[] frame = new byte[header_len + len];
                frame[0] = 0x81;
                if (len <= 125)
                    frame[1] = (byte)(0x80 | len);
                else if (len <= 65535)
                {
                    frame[1] = 0xFE;
                    frame[2] = (byte)(len >> 8);
                    frame[3] = (byte)(len & 0xFF);
                }
                else
                {
                    frame[1] = 0xFF;
                    for (int i = 0; i < 8; i++) frame[9 - i] = (byte)(len >> (8 * i));
                }
                int offset = frame[1] == 0xFE ? 4 : (frame[1] == 0xFF ? 10 : 2);
                Buffer.BlockCopy(mask, 0, frame, offset, 4);
                offset += 4;
                for (int i = 0; i < len; i++)
                    frame[offset + i] = (byte)(msg[i] ^ mask[i % 4]);
                lock (_lock) _stream.Write(frame, 0, frame.Length);
            }
            catch { }
        }

        private void open()
        {
            try
            {
                Console.WriteLine("Attempting to connect to WebSocket server...");
                _client = new TcpClient(_ip, _port);
                Stream baseStream = _client.GetStream();
                if (_port == 443)
                {
                    SslStream ssl = new SslStream(baseStream, false, (s, cert, chain, errors) => true);
                    ssl.AuthenticateAsClient(_ip);
                    _stream = ssl;
                }
                else
                {
                    _stream = baseStream;
                }
                _stream.ReadTimeout = 1000;
                string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                string req = "GET / HTTP/1.1\r\nHost: " + _ip + ":" + _port + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n";
                byte[] b = Encoding.ASCII.GetBytes(req);
                _stream.Write(b, 0, b.Length);
                byte[] handshake_buf = new byte[1024];
                int n = _stream.Read(handshake_buf, 0, handshake_buf.Length);
                string resp = Encoding.ASCII.GetString(handshake_buf, 0, n);
                if (!resp.Contains(" 101 ") || !resp.ToLower().Contains("upgrade: websocket"))
                {
                    Console.WriteLine("WebSocket handshake failed");
                    _stream.Close();
                    _client.Close();
                    return;
                }
                Console.WriteLine("WebSocket connected");
                _running = true;
                _read_thread = new Thread(read_loop);
                _read_thread.Start();
                OnOpened?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine("WebSocket connection error: " + ex.Message);
                try
                {
                    _stream?.Close();
                    _client?.Close();
                }
                catch { }
            }
        }

        private void close()
        {
            Console.WriteLine("Closing WebSocket...");
            _running = false;
            try
            {
                if (_stream != null && _client != null && _client.Connected)
                {
                    byte[] close_frame = new byte[6];
                    close_frame[0] = 0x88;
                    close_frame[1] = 0x80 | 2;
                    byte[] mask = new byte[4]; new RNGCryptoServiceProvider().GetBytes(mask);
                    close_frame[2] = mask[0]; close_frame[3] = mask[1];
                    close_frame[4] = mask[2]; close_frame[5] = mask[3];
                    ushort status = 1000;
                    byte b1 = (byte)(status >> 8);
                    byte b2 = (byte)(status & 0xFF);
                    byte masked_b1 = (byte)(b1 ^ mask[0]);
                    byte masked_b2 = (byte)(b2 ^ mask[1]);
                    _stream.WriteByte(close_frame[0]);
                    _stream.WriteByte(close_frame[1]);
                    _stream.Write(mask, 0, 4);
                    _stream.WriteByte(masked_b1);
                    _stream.WriteByte(masked_b2);
                    _stream.Flush();
                }
            }
            catch { }

            try { _stream?.Close(); _client?.Close(); } catch { }
            try
            {
                if (_read_thread != null && _read_thread.IsAlive)
                {
                    if (!_read_thread.Join(2000)) Console.WriteLine("Warning: read thread did not exit in time.");
                }
                _read_thread = null;
            }
            catch { }
            Console.WriteLine("WebSocket disconnected");
            OnClosed?.Invoke();
        }

        private void reconnect_loop()
        {
            while (_reconnecting)
            {
                if (!IsConnected)
                {
                    Console.WriteLine("Reconnecting to WebSocket...");
                    open();
                }
                Thread.Sleep(5000);
            }
        }

        private void send_ping()
        {
            try
            {
                byte[] ping = new byte[2];
                ping[0] = 0x89;
                ping[1] = 0x00;
                lock (_lock) _stream.Write(ping, 0, 2);
                Console.WriteLine("Ping sent");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ping error: " + ex.Message);
            }
        }

        private void send_pong(byte[] payload)
        {
            try
            {
                int len = payload.Length;
                byte[] frame = new byte[2 + len];
                frame[0] = 0x8A;
                frame[1] = (byte)(len & 0x7F);
                Buffer.BlockCopy(payload, 0, frame, 2, len);
                lock (_lock) _stream.Write(frame, 0, frame.Length);
                Console.WriteLine("Pong sent");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Pong send error: " + ex.Message);
            }
        }

        private void read_loop()
        {
            DateTime last_ping = DateTime.Now;
            while (_running)
            {
                try
                {
                    if (_stream.CanRead && _stream is NetworkStream ns && ns.DataAvailable)
                    {
                        int h1 = _stream.ReadByte();
                        int h2 = _stream.ReadByte();
                        if (h1 == -1 || h2 == -1) throw new IOException("Stream closed");
                        bool fin = (h1 & 0x80) != 0;
                        int opcode = h1 & 0x0F;
                        bool masked = (h2 & 0x80) != 0;
                        int len = h2 & 0x7F;
                        if (len == 126)
                        {
                            byte[] ext = new byte[2]; _stream.Read(ext, 0, 2); len = (ext[0] << 8) | ext[1];
                        }
                        else if (len == 127)
                        {
                            byte[] ext = new byte[8]; _stream.Read(ext, 0, 8);
                            len = (int)(((long)ext[0] << 56) | ((long)ext[1] << 48) | ((long)ext[2] << 40) | ((long)ext[3] << 32) |
                                        ((long)ext[4] << 24) | ((long)ext[5] << 16) | ((long)ext[6] << 8) | ext[7]);
                        }
                        byte[] mask = masked ? new byte[4] : null;
                        if (masked) _stream.Read(mask, 0, 4);
                        byte[] payload = new byte[len];
                        int read = 0;
                        while (read < len)
                        {
                            int r = _stream.Read(payload, read, len - read);
                            if (r == 0) throw new IOException("Disconnected");
                            read += r;
                        }
                        if (masked)
                            for (int i = 0; i < len; i++)
                                payload[i] = (byte)(payload[i] ^ mask[i % 4]);
                        if (opcode == 0x1)
                        {
                            string text;
                            try { text = Encoding.UTF8.GetString(payload); } catch { text = null; }
                            if (text != null) OnMessage?.Invoke(text);
                        }
                        else if (opcode == 0x2) { }
                        else if (opcode == 0x8) { close(); return; }
                        else if (opcode == 0x9) { send_pong(payload); Console.WriteLine("Ping received"); }
                        else if (opcode == 0xA) { Console.WriteLine("Pong received"); }
                    }
                    if ((DateTime.Now - last_ping).TotalSeconds >= 10)
                    {
                        send_ping();
                        last_ping = DateTime.Now;
                    }
                    Thread.Sleep(10);
                }
                catch { close(); }
            }
        }
    }
}
