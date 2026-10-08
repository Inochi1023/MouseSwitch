using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MouseSwitch
{
    internal sealed class DiscoveredPeer
    {
        public string Name;
        public string Address;
        public int Port;

        public override string ToString()
        {
            return Name + "  (" + Address + ")";
        }
    }

    /// <summary>
    /// The receiving PC answers "who is there" probes on the LAN so the
    /// host side does not need the IP typed in by hand.
    /// </summary>
    internal sealed class DiscoveryResponder : IDisposable
    {
        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;
        private readonly int _tcpPort;

        public DiscoveryResponder(int tcpPort)
        {
            _tcpPort = tcpPort;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Start();
        }

        private void Loop()
        {
            try
            {
                _udp = new UdpClient();
                _udp.ExclusiveAddressUse = false;
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, Link.DiscoveryPort));
            }
            catch (Exception ex)
            {
                Config.Log("discovery bind failed: " + ex.Message);
                return;
            }

            while (_running)
            {
                try
                {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udp.Receive(ref from);
                    string text = Encoding.UTF8.GetString(data);
                    if (text.StartsWith("MSWQ"))
                    {
                        string reply = "MSWA|" + Environment.MachineName + "|" + _tcpPort;
                        byte[] rb = Encoding.UTF8.GetBytes(reply);
                        _udp.Send(rb, rb.Length, from);
                    }
                }
                catch (SocketException)
                {
                    if (!_running) break;
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            try { if (_udp != null) _udp.Close(); }
            catch { }
        }
    }

    internal static class DiscoveryProbe
    {
        /// <summary>Broadcasts a probe and collects answers for a moment.</summary>
        public static List<DiscoveredPeer> Scan(int timeoutMs)
        {
            List<DiscoveredPeer> found = new List<DiscoveredPeer>();
            UdpClient udp = null;
            try
            {
                udp = new UdpClient();
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = 400;
                byte[] probe = Encoding.UTF8.GetBytes("MSWQ");

                foreach (IPAddress target in BroadcastTargets())
                {
                    try { udp.Send(probe, probe.Length, new IPEndPoint(target, Link.DiscoveryPort)); }
                    catch { }
                }

                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref from);
                        string text = Encoding.UTF8.GetString(data);
                        if (!text.StartsWith("MSWA|")) continue;
                        string[] parts = text.Split('|');
                        if (parts.Length < 3) continue;
                        int port;
                        if (!int.TryParse(parts[2], out port)) continue;
                        string addr = from.Address.ToString();
                        bool dup = false;
                        foreach (DiscoveredPeer p in found)
                            if (p.Address == addr) { dup = true; break; }
                        if (dup) continue;
                        DiscoveredPeer peer = new DiscoveredPeer();
                        peer.Name = parts[1];
                        peer.Address = addr;
                        peer.Port = port;
                        found.Add(peer);
                    }
                    catch (SocketException)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Config.Log("scan failed: " + ex.Message);
            }
            finally
            {
                try { if (udp != null) udp.Close(); }
                catch { }
            }
            return found;
        }

        private static List<IPAddress> BroadcastTargets()
        {
            List<IPAddress> list = new List<IPAddress>();
            list.Add(IPAddress.Broadcast);
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface ni in
                         System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (System.Net.NetworkInformation.UnicastIPAddressInformation ua in
                             ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (ua.IPv4Mask == null) continue;
                        byte[] ip = ua.Address.GetAddressBytes();
                        byte[] mask = ua.IPv4Mask.GetAddressBytes();
                        byte[] bc = new byte[4];
                        for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | (mask[i] ^ 0xFF));
                        list.Add(new IPAddress(bc));
                    }
                }
            }
            catch
            {
            }
            return list;
        }
    }
}
