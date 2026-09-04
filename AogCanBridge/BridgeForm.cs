using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AogCanBridge
{
    internal sealed class BridgeForm : Form
    {
        private const int BridgePort = 19000;
        private const int BusBitrate = 250000;
        private readonly Label languageCaptionLabel = new Label();
        private readonly ComboBox languageBox = new ComboBox();
        private readonly Label pcanChannelLabel = new Label();
        private readonly ComboBox channelBox = new ComboBox();
        private readonly Button startButton = new Button();
        private readonly Label statusLabel = new Label();
        private readonly Label clientsLabel = new Label();
        private readonly Label countersLabel = new Label();
        private readonly Label busloadLabel = new Label();
        private readonly ProgressBar busloadBar = new ProgressBar();
        private readonly Label hintLabel = new Label();
        private readonly Dictionary<string, ClientState> clients =
            new Dictionary<string, ClientState>();
        private readonly object sync = new object();
        private readonly ConcurrentQueue<PcanBasic.CanMessage> transmitQueue =
            new ConcurrentQueue<PcanBasic.CanMessage>();
        private readonly AutoResetEvent transmitQueueEvent = new AutoResetEvent(false);
        private Thread worker;
        private Thread transmitWorker;
        private volatile bool stopRequested;
        private UdpClient udp;
        private ushort pcanChannel;
        private long receivedFrames;
        private long transmittedFrames;
        private long busBits;
        private long lastBusBits;
        private DateTime lastBusloadSampleTime;

        internal BridgeForm(bool autoStart = false, bool minimized = false)
        {
            Text = "AOG CAN Bridge";
            ClientSize = new Size(420, 296);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            languageCaptionLabel.Location = new Point(20, 22);
            languageCaptionLabel.AutoSize = true;
            Controls.Add(languageCaptionLabel);

            languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
            languageBox.Location = new Point(125, 18);
            languageBox.Width = 130;
            languageBox.SelectedIndexChanged += (_, __) => ChangeLanguage();
            Controls.Add(languageBox);

            pcanChannelLabel.Location = new Point(20, 56);
            pcanChannelLabel.AutoSize = true;
            Controls.Add(pcanChannelLabel);

            channelBox.DropDownStyle = ComboBoxStyle.DropDownList;
            channelBox.Location = new Point(125, 52);
            channelBox.Width = 130;
            for (int index = 1; index <= 8; index++)
                channelBox.Items.Add("PCAN-USB " + index);
            channelBox.SelectedIndex = 0;
            Controls.Add(channelBox);

            startButton.Location = new Point(275, 50);
            startButton.Size = new Size(120, 30);
            startButton.Click += (_, __) => ToggleBridge();
            Controls.Add(startButton);

            statusLabel.ForeColor = Color.DarkRed;
            statusLabel.Font = new Font(Font, FontStyle.Bold);
            statusLabel.Location = new Point(20, 104);
            statusLabel.Size = new Size(375, 24);
            Controls.Add(statusLabel);

            clientsLabel.Location = new Point(20, 142);
            clientsLabel.Size = new Size(375, 24);
            Controls.Add(clientsLabel);

            countersLabel.Location = new Point(20, 172);
            countersLabel.Size = new Size(375, 24);
            Controls.Add(countersLabel);

            busloadLabel.Location = new Point(20, 200);
            busloadLabel.Size = new Size(375, 18);
            Controls.Add(busloadLabel);

            busloadBar.Location = new Point(20, 220);
            busloadBar.Size = new Size(375, 16);
            busloadBar.Minimum = 0;
            busloadBar.Maximum = 100;
            Controls.Add(busloadBar);

            hintLabel.Location = new Point(20, 254);
            hintLabel.Size = new Size(375, 22);
            Controls.Add(hintLabel);

            FormClosing += (_, __) => StopBridge();

            InitializeLanguages();

            if (autoStart)
            {
                Shown += (_, __) =>
                {
                    StartBridge();
                    if (worker == null || !worker.IsAlive)
                    {
                        Close();
                    }
                    else if (minimized)
                    {
                        WindowState = FormWindowState.Minimized;
                        ShowInTaskbar = false;
                        Hide();
                    }
                };
            }
        }

        private void InitializeLanguages()
        {
            List<LanguageInfo> languages = Localization.DiscoverLanguages();
            LanguageInfo selected = Localization.ResolveSavedLanguage(languages);

            languageBox.Items.Clear();
            foreach (LanguageInfo language in languages) languageBox.Items.Add(language);
            languageBox.SelectedItem = selected;
        }

        private void ChangeLanguage()
        {
            if (!(languageBox.SelectedItem is LanguageInfo language)) return;
            Localization.SetLanguage(language);
            AppSettings.SaveLanguage(language.Code);
            ApplyLocalization();
        }

        private void ApplyLocalization()
        {
            bool running = worker != null && worker.IsAlive;
            languageCaptionLabel.Text = Localization.Get("Language");
            pcanChannelLabel.Text = Localization.Get("PcanChannel");
            startButton.Text = running ? Localization.Get("Stop") : Localization.Get("Start");
            statusLabel.Text = running
                ? Localization.Get("StatusRunning", channelBox.SelectedItem)
                : Localization.Get("StatusStopped");
            hintLabel.Text = Localization.Get("Hint");
            UpdateStatistics();
        }

        private void ToggleBridge()
        {
            if (worker != null && worker.IsAlive) StopBridge();
            else StartBridge();
        }

        private void StartBridge()
        {
            pcanChannel = (ushort)(PcanBasic.PcanUsbBus1 + channelBox.SelectedIndex);
            uint result;
            try
            {
                result = PcanBasic.Initialize(pcanChannel, PcanBasic.Baud250K);
            }
            catch (DllNotFoundException)
            {
                MessageBox.Show(this, Localization.Get("ErrorDllMissing"),
                    "AOG CAN Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (result != PcanBasic.ErrorOk)
            {
                MessageBox.Show(this, GetPcanError(result), Localization.Get("ErrorCannotOpenPcanTitle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, BridgePort));
                udp.Client.ReceiveBufferSize = 4 * 1024 * 1024;

                // Windows reports an ICMP "port unreachable" response as a
                // SocketException on the next UDP receive. A client may exit
                // normally between packets, so this must not stop the broker.
                const int SioUdpConnectionReset = -1744830452;
                udp.Client.IOControl((IOControlCode)SioUdpConnectionReset,
                    new byte[] { 0 }, null);
            }
            catch (Exception exception)
            {
                PcanBasic.Uninitialize(pcanChannel);
                MessageBox.Show(this, Localization.Get("ErrorCannotOpenPort", BridgePort, exception.Message),
                    "AOG CAN Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            lock (sync) clients.Clear();
            receivedFrames = 0;
            transmittedFrames = 0;
            busBits = 0;
            lastBusBits = 0;
            lastBusloadSampleTime = DateTime.UtcNow;
            stopRequested = false;
            worker = new Thread(BridgeLoop) { IsBackground = true, Name = "AOG CAN Bridge" };
            transmitWorker = new Thread(TransmitLoop) { IsBackground = true, Name = "AOG CAN Bridge TX" };
            worker.Start();
            transmitWorker.Start();
            channelBox.Enabled = false;
            startButton.Text = Localization.Get("Stop");
            statusLabel.Text = Localization.Get("StatusRunning", channelBox.SelectedItem);
            statusLabel.ForeColor = Color.DarkGreen;
        }

        private void StopBridge()
        {
            stopRequested = true;
            transmitQueueEvent.Set();
            if (worker != null && worker.IsAlive) worker.Join(1500);
            bool transmitDrained = transmitWorker == null || !transmitWorker.IsAlive ||
                transmitWorker.Join(1500);
            worker = null;
            transmitWorker = null;
            if (udp != null)
            {
                udp.Close();
                udp = null;
            }
            // If the transmit thread is still draining a backlog, PcanBasic.Write
            // may still be in flight on pcanChannel — uninitializing it concurrently
            // could crash the driver, so skip it and leak the handle instead.
            if (pcanChannel != 0 && transmitDrained) PcanBasic.Uninitialize(pcanChannel);
            pcanChannel = 0;
            lock (sync) clients.Clear();
            if (!IsDisposed)
            {
                channelBox.Enabled = true;
                startButton.Text = Localization.Get("Start");
                statusLabel.Text = Localization.Get("StatusStopped");
                statusLabel.ForeColor = Color.DarkRed;
                busloadBar.Value = 0;
                busloadLabel.Text = Localization.Get("BusLoad", "0");
                UpdateStatistics();
            }
        }

        private void BridgeLoop()
        {
            DateTime nextUiUpdate = DateTime.UtcNow;
            while (!stopRequested)
            {
                ReceiveClientPackets();
                ReceiveCanFrames();
                RemoveExpiredClients();
                if (DateTime.UtcNow >= nextUiUpdate)
                {
                    nextUiUpdate = DateTime.UtcNow.AddMilliseconds(250);
                    if (IsHandleCreated) BeginInvoke(new Action(UpdateStatistics));
                }
                Thread.Sleep(1);
            }
        }

        private void ReceiveClientPackets()
        {
            // Do not let a burst from VT or TC starve frames arriving from the
            // physical CAN bus. Transport protocol timing is tight enough that
            // draining the complete UDP queue here can make a control function
            // appear offline to the other client.
            const int maximumPacketsPerPass = 64;
            int processedPackets = 0;
            while (udp != null && processedPackets++ < maximumPacketsPerPass)
            {
                IPEndPoint endpoint;
                byte[] bytes;
                try
                {
                    if (udp.Available <= 0) return;
                    endpoint = new IPEndPoint(IPAddress.Loopback, 0);
                    bytes = udp.Receive(ref endpoint);
                }
                catch (SocketException)
                {
                    // A local UDP client may disappear without a shutdown
                    // handshake. Ignore that transient condition.
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (!endpoint.Address.Equals(IPAddress.Loopback) ||
                    !BridgePacket.TryParse(bytes, bytes.Length, out BridgePacket packet)) continue;

                string endpointKey = endpoint.ToString();
                lock (sync)
                    clients[endpointKey] = new ClientState(endpoint, packet.ClientId, DateTime.UtcNow);

                if (packet.Type == BridgeMessageType.Hello)
                {
                    packet.Type = BridgeMessageType.Heartbeat;
                    byte[] response = packet.Serialize();
                    try { udp.Send(response, response.Length, endpoint); }
                    catch (SocketException) { }
                    catch (ObjectDisposedException) { return; }
                }
                else if (packet.Type == BridgeMessageType.FrameFromClient)
                {
                    PcanBasic.CanMessage message = new PcanBasic.CanMessage
                    {
                        Id = packet.Identifier,
                        MessageType = (byte)((packet.Flags & 1) != 0 ?
                            PcanBasic.MessageExtended : 0),
                        Length = packet.DataLength,
                        Data = new byte[8]
                    };
                    Buffer.BlockCopy(packet.Data, 0, message.Data, 0, 8);

                    // Local VT/TC communication must not wait for a physical
                    // PCAN write. A slow driver call previously stopped both
                    // local forwarding and physical CAN reception for several
                    // seconds, which made VT discard every working set.
                    packet.Type = BridgeMessageType.FrameToClient;
                    Broadcast(packet, endpointKey);

                    transmitQueue.Enqueue(message);
                    transmitQueueEvent.Set();
                }
            }
        }

        private void TransmitLoop()
        {
            while (!stopRequested || !transmitQueue.IsEmpty)
            {
                if (transmitQueue.TryDequeue(out PcanBasic.CanMessage message))
                {
                    if (PcanBasic.Write(pcanChannel, ref message) == PcanBasic.ErrorOk)
                    {
                        Interlocked.Increment(ref transmittedFrames);
                        Interlocked.Add(ref busBits, EstimateFrameBits(message.Length,
                            (message.MessageType & PcanBasic.MessageExtended) != 0));
                    }
                }
                else
                {
                    transmitQueueEvent.WaitOne(5);
                }
            }
        }

        private void ReceiveCanFrames()
        {
            // Share loop time fairly with the two local clients.
            const int maximumFramesPerPass = 64;
            int processedFrames = 0;
            while (processedFrames++ < maximumFramesPerPass)
            {
                uint result = PcanBasic.Read(pcanChannel, out PcanBasic.CanMessage message,
                    out PcanBasic.CanTimestamp timestamp);
                if (result == PcanBasic.ErrorReceiveQueueEmpty) return;
                if (result != PcanBasic.ErrorOk) return;

                BridgePacket packet = new BridgePacket
                {
                    Type = BridgeMessageType.FrameToClient,
                    ClientId = 0,
                    Identifier = message.Id,
                    TimestampUs = ((ulong)timestamp.MillisOverflow << 32 | timestamp.Millis) * 1000UL + timestamp.Micros,
                    DataLength = message.Length,
                    Flags = (byte)((message.MessageType & PcanBasic.MessageExtended) != 0 ? 1 : 0)
                };
                Buffer.BlockCopy(message.Data, 0, packet.Data, 0, 8);
                Interlocked.Increment(ref receivedFrames);
                Interlocked.Add(ref busBits, EstimateFrameBits(message.Length,
                    (message.MessageType & PcanBasic.MessageExtended) != 0));
                Broadcast(packet, null);
            }
        }

        private void Broadcast(BridgePacket packet, string excludedEndpoint)
        {
            byte[] bytes = packet.Serialize();
            List<KeyValuePair<string, ClientState>> snapshot;
            lock (sync) snapshot = new List<KeyValuePair<string, ClientState>>(clients);
            foreach (KeyValuePair<string, ClientState> client in snapshot)
            {
                if (excludedEndpoint != null && client.Key == excludedEndpoint) continue;
                try { udp.Send(bytes, bytes.Length, client.Value.Endpoint); }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
            }
        }

        private void RemoveExpiredClients()
        {
            DateTime limit = DateTime.UtcNow.AddSeconds(-3);
            lock (sync)
            {
                List<string> expired = new List<string>();
                foreach (KeyValuePair<string, ClientState> client in clients)
                    if (client.Value.LastSeen < limit) expired.Add(client.Key);
                foreach (string endpoint in expired) clients.Remove(endpoint);
            }
        }

        private void UpdateStatistics()
        {
            int clientCount;
            bool hasVt;
            bool hasTc;
            lock (sync)
            {
                clientCount = clients.Count;
                hasVt = false;
                hasTc = false;
                foreach (ClientState client in clients.Values)
                {
                    hasVt |= client.ClientId == 2;
                    hasTc |= client.ClientId == 3;
                }
            }
            string connected = Localization.Get("Connected");
            string notConnected = Localization.Get("NotConnected");
            clientsLabel.Text = Localization.Get("Clients", clientCount,
                hasVt ? connected : notConnected, hasTc ? connected : notConnected);
            countersLabel.Text = Localization.Get("Counters",
                Interlocked.Read(ref receivedFrames), Interlocked.Read(ref transmittedFrames));

            long currentBusBits = Interlocked.Read(ref busBits);
            DateTime now = DateTime.UtcNow;
            double elapsedSeconds = (now - lastBusloadSampleTime).TotalSeconds;
            double percent = 0;
            if (elapsedSeconds > 0)
            {
                long deltaBits = currentBusBits - lastBusBits;
                percent = Math.Max(0, Math.Min(100,
                    deltaBits / (elapsedSeconds * BusBitrate) * 100.0));
            }
            lastBusBits = currentBusBits;
            lastBusloadSampleTime = now;
            busloadBar.Value = (int)Math.Round(percent);
            busloadLabel.Text = Localization.Get("BusLoad", percent.ToString("0.#"));
        }

        private static int EstimateFrameBits(byte dataLength, bool extended)
        {
            // Nominal bit count (SOF, arbitration, control, CRC, ACK, EOF, IFS)
            // without bit stuffing; a close-enough estimate for a load indicator.
            return (extended ? 67 : 47) + 8 * dataLength;
        }

        private static string GetPcanError(uint error)
        {
            StringBuilder text = new StringBuilder(256);
            return PcanBasic.GetErrorText(error, 0, text) == PcanBasic.ErrorOk
                ? text.ToString() : Localization.Get("ErrorPcan", error.ToString("X"));
        }

        private sealed class ClientState
        {
            internal ClientState(IPEndPoint endpoint, ushort clientId, DateTime lastSeen)
            {
                Endpoint = endpoint;
                ClientId = clientId;
                LastSeen = lastSeen;
            }
            internal IPEndPoint Endpoint { get; }
            internal ushort ClientId { get; }
            internal DateTime LastSeen { get; }
        }
    }
}
