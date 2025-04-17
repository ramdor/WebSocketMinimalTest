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
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace WebSocketMinimalTest
{
    public sealed class LGEWebSocketLite : IDisposable
    {
        private const int DEFAULT_PING_INTERVAL_SECONDS = 10;
        private const int RECONNECT_DELAY_MILLISECONDS = 1000;
        private const int READ_TIMEOUT_MILLISECONDS = 1000;

        private readonly string _ip;
        private readonly int _port;
        private readonly object _syncRoot = new object();
        private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);

        private TcpClient _client;
        private Stream _stream;
        private Thread _readThread;
        private Thread _reconnectThread;

        private volatile bool _running;
        private volatile bool _reconnecting;
        private bool _disposed;

        public event Action OnOpened;
        public event Action OnClosed;
        public event Action<string> OnMessage;

        public bool IsConnected
        {
            get
            {
                if (_client == null || !_running) return false;

                Socket s = _client.Client;
                if (s == null || !s.Connected) return false;

                return !(s.Poll(0, SelectMode.SelectRead) && s.Available == 0);
            }
        }

        public LGEWebSocketLite(string ip, int port)
        {
            _ip = ip ?? throw new ArgumentNullException(nameof(ip));
            _port = port;
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LGEWebSocketLite));
            if (_reconnectThread != null && _reconnectThread.IsAlive) return;

            _reconnecting = true;
            _stopEvent.Reset();
            _reconnectThread = new Thread(reconnectLoop) { IsBackground = true, Name = "WS‑Reconnect" };
            _reconnectThread.Start();
        }

        public void Stop()
        {
            _reconnecting = false;
            _stopEvent.Set();
            closeInternal();
        }

        public void Send(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (!IsConnected || _stream == null) return;

            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(text);
                byte[] frame = buildFrame(0x1, payload, true);

                lock (_syncRoot)
                {
                    if (_stream != null && _client != null && _client.Connected)
                        _stream.Write(frame, 0, frame.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Send error: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
            _stopEvent.Dispose();
            GC.SuppressFinalize(this);
        }

        private void reconnectLoop()
        {
            while (_reconnecting && !_stopEvent.WaitOne(RECONNECT_DELAY_MILLISECONDS))
            {
                if (!IsConnected) openInternal();
            }
        }

        private void openInternal()
        {
            if (_stopEvent.WaitOne(0)) return;

            try
            {
                Console.WriteLine("Attempting to connect to WebSocket server...");

                _client = new TcpClient();
                IAsyncResult ar = _client.BeginConnect(_ip, _port, null, null);
                WaitHandle handle = ar.AsyncWaitHandle;
                bool connected = handle.WaitOne(RECONNECT_DELAY_MILLISECONDS);
                if (!connected || !_client.Connected)
                {
                    Console.WriteLine("TCP connect timeout");
                    _client.Close();
                    return;
                }
                _client.EndConnect(ar);

                Stream baseStream = _client.GetStream();

                if (_port == 443)
                {
                    RemoteCertificateValidationCallback cv = validateServerCertificate;
                    SslStream ssl = new SslStream(baseStream, false, cv);
                    ssl.AuthenticateAsClient(_ip);
                    _stream = ssl;
                }
                else
                {
                    _stream = baseStream;
                }

                _stream.ReadTimeout = READ_TIMEOUT_MILLISECONDS;

                string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                StringBuilder sb = new StringBuilder()
                    .Append("GET / HTTP/1.1\r\n")
                    .Append("Host: ").Append(_ip).Append(':').Append(_port).Append("\r\n")
                    .Append("Upgrade: websocket\r\nConnection: Upgrade\r\n")
                    .Append("Sec-WebSocket-Key: ").Append(key).Append("\r\n")
                    .Append("Sec-WebSocket-Version: 13\r\n\r\n");

                byte[] request = Encoding.ASCII.GetBytes(sb.ToString());
                _stream.Write(request, 0, request.Length);

                byte[] respBuf = new byte[1024];
                int bytes = _stream.Read(respBuf, 0, respBuf.Length);
                string resp = Encoding.ASCII.GetString(respBuf, 0, bytes);

                if (!resp.Contains(" 101 ") || !resp.ToLowerInvariant().Contains("upgrade: websocket"))
                {
                    Console.WriteLine("WebSocket handshake failed");
                    _stream.Close();
                    _client.Close();
                    return;
                }

                Console.WriteLine("WebSocket connected");
                _running = true;
                _readThread = new Thread(readLoop) { IsBackground = true, Name = "WS‑Read" };
                _readThread.Start();
                OnOpened?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine("WebSocket connection error: " + ex.Message);
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
            }
        }

        private void closeInternal()
        {
            Thread t = _readThread;
            _readThread = null;
            _running = false;

            try
            {
                if (_stream != null && _client != null && _client.Connected)
                {
                    ushort code = 1000;
                    byte[] payload = { (byte)(code >> 8), (byte)(code & 0xFF) };
                    byte[] frame = buildFrame(0x8, payload, true);

                    _stream.Write(frame, 0, frame.Length);
                    _stream.Flush();
                }
            }
            catch { }

            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }

            if (t != null && t.IsAlive && t != Thread.CurrentThread)
            {
                t.Interrupt();
                if (!t.Join(1000))
                    Console.WriteLine("Warning: read thread did not exit in time.");
            }

            Console.WriteLine("WebSocket disconnected");
            OnClosed?.Invoke();
        }

        private void readLoop()
        {
            DateTime lastPing = DateTime.UtcNow;

            try
            {
                while (_running && !_stopEvent.WaitOne(0))
                {
                    bool more = false;
                    try
                    {
                        NetworkStream ns = _stream as NetworkStream;
                        if (_stream.CanRead && ns != null && ns.DataAvailable)
                            more = processIncomingFrame();
                    }
                    catch
                    {
                        _running = false;
                    }

                    if ((DateTime.UtcNow - lastPing).TotalSeconds >= DEFAULT_PING_INTERVAL_SECONDS)
                    {
                        sendPing();
                        lastPing = DateTime.UtcNow;
                    }

                    if (!more) Thread.Sleep(10);
                }
            }
            catch (ThreadInterruptedException) { }
            finally
            {
                Console.WriteLine("Out of read loop.");
                closeInternal();
            }
        }

        private bool processIncomingFrame()
        {
            int b1 = _stream.ReadByte();
            int b2 = _stream.ReadByte();
            if (b1 == -1 || b2 == -1) throw new IOException("Stream closed");

            bool mask = (b2 & 0x80) != 0;
            int op = b1 & 0x0F;
            int lenFlag = b2 & 0x7F;
            int len = lenFlag;

            if (lenFlag == 126)
            {
                byte[] ext = new byte[2];
                readExactly(ext, 0, 2);
                len = (ext[0] << 8) | ext[1];
            }
            else if (lenFlag == 127)
            {
                byte[] ext = new byte[8];
                readExactly(ext, 0, 8);
                long l = ((long)ext[0] << 56) | ((long)ext[1] << 48) | ((long)ext[2] << 40) | ((long)ext[3] << 32) |
                         ((long)ext[4] << 24) | ((long)ext[5] << 16) | ((long)ext[6] << 8) | ext[7];
                if (l > int.MaxValue) throw new NotSupportedException("Frame too large.");
                len = (int)l;
            }

            byte[] key = null;
            if (mask)
            {
                key = new byte[4];
                readExactly(key, 0, 4);
            }

            byte[] payload = new byte[len];
            if (len > 0) readExactly(payload, 0, len);

            if (mask && len > 0)
                for (int i = 0; i < len; i++)
                    payload[i] = (byte)(payload[i] ^ key[i % 4]);

            switch (op)
            {
                case 0x1:
                    try { OnMessage?.Invoke(Encoding.UTF8.GetString(payload)); } catch { }
                    break;
                case 0x8:
                    Console.WriteLine("Close frame received");
                    _running = false;
                    break;
                case 0x9:
                    sendPong(payload);
                    Console.WriteLine("Ping received");
                    break;
                case 0xA:
                    Console.WriteLine("Pong received");
                    break;
            }

            NetworkStream ns2 = _stream as NetworkStream;
            return ns2 != null && ns2.DataAvailable;
        }

        private void readExactly(byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = _stream.Read(buffer, offset + read, count - read);
                if (n == 0) throw new IOException("Disconnected");
                read += n;
            }
        }

        private void sendPing()
        {
            try
            {
                byte[] frame = buildFrame(0x9, Array.Empty<byte>(), true);
                lock (_syncRoot) _stream.Write(frame, 0, frame.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ping error: " + ex.Message);
            }
        }

        private void sendPong(byte[] payload)
        {
            try
            {
                byte[] frame = buildFrame(0xA, payload, true);
                lock (_syncRoot) _stream.Write(frame, 0, frame.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Pong send error: " + ex.Message);
            }
        }

        private static byte[] buildFrame(byte opcode, byte[] payload, bool mask)
        {
            int len = payload?.Length ?? 0;
            int hdr = 2;

            if (len >= 126 && len <= 65535) hdr += 2;
            else if (len > 65535) hdr += 8;

            byte[] key = null;
            if (mask)
            {
                key = new byte[4];
                _rng.GetBytes(key);
                hdr += 4;
            }

            byte[] frame = new byte[hdr + len];
            frame[0] = (byte)(0x80 | opcode);

            if (len <= 125)
            {
                frame[1] = (byte)((mask ? 0x80 : 0) | len);
            }
            else if (len <= 65535)
            {
                frame[1] = (byte)((mask ? 0x80 : 0) | 126);
                frame[2] = (byte)(len >> 8);
                frame[3] = (byte)(len & 0xFF);
            }
            else
            {
                frame[1] = (byte)((mask ? 0x80 : 0) | 127);
                for (int i = 0; i < 8; i++)
                    frame[9 - i] = (byte)(len >> (8 * i));
            }

            int off = 2;
            if (len >= 126 && len <= 65535) off = 4;
            else if (len > 65535) off = 10;

            if (mask)
            {
                Buffer.BlockCopy(key, 0, frame, off, 4);
                off += 4;
            }

            if (len > 0)
            {
                if (mask)
                {
                    for (int i = 0; i < len; i++)
                        frame[off + i] = (byte)(payload[i] ^ key[i % 4]);
                }
                else
                {
                    Buffer.BlockCopy(payload, 0, frame, off, len);
                }
            }

            return frame;
        }

        private static bool validateServerCertificate(object sender,
                                                      System.Security.Cryptography.X509Certificates.X509Certificate cert,
                                                      System.Security.Cryptography.X509Certificates.X509Chain chain,
                                                      SslPolicyErrors errors)
        {
            return errors == SslPolicyErrors.None;
        }
    }
}